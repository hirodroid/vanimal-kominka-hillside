using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace KominkaDogRun.EditorTools
{
    /// <summary>
    /// 破壊的監査 RC-14: シーンに残った **Missing Script** (参照先のスクリプトが削除された
    /// MonoBehaviour コンポーネント) を除去する。
    ///
    /// ギャラリー板 Board0〜Board11 に、`e027bf6` で削除済みの `KD_GalleryBoard.cs`
    /// (guid 744ff587…) を指すコンポーネントが **12 個**残っていた。
    ///
    /// **これが厄介なのは、Setup の strip も Verify の検査も「型で探す」から** —
    /// 型が存在しないコンポーネントはどちらの網にも掛からず、
    /// **Verify が誤って PASS する**。ビルド時には 12 件のエラーログになる。
    ///
    /// 検出も除去も `GameObjectUtility` の専用 API でしか行えないため、
    /// 一度きりの掃除でもメニューが要る (`vrc-editor-script-channel` の
    /// 「一時 Editor スクリプト + ExecuteMenuItem」方式)。
    ///
    /// **掃除が済んだらこのファイルごと消してよい。**
    /// </summary>
    public static class KD_MissingScriptCleanupEditor
    {
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Count Missing Scripts")]
        public static void Count()
        {
            // **除去の前に必ず対象を名指しさせる。** 購入アセット
            // (`Assets/TORINOSUKE/` `Assets/モサンゴ屋/` 由来のオブジェクト) が
            // 混ざっていたら触ってはいけないので、パスを見てから判断する
            var log = new StringBuilder();
            int objects, components;
            Scan(out objects, out components, log);
            Debug.Log("[KD_MissingScriptCleanup] Missing Script = " + components
                + " 件 / " + objects + " GameObject\n" + log);
        }

        /// <summary>除去してよい範囲。**自作物だけに限る。**
        ///
        /// 2026-08-20 の実測で、シーンの Missing Script は 25 件あり内訳は 3 系統だった:
        ///   - `KD_PhotoSystem/Gallery/Board0〜11` (12 件) — **自作。削除済み
        ///     `KD_GalleryBoard.cs` の残骸で、これが監査 (FUNC-16/BUG-06) の指摘対象**
        ///   - `KD_Dog_Pome` / `KD_Dog_Lab` / `KD_Dog_Dachs` とその `avater_taget` (12 件) —
        ///     **購入アセットのプレハブ由来**。これらはプレハブインスタンスなので、
        ///     消すとプレハブ差分 (コンポーネント削除のオーバーライド) になる。
        ///     「素の購入プレハブから Setup が通り Unpack を要求しない」という不変条件に
        ///     触れるため、**ユーザー判断なしに触らない**
        ///   - `nadena.dev.ndmf__Activator` (1 件) — 第三者ツールがシーンへ置いたもの
        ///
        /// **KD_Dog_Corgi には 1 件も無い** — 犬種によって同梱スクリプトが違う。</summary>
        const string RemoveScope = "KD_PhotoSystem/";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Remove Missing Scripts")]
        public static void Remove()
        {
            var done = new StringBuilder();
            var skipped = new StringBuilder();
            int removed = 0;
            int skippedCount = 0;

            // **除去は 1 GameObject ずつ。** まとめて消す API は無く、
            // また「どのオブジェクトから何個消したか」をログへ残したい
            foreach (var go in AllSceneObjects())
            {
                int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (n == 0) continue;
                string path = Path(go);
                if (path.IndexOf(RemoveScope, System.StringComparison.Ordinal) < 0)
                {
                    skippedCount += n;
                    skipped.AppendLine("  skip " + path + " : " + n + " 件 (範囲外)");
                    continue;
                }
                Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
                int r = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                removed += r;
                done.AppendLine("  " + path + " : " + r + " 件");
            }

            if (removed == 0)
            {
                Debug.Log("[KD_MissingScriptCleanup] 範囲 (" + RemoveScope
                    + ") に Missing Script は 0 件。何もしない\n" + skipped);
                return;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[KD_MissingScriptCleanup] 除去 " + removed + " 件 / 範囲外のため据え置き "
                + skippedCount + " 件\n" + done + skipped
                + "**シーンは保存していない。** 確認してから Ctrl+S で保存すること");
        }

        static void Scan(out int objects, out int components, StringBuilder log)
        {
            objects = 0;
            components = 0;
            foreach (var go in AllSceneObjects())
            {
                int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (n == 0) continue;
                objects++;
                components += n;
                if (log != null) log.AppendLine("  " + Path(go) + " : " + n + " 件");
            }
        }

        // ================================================================
        // NetworkIDs の死骸 (IMP-PRG-002 で踏んだ)
        //
        // **オブジェクトを消すと `VRCSceneDescriptor.NetworkIDs` に
        // `{fileID: 0}` の死骸が残る** (`vrc-scene-edit-safety`)。
        // `Fetch System/3` と `Scene Layout/3` の両方が
        // 「NetworkIDs broken refs == 0」を検査しているので、両方が同時に赤くなる。
        //
        // 踏んだ経路: `Debug/Attach Progress HUD` は付け直しのたびに
        // 既存の盤を `DestroyObjectImmediate` するので、**盤の付け外しを繰り返すと溜まる。**
        // 盤は開発専用なので死骸も開発専用だが、**Verify が赤いままだと
        // 本物の破損を見落とす**ので掃除できるようにしておく。

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Count Broken NetworkIDs")]
        public static void CountBrokenNetworkIds()
        {
            int total, broken;
            var log = new StringBuilder();
            ScanNetworkIds(out total, out broken, log);
            Debug.Log($"[KD_NetworkIdCleanup] broken {broken} / total {total}\n{log}");
        }

        /// <summary>死骸だけを取り除く。**生きている項目には触らない** —
        /// 生きている ID を消すと、公開済みインスタンスとの Ownership の
        /// 対応が変わってしまう。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Prune Broken NetworkIDs")]
        public static void PruneBrokenNetworkIds()
        {
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (desc == null || desc.NetworkIDCollection == null)
            {
                Debug.Log("[KD_NetworkIdCleanup] VRCSceneDescriptor が無い");
                return;
            }

            var log = new StringBuilder();
            int before = desc.NetworkIDCollection.Count;
            int removed = 0;
            for (int i = desc.NetworkIDCollection.Count - 1; i >= 0; i--)
            {
                var e = desc.NetworkIDCollection[i];
                if (e != null && e.gameObject != null) continue;
                log.AppendLine($"  removed ID {(e == null ? -1 : e.ID)} (参照先が居ない)");
                desc.NetworkIDCollection.RemoveAt(i);
                removed++;
            }

            if (removed == 0)
            {
                Debug.Log("[KD_NetworkIdCleanup] 死骸は無い (total " + before + ")");
                return;
            }
            EditorUtility.SetDirty(desc);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[KD_NetworkIdCleanup] removed {removed} / {before}\n{log}"
                + "**シーンは保存していない。** 保存前に Verify を流し直すこと");
        }

        static void ScanNetworkIds(out int total, out int broken, StringBuilder log)
        {
            total = 0; broken = 0;
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (desc == null || desc.NetworkIDCollection == null) return;
            foreach (var e in desc.NetworkIDCollection)
            {
                total++;
                if (e != null && e.gameObject != null) continue;
                broken++;
                log.AppendLine($"  broken: ID {(e == null ? -1 : e.ID)}");
            }
        }

        /// <summary>開いているシーンの全 GameObject (非アクティブを含む)。
        /// **プレハブアセットは触らない** — シーン側だけを対象にする。</summary>
        static GameObject[] AllSceneObjects()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var r in roots)
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    list.Add(t.gameObject);
            return list.ToArray();
        }

        static string Path(GameObject go)
        {
            string p = go.name;
            var t = go.transform.parent;
            while (t != null) { p = t.name + "/" + p; t = t.parent; }
            return p;
        }
    }
}
