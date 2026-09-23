using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using KominkaDogRun.DebugTools;
using KominkaDogRun.Editor;  // IMP-INF-003: KD_SceneObjects

namespace KominkaDogRun.EditorTools
{
    /// <summary>
    /// じゃれ合い / 徘徊の予定表デバッグ盤 (<see cref="KD_DogPlayDebugHud"/>) を付け外しする。
    ///
    /// **Play / ClientSim 専用**。確認が済んだら外すこと
    /// (素の MonoBehaviour なのでアップロードしても動かないが、シーンに残すと紛らわしい)。
    ///
    /// 頭上 HUD (`KD_DogAnimDebugHud`) は**犬 1 匹の動き**を見る道具、
    /// こちらは**犬をまたぐ予定表**を見る道具。目的が違うので併用してよい。
    ///
    /// メニュー名を ASCII にしてあるのは、日本語パスが API 経由の
    /// ExecuteMenuItem で文字化けして呼べないため
    /// (`KD_DogAnimDebugHudEditor.DumpClipCurves` と同じ理由)。
    /// </summary>
    public static class KD_DogPlayDebugHudEditor
    {
        const string PanelName = KD_DogDefs.PlayDebugPanelName;   // IMP-INF-003: 正本は KD_DogDefs

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Attach Dog Play HUD")]
        public static void Attach()
        {
            var existing = KD_SceneObjects.Find(PanelName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing); // 作り直す (配線が古いまま残るのを避ける)
            }

