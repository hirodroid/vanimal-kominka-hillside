using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using KominkaDogRun.DebugTools;

namespace KominkaDogRun.EditorTools
{
    /// <summary>
    /// 犬の頭上デバッグ HUD を付け外しする (KD_DogAnimDebugHud)。
    ///
    /// 「止まったと判定しているのに動いている」のかを実測で切り分けるための道具。
    /// **Play / ClientSim 専用**なので、確認が済んだら必ず外す
    /// (素の MonoBehaviour なのでアップロードしても動かないが、シーンに残すと紛らわしい)。
    ///
    /// 対象は KD_DogWanderController が指している dogRoot / dogAnimator を正とする
    /// — 犬の探し方を HUD 側で二重に持たないため。
    /// </summary>
    public static class KD_DogAnimDebugHudEditor
    {
        const string HudName = "KD_DebugHUD";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/KD_DogAnimDebugHud - Attach")]
        public static void Attach()
        {
            int made = 0, skipped = 0;
            foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "KD_DogWanderController") continue;

                var so = new SerializedObject(mb);
                var root = so.FindProperty("dogRoot").objectReferenceValue as Transform;
                var anim = so.FindProperty("dogAnimator").objectReferenceValue as Animator;
                if (root == null || anim == null)
                {
                    Debug.LogWarning("[KD_DogAnimDebugHud] wiring incomplete on " + mb.gameObject.name);
                    skipped++;
                    continue;
                }

                // 既にあるなら作り直さない (二重に付けて数字が重なるのを避ける)
                var exist = root.Find(HudName);
                if (exist != null) { skipped++; continue; }

                var go = new GameObject(HudName);
                Undo.RegisterCreatedObjectUndo(go, "Attach dog debug HUD");
                go.transform.SetParent(root, false);
                go.transform.localPosition = Vector3.up * 0.9f;

                var tm = go.AddComponent<TextMesh>();
                tm.text = "(waiting for play mode)";
                tm.anchor = TextAnchor.LowerCenter;
                tm.alignment = TextAlignment.Center;
                // 文字を小さく作って縮小すると滲むので、大きいフォントサイズ + 小さいスケール
                tm.fontSize = 64;
                tm.characterSize = 0.012f;
                tm.color = Color.white;

                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (font != null)
                {
                    tm.font = font;
                    var mr = go.GetComponent<MeshRenderer>();
                    if (mr != null) mr.sharedMaterial = font.material;
                }

                var hud = go.AddComponent<KD_DogAnimDebugHud>();
                hud.dogRoot = root;
                hud.dogAnimator = anim;
                hud.label = tm;

                // Play 中の内部値は C# プロキシではなく UdonBehaviour 側にある。
                // Wander はこの GameObject、Fetch は同じ dogRoot を指しているものを探す
                hud.wanderUdon = mb.GetComponent<VRC.Udon.UdonBehaviour>();
                hud.fetchUdon = _FindFetchUdonFor(root);
                if (hud.wanderUdon == null || hud.fetchUdon == null)
                    Debug.LogWarning("[KD_DogAnimDebugHud] udon not found for " + root.name
                        + " (wander=" + (hud.wanderUdon != null) + " fetch=" + (hud.fetchUdon != null)
                        + ") - SPD 行は -1 のままになる");

                made++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[KD_DogAnimDebugHud] attached=" + made + " skipped=" + skipped
                + " (Play / ClientSim でのみ動作。確認後は必ず外すこと)");
        }

        /// <summary>同じ dogRoot を動かしている KD_DogFetchController の UdonBehaviour を探す。
        /// 犬とシステムの対応は dogRoot 参照が正 (名前の一致に頼らない)。</summary>
        static VRC.Udon.UdonBehaviour _FindFetchUdonFor(Transform root)
        {
            foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "KD_DogFetchController") continue;
                var so = new SerializedObject(mb);
                var r = so.FindProperty("dogRoot").objectReferenceValue as Transform;
                if (r != root) continue;
                return mb.GetComponent<VRC.Udon.UdonBehaviour>();
            }
            return null;
        }

        /// <summary>各犬の Controller に載っているクリップが「自分で位置を動かす成分」を
        /// 持っていないかを一覧する。ルート (path 空) の m_LocalPosition カーブがあると、
        /// applyRootMotion が false でも Animator が Transform を書いてしまい、
        /// スクリプトの減速をいくら詰めても滑りが消えない。
        ///
        /// メニュー名を ASCII にしてあるのは、日本語パスが API 経由の
        /// ExecuteMenuItem で文字化けして呼べないため。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Dump Dog Clip Curves")]
        public static void DumpClipCurves()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController AC_KD_Dog"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
                if (ac == null) continue;
                sb.AppendLine("== " + System.IO.Path.GetFileName(path));
                foreach (var layer in ac.layers)
                {
                    if (layer.stateMachine == null) continue;
                    foreach (var cs in layer.stateMachine.states)
                        _DumpMotion(cs.state.name, cs.state.motion, sb);
                }
            }
            // コンソールだけだと AI 側から読み出せない (ログ取得が error 限定) ため
            // Temp へも落とす。Temp/ は git 管理外
            string outPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath), "Temp/kd_clip_curves.txt");
            System.IO.File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[KD_DogClipCurves] -> " + outPath + "\n" + sb);
        }

        static void _DumpMotion(string stateName, Motion motion, System.Text.StringBuilder sb)
        {
            var tree = motion as UnityEditor.Animations.BlendTree;
            if (tree != null)
            {
                foreach (var child in tree.children)
                    _DumpMotion(stateName + "/" + (child.motion == null ? "?" : child.motion.name),
                        child.motion, sb);
                return;
            }

            var clip = motion as AnimationClip;
            if (clip == null)
            {
                sb.AppendLine("  " + stateName + " : NO CLIP");
                return;
            }

            int rootPos = 0, anyPos = 0;
            float rootRange = 0f;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.propertyName.IndexOf("LocalPosition") < 0) continue;
                anyPos++;
                if (b.path.Length != 0) continue; // ルート (犬そのもの) のカーブだけが問題
                rootPos++;
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.keys.Length == 0) continue;
                float mn = float.MaxValue, mx = float.MinValue;
                foreach (var k in curve.keys)
                {
                    if (k.value < mn) mn = k.value;
                    if (k.value > mx) mx = k.value;
                }
                if (mx - mn > rootRange) rootRange = mx - mn;
            }

            sb.AppendLine("  " + stateName + " : clip=" + clip.name
                + " loop=" + clip.isLooping
                + " len=" + clip.length.ToString("F2")
                + " posCurves=" + anyPos
                + " ROOTposCurves=" + rootPos
                + (rootPos > 0 ? "  <<< ROOT MOVES range=" + rootRange.ToString("F3") + "m" : ""));
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/KD_DogAnimDebugHud - Detach")]
        public static void Detach()
        {
            int removed = 0;
            foreach (var hud in Object.FindObjectsOfType<KD_DogAnimDebugHud>(true))
            {
                if (hud == null) continue;
                Undo.DestroyObjectImmediate(hud.gameObject);
                removed++;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[KD_DogAnimDebugHud] removed=" + removed);
        }
    }
}
