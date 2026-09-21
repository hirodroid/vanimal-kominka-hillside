using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// フリスビーの ClientSim 自動テスト台 (IMP-DOG-008 の繰り返し確認用)。
    ///
    /// 何をするか: プロトタイプシーン (KH_FrisbeePrototype) を単独で開き、Play へ入り、
    /// 固定投げ台 (KH_FrisbeeStand) の Interact を 6 プリセット x 4 投げ分だけ叩き、
    /// 毎回 [KH_Frisbee*] のコンソール行から成否を分類して JSON に残す。
    ///
    /// 原則 (古民家ドッグランの KD_ClientSimHarnessEditor と同じ):
    ///  - 入口は固定 (メニュー 1 つ)。終了も固定 (完了・失敗・停止・時間切れのどれでも Edit モードへ戻る)。
    ///  - シーンは保存しない。保存シーンへコンポーネントを足さない。Editor アセンブリだけで動く。
    ///  - 結果はファイル。`START_REQUEST_ACCEPTED` は受付であって PASS ではない。
    ///  - 実行時の値は **ランタイムの UdonBehaviour** から読む (Play 中の U# プロキシは信用しない)。
    /// </summary>
    [InitializeOnLoad]
    public static class KH_FrisbeeHarnessEditor
    {
        const string Menu = "Tools/KominkaHillside/Frisbee/";
        const string Base = "Logs/kh-frisbee-harness";
        const string Journal = Base + "/journal.json";
        const string StopFlag = Base + "/stop.flag";
        const string OptionsPath = Base + "/options.json";
        const string Key = "KH.Frisbee.Harness.Run";

        // シーン内のオブジェクト名 (KH_FrisbeePrototypeEditor.BuildInto が作る名前)
        const string CatcherName = "KH_FrisbeeCatch_Lab";
        const string ThrowerName = "KH_FrisbeeStand";
        const string RecallName = "KH_FrisbeeRecall";
        const string CyclerName = "KH_FrisbeeVariant";

        const int StateIdle = 0, StateReady = 10;
        const int VariantCount = 6;
        // 全体の番犬。下限 240 秒 (設計値)。24 投げの全面実行は 1 投げ 10 秒 + 片付けで 240 秒に収まらないので、
        // 計画した投げ数に応じて伸ばす。options.json の globalTimeoutSeconds で上書きできる。
        const double MinBudget = 240.0;
        const double BudgetPerThrow = 20.0;
        const double ThrowBudget = 12.0;      // 1 投げ (投げてから片付けるまで) の上限 (秒)
        const double ThrowSettle = 10.0;      // 投げてから Idle へ戻るのを待つ上限 (秒)
        // 動きの質のしきい値は捕り手側の定数をそのまま使う (2 か所に数字を置かない)。
        const float MotionMaxYawRate = KH_FrisbeeCatch.MotionMaxYawRate;
        const float MotionMinSpeed = KH_FrisbeeCatch.MotionMinSpeed;
        const float MotionWindow = KH_FrisbeeCatch.MotionWindow;
        const float MotionMinPath = KH_FrisbeeCatch.MotionMinPath;

        // ---------------------------------------------------------------- 投げ表
        struct Shot { public string name; public float speed, elevation; public string[] allowed; public string note; }

        static readonly Shot[] Shots =
        {
            new Shot { name = "flat", speed = 7.5f, elevation = 10f, allowed = new[] { "caught" },
                note = "基準の投げ。跳んで取るか、走り込んで取る" },
            // ClientSim の Desktop 投げ (5.6-6.2 m/s・低い仰角、犬は 6.5-7.5 m 前) を再現する投げ。
            // ひねり (v4) が跳ばずに立って取る / 地上回収へ落ちる回帰は、この幾何でだけ出た。
            new Shot { name = "mid",  speed = 5.8f, elevation = 8f,  allowed = new[] { "caught" },
                note = "ClientSim の Desktop 投げの再現。低く短い。走り込んで跳べるか" },
            new Shot { name = "lob",  speed = 6f,   elevation = 35f, allowed = new[] { "caught" },
                note = "山なり。滞空が長いので先回りできるはず" },
            new Shot { name = "low",  speed = 5f,   elevation = 0f,  allowed = new[] { "caught", "ground" },
                note = "水平・低速。犬の手前に落ちるので、立ったまま取る / 落ちたものを拾いに行く のどちらも想定内" },
            new Shot { name = "far",  speed = 9f,   elevation = 15f, allowed = new[] { "caught" },
                note = "遠投。走って追いつけるかどうか" },
        };

        static readonly string[] NotCovered =
        {
            "Ready (持ち手が構えている間の待機位置)。プロトタイプに持ち手がいないので Ready へ入らず、走り込み系プリセット v1..v3 は待機位置の違いが出ない = ここでは v0 と同じ投げになる",
            "VR の手投げ・狙い (aim line / marker / 広場外の警告) は一切通らない。固定投げ台の +Z 方向の投げだけ",
            "Interact は UdonBehaviour.Interact() を直接呼ぶので、実際のレイキャスト・距離 (proximity)・InteractionText の当たり判定は通っていない",
            "他プレイヤー、Owner 移譲、同期 (単一 ClientSim クライアント)",
            "体験としての見え方 (間・迫力・不自然さ) は人の Play でしか判定できない",
            "実機ビルド (PCVR / Quest / iOS) と実クライアント",
        };

        // ---------------------------------------------------------------- レポート
        [Serializable] public class Options { public double globalTimeoutSeconds; public string variants = ""; public string shots = ""; }

        [Serializable]
        public class ThrowResult
        {
            public int index;
            public int variant;
            public int variantApplied = -1;
            public string shot;
            public float speed, elevationDeg;
            public string outcome = "none";      // caught / miss / ground / none / timeout / blocked
            public string[] allowed;
            public bool ok;
            // 動きの質 (IMP-DOG-008 round 4)。取れたかどうかとは別の合否。
            // mode = hook / run / stand / overtake / "" (跳ばず取らずに終わった投げ)。
            public string mode = "";
            public float yawRateMax = -1f, speedMin = -1f, vTake = -1f, approachPath = -1f;
            public bool motionOk = true;
            public string motionNote = "no takeoff / catch line: motion not judged";
            public double seconds;
            public int endState = -1;
            public bool reachedIdle;
            public string dogStart, launchLine, planLine, takeoffLine, outcomeLine, note;
            public List<string> lines = new List<string>();
        }

        [Serializable]
        public class VariantSummary
        {
            public int variant;
            public int caught, miss, ground, none, other;
        }

        [Serializable]
        public class Report
        {
            public string runId, status = "RUNNING", failure;
            public string unity, sdk, scene, previousScene, sceneHashBefore, sceneHashAfter;
            public string started, ended, revision, dirty, sourceManifest;
            public bool sceneDirtyBefore, sceneDirtyAfterPlay, sceneFileUnchanged;
            public double deadline, budgetSeconds;
            public int throwsPlanned, throwsRun, throwsOk;
            public string injection = "毎回、犬をシーンの立ち位置へ戻してから投げる (円盤の recall だけでは犬が戻らないため)。それ以外の注入はない";
            public string scope = "実行した投げだけの結果。notCovered は PASS でも NOT_TESTED のまま";
            public string buildTest = "NOT_TESTED", humanExperience = "NOT_TESTED";
            public string[] notCovered = NotCovered;
            public List<ThrowResult> throwResults = new List<ThrowResult>();
            public List<VariantSummary> perVariant = new List<VariantSummary>();
            public List<string> errors = new List<string>();
            public List<string> trace = new List<string>();
        }

        static Report report;
        static IEnumerator routine;
        static int lastFrame = -1;
        static bool ending;
        static bool closed;          // RestoreScene が journal を消した後 (もう書き戻さない)
        static bool pendingRestore;  // Play を抜けたので、あとはシーンを戻して記録を閉じるだけ
        static double throwDeadline;
        static double nextStopPoll;
        static readonly List<string> Lines = new List<string>();
        static UdonBehaviour catcher, thrower, recaller, cycler;
        static Transform dogRoot;
        static Vector3 dogHome;
        static Quaternion dogHomeRot;
        static string RunDir { get { return Base + "/" + report.runId; } }

        static KH_FrisbeeHarnessEditor()
        {
            EditorApplication.playModeStateChanged += Mode;
            EditorApplication.update += Tick;
            if (!string.IsNullOrEmpty(SessionState.GetString(Key, "")) && File.Exists(Journal))
            {
                report = JsonUtility.FromJson<Report>(File.ReadAllText(Journal));
                // ended が入っている journal は「判定は済んでいて、あとはシーンを戻すだけ」の状態。
                pendingRestore = SessionState.GetBool(Key + ".restore", false) || !string.IsNullOrEmpty(report.ended);
                Configure();
            }
        }

        static void Configure()
        {
            Application.logMessageReceived -= Log;
            Application.logMessageReceived += Log;
            ClientSimRuntimeLoader.BeginUnityTesting(new ClientSimSettings
            {
                enableClientSim = true,
                hideMenuOnLaunch = true,
                displayLogs = false,
                customLocalPlayerName = "KH Frisbee Harness",
                localPlayerIsMaster = true,
                initializationDelay = 0f,
                setTargetFrameRate = true,
                targetFrameRate = 60,
                spawnPlayer = true,
            });
        }

        // ---------------------------------------------------------------- 入口 / 出口
        [MenuItem(Menu + "7 Harness (ClientSim)")]
        public static void StartRun()
        {
            try { Begin(); }
            catch (Exception e)
            {
                if (report != null && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SessionState.EraseString(Key);
                    SessionState.EraseString(Key + ".options");
                    if (File.Exists(Journal)) File.Delete(Journal);
                    report = null;
                }
                Blocked(e.Message);
                Debug.LogError("[KH Frisbee Harness] BLOCKED " + e.Message);
            }
        }

        [MenuItem(Menu + "8 Harness Stop")]
        public static void StopRun()
        {
            if (report == null)
            {
                // クラッシュ等で journal だけが残っている場合の後始末 (Edit モードでのみ)。
                if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Journal))
                {
                    var stale = JsonUtility.FromJson<Report>(File.ReadAllText(Journal));
                    stale.status = "BLOCKED"; stale.failure = "Interrupted run closed by an explicit stop";
                    stale.ended = DateTime.Now.ToString("o");
                    string json = JsonUtility.ToJson(stale, true);
                    Directory.CreateDirectory(Base + "/" + stale.runId);
                    File.WriteAllText(Base + "/" + stale.runId + "/report.json", json);
                    File.WriteAllText(Base + "/latest.json", json);
                    File.Delete(Journal);
                    SessionState.EraseString(Key);
                    Debug.Log("[KH Frisbee Harness] closed a stale journal as BLOCKED");
                    return;
                }
                Debug.Log("[KH Frisbee Harness] no run in progress");
                return;
            }
            report.status = "BLOCKED";
            report.failure = "Explicit stop";
            StopInternal();
        }

        static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Harness needs an idle Edit mode (playing / compiling / building).");
            if (EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Compile errors are present; fix them first.");
            if (File.Exists(Journal))
                throw new InvalidOperationException("A previous run did not finish. Inspect " + Journal + " and delete it once the editor is back in Edit mode.");
            string proto = KH_FrisbeePrototypeEditor.ScenePath;
            if (!File.Exists(proto))
                throw new InvalidOperationException("Prototype scene missing: " + proto + " (run Frisbee/4 Build Scene).");

            // 開いているシーンは保存も破棄もしない。未保存なら何もせず BLOCKED。
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isDirty)
                    throw new InvalidOperationException("Open scene has unsaved changes: " + (string.IsNullOrEmpty(s.path) ? s.name : s.path) + ". Save or discard it yourself first.");
            }
            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path))
                throw new InvalidOperationException("The active scene has never been saved; the harness could not put it back.");

            Directory.CreateDirectory(Base);
            if (File.Exists(StopFlag)) File.Delete(StopFlag);
            var options = ReadOptions();
            int planned = Variants(options).Length * Plan(options).Length;

            report = new Report
            {
                runId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                unity = Application.unityVersion,
                sdk = SdkVersion(),
                scene = proto,
                previousScene = active.path,
                sceneHashBefore = HashFile(proto),
                started = DateTime.Now.ToString("o"),
                revision = Git("rev-parse HEAD"),
                dirty = Git("status --porcelain"),
                sourceManifest = HashDirectory("Assets/KominkaHillside/Scripts"),
                sceneDirtyBefore = false,
                throwsPlanned = planned,
                budgetSeconds = options.globalTimeoutSeconds > 0
                    ? options.globalTimeoutSeconds
                    : Math.Max(MinBudget, planned * BudgetPerThrow + 30.0),
            };
            report.deadline = EditorApplication.timeSinceStartup + report.budgetSeconds;
            report.trace.Add("variants=" + string.Join(",", Variants(options).Select(v => v.ToString()).ToArray()) +
                " shots=" + string.Join(",", Plan(options).Select(s => s.name).ToArray()) +
                " budget=" + report.budgetSeconds + "s");
            SessionState.SetString(Key, report.runId);
            SessionState.SetString(Key + ".options", JsonUtility.ToJson(options));
            Directory.CreateDirectory(RunDir);
            Save();

            try
            {
                if (active.path != proto || SceneManager.sceneCount != 1)
                    EditorSceneManager.OpenScene(proto, OpenSceneMode.Single);
                Lines.Clear();
                ending = false;
                Configure();
                EditorApplication.EnterPlaymode();
            }
            catch
            {
                // Play へ入る前に転んだときは、開いたままにせず記録を閉じてから投げ直す。
                SessionState.EraseString(Key);
                SessionState.EraseString(Key + ".options");
                if (File.Exists(Journal)) File.Delete(Journal);
                report = null;
                throw;
            }
        }

        static void Mode(PlayModeStateChange state)
        {
            if (report == null || string.IsNullOrEmpty(SessionState.GetString(Key, ""))) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { routine = Suite(); lastFrame = -1; }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                if (report.status == "RUNNING")
                {
                    report.status = "BLOCKED";
                    // 自分で止めたなら理由は入っている。空なら外から止まった
                    // (手動 Stop / スクリプト再コンパイル / Unity 側の都合)。
                    Why("Play ended before the suite finished (external Stop, a script recompile, or Unity itself). compiling=" +
                        EditorApplication.isCompiling + " throws=" + report.throwResults.Count + "/" + report.throwsPlanned);
                }
                Save();
            }
            if (state == PlayModeStateChange.EnteredEditMode) Finish();
        }

        static void Tick()
        {
            if (pendingRestore)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                pendingRestore = false;
                SessionState.SetBool(Key + ".restore", false);
                RestoreScene();
                return;
            }
            if (report == null || ending || string.IsNullOrEmpty(SessionState.GetString(Key, ""))) return;

            if (EditorApplication.timeSinceStartup > nextStopPoll)
            {
                nextStopPoll = EditorApplication.timeSinceStartup + 0.5;
                if (File.Exists(StopFlag))
                {
                    try { File.Delete(StopFlag); } catch { }
                    report.status = "BLOCKED"; report.failure = "Stop requested by the runner";
                    StopInternal(); return;
                }
            }
            if (EditorApplication.timeSinceStartup > report.deadline ||
                (throwDeadline > 0 && EditorApplication.timeSinceStartup > throwDeadline + 5) ||
                (EditorApplication.isPlaying && EditorApplication.isPaused))
            {
                report.status = "BLOCKED";
                report.failure = EditorApplication.isPaused ? "Editor paused; no automatic resume"
                    : EditorApplication.timeSinceStartup > report.deadline ? "Global watchdog (" + report.budgetSeconds + "s)"
                    : "One throw did not finish inside its budget";
                StopInternal(); return;
            }
            // Editor の MonoBehaviour を置かずに進める: ゲームのフレームが 1 つ進むごとに 1 歩。
            if (EditorApplication.isPlaying && routine != null && Time.frameCount != lastFrame)
            {
                lastFrame = Time.frameCount;
                Step();
            }
        }

        static void Step()
        {
            try
            {
                if (!routine.MoveNext())
                {
                    Summarise();
                    report.status = report.errors.Count == 0
                        && report.throwResults.Count == report.throwsPlanned
                        && report.throwsOk == report.throwResults.Count
                        && report.throwResults.Count > 0 ? "PASS" : "FAIL";
                    StopInternal();
                }
            }
            catch (Exception e)
            {
                Summarise();
                report.status = e is TimeoutException ? "BLOCKED" : "FAIL";
                report.failure = e.ToString();
                StopInternal();
            }
        }

        static void StopInternal()
        {
            routine = null;
            if (!ending && EditorApplication.isPlaying)
            {
                // Udon を先に止めてから Play を抜ける (終了順で SDK 側が先に消えるのを避ける)。
                foreach (var u in Resources.FindObjectsOfTypeAll<UdonBehaviour>())
                    if (u != null && u.gameObject.scene.IsValid() && u.enabled) u.enabled = false;
            }
            ending = true;
            Save();
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
            else Finish();
        }

        static void Finish()
        {
            try
            {
                ClientSimRuntimeLoader.EndUnityTesting();
                Application.logMessageReceived -= Log;
                Summarise();
                if (report.status == "RUNNING") { report.status = "BLOCKED"; Why("Run ended without a verdict"); }
                if (report.errors.Count > 0 && report.status == "PASS") report.status = "FAIL";
                report.ended = DateTime.Now.ToString("o");
                Save();
            }
            catch (Exception e)
            {
                report.status = "BLOCKED";
                report.failure += "\nFinish: " + e;
                Save();
            }
            // シーンの復帰は playModeStateChanged の中では行わない。EditorApplication.update (Tick) へ回す。
            // delayCall は使わない: Play 退出まわりのドメイン再読み込みで登録が消え、シーンが戻らないまま
            // journal が残る状態を実測した (2026-09-21)。update は [InitializeOnLoad] が毎回つなぎ直し、
            // 予約自体も SessionState に置くので、再読み込みをまたいでも必ず片付く。
            pendingRestore = true;
            SessionState.SetBool(Key + ".restore", true);
        }

        static void RestoreScene()
        {
            if (report == null) return;
            try
            {
                var active = SceneManager.GetActiveScene();
                report.sceneDirtyAfterPlay = active.isDirty;
                // Play へ入るだけで SDK が NetworkID を復旧し、シーンが dirty になる。保存はしない。
                // EditorSceneManager.OpenScene は確認を出さずに未保存の変更を捨てて開き直すので、
                // 「元のシーンへ戻す」と「Play が付けた dirty を捨てる」を同じ 1 回で済ませる。
                // 実行前は全シーンが未変更であることを確認済み (Begin)。ここで捨てるのは Play 由来の差分だけ。
                if (!string.IsNullOrEmpty(report.previousScene) && File.Exists(report.previousScene) &&
                    (active.path != report.previousScene || active.isDirty))
                    EditorSceneManager.OpenScene(report.previousScene, OpenSceneMode.Single);
                report.sceneHashAfter = File.Exists(report.scene) ? HashFile(report.scene) : "MISSING";
                report.sceneFileUnchanged = report.sceneHashAfter == report.sceneHashBefore;
                if (!report.sceneFileUnchanged)
                {
                    report.errors.Add("Prototype scene file changed during the run: " + report.scene);
                    if (report.status == "PASS") report.status = "FAIL";
                }
                report.trace.Add("scene restored to " + SceneManager.GetActiveScene().path +
                    " (dirty after play=" + report.sceneDirtyAfterPlay + ", reopened from disk without saving)");
                closed = true;
                Save();
                Debug.Log("[KH Frisbee Harness] " + report.status + " " + RunDir + "/report.json");
                if (File.Exists(Journal)) File.Delete(Journal);
                SessionState.EraseString(Key);
                SessionState.EraseString(Key + ".options");
                SessionState.SetBool(Key + ".restore", false);
                report = null; ending = false; closed = false; routine = null; throwDeadline = 0;
                catcher = thrower = recaller = cycler = null;
                Lines.Clear();
            }
            catch (Exception e)
            {
                report.status = "BLOCKED";
                report.failure += "\nScene restore: " + e;
                Save();
                Debug.LogError("[KH Frisbee Harness] scene restore failed; the journal is kept. " + e.Message);
            }
        }

        static void Blocked(string why)
        {
            Directory.CreateDirectory(Base);
            var r = new Report
            {
                runId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-blocked",
                status = "BLOCKED",
                failure = why,
                unity = Application.unityVersion,
                started = DateTime.Now.ToString("o"),
                ended = DateTime.Now.ToString("o"),
            };
            Directory.CreateDirectory(Base + "/" + r.runId);
            string json = JsonUtility.ToJson(r, true);
            File.WriteAllText(Base + "/" + r.runId + "/report.json", json);
            File.WriteAllText(Base + "/latest.json", json);
        }

        // ---------------------------------------------------------------- 本体
        static IEnumerator Suite()
        {
            double until = EditorApplication.timeSinceStartup + 30;
            while (Networking.LocalPlayer == null || LocalPlayer() == null)
            {
                if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("ClientSim local player did not appear within 30 s");
                yield return null;
            }
            report.trace.Add("local player ready: " + Networking.LocalPlayer.displayName);
            var settle = Wait(1.5); while (settle.MoveNext()) yield return null;

            catcher = Find(CatcherName);
            thrower = Find(ThrowerName);
            recaller = Find(RecallName);
            cycler = Find(CyclerName);
            if (!Has(catcher, "state") || !Has(catcher, "variant"))
                throw new InvalidOperationException("KH_FrisbeeCatch no longer exposes 'state' / 'variant'; the harness reads those symbols.");

            // 犬の立ち位置。円盤を戻すだけでは犬は戻らないので (咥えて人の足元まで運んで置く)、
            // 2 投げ目以降が別の距離からの投げになってしまう。毎回この位置へ戻してから投げる。
            object value;
            if (catcher.TryGetProgramVariable("dogRoot", out value)) dogRoot = value as Transform;
            if (dogRoot == null) throw new InvalidOperationException("KH_FrisbeeCatch.dogRoot is not wired; the harness cannot put the dog back.");
            dogHome = dogRoot.position; dogHomeRot = dogRoot.rotation;
            report.trace.Add("dog home = " + dogHome.ToString("F2") + " " + dogHomeRot.eulerAngles.ToString("F0"));

            var options = JsonUtility.FromJson<Options>(SessionState.GetString(Key + ".options", "{}")) ?? new Options();
            var shots = Plan(options);
            int index = 0;
            foreach (int v in Variants(options))
                foreach (var shot in shots)
                {
                    var one = RunThrow(index++, v, shot);
                    while (one.MoveNext()) yield return null;
                    Save();
                }
        }

        static IEnumerator RunThrow(int index, int variant, Shot shot)
        {
            var rec = new ThrowResult
            {
                index = index, variant = variant, shot = shot.name,
                speed = shot.speed, elevationDeg = shot.elevation,
                allowed = shot.allowed, note = shot.note,
            };
            report.throwResults.Add(rec);
            report.throwsRun++;

            // 1. 落ち着くまで待ち、プリセットを合わせる (犬が動いている間は要求が保留されるため)
            var idle = WaitIdle(4); while (idle.MoveNext()) yield return null;
            int guard = 0;
            while (ReadInt(catcher, "variant") != variant && guard++ < VariantCount + 2)
            {
                cycler.Interact();
                var w = Wait(0.35); while (w.MoveNext()) yield return null;
                idle = WaitIdle(2); while (idle.MoveNext()) yield return null;
            }
            rec.variantApplied = ReadInt(catcher, "variant");
            if (rec.variantApplied != variant)
            {
                rec.outcome = "blocked"; rec.ok = false;
                rec.note = "preset " + variant + " could not be selected (stuck on " + rec.variantApplied + ")";
                yield break;
            }

            // 2. 犬を元の立ち位置へ戻す (注入)。これをしないと 2 投げ目以降は犬が人の足元に
            //    いる状態からの投げになり、同じ投げの比較にならない。
            if (dogRoot != null)
            {
                dogRoot.SetPositionAndRotation(dogHome, dogHomeRot);
                yield return null;
                rec.dogStart = dogRoot.position.ToString("F2");
            }
            var settle = Wait(0.3); while (settle.MoveNext()) yield return null;

            // 3. 投げの値を固定投げ台へ書き、Interact で投げる。ここから 1 投げ分の時計を回す
            //    (プリセット合わせは投げそのものではないので、全体の番犬に任せる)。
            double start = EditorApplication.timeSinceStartup;
            throwDeadline = start + ThrowBudget;
            thrower.SetProgramVariable("speed", shot.speed);
            thrower.SetProgramVariable("elevationDeg", shot.elevation);
            thrower.SetProgramVariable("sideDeg", 0f);
            int mark = Lines.Count;
            thrower.Interact();

            // 4. 犬が動き出すのを少し待ってから、Idle へ戻るか 10 秒経つまで
            bool engaged = false;
            double limit = Math.Min(start + ThrowSettle, throwDeadline);
            while (EditorApplication.timeSinceStartup < limit)
            {
                int st = ReadInt(catcher, "state");
                if (st != StateIdle && st != StateReady) engaged = true;
                if (engaged && st == StateIdle) break;
                yield return null;
            }
            rec.seconds = Math.Round(EditorApplication.timeSinceStartup - start, 2);
            rec.endState = ReadInt(catcher, "state");
            rec.reachedIdle = rec.endState == StateIdle;
            for (int i = mark; i < Lines.Count; i++) rec.lines.Add(Lines[i]);
            Classify(rec, engaged);
            JudgeMotion(rec);
            // round 4: 取れたかどうか **と** 動きが機械でないこと の両方で 1 投げの合否を決める。
            rec.ok = shot.allowed.Contains(rec.outcome) && rec.motionOk;
            if (rec.ok) report.throwsOk++;

            // 5. 次の投げのために円盤を台へ戻す (犬の現在の追いかけも終わる)。
            //    片付けは投げの判定が済んだあとなので、1 投げの時計は先に止める。
            throwDeadline = 0;
            recaller.Interact();
            var back = Wait(0.4); while (back.MoveNext()) yield return null;
            idle = WaitIdle(4); while (idle.MoveNext()) yield return null;
        }

        static void Classify(ThrowResult rec, bool engaged)
        {
            string ok = null, miss = null;
            foreach (string l in rec.lines)
            {
                if (l.Contains("catch OK")) ok = l;
                if (l.Contains("catch MISS")) miss = l;
                if (l.Contains("] plan=")) rec.planLine = l;
                if (l.Contains("] takeoff ")) rec.takeoffLine = l;
                if (l.Contains("[KH_Frisbee] launch ")) rec.launchLine = l;
            }
            if (ok != null) { rec.outcome = "caught"; rec.outcomeLine = ok; return; }
            if (miss != null) { rec.outcome = "miss"; rec.outcomeLine = miss; return; }
            if (rec.planLine != null && rec.planLine.Contains("plan=2 ")) { rec.outcome = "ground"; rec.outcomeLine = rec.planLine; return; }
            if (!rec.reachedIdle) { rec.outcome = "timeout"; rec.outcomeLine = "state=" + rec.endState + " when the " + ThrowSettle + " s window ran out"; return; }
            rec.outcome = "none";
            rec.outcomeLine = rec.launchLine == null ? "no launch line: the throw never left the stand"
                : engaged ? "the dog engaged but logged no catch / miss / ground line"
                : "the disc flew but the dog never left Idle";
        }

        /// <summary>動きの質 (round 4)。「取れた」だけでは PCVR 3 の「機械っぽい」を落とせないので、
        /// 踏み切り / キャッチの行に残る mode と直前 1 秒の最大旋回速度・最小速度で別に合否を出す。
        /// 減速せずに跳ぶのが既定なので、速度 0 を許すのは wait (ロブなどで回り込みでも時間が余る) だけ。
        /// 旋回の急さはどの mode でも許さない。</summary>
        static void JudgeMotion(ThrowResult rec)
        {
            // 判定に使えるのは踏み切り / キャッチの行だけ。plan= の行にも mode= は載るが数値が無いので、
            // 地上回収 (ground) のように跳ばずに終わった投げは「判定していない」(motionOk 既定 true) のまま。
            string line = rec.outcomeLine != null && rec.outcomeLine.Contains(" yawRateMax=") ? rec.outcomeLine
                : rec.takeoffLine != null && rec.takeoffLine.Contains(" yawRateMax=") ? rec.takeoffLine : null;
            if (line == null) return;
            rec.mode = Field(line, " mode=");
            rec.yawRateMax = Num(line, " yawRateMax=");
            rec.speedMin = Num(line, " speedMin=");
            rec.vTake = Num(line, " vTake=");
            rec.approachPath = Num(line, " path=");
            if (rec.yawRateMax < 0f || rec.speedMin < 0f)
            { rec.motionOk = false; rec.motionNote = "motion numbers missing from: " + line; return; }
            if (rec.yawRateMax > MotionMaxYawRate)
            { rec.motionOk = false; rec.motionNote = "yawRateMax " + rec.yawRateMax.ToString("F0") + " > " + MotionMaxYawRate + " deg/s (snap turn)"; return; }
            // 助走が 1 秒ぶんの距離 (1.5 m/s x 1 s) に満たない投げは速度で判定しない。円盤が犬のすぐ横に
            // 落ちてくる投げでは、どれだけ生き物らしく動いても直前 1 秒を走り続けることは幾何的にできない。
            if (rec.approachPath >= 0f && rec.approachPath < MotionMinPath)
            {
                rec.motionOk = true;
                rec.motionNote = "mode=" + rec.mode + " yawRateMax=" + rec.yawRateMax.ToString("F0")
                    + " (speed not judged: approach was only " + rec.approachPath.ToString("F1") + " m)";
                return;
            }
            if (rec.mode != "wait" && rec.speedMin < MotionMinSpeed)
            { rec.motionOk = false; rec.motionNote = "mode=" + rec.mode + " but speedMin " + rec.speedMin.ToString("F2") + " < " + MotionMinSpeed + " m/s (slowed to a stop before taking off)"; return; }
            rec.motionOk = true;
            rec.motionNote = "mode=" + rec.mode + " yawRateMax=" + rec.yawRateMax.ToString("F0") + " speedMin=" + rec.speedMin.ToString("F2");
        }

        static string Field(string line, string key)
        {
            int at = line.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return "";
            at += key.Length;
            int end = line.IndexOf(' ', at);
            return end < 0 ? line.Substring(at) : line.Substring(at, end - at);
        }

        static float Num(string line, string key)
        {
            string s = Field(line, key);
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : -1f;
        }


        static void Summarise()
        {
            report.perVariant.Clear();
            foreach (var group in report.throwResults.GroupBy(t => t.variant).OrderBy(g => g.Key))
            {
                var c = new VariantSummary { variant = group.Key };
                foreach (var t in group)
                {
                    if (t.outcome == "caught") c.caught++;
                    else if (t.outcome == "miss") c.miss++;
                    else if (t.outcome == "ground") c.ground++;
                    else if (t.outcome == "none") c.none++;
                    else c.other++;
                }
                report.perVariant.Add(c);
            }
            report.throwsOk = report.throwResults.Count(t => t.ok);
        }

        // ---------------------------------------------------------------- 小道具
        /// <summary>まだ理由が入っていないときだけ理由を入れる。JsonUtility は null 文字列を "" にして返すので
        /// `??` では判定できない (journal を読み直した後の report は failure が "" になっている)。</summary>
        static void Why(string reason)
        {
            if (string.IsNullOrEmpty(report.failure)) report.failure = reason;
        }

        static IEnumerator Wait(double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until) yield return null;
        }

        static IEnumerator WaitIdle(double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until && ReadInt(catcher, "state") != StateIdle) yield return null;
        }

        static UdonBehaviour Find(string goName)
        {
            foreach (var u in Resources.FindObjectsOfTypeAll<UdonBehaviour>())
                if (u != null && u.gameObject.scene.IsValid() && u.gameObject.name == goName) return u;
            throw new InvalidOperationException("No runtime UdonBehaviour on a GameObject named " + goName);
        }

        static bool Has(UdonBehaviour u, string symbol) { object _; return u.TryGetProgramVariable(symbol, out _); }

        static int ReadInt(UdonBehaviour u, string symbol)
        {
            object value;
            if (!u.TryGetProgramVariable(symbol, out value))
                throw new InvalidOperationException("Missing live Udon symbol: " + u.gameObject.name + "." + symbol);
            return value is int ? (int)value : -1;
        }

        static ClientSimPlayer LocalPlayer()
        {
            return Resources.FindObjectsOfTypeAll<ClientSimPlayer>().FirstOrDefault(p => p.Player != null && p.Player.isLocal);
        }

        static int[] Variants(Options o)
        {
            if (!string.IsNullOrEmpty(o.variants))
            {
                var picked = o.variants.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                    .Select(s => { int n; return int.TryParse(s, out n) ? n : -1; })
                    .Where(n => n >= 0 && n < VariantCount).Distinct().ToArray();
                if (picked.Length > 0) return picked;
            }
            return Enumerable.Range(0, VariantCount).ToArray();
        }

        static Shot[] Plan(Options o)
        {
            if (!string.IsNullOrEmpty(o.shots))
            {
                var names = o.shots.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                var picked = Shots.Where(s => names.Contains(s.name)).ToArray();
                if (picked.Length > 0) return picked;
            }
            return Shots;
        }

        static Options ReadOptions()
        {
            try
            {
                if (!File.Exists(OptionsPath)) return new Options();
                var o = JsonUtility.FromJson<Options>(File.ReadAllText(OptionsPath));
                return o ?? new Options();
            }
            catch { return new Options(); }
        }

        static void Log(string message, string stack, LogType type)
        {
            if (report == null) return;
            if (message != null && message.StartsWith("[KH_Frisbee") && Lines.Count < 4000) Lines.Add(message);
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && report.errors.Count < 50)
            {
                report.errors.Add(message + "\n" + stack);
                if (report.status == "PASS") report.status = "FAIL";
            }
        }

        static void Save()
        {
            if (report == null) return;
            Directory.CreateDirectory(RunDir);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(RunDir + "/report.json", json);
            File.WriteAllText(Base + "/latest.json", json);
            // journal は「実行中の記録」ではなく「ドメイン再読み込みをまたぐ唯一の記憶」。
            // Play を抜けるときにもドメインは作り直されるので、status が RUNNING でなくなった後も
            // 書き続ける (ここを止めると、静的コンストラクタが古い journal を読み直して
            // 最後の投げと停止理由を丸ごと失う)。消すのは RestoreScene だけ。
            if (!closed) File.WriteAllText(Journal, json);
        }

        static string SdkVersion()
        {
            try
            {
                string text = File.ReadAllText("Packages/com.vrchat.worlds/package.json");
                int i = text.IndexOf("\"version\"", StringComparison.Ordinal);
                int a = text.IndexOf('"', text.IndexOf(':', i)) + 1, b = text.IndexOf('"', a);
                return text.Substring(a, b - a);
            }
            catch { return "unknown"; }
        }

        static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }

        static string HashDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return "ABSENT";
            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal);
            using (var sha = SHA256.Create())
            {
                var all = new List<byte>();
                foreach (string f in files) all.AddRange(sha.ComputeHash(File.ReadAllBytes(f)));
                return BitConverter.ToString(sha.ComputeHash(all.ToArray())).Replace("-", "");
            }
        }

        static string Git(string args)
        {
            try
            {
                using (var p = new System.Diagnostics.Process())
                {
                    p.StartInfo = new System.Diagnostics.ProcessStartInfo("git",
                        "-c safe.directory=\"" + Path.GetFullPath(".").Replace('\\', '/') + "\" " + args)
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    p.Start();
                    string output = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(10000)) { p.Kill(); return "TIMEOUT"; }
                    return output.Trim();
                }
            }
            catch { return "UNKNOWN"; }
        }
    }
}
