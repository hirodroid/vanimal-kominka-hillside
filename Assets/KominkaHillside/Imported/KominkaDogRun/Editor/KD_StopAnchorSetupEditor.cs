using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-NET-003: 停止アンカー (KD_DogStopAnchor) のセットアップと検証。
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    ///   1 - Create Program Asset → force refresh
    ///   2 - Setup Stop Anchor
    ///   3 - Verify Stop Anchor
    ///   4 - Add Debug Display   (実機 2 人セッション用の板 + `[KDNET]` ログ。**済んだら 5 で外す**)
    ///   5 - Remove Debug Display (板とログの両方)
    ///
    /// 前提: Fetch System / Wander System / Pet System が構築済み。
    ///
    /// **犬ごとに犬ユニット (`KD_DogUnit_<key>`) の中へ `KD_StopAnchor_<key>` を作る。**
    /// dogRoot の子にしないのは、dogRoot が購入アセットのプレハブインスタンスで、
    /// 子を足すとプレハブ差分になるため (「Unpack を要求しない」不変条件)。
    /// Fetch (Manual) と同居させないのは、**Manual が Behaviour 単位で一括
    /// シリアライズする**ため — 相乗りすると petState の変化のたびに
    /// 古い停止標本まで再放送される (DES-NET-001 §8 の少数意見)。
    ///
    /// **Ownership は触らない。** シーン配置の既定 (インスタンスマスター) のままでよく、
    /// Setup も実行時コードも SetOwner を一度も呼ばない — 決定論シムは全クライアント
    /// 等価なので、正本が誰であっても成立する。
    /// </summary>
    public static class KD_StopAnchorSetupEditor
    {
        const string ScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogStopAnchor.cs";
        const string ProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogStopAnchor.asset";
        const string DebugTextName = "KD_StopAnchorDebugText";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Stop Anchor/1 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_StopAnchorSetup] Create program asset");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) != null)
            {
                log.AppendLine("already exists: " + ProgramPath);
            }
            else
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath);
                if (script == null)
                {
                    log.AppendLine("FAIL script not found: " + ScriptPath);
                    Finish(log);
                    return;
                }
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                AssetDatabase.CreateAsset(asset, ProgramPath);
                AssetDatabase.SaveAssets();
                log.AppendLine("created: " + ProgramPath);
            }
            log.AppendLine("次: force refresh してから 2 - Setup Stop Anchor");
            Finish(log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Stop Anchor/2 - Setup Stop Anchor")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_StopAnchorSetup] Setup");
            bool copyOk = true;
            int done = 0;

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("========= " + def.displayName + " =========");
                int dogCount, fetchCount, wanderCount, found;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out fetchCount);
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out wanderCount);
                if (dogCount != 1 || fetchCount != 1 || wanderCount != 1)
                {
                    log.AppendLine($"   FAIL {def.DogName}={dogCount} / {def.FetchSystemName}={fetchCount}"
                        + $" / {def.WanderSystemName}={wanderCount} (それぞれ 1 個が前提)");
                    copyOk = false;
                    continue;
                }
                var fetch = fetchGo.GetUdonSharpComponent<KD_DogFetchController>();
                var wander = wanderGo.GetUdonSharpComponent<KD_DogWanderController>();
                if (fetch == null || wander == null)
                {
                    log.AppendLine("   FAIL Fetch / Wander のコントローラが取れない");
                    copyOk = false;
                    continue;
                }
                int petCount;
                var petGo = KD_SceneObjects.Find(def.PetSystemName, out petCount);
                var pet = petGo == null ? null : petGo.GetUdonSharpComponent<KD_DogPetController>();
                if (pet == null)
                    log.AppendLine("   WARN " + def.PetSystemName + " が無い - "
                        + "撫でている最中でも補正が走る (虚無撫では塞がるが、手から犬が離れうる)");

                var sysGo = KD_SceneObjects.Find(def.StopAnchorName, out found);
                if (found > 1)
                {
                    log.AppendLine($"   FAIL '{def.StopAnchorName}' がシーン内に {found} 個ある");
                    copyOk = false;
                    continue;
                }
                if (sysGo == null)
                {
                    sysGo = new GameObject(def.StopAnchorName);
                    KD_SceneObjects.PlaceIfNew(sysGo, def.UnitPath);   // IMP-INF-003: 新規のときだけ犬ユニットへ
                    sysGo.transform.position = dog.transform.position;
                    log.AppendLine("   created " + def.StopAnchorName);
                }

                var proxy = sysGo.GetUdonSharpComponent<KD_DogStopAnchor>();
                if (proxy == null)
                {
                    proxy = sysGo.AddUdonSharpComponent<KD_DogStopAnchor>();
                    log.AppendLine("   added KD_DogStopAnchor");
                }

                // 上書きするのは配線と「Fetch を正本とする値」だけ。
                // しきい値 (slideDistance / snapDistance / minSendInterval) の手調整は保持する —
                // **実機で頻度を見て詰める値**なので、Setup が毎回踏み潰してはいけない
                proxy.dogRoot = dog.transform;
                proxy.fetchController = fetch;
                proxy.wanderController = wander;
                proxy.petController = pet;
                // 「止まった」の定義は犬の足と同じでなければならない —
                // 別値だと「Wander は止まったつもり / アンカーはまだ動いている」がずれる
                proxy.stopSpeedThreshold = wander.stopSpeedThreshold;

                copyOk &= TryCopy(proxy, log);
                done++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine($"SETUP RESULT: {(copyOk ? "PASS" : "FAIL")} ({done} 匹)");
            log.AppendLine("**シーンは保存していない。** 確認してから Ctrl+S");
            Finish(log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Stop Anchor/3 - Verify Stop Anchor")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_StopAnchorSetup] Verify");
            int fail = 0;

            // IMP-INF-003: 犬の Transform はループの外で 1 回だけ引く
            // (IsUnderAnyDog へ毎回渡さないとシーンを犬の数だけ歩き直す)
            var dogTransforms = KD_SceneObjects.AllDogTransforms();

            foreach (var def in KD_DogDefs.All)
            {
                string tag = def.key + ": ";
                int found;
                var sysGo = KD_SceneObjects.Find(def.StopAnchorName, out found);
                if (!Check(ref fail, log, found == 1, tag + def.StopAnchorName + " がシーン内に 1 個")) continue;

                var a = sysGo.GetUdonSharpComponent<KD_DogStopAnchor>();
                if (!Check(ref fail, log, a != null, tag + "KD_DogStopAnchor が付いている")) continue;

                int dogCount, wanderCount;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out wanderCount);
                var wander = wanderGo == null ? null : wanderGo.GetUdonSharpComponent<KD_DogWanderController>();

                Check(ref fail, log, a.dogRoot != null && dog != null && a.dogRoot == dog.transform,
                    tag + "dogRoot == " + def.DogName);
                Check(ref fail, log, a.fetchController != null, tag + "fetchController 配線");
                Check(ref fail, log, a.wanderController != null && a.wanderController == wander,
                    tag + "wanderController == " + def.WanderSystemName + " (滑り寄せの足)");
                Check(ref fail, log, a.petController != null,
                    tag + "petController 配線 (撫で中に犬を引き剥がさないため)");

                // **同居していないこと。** Manual の一括シリアライズで古い標本が
                // 再放送される構図を、構造として禁じる (DES-NET-001 §8)
                Check(ref fail, log, sysGo.GetUdonSharpComponent<KD_DogFetchController>() == null,
                    tag + "Fetch と同居していない (Manual の一括シリアライズを避ける)");
                // IMP-INF-003: 旧 `parent == null` の書き換え。
                // **括弧の中が本題だった** — 守りたかったのは「シーン直下であること」ではなく
                // 「購入プレハブへ子を足していないこと」。兄弟の 5 メニュー
                // (Fetch / Pet / Wander / Photo / DogPack) が既に使っている判定へそろえた
                Check(ref fail, log, !PrefabUtility.IsPartOfPrefabInstance(sysGo),
                    tag + "プレハブインスタンスの一部でない (購入プレハブへ子を足さない)");
                Check(ref fail, log, !KD_SceneObjects.IsUnderAnyDog(sysGo.transform, dogTransforms),
                    tag + "どの犬の子孫でもない");
                string anchorOffender;
                Check(ref fail, log, KD_SceneObjects.HasStaticAncestry(sysGo, out anchorOffender),
                    tag + "先祖が動かないコンテナだけ" +
                        (anchorOffender == null ? "" : " (" + anchorOffender + ")"));

                // 「止まった」の定義が犬の足と一致していること
                Check(ref fail, log, wander != null
                        && Mathf.Approximately(a.stopSpeedThreshold, wander.stopSpeedThreshold),
                    tag + "stopSpeedThreshold == Wander の値 (期待 "
                        + (wander != null ? wander.stopSpeedThreshold.ToString("F3") : "?")
                        + " / 実際 " + a.stopSpeedThreshold.ToString("F3") + ")");

                // しきい値の大小 (ランタイムにも同じ補正があるが、Setup 段階で気付ける方がよい)
                Check(ref fail, log, a.snapDistance > a.slideDistance,
                    tag + "snapDistance > slideDistance (滑って寄せる帯が要る)");
                Check(ref fail, log, a.slideDistance > 0.01f && a.minSendInterval > 0f
                        && a.stopSpeedThreshold > 0f,
                    tag + "しきい値が 0 でない");

                // 計器は既定で未配線 (付いていても FAIL にはしない — 4 で付ける運用)
                if (a.debugText != null)
                    log.AppendLine("  NOTE " + tag + "計器が付いたまま。**公開前に 5 - Remove Debug Display**");
                // ログは板と違って**見た目に出ない**ので、消し忘れは目視で気付けない。
                // ここで名指ししておく (公開ビルドで毎停止 Debug.Log を叩き続けることになる)
                if (a.logToConsole)
                    log.AppendLine("  NOTE " + tag + "[KDNET] ログが有効なまま。**公開前に 5 - Remove Debug Display**");
            }

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail} checks)");
            Finish(log);
        }

        // ================================================================ 計器 (§5)
        // **実機 2 人セッションで読む必要がある** ので、Editor 専用の頭上 HUD
        // (KD_DogAnimDebugHud / KD_DogPlayDebugHud) では届かない — 素の MonoBehaviour は
        // アップロードしたワールドでは動かない。ワールドに置く TextMeshPro にしてある。
        // 着脱は KD_PetSetupEditor の「4 - Add Debug Display / 5 - Remove Debug Display」と同じ作法。

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Stop Anchor/4 - Add Debug Display")]
        public static void AddDebugDisplay()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_StopAnchorSetup] Add debug display");
            bool ok = true;
            foreach (var def in KD_DogDefs.All)
            {
                int found;
                var sysGo = KD_SceneObjects.Find(def.StopAnchorName, out found);
                if (found != 1) { log.AppendLine("  skip " + def.key + " (アンカーが無い)"); continue; }
                var a = sysGo.GetUdonSharpComponent<KD_DogStopAnchor>();
                if (a == null) { log.AppendLine("  skip " + def.key); continue; }

                var t = sysGo.transform.Find(DebugTextName);
                GameObject textGo;
                if (t == null)
                {
                    textGo = new GameObject(DebugTextName);
                    textGo.transform.SetParent(sysGo.transform, false);
                    var tmp = textGo.AddComponent<TMPro.TextMeshPro>();
                    tmp.text = "-";
                    tmp.fontSize = 1.2f;
                    tmp.alignment = TMPro.TextAlignmentOptions.Center;
                    var rt = textGo.GetComponent<RectTransform>();
                    if (rt != null) rt.sizeDelta = new Vector2(1.4f, 0.7f);
                    log.AppendLine("  created " + def.key + "/" + DebugTextName);
                }
                else
                {
                    textGo = t.gameObject;
                    log.AppendLine("  reused " + def.key + "/" + DebugTextName);
                }
                a.debugText = textGo.GetComponent<TMPro.TextMeshPro>();
                a.debugTextTransform = textGo.transform;
                // 板とログは同じスイッチで着脱する。**別々にすると 5 で板だけ外れて
                // ログが公開ビルドに残る** (板と違って残っても見た目には出ないので気付けない)
                a.logToConsole = true;
                ok &= TryCopy(a, log);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("ADD RESULT: " + (ok ? "PASS" : "FAIL"));
            log.AppendLine("板 + ログ (`[KDNET]`) の両方を有効にした。");
            log.AppendLine("**VRChat の起動オプションへ `--enable-udon-debug-logging` を入れること** "
                + "(無いと Udon の Debug.Log は 1 行も残らない)。");
            log.AppendLine("読み方: node scripts/parse-net-log.js <output_log_*.txt>");
            log.AppendLine("**計測が済んだら 5 - Remove Debug Display で必ず外すこと。**");
            Finish(log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Stop Anchor/5 - Remove Debug Display")]
        public static void RemoveDebugDisplay()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_StopAnchorSetup] Remove debug display");
            bool ok = true;
            foreach (var def in KD_DogDefs.All)
            {
                int found;
                var sysGo = KD_SceneObjects.Find(def.StopAnchorName, out found);
                if (found != 1) continue;
                var a = sysGo.GetUdonSharpComponent<KD_DogStopAnchor>();
                if (a != null)
                {
                    a.debugText = null;
                    a.debugTextTransform = null;
                    a.logToConsole = false;
                    ok &= TryCopy(a, log);
                }
                var t = sysGo.transform.Find(DebugTextName);
                if (t != null)
                {
                    Object.DestroyImmediate(t.gameObject);
                    log.AppendLine("  removed " + def.key + "/" + DebugTextName);
                }
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("REMOVE RESULT: " + (ok ? "PASS" : "FAIL"));
            Finish(log);
        }

        // ---- helpers (他の Setup Editor と同型) ----

        static bool Check(ref int fail, StringBuilder log, bool ok, string label)
        {
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + label);
            if (!ok) fail++;
            return ok;
        }

        static bool TryCopy(UdonSharpBehaviour proxy, StringBuilder log)
        {
            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                EditorUtility.SetDirty(proxy);
                return true;
            }
            catch (System.Exception e)
            {
                log.AppendLine("CopyProxyToUdon FAILED (" + proxy.GetType().Name + "): " + e.Message);
                return false;
            }
        }


        static void Finish(StringBuilder log)
        {
            const string file = "Logs/imp-net-003-stop-anchor.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