            // ---- 犬を dogIndex 順に集める ----
            //
            // **順序が dogIndex と一致していることが必須。** 盤は先読みが出した組 (a, b) を
            // 犬が記録した _dbgPairA/_dbgPairB と突き合わせるが、これは名簿の番号なので、
            // 配列の並びがずれると「常に MISMATCH」という嘘の警報になる
            var wanders = new System.Collections.Generic.List<MonoBehaviour>();
            foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "KD_DogWanderController") continue;
                wanders.Add(mb);
            }
            if (wanders.Count == 0)
            {
                Debug.LogError("[DogPlayHUD] KD_DogWanderController がシーンに無い");
                return;
            }

            int n = wanders.Count;
            var byIndex = new MonoBehaviour[n];
            bool indexOk = true;
            foreach (var mb in wanders)
            {
                int idx = new SerializedObject(mb).FindProperty("dogIndex").intValue;
                if (idx < 0 || idx >= n || byIndex[idx] != null)
                {
                    indexOk = false;
                    break;
                }
                byIndex[idx] = mb;
            }
            if (!indexOk)
            {
                Debug.LogError("[DogPlayHUD] dogIndex が 0..n-1 で埋まっていない。"
                    + "先に Tools/KominkaHillside/Menu/Imported-Unused/Dog Pack/2 - Setup Dog Pack を流すこと "
                    + "(並びがずれると先読みの検算が常に MISMATCH になる)");
                return;
            }

            var udons = new VRC.Udon.UdonBehaviour[n];
            var names = new string[n];
            var roots = new Transform[n];
            Vector3 center = Vector3.zero;
            int centers = 0;
            for (int i = 0; i < n; i++)
            {
                var so = new SerializedObject(byIndex[i]);
                roots[i] = so.FindProperty("dogRoot").objectReferenceValue as Transform;
                names[i] = roots[i] != null
                    ? roots[i].name.Replace("KD_Dog_", "")
                    : byIndex[i].gameObject.name;
                udons[i] = byIndex[i].GetComponent<VRC.Udon.UdonBehaviour>();
                if (udons[i] == null)
                    Debug.LogWarning("[DogPlayHUD] UdonBehaviour が無い: " + names[i]
                        + " (この犬の行は空のままになる)");

                var ac = so.FindProperty("areaCenter").objectReferenceValue as Transform;
                if (ac != null) { center += ac.position; centers++; }
            }
            if (centers > 0) center /= centers; else center = Vector3.zero;

            // ---- 盤を作る ----
            var go = new GameObject(PanelName);
            Undo.RegisterCreatedObjectUndo(go, "Attach dog play HUD");
            KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.DebugPath);   // IMP-INF-003: 開発専用
            // 庭の中心の少し上。犬に重ならず、庭全体を見る位置から読める高さ
            go.transform.position = center + Vector3.up * 1.7f;

            var tm = go.AddComponent<TextMesh>();
            tm.text = "(waiting for play mode)";
            tm.anchor = TextAnchor.UpperLeft;
            tm.alignment = TextAlignment.Left;
            // 大きいフォントサイズ + 小さいスケールでないと滲む (頭上 HUD と同じ作法)
            tm.fontSize = 64;
            tm.characterSize = 0.011f;
            tm.lineSpacing = 0.9f;
            tm.color = Color.white;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
            {
                tm.font = font;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = font.material;
            }

            var hud = go.AddComponent<KD_DogPlayDebugHud>();
            hud.wanderUdons = udons;
            hud.dogNames = names;
            hud.dogRoots = roots;
            hud.label = tm;

            string symbolReport = _CheckSymbols(udons);
            bool ok = symbolReport.StartsWith("symbols=ok");

            // 結果は Logs/ にも残す。コンソール解析には偽陰性があるので、
            // 独立チャネルで確かめられるようにしておく (vrc-verification)
            var rep = new System.Text.StringBuilder();
            rep.AppendLine("dogs=" + n + " (dogIndex 順)");
            for (int i = 0; i < n; i++)
                rep.AppendLine("  [" + i + "] " + names[i]
                    + " udon=" + (udons[i] != null) + " root=" + (roots[i] != null));
            rep.AppendLine("panel=" + go.transform.position.ToString("F2"));
            rep.AppendLine("log=" + hud.logPath);
            rep.AppendLine(symbolReport);
            rep.AppendLine();
            rep.AppendLine("ATTACH RESULT: " + (ok ? "PASS" : "FAIL"));
            try { System.IO.File.WriteAllText("Logs/imp-dog-013-attach.txt", rep.ToString()); }
            catch (System.Exception e) { Debug.LogWarning("[DogPlayHUD] " + e.Message); }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[DogPlayHUD] attached dogs=" + n
                + " at " + go.transform.position.ToString("F2")
                + " / log=" + hud.logPath
                + " / " + symbolReport
                + " (Play / ClientSim でのみ動作。確認後は Detach すること)");
        }

        /// <summary>盤が名前で読む Udon の記号が、実際にコンパイル済みプログラムへ
        /// 出ているかを確かめる。
        ///
        /// **これが無いと、フィールドを 1 つ改名しただけで盤が黙って 0 を並べる。**
        /// `GetProgramVariable` は記号が無くても null を返すだけで、盤は
        /// フォールバック値を表示してしまう = **静かに嘘をつく**。
        /// 記号表は編集時に読めるので、Play を待たずにここで落とす。
        /// (編集時のヒープ自体は未初期化なので、値ではなく**記号表**を見るのが要点)
        /// </summary>
        static string _CheckSymbols(VRC.Udon.UdonBehaviour[] udons)
        {
            string[] required = {
                "debugInstrument", "_dbgReason", "_dbgServerTime", "_dbgCycle",
                "_dbgPhase", "_dbgWinStart", "_dbgWinCore", "_dbgWinTotal",
                "_dbgPairA", "_dbgPairB", "_dbgPairCycle", "_dbgPartySize",
                "_dbgType", "_dbgRole",
                "_dbgWanderState", "_dbgWaypoint", "_speed",
                "playCyclePeriod", "playDurationMin", "playDurationMax",
                "playBreakOff", "playFaceChance", "roleSwapPeriod",
                "playMaxDogs", "playJoinWindow",
            };

            foreach (var ub in udons)
            {
                if (ub == null || ub.programSource == null
                    || ub.programSource.SerializedProgramAsset == null) continue;
                var prog = ub.programSource.SerializedProgramAsset.RetrieveProgram();
                if (prog == null || prog.SymbolTable == null) continue;

                var symbols = new System.Collections.Generic.HashSet<string>(
                    prog.SymbolTable.GetSymbols());
                var missing = new System.Collections.Generic.List<string>();
                foreach (var s in required)
                    if (!symbols.Contains(s)) missing.Add(s);

                if (missing.Count > 0)
                {
                    Debug.LogError("[DogPlayHUD] KD_DogWanderController に記号が無い: "
                        + string.Join(", ", missing.ToArray())
                        + " — 盤はこの項目を 0 として表示してしまう "
                        + "(改名したなら KD_DogPlayDebugHud 側もそろえること)");
                    return "symbols=MISSING(" + missing.Count + ")";
                }
                return "symbols=ok(" + required.Length + ")";
            }
            return "symbols=未確認 (program asset へ辿れない)";
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Detach Dog Play HUD")]
        public static void Detach()
        {
            var go = KD_SceneObjects.Find(PanelName);
            if (go == null)
            {
                Debug.Log("[DogPlayHUD] 盤は付いていない");
                return;
            }
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[DogPlayHUD] detached");
        }

        /// <summary>ログを消す。**セッションをまたいで追記される**ので、
        /// 「今回の Play だけを見たい」ときに先に流す。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Clear Dog Play Log")]
        public static void ClearLog()
        {
            const string path = "Logs/dog-play-debug.txt";
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
                Debug.Log("[DogPlayHUD] cleared " + path);
            }
            else
            {
                Debug.Log("[DogPlayHUD] ログはまだ無い: " + path);
            }
        }
    }
}
