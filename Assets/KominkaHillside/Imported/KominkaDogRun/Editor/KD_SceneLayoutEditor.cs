using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-INF-003: シーン階層の調査・是正・検査。
    ///
    /// **役割分担 (これを崩すと Setup 再実行の冪等性が壊れる):**
    /// <code>
    ///   Setup   = 作るだけ。既存の親も位置も絶対に触らない。
    ///   Layout  = 動かすだけ。作りはしない (コンテナを除く)。
    ///   Verify  = 数えるだけ。
    /// </code>
    ///
    /// 正本は <see cref="KD_DogDefs.LayoutTable"/>。**新しい常設物を足したらそこへ 1 行**
    /// 足せば、水場・おもちゃ箱・おやつ枠・店のどれでもこの 3 メニューが置く。
    ///
    /// **手でドラッグしない理由**: `KD_FetchSystem_Corgi` を `KD_Dog_Corgi` の*隣*でなく
    /// *上*に落とすと購入プレハブへ <c>m_AddedGameObjects</c> の override が書かれる
    /// (`allowUnpacked = false` が禁じている当のもの)。38 回 × 似た落とし先 4 つで、
    /// しかも `Dog Unit/5 - Diff` を回すまで見えない。
    /// 加えて `vrc-scene-edit-safety` が**適用と検証を別メニューに分ける**ことを要求する。
    /// </summary>
    public static class KD_SceneLayoutEditor
    {
        const string SurveyLog = "Logs/imp-inf-003-layout-survey.txt";
        const string VerifyLog = "Logs/imp-inf-003-layout-verify.txt";

        enum Status { Ok, Misplaced, Missing, Duplicate, Legacy }

        struct Row
        {
            public KD_LayoutRow def;
            public GameObject go;
            public int count;
            public Status status;
            public string currentPath;
        }

        // ================================================================ 1 - Survey

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Scene Layout/1 - Survey Scene Layout")]
        public static void Survey()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_SceneLayout] Survey (IMP-INF-003) — **シーンには一切触れない**");
            log.AppendLine();

            var rows = Collect();
            AppendRows(log, rows);
            AppendOrphans(log);
            AppendCounts(log, rows);

            log.AppendLine();
            log.AppendLine("SURVEY RESULT: DONE (是正は 2 - Apply Scene Layout)");
            Finish(SurveyLog, log);
        }

        // ================================================================ 2 - Apply

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Scene Layout/2 - Apply Scene Layout")]
        public static void Apply()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_SceneLayout] Apply (IMP-INF-003)");
            log.AppendLine();

            Undo.SetCurrentGroupName("IMP-INF-003 Apply scene layout");
            int undoGroup = Undo.GetCurrentGroup();

            // --- 旧名の改名を先に済ませる (以後は新名で引ける) ---
            int renamed = 0;
            foreach (var def in KD_DogDefs.LayoutTable)
            {
                if (string.IsNullOrEmpty(def.legacyName)) continue;
                int newCount;
                KD_SceneObjects.Find(def.name, out newCount);
                if (newCount > 0) continue;   // 既に新名がある

                int oldCount;
                var old = KD_SceneObjects.Find(def.legacyName, out oldCount);
                if (old == null) continue;
                if (oldCount > 1)
                {
                    log.AppendLine("skip rename " + def.legacyName + " — " + oldCount + " 個ある (手で解消する)");
                    continue;
                }
                // fileID は変わらないので NetworkIDs も Inspector 参照も無傷
                Undo.RecordObject(old, "Rename " + def.legacyName);
                old.name = def.name;
                log.AppendLine("renamed " + def.legacyName + " -> " + def.name);
                renamed++;
            }

            // --- コンテナを掘る (Transform は単位元へ強制される) ---
            int containers = 0;
            foreach (var path in KD_DogDefs.ContainerPaths)
            {
                KD_SceneObjects.EnsureContainer(path);
                containers++;
            }
            log.AppendLine("ensured " + containers + " containers");
            log.AppendLine();

            // --- 表に載っている名前だけを動かす ---
            int moved = 0, kept = 0, skipped = 0;
            foreach (var def in KD_DogDefs.LayoutTable)
            {
                int count;
                var go = KD_SceneObjects.Find(def.name, out count);
                if (go == null) { log.AppendLine("skip  " + def.name + " — シーンに無い"); skipped++; continue; }
                if (count > 1) { log.AppendLine("skip  " + def.name + " — " + count + " 個ある (手で解消する)"); skipped++; continue; }

                var target = KD_SceneObjects.EnsureContainer(def.containerPath);
                if (go.transform.parent == target) { kept++; continue; }

                string before = KD_SceneObjects.PathOf(go);
                // worldPositionStays: true — コンテナが単位元なので local Transform は 1 バイトも変わらない
                Undo.SetTransformParent(go.transform, target, true, "Apply scene layout");
                log.AppendLine("moved " + before + "  ->  " + def.containerPath + "/" + def.name);
                moved++;
            }

            // --- 兄弟順を表の順にそろえる (.unity の差分をレビュー可能にする) ---
            int reordered = 0;
            var perContainer = new Dictionary<string, int>();
            foreach (var def in KD_DogDefs.LayoutTable)
            {
                int count;
                var go = KD_SceneObjects.Find(def.name, out count);
                if (go == null || count > 1) continue;
                var target = KD_SceneObjects.EnsureContainer(def.containerPath);
                if (go.transform.parent != target) continue;

                int idx;
                perContainer.TryGetValue(def.containerPath, out idx);
                perContainer[def.containerPath] = idx + 1;
                if (go.transform.GetSiblingIndex() == idx) continue;
                Undo.SetSiblingIndex(go.transform, idx, "Apply scene layout");
                reordered++;
            }

            reordered += NormalizeGalleryOrder(log);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            log.AppendLine();
            log.AppendLine("renamed=" + renamed + " containersEnsured=" + containers +
                           " moved=" + moved + " kept=" + kept + " skipped=" + skipped +
                           " reordered=" + reordered);
            log.AppendLine("APPLY RESULT: DONE — **シーンは保存しない。** " +
                           "3 - Verify Scene Layout と Verify スイートを通してから保存する");
            Finish(VerifyLog.Replace("verify", "apply"), log);
        }

        /// <summary>ギャラリーの兄弟順を正規化する (`Board0..23` / `Label0..5` / `Caption` /
        /// `KD_CreditBoard` の順)。今は `KD_CreditBoard` が `Board19` と `Board20` の間に
        /// 埋まっている。**板は `galleryT.Find("Board" + i)` で名前引きされていて
        /// 兄弟順に依存していない**ことを確認済みなので、参照は一切動かない。</summary>
        static int NormalizeGalleryOrder(StringBuilder log)
        {
            var photo = KD_SceneObjects.Find(KD_DogDefs.PhotoSystemName);
            var gallery = photo == null ? null : photo.transform.Find("Gallery");
            if (gallery == null) return 0;

            var order = new List<string>();
            for (int i = 0; i < 64; i++) order.Add("Board" + i);
            for (int i = 0; i < 16; i++) order.Add("Label" + i);
            order.Add("Caption");
            order.Add(KD_DogDefs.CreditBoardName);

            int idx = 0, n = 0;
            foreach (var name in order)
            {
                var t = gallery.Find(name);
                if (t == null) continue;
                if (t.GetSiblingIndex() != idx) { Undo.SetSiblingIndex(t, idx, "Apply scene layout"); n++; }
                idx++;
            }
            if (n > 0) log.AppendLine("normalized Gallery sibling order (" + n + " moved, " + idx + " known children)");
            return n;
        }

        // ================================================================ 3 - Verify

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Scene Layout/3 - Verify Scene Layout")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_SceneLayout] Verify (IMP-INF-003)");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            log.AppendLine();
            var rows = Collect();
            AppendRows(log, rows);

            int orphans = AppendOrphans(log);

            // --- コンテナが動いていないこと ---
            log.AppendLine();
            log.AppendLine("== コンテナ (Transform が単位元であること) ==");
            int containersMoved = 0, containersMissing = 0;
            foreach (var path in KD_DogDefs.ContainerPaths)
            {
                var t = FindContainer(path);
                if (t == null) { log.AppendLine("  --   " + path + " (まだ無い)"); containersMissing++; continue; }
                bool identity = t.localPosition == Vector3.zero
                                && t.localRotation == Quaternion.identity
                                && t.localScale == Vector3.one;
                if (!identity) containersMoved++;
                log.AppendLine((identity ? "  ok   " : "  FAIL ") + path +
                    (identity ? "" : " — pos" + t.localPosition + " rot" + t.localRotation.eulerAngles + " scale" + t.localScale));
            }

            // --- 数える ---
            int ok = 0, misplaced = 0, missing = 0, duplicate = 0, legacy = 0;
            int optionalAbsent = 0;
            foreach (var r in rows)
            {
                switch (r.status)
                {
                    case Status.Ok: ok++; break;
                    case Status.Misplaced: misplaced++; break;
                    case Status.Missing:
                        // optional (付け外しされる計器) は「無い」のが正しい状態がある
                        if (r.def.optional) optionalAbsent++; else missing++;
                        break;
                    case Status.Duplicate: duplicate++; break;
                    case Status.Legacy: legacy++; break;
                }
            }

            log.AppendLine();
            log.AppendLine("== 判定 ==");
            Check(duplicate == 0, "同名の重複が無い (actual " + duplicate + ")");
            Check(missing == 0, "表の名前がすべてシーンにある (actual missing " + missing + ")");
            Check(legacy == 0, "旧名が残っていない (actual " + legacy + ")");
            Check(misplaced == 0, "すべて所定のコンテナの下にある (actual misplaced " + misplaced + ")");
            Check(orphans == 0, "表に載っていないルート直下の KD_* が無い (actual " + orphans + ")");
            Check(containersMoved == 0, "コンテナの Transform が単位元 (actual moved " + containersMoved + ")");
            Check(containersMissing == 0, "コンテナがすべてある (actual missing " + containersMissing + ")");

            // --- NetworkIDs (削除・生成をしていない証拠) ---
            int netTotal, netBroken;
            CountNetworkIds(out netTotal, out netBroken);
            Check(netBroken == 0, "NetworkIDs broken refs == 0 (actual " + netBroken + ", total " + netTotal + ")");

            // --- 犬が Connected なプレハブインスタンスのまま ---
            foreach (var def in KD_DogDefs.All)
            {
                int c;
                var dog = KD_SceneObjects.Find(def.DogName, out c);
                bool connected = dog != null &&
                    PrefabUtility.GetPrefabInstanceStatus(dog) == PrefabInstanceStatus.Connected;
                Check(connected, def.DogName + " が Connected なプレハブインスタンス (Unpack していない)");
            }

            AppendCounts(log, rows);

            // --- 開発専用オブジェクト (NOTE。**FAIL にはしない**) ---
            //
            // 公開まで毎日落ちる検査を足すと Verify を無視する習慣がつく。
            // 撤去は公開前チェックリストの担当で、ここは「見えていること」だけを担う
            int debugCount = 0;
            var debugRoot = FindContainer(KD_DogDefs.DebugPath);
            if (debugRoot != null)
                foreach (Transform c in debugRoot) if (c.gameObject.activeSelf) debugCount++;
            log.AppendLine();
            log.AppendLine("NOTE 開発専用オブジェクト (KD_Debug の下でアクティブなもの): " + debugCount +
                           " — **公開前に空か非アクティブにすること** (ここでは FAIL にしない)");

            log.AppendLine();
            log.AppendLine("expected=" + rows.Count + " ok=" + ok + " misplaced=" + misplaced +
                           " missing=" + missing + " optionalAbsent=" + optionalAbsent +
                           " duplicate=" + duplicate + " legacy=" + legacy +
                           " orphanKD=" + orphans + " containersMoved=" + containersMoved +
                           " debugObjects=" + debugCount +
                           " networkIdsTotal=" + netTotal + " networkIdsBroken=" + netBroken +
                           " sceneRoots=" + EditorSceneManager.GetActiveScene().rootCount);
            log.AppendLine();
            log.AppendLine(fail == 0 ? "LAYOUT RESULT: PASS" : "LAYOUT RESULT: FAIL (" + fail + " checks)");
            Finish(VerifyLog, log);
        }

        // ================================================================ 共有

        static List<Row> Collect()
        {
            var rows = new List<Row>();
            foreach (var def in KD_DogDefs.LayoutTable)
            {
                int count;
                var go = KD_SceneObjects.Find(def.name, out count);
                var r = new Row { def = def, go = go, count = count };

                if (count > 1) r.status = Status.Duplicate;
                else if (go == null)
                {
                    int legacyCount = 0;
                    if (!string.IsNullOrEmpty(def.legacyName))
                        KD_SceneObjects.Find(def.legacyName, out legacyCount);
                    r.status = legacyCount > 0 ? Status.Legacy : Status.Missing;
                }
                else
                {
                    var target = FindContainer(def.containerPath);
                    r.status = (target != null && go.transform.parent == target)
                        ? Status.Ok : Status.Misplaced;
                }
                r.currentPath = go == null ? "(無い)" : KD_SceneObjects.PathOf(go);
                rows.Add(r);
            }
            return rows;
        }

        static void AppendRows(StringBuilder log, List<Row> rows)
        {
            log.AppendLine("== 配置表 (" + rows.Count + " 行) ==");
            string lastContainer = null;
            foreach (var r in rows)
            {
                if (r.def.containerPath != lastContainer)
                {
                    log.AppendLine("-- " + r.def.containerPath);
                    lastContainer = r.def.containerPath;
                }
                string tag = r.status == Status.Ok ? "ok       "
                    : r.status == Status.Misplaced ? "misplaced"
                    : r.status == Status.Missing ? (r.def.optional ? "--       " : "missing  ")
                    : r.status == Status.Legacy ? "legacy   " : "duplicate";
                log.AppendLine("  " + tag + "  " + r.def.name +
                    (r.status == Status.Ok ? ""
                        : r.status == Status.Missing && r.def.optional ? "   [optional — 今は無い]"
                        : "   [現在 " + r.currentPath +
                        (r.count > 1 ? " ほか計 " + r.count + " 個" : "") + "]"));
            }
        }

        /// <summary>表に載っていないルート直下の `KD_*` を列挙する。**取りこぼし防止。**</summary>
        static int AppendOrphans(StringBuilder log)
        {
            var known = new HashSet<string>();
            foreach (var def in KD_DogDefs.LayoutTable)
            {
                known.Add(def.name);
                if (!string.IsNullOrEmpty(def.legacyName)) known.Add(def.legacyName);
            }
            foreach (var p in KD_DogDefs.ContainerPaths)
            {
                var seg = p.Split('/');
                known.Add(seg[seg.Length - 1]);
            }

            var orphans = new List<string>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!root.name.StartsWith("KD_")) continue;
                if (known.Contains(root.name)) continue;
                orphans.Add(root.name);
            }

            log.AppendLine();
            log.AppendLine("== 表に載っていないルート直下の KD_* (orphanKD) ==");
            if (orphans.Count == 0) log.AppendLine("  (無し)");
            else foreach (var o in orphans)
                log.AppendLine("  " + o + "  ← LayoutTable へ足すか、置き場所を決めること");
            return orphans.Count;
        }

        static void AppendCounts(StringBuilder log, List<Row> rows)
        {
            var scene = EditorSceneManager.GetActiveScene();
            log.AppendLine();
            log.AppendLine("sceneRoots=" + scene.rootCount + " layoutRows=" + rows.Count);
        }

        static Transform FindContainer(string path)
        {
            Transform cur = null;
            foreach (var seg in path.Split('/'))
            {
                if (cur == null)
                {
                    foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                        if (root.name == seg) { cur = root.transform; break; }
                    if (cur == null) return null;
                }
                else
                {
                    cur = cur.Find(seg);
                    if (cur == null) return null;
                }
            }
            return cur;
        }

        /// <summary>`VRCSceneDescriptor.NetworkIDCollection` の件数と破損参照。
        /// **削除・生成をしていない証拠**として毎回数える
        /// (`vrc-scene-edit-safety`: 消したオブジェクトは `{fileID: 0}` の死骸を残す)。</summary>
        static void CountNetworkIds(out int total, out int broken)
        {
            total = 0; broken = 0;
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (desc == null || desc.NetworkIDCollection == null) return;
            foreach (var e in desc.NetworkIDCollection)
            {
                total++;
                if (e == null || e.gameObject == null) broken++;
            }
        }

        static void Finish(string path, StringBuilder log)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(path, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
