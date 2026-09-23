using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// NetworkIDs の「毎回別 ID で再発する参照切れ 13 件」の根治 (IMP-INF-019 X1、2026-09-13。Codex ③ で機構の説明を訂正)。
    ///
    /// 機構: 購入アセット RBS SleepKit2 の <c>PenManagerButtonGenerator</c> ([InitializeOnLoad]) は
    /// <b>シーン保存時 (sceneSaving)</b> にタブレットの QvPen ページのボタン (PenButton_PenManager ×12 +
    /// ObjectsButtonPrefab ×1、UdonBehaviour 付きのプレハブインスタンス) を**作り直す** (破棄 → 再生成)。
    /// VRChat SDK も同じ sceneSaving で NetworkID を配るが、生成器より先に走るため、ID は**破棄される側**の
    /// 旧インスタンスへ付く。保存されるのは新インスタンス (シーンには残る) と、旧インスタンスを指したまま死んだ
    /// 台帳項目 <c>gameObject: {fileID: 0}</c> の 13 件。Prune しても次の保存で同じ経路を通り、別 ID で再発する。
    ///
    /// 対処: 購入アセットは触らない。sceneSaving の**最後** (SDK → 生成器 → ここ。Assembly-CSharp-Editor は
    /// SDK パッケージより後に読み込まれる) で**死骸だけ**を台帳から外す。生きている項目は触らない —
    /// 新インスタンスにはビルド時に SDK が改めて ID を配る (<c>AssignSceneNetworkIDs.OnPreprocessScene</c>)。
    /// 保存後にも死骸だけを掃除する (SDK が保存後に配り直す分で Verify が汚れないように)。
    ///
    /// 順序が期待と違う場合の検出: <c>Debug/Count Broken NetworkIDs (file vs memory)</c> が
    /// **保存ファイル側**の死骸も数える。メモリが 0 でもファイルが 0 でなければフックの順序が SDK に負けている。
    /// </summary>
    [InitializeOnLoad]
    public static class KD_NetworkIdHygieneEditor
    {
        static KD_NetworkIdHygieneEditor()
        {
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        static void OnSceneSaving(Scene scene, string path)
        {
            int removed = PruneDead(scene, false);
            if (removed > 0)
                Debug.Log("[KD_NetworkIdHygiene] 保存前に NetworkIDs の死骸 " + removed + " 件を外した (RBS 生成器が作り直したボタンの旧参照)");
        }

        static void OnSceneSaved(Scene scene)
        {
            int removed = PruneDead(scene, false);
            if (removed > 0)
                Debug.Log("[KD_NetworkIdHygiene] 保存後にメモリ上の NetworkIDs の死骸 " + removed + " 件を外した");
        }

        static VRC.SDK3.Components.VRCSceneDescriptor DescriptorOf(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var desc = root.GetComponentInChildren<VRC.SDK3.Components.VRCSceneDescriptor>(true);
                if (desc != null) return desc;
            }
            return null;
        }

        /// <summary>参照先が居ない項目 (死骸) だけを外す。**生きている項目は外さない。**
        /// 戻り値は外した数。<paramref name="markDirty"/> はメニューから呼ぶときだけ true。</summary>
        public static int PruneDead(Scene scene, bool markDirty)
        {
            var desc = DescriptorOf(scene);
            if (desc == null || desc.NetworkIDCollection == null) return 0;
            int removed = 0;
            for (int i = desc.NetworkIDCollection.Count - 1; i >= 0; i--)
            {
                var e = desc.NetworkIDCollection[i];
                if (e != null && e.gameObject != null) continue;
                desc.NetworkIDCollection.RemoveAt(i);
                removed++;
            }
            if (removed > 0 && markDirty)
            {
                EditorUtility.SetDirty(desc);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return removed;
        }

        /// <summary>メモリ上の台帳と、保存ファイル上の台帳の両方で死骸を数える。
        /// ファイル側は <c>gameObject: {fileID: 0}</c> の出現数 (NetworkIDs 以外には現れない)。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Count Broken NetworkIDs (file vs memory)")]
        public static void CountFileVsMemory()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var desc = DescriptorOf(scene);
            int total = 0, dead = 0;
            if (desc != null && desc.NetworkIDCollection != null)
                foreach (var e in desc.NetworkIDCollection) { total++; if (e == null || e.gameObject == null) dead++; }
            int fileDead = -1, fileTotal = -1;
            if (!string.IsNullOrEmpty(scene.path) && File.Exists(scene.path))
            {
                fileDead = 0; fileTotal = 0;
                foreach (var line in File.ReadLines(scene.path))
                {
                    if (line.Contains("gameObject: {fileID: 0}")) fileDead++;
                    if (line.StartsWith("    ID: ")) fileTotal++;
                }
            }
            Debug.Log("[KD_NetworkIdHygiene] memory total " + total + " / dead " + dead
                + " | file total " + fileTotal + " / dead " + fileDead
                + (dead == 0 && fileDead > 0 ? "\n  ⚠ ファイルに死骸が残っている = 保存時の掃除が SDK の割り当てより先に走った。順序を疑うこと" : ""));
        }
    }
}
