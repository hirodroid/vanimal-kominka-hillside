using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-005 Photo/3 - Harness: 自動写真の小さいケース群 (ClientSim、1 クライアント、約 1〜2 分。歩行・洗浄のハーネスは回さない)。
    /// IMP-WLD-007 W1 で 24 枠・出来事 3 カテゴリ (散歩 0..11 / 撫で 12..17 / 遊び 18..23)・8 枚ずつのページになった。
    ///   H0-ready          入室 → 設定の復元 → 実行時の RT 24 枚が端末の寸法 (Editor = PC 1280x720) で作られ、撮影の見回りが有効、カメラは無効
    ///   H1-capture        きっかけ (フリスビーを取った: KD_PhotoDirector._OnFrisbeeCatch) → 遊びの最初の枠 (18) へ 1 枚。色が入っている (真っ黒・一色でない)。
    ///                     ボードの左上と縮小表示がその RT を出す。撮り終えたらカメラは無効
    ///   H2-wrap           さらに 6 回 → 遊びの 7 枚目で遊びのいちばん古い枠 (18) が置き換わり、新しい順に並ぶ (18, 23, 22, 21, 20, 19)。散歩・撫での枠は使わない
    ///   H6-category       散歩の枠へ 13 回 (散歩のきっかけは W2 なので、実行時だけ _hillsideGroupOverride = 0 にして撮る) → 散歩の中だけで上書き (0 が 2 回目)、
    ///                     遊びの 6 枚は残る (合計 18 枚 = 3 ページ)
    ///   H3-off            設定の [写真: ON/OFF] (KH_PhotoAlbum._ToggleCapture) で OFF → 見回りが止まり、きっかけが来ても撮らない、写真は残る、保存値 0。
    ///                     もう一度押して ON → 保存値 1、RT は作り直さない (同じ寸法)
    ///   H4-menu           手元メニューの [写真] (KD_TreatPouch._PhotoOpen) → 写真の面がボードと同じ RT を出す、「新しい」の印、
    ///                     ページ送り (9 枚以上のとき: ▶ で次の 8 枚・「2/3」・◀ で戻る)、押すと大きく、[?] の説明、[もどる] で元のタブへ。
    ///                     ボードの ▶ で次の 8 枚、戻す時間 (実行時だけ 1 s) で 1 ページ目へ
    ///   H5-no-camera-left 最後にカメラが無効で、撮りかけの枠が残っていない
    /// IMP-WLD-007 W2 (散歩の写真。ここだけ Corgi (犬 0) を自分につなぐ = KH_LeashManager.walkers を実行時だけ書く。終わったら外す):
    ///   H6w-walk-trigger  見回り (KH_PhotoWalkWatcher) の「時間と距離」をしきい値の直前にして 1 枚 → 散歩の枠 (0..11) に入る。犬目線なら、カメラが頭アンカーの前
    ///                     (前出し 0.08〜0.20 m、上 0.04 m) にあり、向きが「ルートの前方を水平にして 3° 上」から 2° 以内 (撮った瞬間の値で照合)
    ///   H7-fade-gate      移動の暗転 (KH_TravelFade._Begin) の間と、明けてから 2 s は撮らない。その後は撮る
    ///   H8-selfie         自撮り風を直接頼む: 犬を背にして立つ → 頭から水平 0.9 m・0.15 m 上、犬が画面の内側 80 % (Camera.WorldToViewportPoint)。
    ///                     6 m より遠い犬だけを「自分の犬」として頼む → 自撮り風は撮らない (犬目線へ落ちるか、撮らない)
    /// **一部だけ回す**: Temp/kh-photo/harness-cases.txt に "H1,H6" のように書いてから Photo/3 (読んだら消す。無ければ全部)。
    /// H0 と H5 は必ず回る。H2・H3・H4・H6 は写真が要るので H1 を足す。回したケースは report.casesRun に残る。
    /// 疑似入力 (Udon のイベントを直接送る) なので、実際のフリスビーの跳び・ボタンの光線・VRChat カメラの吸着・端末差 (Quest の一瞬の引っかかり、
    /// iOS のタップ表示) は見ない。PlayerData は実行前に退避し、終わったら戻す。シーンは保存しない (前後のハッシュを比べる)。
    /// </summary>
    [InitializeOnLoad]
    public static class KH_PhotoHarnessEditor
    {
        const string Key = "KH.PhotoHarness", Dir = "Logs/kh-photo-harness", Journal = Dir + "/journal.json";
        public const string CasesFile = "Temp/kh-photo/harness-cases.txt";
        /// <summary>回す順。H6 (散歩の枠) は H3 より前 (OFF の検査に写真が要る・H4 のページ送りに 9 枚以上が要る)。</summary>
        public static readonly string[] AllCases = { "H0-ready", "H1-capture", "H2-wrap", "H6-category", "H6w-walk-trigger", "H7-fade-gate", "H8-selfie", "H3-off", "H4-menu", "H5-no-camera-left" };

        /// <summary>"H1,H6" → 回すケース (前方一致。H0・H5 は必ず、写真が要るケースには H1 を足す)。空なら全部。知らない名前は例外 (Play に入らない)。</summary>
        public static List<string> Selected(string request)
        {
            if (string.IsNullOrWhiteSpace(request)) return AllCases.ToList();
            var want = new HashSet<string> { "H0-ready", "H5-no-camera-left" };
            foreach (var raw in request.Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var hit = AllCases.Where(c => c == raw || c.StartsWith(raw + "-", StringComparison.Ordinal)).ToArray();
                if (hit.Length != 1) throw new InvalidOperationException("unknown photo harness case '" + raw + "' (known: " + string.Join(", ", AllCases) + ")");
                want.Add(hit[0]);
            }
            // the walk cases (H6w / H7 / H8) need no earlier photo
            if (want.Any(c => c == "H2-wrap" || c == "H3-off" || c == "H4-menu" || c == "H6-category")) want.Add("H1-capture");
            return AllCases.Where(want.Contains).ToList();
        }

        [Serializable] public class Case { public string name, status = "PENDING", reason; public List<string> notes = new List<string>(); }
        [Serializable] public class Report
        {
            public string runId, status = "RUNNING", reason, utc, endedUtc, scene, hashBefore, hashAfter, buildTarget, playerDataBackup;
            public string casesRequested = "";            // IMP-WLD-007: Temp/kh-photo/harness-cases.txt ("" = all)
            public List<string> casesRun = new List<string>();
            public bool sceneFileUnchanged, playerDataRestored, settingsRestored, originalBackground;
            public double deadline;
            public List<Case> cases = new List<Case>();
            public List<string> errors = new List<string>();
            public string limitations = "Single local ClientSim. Udon events are sent directly (the frisbee catch notice, the settings row, the menu buttons): no real jump, ray click, "
                + "VRChat camera snap, network or second player. The look of the photos (composition, light) and the device behaviour (Quest hitch, iOS tap view) are not judged.";
        }

        static Report report;
        static bool ending;
        static int step = -1, lastFrame = -1;
        static double stepAt;
        static List<Step> steps;
        class Step { public string caseName; public Func<bool> until; public Action action; public double timeout; public string fail; }

        static KH_PhotoHarnessEditor()
        {
            EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Mode;
            Application.logMessageReceived += Log;
            if (!SessionState.GetBool(Key, false)) return;
            try { report = JsonUtility.FromJson<Report>(File.ReadAllText(Journal)); ending = report.status != "RUNNING"; }
            catch (Exception e) { Debug.LogError("[KH Photo Harness] journal unreadable: " + e.Message); SessionState.SetBool(Key, false); report = null; }
        }

        [MenuItem("Tools/KominkaHillside/Photo/3 - Harness")]
        public static void Run()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if (SessionState.GetBool(Key, false)) throw new InvalidOperationException("photo harness already running (Photo/3b stops it)");
            var album = UnityEngine.Object.FindObjectsOfType<KH_PhotoAlbum>(true);
            var board = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == KH_PhotoGalleryEditor.BoardRootName);
            if (album.Length != 1 || !album[0].surfacesPlaced || board == null)
            {
                Debug.LogError("[KH Photo Harness] PHOTO_HARNESS_BLOCKED needs Photo/1 and Photo/4 (album " + album.Length + ", surfacesPlaced " + (album.Length == 1 && album[0].surfacesPlaced) + ", board " + (board != null) + "); Play not entered");
                return;
            }
            report = new Report { runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, originalBackground = Application.runInBackground,
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString() };
            report.hashBefore = Hash(report.scene);
            if (File.Exists(CasesFile)) { report.casesRequested = File.ReadAllText(CasesFile).Trim(); File.Delete(CasesFile); }
            report.casesRun = Selected(report.casesRequested);
            Debug.Log("[KH Photo Harness] cases: " + string.Join(", ", report.casesRun) + (report.casesRequested.Length > 0 ? " (requested '" + report.casesRequested + "')" : " (all)"));
            report.deadline = EditorApplication.timeSinceStartup + 240;
            BackupPlayerData();
            Application.runInBackground = true;
            Save(); ending = false; step = -1; steps = null;
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Tools/KominkaHillside/Photo/3b - Stop Harness")]
        public static void Stop() { End("STOPPED", "operator stop"); }

        static void Mode(PlayModeStateChange state)
        {
            if (report == null || !SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { step = -1; steps = null; stepAt = EditorApplication.timeSinceStartup; }
            if (state == PlayModeStateChange.ExitingPlayMode && !ending) { report.status = "STOPPED"; report.reason = "Play stopped externally"; ending = true; SafeSave(); }
            if (state == PlayModeStateChange.EnteredEditMode) Finish();
        }

        static void Log(string condition, string stack, LogType type)
        {
            if (report == null || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            if (condition != null && condition.Contains("Runtime error detected, but the client has not been launched with")) return;   // a VRChat client on the same PC (see the walk harness)
            if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && report.errors.Count < 20) report.errors.Add(condition);
        }

        static void Tick()
        {
            if (report == null || !SessionState.GetBool(Key, false)) return;
            if (ending) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(); else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); return; }
            if (EditorApplication.timeSinceStartup > report.deadline) { End("TIMEOUT", "global watchdog (240 s)"); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
            try
            {
                if (steps == null)
                {
                    if (EditorApplication.timeSinceStartup - stepAt < 3) return;   // let ClientSim spawn the local player
                    if (!Utilities.IsValid(Networking.LocalPlayer)) return;
                    Resolve(); MakeSteps(); step = 0; stepAt = EditorApplication.timeSinceStartup; Save();
                }
                while (step < steps.Count)
                {
                    var s = steps[step];
                    var cs = report.cases.First(x => x.name == s.caseName);
                    if (cs.status == "FAIL") { step++; stepAt = EditorApplication.timeSinceStartup; continue; }   // a failed case skips its remaining steps
                    if (s.action != null) { current = cs; s.action(); step++; stepAt = EditorApplication.timeSinceStartup; continue; }
                    current = cs;
                    if (s.until()) { step++; stepAt = EditorApplication.timeSinceStartup; return; }
                    if (EditorApplication.timeSinceStartup - stepAt > s.timeout) { Fail(s.fail + " (" + s.timeout + " s)"); step++; stepAt = EditorApplication.timeSinceStartup; }
                    return;
                }
                foreach (var cs in report.cases) if (cs.status == "PENDING" || cs.status == "RUNNING") cs.status = "PASS";
                bool pass = report.cases.All(x => x.status == "PASS") && report.errors.Count == 0;
                End(pass ? "PASS" : "FAIL", pass ? "all photo cases passed (local ClientSim, simulated input)" : "failed: " + string.Join(" | ", report.cases.Where(x => x.status != "PASS").Select(x => x.name + ": " + x.reason))
                    + (report.errors.Count > 0 ? " | runtime errors: " + report.errors.Count : ""));
            }
            catch (Exception e) { End("FAIL", "harness exception: " + e.Message); }
        }

        // ================================================================ runtime handles

        static UdonBehaviour uAlbum, uSettings, uCapture, uGallery, uDirector, uPouch, uWatcher, uManager, uFade;
        static Camera cam;
        static MeshRenderer[] boardPhotos;
        static RenderTexture[] runtimeSlots;
        static Case current;
        static int capturesBefore, tabBefore, countBeforeOff;
        static string frisbeeEntry, frisbeeParam;

        static void Resolve()
        {
            if (report.casesRun == null || report.casesRun.Count == 0) report.casesRun = AllCases.ToList();
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            UdonBehaviour One(string program) { var m = all.Where(u => u.programSource != null && u.programSource.name == program).ToArray(); if (m.Length != 1) throw new InvalidOperationException(program + " x" + m.Length); return m[0]; }
            uAlbum = One("KH_PhotoAlbum"); uSettings = One("KD_PhotoSettings"); uCapture = One("KD_PhotoCapture"); uGallery = One("KD_GalleryController"); uDirector = One("KD_PhotoDirector"); uPouch = One("KD_TreatPouch");
            if (report.casesRun.Any(n => n == "H6w-walk-trigger" || n == "H7-fade-gate" || n == "H8-selfie"))
            {
                uWatcher = One("KH_PhotoWalkWatcher"); uManager = One("KH_LeashManager");
                var fades = all.Where(u => u.programSource != null && u.programSource.name == "KH_TravelFade").ToArray();
                uFade = fades.Length == 1 ? fades[0] : null;   // H7 reports a missing fade itself
            }
            var sys = SceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == KH_PhotoGalleryEditor.SystemName);
            cam = sys.transform.Find("PhotoCamera").GetComponent<Camera>();
            var board = SceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == KH_PhotoGalleryEditor.BoardRootName);
            boardPhotos = Enumerable.Range(0, KH_PhotoAlbum.PageSize).Select(k => board.transform.Find("Photos/Photo" + k).GetComponent<MeshRenderer>()).ToArray();
            // _OnFrisbeeCatch(int dog): U# renames a method with parameters (__N__OnFrisbeeCatch, __N_dog__param)
            var prog = uDirector.programSource.SerializedProgramAsset.RetrieveProgram();
            for (int n = 0; n <= 200 && frisbeeEntry == null; n++) if (prog.EntryPoints.TryGetAddressFromSymbol("__" + n + "__OnFrisbeeCatch", out _)) frisbeeEntry = "__" + n + "__OnFrisbeeCatch";
            if (frisbeeEntry == null && prog.EntryPoints.TryGetAddressFromSymbol("_OnFrisbeeCatch", out _)) frisbeeEntry = "_OnFrisbeeCatch";
            for (int n = 0; n <= 200 && frisbeeParam == null; n++) if (prog.SymbolTable.TryGetAddressFromSymbol("__" + n + "_dog__param", out _)) frisbeeParam = "__" + n + "_dog__param";
            if (frisbeeEntry == null || frisbeeParam == null) throw new InvalidOperationException("KD_PhotoDirector._OnFrisbeeCatch entry / parameter not found (" + frisbeeEntry + " / " + frisbeeParam + ")");
        }

        static T Get<T>(UdonBehaviour u, string key)
        {
            if (u == null || !u.TryGetProgramVariable(key, out object v) || !(v is T)) throw new InvalidOperationException((u != null && u.TryGetProgramVariable(key, out object w) && w == null ? "runtime symbol is null: " : "missing runtime symbol ") + key + " on " + (u != null && u.programSource != null ? u.programSource.name : "?"));
            return (T)v;
        }

        static void Trigger(int dog) { uDirector.SetProgramVariable(frisbeeParam, dog); uDirector.SendCustomEvent(frisbeeEntry); }
        static int Captures() { return Get<int>(uAlbum, "_captures"); }
        static int[] Order() { return Get<int[]>(uAlbum, "_order"); }
        static RenderTexture[] Slots() { return Get<RenderTexture[]>(uGallery, "slotTextures"); }
        static bool SettingsReady() { return Get<bool>(uSettings, "_restored") && !Get<bool>(uSettings, "_busy"); }
        static int SavedMode() { int v; return VRC.SDK3.Persistence.PlayerData.TryGetInt(Networking.LocalPlayer, KominkaDogRun.KD_PhotoSettings.SaveKey, out v) ? v : -1; }

        static void Expect(bool ok, string what) { if (ok) current.notes.Add("ok " + what); else Fail(what); }
        static void Fail(string why) { if (current.status != "FAIL") { current.status = "FAIL"; current.reason = why; } current.notes.Add("FAIL " + why); }
        static void Note(string s) { current.notes.Add(s); }

        static Step Do(string c, Action a) { return new Step { caseName = c, action = a }; }
        static Step Until(string c, Func<bool> f, double t, string fail) { return new Step { caseName = c, until = f, timeout = t, fail = fail }; }

        /// <summary>撮れた RT の中身が「真っ黒でも一色でもない」か (Editor で読み戻す。実行時の Udon は読まない)。</summary>
        static string PixelCheck(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                var px = tex.GetPixels32(); double sum = 0, sum2 = 0; int n = 0;
                for (int i = 0; i < px.Length; i += 97) { double l = (0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b) / 255.0; sum += l; sum2 += l * l; n++; }
                double mean = sum / n, sd = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
                return (mean > 0.02 && sd > 0.01 ? "OK" : "BLANK") + " mean " + mean.ToString("F3") + " sd " + sd.ToString("F3");
            }
            finally { RenderTexture.active = prev; UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>ボードの今のページ (page) の 8 枚が、並び (新しい順) の page × 8 + k を出しているか。expectCount &lt; 0 なら枚数は見ない。</summary>
        static void CheckBoardOrder(int expectCount, int page = 0)
        {
            var order = Order(); var slots = Slots();
            int count = Get<int>(uAlbum, "_count");
            if (expectCount >= 0)
            {
                Expect(count == expectCount, "album holds " + expectCount + " photos (actual " + count + ")");
                if (count != expectCount) return;
            }
            for (int k = 0; k < PageSize; k++)
            {
                int p = page * PageSize + k;
                bool show = p < count;
                Expect(boardPhotos[k].gameObject.activeSelf == show, "board page " + (page + 1) + " position " + k + (show ? " shown" : " hidden"));
                if (!show) continue;
                Expect(boardPhotos[k].sharedMaterial.mainTexture == slots[order[p]], "board page " + (page + 1) + " position " + k + " shows slot " + order[p]);
                var tap = boardPhotos[k].GetComponent<UdonBehaviour>();
                Expect(tap != null && Get<int>(tap, "slotIndex") == order[p], "board page " + (page + 1) + " position " + k + " tap opens slot " + order[p]);
            }
        }

        /// <summary>メニューの縮小表示が、並びの page × 8 + k を出しているか。</summary>
        static void CheckMenuPage(int page)
        {
            var order = Order(); var slots = Slots();
            int count = Get<int>(uAlbum, "_count");
            var thumbs = Get<UnityEngine.UI.RawImage[]>(uAlbum, "menuThumbs"); var buttons = Get<GameObject[]>(uAlbum, "menuThumbButtons");
            bool ok = true;
            for (int k = 0; k < PageSize; k++)
            {
                int p = page * PageSize + k;
                bool show = p < count;
                if (buttons[k].activeSelf != show) ok = false;
                if (show && thumbs[k].texture != slots[order[p]]) ok = false;
            }
            Expect(ok, "menu page " + (page + 1) + " shows positions " + (page * PageSize) + ".." + Math.Min(count, (page + 1) * PageSize) + " of " + count + ", newest first, the rest hidden");
        }

        const int PageSize = KH_PhotoAlbum.PageSize;
        static int GroupFirst(int group) { int s = 0; for (int g = 0; g < group; g++) s += KH_PhotoGalleryEditor.GroupSizes[g]; return s; }
        static bool On(string name) { return report.casesRun.Contains(name); }

        // ================================================================ cases

        static void MakeSteps()
        {
            steps = new List<Step>();
            if (report.casesRun == null || report.casesRun.Count == 0) report.casesRun = AllCases.ToList();
            foreach (var name in report.casesRun) report.cases.Add(new Case { name = name });
            int playFirst = GroupFirst(KominkaDogRun.KD_PhotoDirector.HillsideGroupPlay), walkFirst = GroupFirst(KominkaDogRun.KD_PhotoDirector.HillsideGroupWalk);
            int playSize = KH_PhotoGalleryEditor.GroupSizes[KominkaDogRun.KD_PhotoDirector.HillsideGroupPlay], walkSize = KH_PhotoGalleryEditor.GroupSizes[KominkaDogRun.KD_PhotoDirector.HillsideGroupWalk];
            bool mobile = report.buildTarget == "Android" || report.buildTarget == "iOS";
            int wantW = mobile ? KH_PhotoGalleryEditor.MobileWidth : KH_PhotoGalleryEditor.PcWidth, wantH = mobile ? KH_PhotoGalleryEditor.MobileHeight : KH_PhotoGalleryEditor.PcHeight;

            // ---- H0 ----
            steps.Add(Until("H0-ready", () => Get<bool>(uSettings, "_restored"), 30, "OnPlayerRestored did not reach KD_PhotoSettings"));
            steps.Add(Do("H0-ready", () =>
            {
                if (Get<int>(uSettings, "_mode") == 0) { Note("the saved preference was OFF (an earlier manual test): switched ON first"); uAlbum.SendCustomEvent("_ToggleCapture"); }
            }));
            steps.Add(Until("H0-ready", () => SettingsReady() && Get<int>(uSettings, "_mode") == 1, 10, "settings did not become ready in standard mode"));
            steps.Add(Do("H0-ready", () =>
            {
                // 2026-10-03 run 20261002-232223: _failed (empty boards array) left capture off; say so instead of a director timeout
                Expect(!Get<bool>(uSettings, "_failed"), "KD_PhotoSettings created the runtime textures (_failed false; true = no capture at all)");
                Expect(Get<bool>(uAlbum, "surfacesPlaced"), "album.surfacesPlaced is true at runtime (Photo/4)");
            }));
            steps.Add(Until("H0-ready", () => uDirector.enabled, 3, "the album did not enable the photo director (surfacesPlaced + CaptureAllowed)"));
            steps.Add(Until("H0-ready", () => Get<int>(uDirector, "_n") == 6, 3, "the photo director did not start with 6 dogs (wiring)"));
            steps.Add(Do("H0-ready", () =>
            {
                Expect(!Get<bool>(uSettings, "_failed"), "runtime textures created (no failure)");
                var slots = Slots();
                runtimeSlots = (RenderTexture[])slots.Clone();
                bool runtime = true;
                for (int i = 0; i < slots.Length; i++)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<RenderTexture>(KH_PhotoGalleryEditor.SlotRTPath(i));
                    if (slots[i] == null || slots[i] == asset || slots[i].width != wantW || slots[i].height != wantH || slots[i].depth != 0) runtime = false;
                }
                Expect(slots.Length == KH_PhotoAlbum.Slots && runtime, KH_PhotoAlbum.Slots + " runtime slots " + wantW + "x" + wantH + " depth 0 (build target " + report.buildTarget + "), not the templates");
                Expect(Get<int[]>(uGallery, "slotGroups").SequenceEqual(KH_PhotoGalleryEditor.SlotGroups()) && Get<bool>(uDirector, "hillsideEventGroups"), "3 event categories (walk / pet / play) and the director picks them");
                object bp; uAlbum.TryGetProgramVariable("boardPager", out bp); var pagerGo = bp as GameObject;
                Expect(pagerGo != null && !pagerGo.activeSelf, "board pager present (Photo/4 after IMP-WLD-007) and hidden (no photo yet)");
                var capRt = Get<RenderTexture>(uCapture, "captureTexture");
                Expect(capRt != null && capRt.width == wantW && capRt.height == wantH && capRt.depth >= 16 && cam.targetTexture == capRt, "runtime capture RT " + wantW + "x" + wantH + " with depth, the camera's target");
                Expect(!cam.enabled, "photo camera disabled while idle");
                Expect(Get<int>(uAlbum, "_count") == 0 && boardPhotos.All(b => !b.gameObject.activeSelf), "no photo yet: board positions hidden");
                var state = Get<TMPro.TMP_Text>(uAlbum, "boardState");
                Expect(state != null && !string.IsNullOrEmpty(state.text), "board shows the empty state: '" + (state != null ? state.text : "") + "'");
                Note("PlayerData " + KominkaDogRun.KD_PhotoSettings.SaveKey + " = " + SavedMode());
            }));

            // ---- H1 (a frisbee catch is a "play" event: the first play slot) ----
            if (On("H1-capture"))
            {
                steps.Add(Do("H1-capture", () =>
                {
                    uDirector.SetProgramVariable("frisbeeShotCooldown", 0f);   // runtime only (the scene keeps 20 s)
                    capturesBefore = Captures();
                    Trigger(0);
                    Note("frisbee catch notice for dog 0 (Corgi), cooldown 0 at runtime");
                }));
                steps.Add(Until("H1-capture", () => Captures() == capturesBefore + 1, 3, "no capture after the frisbee catch notice"));
                steps.Add(Until("H1-capture", () => !cam.enabled, 1, "the photo camera stayed enabled after the capture"));
                steps.Add(Do("H1-capture", () =>
                {
                    var order = Order(); var slots = Slots();
                    Expect(order[0] == playFirst, "the first photo went into the first play slot " + playFirst + " (actual " + order[0] + ")");
                    Expect(Get<RenderTexture>(uCapture, "_lastSlot") == slots[playFirst], "capture reports slot " + playFirst + " as the last one copied");
                    string px = PixelCheck(slots[playFirst]);
                    Expect(px.StartsWith("OK"), "slot " + playFirst + " holds a picture (" + px + ")");
                    CheckBoardOrder(1);
                    var thumbs = Get<UnityEngine.UI.RawImage[]>(uAlbum, "menuThumbs");
                    Expect(thumbs[0].texture == slots[playFirst], "menu thumbnail 0 is the same RenderTexture as the board");
                    var next = Get<int[]>(uGallery, "_nextInGroup");
                    Expect(next[KominkaDogRun.KD_PhotoDirector.HillsideGroupPlay] == 1 && next[KominkaDogRun.KD_PhotoDirector.HillsideGroupWalk] == 0 && next[KominkaDogRun.KD_PhotoDirector.HillsideGroupPet] == 0,
                        "only the play category moved on (next " + string.Join(",", next) + ")");
                }));
            }

            // ---- H2: the play category wraps inside itself ----
            if (On("H2-wrap"))
            {
                for (int i = 1; i <= playSize; i++)
                {
                    int dog = i % 6, n = i + 1;
                    steps.Add(Do("H2-wrap", () => { capturesBefore = Captures(); Trigger(dog); }));
                    steps.Add(Until("H2-wrap", () => Captures() == capturesBefore + 1, 3, "capture " + n + " did not happen"));
                    steps.Add(Until("H2-wrap", () => !cam.enabled, 1, "camera stayed enabled after capture " + n));
                }
                steps.Add(Do("H2-wrap", () =>
                {
                    var order = Order();
                    var want = new List<int> { playFirst }; for (int s = playFirst + playSize - 1; s > playFirst; s--) want.Add(s);   // 18, 23, 22, 21, 20, 19
                    int count = Get<int>(uAlbum, "_count");
                    Expect(count == playSize && order.Take(count).SequenceEqual(want), "after " + (playSize + 1) + " play captures the oldest play slot (" + playFirst + ") was replaced, newest first: [" + string.Join(",", order.Take(count)) + "] want [" + string.Join(",", want) + "]");
                    Expect(order.Take(count).All(s => s >= playFirst && s < playFirst + playSize), "no walk / pet slot was used by play events");
                    Expect(Get<int[]>(uGallery, "_nextInGroup")[KominkaDogRun.KD_PhotoDirector.HillsideGroupPlay] == 1, "the play category wraps (next = its 2nd slot)");
                    CheckBoardOrder(playSize);
                    var slots = Slots(); bool same = runtimeSlots != null && slots.Length == runtimeSlots.Length; for (int i = 0; same && i < slots.Length; i++) if (slots[i] != runtimeSlots[i]) same = false;
                    Expect(same, "captures reuse the same runtime textures (no allocation per capture)");
                }));
            }

            // ---- H6: the walk category (W2 has no walk trigger yet: the runtime override sends frisbee shots there) ----
            if (On("H6-category"))
            {
                int[] before = null; int countBefore = 0;
                steps.Add(Do("H6-category", () =>
                {
                    countBefore = Get<int>(uAlbum, "_count"); before = Order().Take(countBefore).ToArray();
                    uDirector.SetProgramVariable("_hillsideGroupOverride", KominkaDogRun.KD_PhotoDirector.HillsideGroupWalk);
                    Note("runtime only: _hillsideGroupOverride = walk; " + countBefore + " photos before");
                }));
                for (int i = 0; i <= walkSize; i++)
                {
                    int dog = i % 6, n = i + 1;
                    steps.Add(Do("H6-category", () => { capturesBefore = Captures(); Trigger(dog); }));
                    steps.Add(Until("H6-category", () => Captures() == capturesBefore + 1, 3, "walk capture " + n + " did not happen"));
                    steps.Add(Until("H6-category", () => !cam.enabled, 1, "camera stayed enabled after walk capture " + n));
                }
                steps.Add(Do("H6-category", () =>
                {
                    uDirector.SetProgramVariable("_hillsideGroupOverride", -1);
                    var order = Order(); int count = Get<int>(uAlbum, "_count");
                    var want = new List<int> { walkFirst }; for (int s = walkFirst + walkSize - 1; s > walkFirst; s--) want.Add(s);   // 0, 11, 10, … 1
                    want.AddRange(before);                                                                                            // the play photos keep their order behind
                    Expect(count == walkSize + countBefore && order.Take(count).SequenceEqual(want), (walkSize + 1) + " walk captures: walk wraps inside itself (" + walkFirst + " twice), the earlier photos stay: ["
                        + string.Join(",", order.Take(count)) + "] want [" + string.Join(",", want) + "]");
                    Expect(Get<int[]>(uGallery, "_nextInGroup")[KominkaDogRun.KD_PhotoDirector.HillsideGroupWalk] == 1, "the walk category wraps (next = its 2nd slot)");
                    CheckBoardOrder(count);
                    object pagerObj; uAlbum.TryGetProgramVariable("boardPager", out pagerObj);
                    Expect(pagerObj is GameObject pg && pg.activeSelf == (count > PageSize), "board pager shown iff more than " + PageSize + " photos (" + count + ")");
                }));
            }

            // ---- H6w / H7 / H8: walk photos (IMP-WLD-007 W2) ----
            AddWalkSteps(walkFirst, walkSize);

            // ---- H3 ----
            if (On("H3-off")) AddOffSteps();
            if (On("H4-menu")) AddMenuSteps();

            // ---- H5 ----
            steps.Add(Do("H5-no-camera-left", () =>
            {
                Expect(!cam.enabled, "the photo camera is disabled at the end");
                // _pendingSlot is null once a capture finishes; Get<T> treats a null value as missing (null is not T), so read it directly.
                // A symbol that is absent from the heap and a null value both mean "nothing pending".
                object pending; uCapture.TryGetProgramVariable("_pendingSlot", out pending);
                Expect(!(pending is RenderTexture), "no capture left half-done (_pendingSlot " + (pending == null ? "null" : pending.ToString()) + ")");
                Note("cases run: " + string.Join(", ", report.casesRun) + "; captures " + Captures() + ", runtime errors " + report.errors.Count);
            }));
        }

        // ================================================================ walk photos (IMP-WLD-007 W2)

        const int WalkDog = 0;   // Corgi
        static int shotsBefore;
        static double waitFrom;
        static bool leashedByHarness;

        /// <summary>犬 0 を自分につなぐ / 外す。KH_LeashManager.walkers / hands を実行時だけ書く (Owner の自分の書き込み。歩行のハーネスと同じ手。
        /// 0.5 s の見回りが ReleaseInteracts → KH_PhotoWalkWatcher._OnLeashChanged を呼ぶ = 実際の知らせの経路を通る)。</summary>
        static void Leash(bool on)
        {
            var walkers = (int[])Get<int[]>(uManager, "walkers").Clone(); var hands = (int[])Get<int[]>(uManager, "hands").Clone();
            walkers[WalkDog] = on ? Networking.LocalPlayer.playerId : -1; hands[WalkDog] = 1;
            uManager.SetProgramVariable("walkers", walkers); uManager.SetProgramVariable("hands", hands);
            leashedByHarness = on;
        }

        /// <summary>「時間と距離」のしきい値を越えた状態にする (歩いた距離と時刻を書く。暗転の名残も消す)。</summary>
        static void PokeTimeTrigger()
        {
            uWatcher.SetProgramVariable("_walked", 1000f);
            uWatcher.SetProgramVariable("_timeRefAt", -1000f);
            uWatcher.SetProgramVariable("_lastWalkShotAt", -1000f);
            uWatcher.SetProgramVariable("_timeToggle", false);   // the next time trigger starts with the dog's-eye
        }

        static Vector3 DogSubjectNow(int dog)
        {
            var roots = Get<Transform[]>(uDirector, "dogRoots"); var heads = Get<Transform[]>(uDirector, "dogHeadAnchors");
            Vector3 r = roots[dog].position, h = heads[dog] != null ? heads[dog].position : r + Vector3.up * 0.3f;
            return Vector3.Lerp(r, h, 0.75f);
        }

        static string WalkReason() { object o; uDirector.TryGetProgramVariable("_walkLastReason", out o); return o as string ?? ""; }

        /// <summary>自撮り風の 1 枚が定義どおりか: 頭から水平 0.9 m・0.15 m 上、FOV 60、自分の犬 (dog) が 6 m 以内で画面の内側 80 % (ビューポート 0.1〜0.9)。</summary>
        static void CheckSelfie(int dog)
        {
            var head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            var off = cam.transform.position - head; float horiz = new Vector2(off.x, off.z).magnitude;
            Expect(Math.Abs(horiz - KH_PhotoGalleryEditor.SelfieReach) < 0.06f && Math.Abs(off.y - KH_PhotoGalleryEditor.SelfieLift) < 0.06f,
                "selfie camera " + horiz.ToString("F2") + " m from the head, " + off.y.ToString("F2") + " m up (want " + KH_PhotoGalleryEditor.SelfieReach + " / " + KH_PhotoGalleryEditor.SelfieLift + ")");
            Expect(Mathf.Approximately(cam.fieldOfView, KH_PhotoGalleryEditor.SelfieFov), "selfie FOV " + cam.fieldOfView);
            var subject = DogSubjectNow(dog);
            var vp = cam.WorldToViewportPoint(subject);
            float dist = Vector3.Distance(cam.transform.position, subject);
            const float slack = 0.05f;   // the leashed dog keeps moving for a few frames after the shot
            Expect(vp.z > 0f && dist <= KH_PhotoGalleryEditor.SelfieDogMax + 0.5f && vp.x >= 0.1f - slack && vp.x <= 0.9f + slack && vp.y >= 0.1f - slack && vp.y <= 0.9f + slack,
                "own dog " + dog + " inside the inner 80% of the frame (viewport " + vp.x.ToString("F2") + ", " + vp.y.ToString("F2") + ", " + dist.ToString("F1") + " m)");
        }

        static void AddWalkSteps(int walkFirst, int walkSize)
        {
            var walkCases = new[] { "H6w-walk-trigger", "H7-fade-gate", "H8-selfie" }.Where(On).ToArray();
            if (walkCases.Length == 0) return;
            string first = walkCases[0], last = walkCases[walkCases.Length - 1];
            steps.Add(Do(first, () =>
            {
                var walkers = Get<int[]>(uManager, "walkers");
                Expect(walkers[WalkDog] < 0, "dog " + WalkDog + " is free before the walk cases (walkers " + walkers[WalkDog] + ")");
                Leash(true);
                Note("runtime only: dog " + WalkDog + " leashed to the local player (right hand)");
            }));
            steps.Add(Until(first, () => Get<int>(uWatcher, "_mask") == (1 << WalkDog) && Get<bool>(uWatcher, "_ticking"), 3, "the walk watcher did not see the leashed dog (KH_LeashManager.ReleaseInteracts -> _OnLeashChanged)"));

            // ---- H6w: the time + distance trigger -> one walk photo, the dog's-eye by definition ----
            if (On("H6w-walk-trigger"))
            {
                steps.Add(Do("H6w-walk-trigger", () => { capturesBefore = Captures(); shotsBefore = Get<int>(uWatcher, "_shots"); uWatcher.SetProgramVariable("_fadeUntil", 0f); PokeTimeTrigger(); }));
                steps.Add(Until("H6w-walk-trigger", () => Captures() == capturesBefore + 1, 4, "no walk photo after the time + distance trigger (see the notes for the watcher's skip)"));
                steps.Add(Until("H6w-walk-trigger", () => !cam.enabled, 1, "camera stayed enabled after the walk photo"));
                steps.Add(Do("H6w-walk-trigger", () =>
                {
                    object skip; uWatcher.TryGetProgramVariable("_lastSkip", out skip);
                    Note("watcher last skip '" + skip + "', director reason '" + WalkReason() + "'");
                    Expect(Get<int>(uWatcher, "_shots") == shotsBefore + 1, "the watcher counted one walk photo");
                    var order = Order();
                    Expect(order[0] >= walkFirst && order[0] < walkFirst + walkSize, "the walk photo went into a walk slot (" + order[0] + ")");
                    int kind = Get<int>(uDirector, "_walkLastKind");
                    if (kind == KominkaDogRun.KD_PhotoDirector.WalkShotDogEye)
                    {
                        Vector3 pos = Get<Vector3>(uDirector, "_walkLastPos"), dir = Get<Vector3>(uDirector, "_walkLastDir"), head = Get<Vector3>(uDirector, "_walkLastHead"), rf = Get<Vector3>(uDirector, "_walkLastRootFwd");
                        Expect(Vector3.Distance(cam.transform.position, pos) < 0.001f && Vector3.Angle(cam.transform.forward, dir) < 0.5f, "the camera is where the director put it");
                        Expect(Mathf.Approximately(cam.fieldOfView, KH_PhotoGalleryEditor.DogEyeFov), "dog's-eye FOV " + cam.fieldOfView);
                        rf.y = 0f; rf.Normalize();
                        Vector3 off = pos - head; float along = Vector3.Dot(off, rf);
                        Vector3 side = off - rf * along - Vector3.up * off.y;
                        Expect(along >= KH_PhotoGalleryEditor.DogEyeAheadMin - 0.005f && along <= KH_PhotoGalleryEditor.DogEyeAheadMax + 0.005f && Math.Abs(off.y - KH_PhotoGalleryEditor.DogEyeLift) < 0.005f && side.magnitude < 0.01f,
                            "camera in front of the muzzle: " + along.ToString("F3") + " m ahead of the head anchor along the dog's forward, " + off.y.ToString("F3") + " m up, " + side.magnitude.ToString("F3") + " m aside");
                        float r = KH_PhotoGalleryEditor.DogEyePitch * Mathf.Deg2Rad;
                        Vector3 want = rf * Mathf.Cos(r) + Vector3.up * Mathf.Sin(r);
                        Expect(Vector3.Angle(dir, want) < 2f, "looking where the dog faces: " + Vector3.Angle(dir, want).ToString("F2") + "° from its flattened forward tilted up " + KH_PhotoGalleryEditor.DogEyePitch + "°");
                        int dog = Get<int>(uDirector, "_walkLastDog");
                        var heads = Get<Transform[]>(uDirector, "dogHeadAnchors");
                        Expect(dog == WalkDog && heads[dog] != null && Vector3.Distance(heads[dog].position, head) < 2f, "the dog's-eye is the leashed dog's (" + dog + ")");
                    }
                    else if (kind == KominkaDogRun.KD_PhotoDirector.WalkShotSelfie)
                    {
                        Note("the dog's-eye was skipped here (" + WalkReason() + "): the fallback selfie is checked instead");
                        CheckSelfie(WalkDog);
                    }
                    else Fail("the director reports no walk shot (" + WalkReason() + ")");
                }));
            }

            // ---- H7: no walk photo during the travel fade and for 2 s after it ----
            if (On("H7-fade-gate"))
            {
                steps.Add(Do("H7-fade-gate", () =>
                {
                    if (uFade == null) { Fail("KH_TravelFade is not in the scene (Course/84): the fade gate cannot be tested"); return; }
                    capturesBefore = Captures(); PokeTimeTrigger();
                    uFade.SendCustomEvent("_Begin");
                    waitFrom = EditorApplication.timeSinceStartup;
                }));
                steps.Add(Until("H7-fade-gate", () => EditorApplication.timeSinceStartup - waitFrom > 1.5, 3, "wait"));
                steps.Add(Do("H7-fade-gate", () =>
                {
                    object skip; uWatcher.TryGetProgramVariable("_lastSkip", out skip);
                    Expect(Captures() == capturesBefore, "no walk photo during the fade (" + Captures() + " vs " + capturesBefore + ")");
                    Expect((skip as string) == "fade", "the watcher says it waits for the fade ('" + skip + "')");
                    uFade.SendCustomEvent("_Release");
                }));
                steps.Add(Until("H7-fade-gate", () => Get<int>(uFade, "_state") == 0, 3, "the fade did not finish after _Release"));
                steps.Add(Do("H7-fade-gate", () => waitFrom = EditorApplication.timeSinceStartup));
                steps.Add(Until("H7-fade-gate", () => EditorApplication.timeSinceStartup - waitFrom > 1.5, 3, "wait"));
                steps.Add(Do("H7-fade-gate", () => Expect(Captures() == capturesBefore, "no walk photo in the first 1.5 s after the fade")));
                steps.Add(Until("H7-fade-gate", () => Captures() == capturesBefore + 1, 4, "no walk photo after the 2 s grace"));
                steps.Add(Do("H7-fade-gate", () =>
                {
                    double after = EditorApplication.timeSinceStartup - waitFrom;
                    Expect(after >= 1.9, "the first walk photo came " + after.ToString("F2") + " s after the fade ended (>= 2 s grace)");
                }));
                steps.Add(Until("H7-fade-gate", () => !cam.enabled, 1, "camera stayed enabled"));
            }

            // ---- H8: the selfie-like shot, and its refusal when no own dog can be in the frame ----
            if (On("H8-selfie"))
            {
                steps.Add(Do("H8-selfie", () =>
                {
                    // stand 2 m from the dog with the back to it: the dog is behind the player, where an arm's-length selfie sees it
                    var lp = Networking.LocalPlayer;
                    Vector3 dogPos = Get<Transform[]>(uDirector, "dogRoots")[WalkDog].position;
                    Vector3 away = lp.GetPosition() - dogPos; away.y = 0f;
                    if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                    away.Normalize();
                    Vector3 target = dogPos + away * 2f;
                    if (UnityEngine.AI.NavMesh.SamplePosition(target, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) target = hit.position;
                    lp.TeleportTo(target, Quaternion.LookRotation(away, Vector3.up));
                    waitFrom = EditorApplication.timeSinceStartup;
                    Note("teleported 2 m from dog " + WalkDog + ", facing away from it");
                }));
                steps.Add(Until("H8-selfie", () => EditorApplication.timeSinceStartup - waitFrom > 0.6, 2, "wait"));
                steps.Add(Do("H8-selfie", () =>
                {
                    capturesBefore = Captures();
                    uDirector.SetProgramVariable("walkDog", -1); uDirector.SetProgramVariable("walkOwnMask", 1 << WalkDog);
                    uDirector.SetProgramVariable("walkDogEyeFirst", false); uDirector.SetProgramVariable("walkUseLook", false); uDirector.SetProgramVariable("walkUseBackdrop", false);
                    uDirector.SendCustomEvent("_WalkShoot");
                    int kind = Get<int>(uDirector, "_walkLastKind");
                    Expect(kind == KominkaDogRun.KD_PhotoDirector.WalkShotSelfie, "selfie first with the dog behind the player -> a selfie (kind " + kind + ", " + WalkReason() + ")");
                }));
                steps.Add(Until("H8-selfie", () => Captures() == capturesBefore + 1 || current.status == "FAIL", 3, "the selfie was not captured"));
                steps.Add(Until("H8-selfie", () => !cam.enabled, 1, "camera stayed enabled"));
                steps.Add(Do("H8-selfie", () => { if (Get<int>(uDirector, "_walkLastKind") == KominkaDogRun.KD_PhotoDirector.WalkShotSelfie) CheckSelfie(WalkDog); }));
                steps.Add(Do("H8-selfie", () =>
                {
                    // a far dog as the only "own" dog: no angle can hold it within 6 m -> no selfie (the dog's-eye of that dog, or nothing)
                    var roots = Get<Transform[]>(uDirector, "dogRoots");
                    var head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                    int far = -1; float best = KH_PhotoGalleryEditor.SelfieDogMax + 2f;
                    for (int i = 0; i < roots.Length; i++) { if (i == WalkDog || roots[i] == null) continue; float d = Vector3.Distance(roots[i].position, head); if (d > best) { best = d; far = i; } }
                    if (far < 0) { Note("no dog farther than " + (KH_PhotoGalleryEditor.SelfieDogMax + 2f) + " m: the refusal is not tested this run"); return; }
                    capturesBefore = Captures();
                    uDirector.SetProgramVariable("walkDog", -1); uDirector.SetProgramVariable("walkOwnMask", 1 << far); uDirector.SetProgramVariable("walkDogEyeFirst", false);
                    uDirector.SendCustomEvent("_WalkShoot");
                    int kind = Get<int>(uDirector, "_walkLastKind"); string why = WalkReason();
                    Expect(kind != KominkaDogRun.KD_PhotoDirector.WalkShotSelfie && why.Contains("selfie: no own dog in view"), "dog " + far + " at " + best.ToString("F1") + " m: no selfie of an empty frame (kind " + kind + ", '" + why + "')");
                    Note(kind == KominkaDogRun.KD_PhotoDirector.WalkShotDogEye ? "fell back to the dog's-eye of dog " + far : "took nothing (" + why + ")");
                }));
                steps.Add(Until("H8-selfie", () => !cam.enabled, 2, "camera stayed enabled"));
            }

            // ---- unleash and see the watcher stop ----
            // under H0 (always run and normally PASS) so that a failed walk case does not skip the unleash and leave the dog on the leash for H3 / H4
            steps.Add(Do("H0-ready", () => { if (leashedByHarness) Leash(false); }));
            steps.Add(Until(last, () => !Get<bool>(uWatcher, "_ticking"), 3, "the walk watcher kept running with no dog on the leash"));
        }

        static void AddOffSteps()
        {
            steps.Add(Do("H3-off", () => uAlbum.SendCustomEvent("_ToggleCapture")));
            steps.Add(Until("H3-off", () => SettingsReady() && Get<int>(uSettings, "_mode") == 0, 5, "the settings row did not switch OFF"));
            steps.Add(Do("H3-off", () =>
            {
                Expect(!uDirector.enabled, "OFF: the photo director is disabled (no per-frame watch)");
                Expect(SavedMode() == 0, "OFF saved to PlayerData (0)");
                var label = Get<TMPro.TMP_Text>(uAlbum, "settingsToggleLabel");
                Expect(label != null && label.text.EndsWith("OFF"), "settings row reads OFF ('" + (label != null ? label.text : "") + "')");
                capturesBefore = Captures();
                countBeforeOff = Get<int>(uAlbum, "_count");
                Expect(countBeforeOff > 0, "there are photos to keep before OFF (" + countBeforeOff + "; 0 = H1/H2 took nothing, see those cases)");
                Trigger(1);
            }));
            double offAt = 0;
            steps.Add(Do("H3-off", () => offAt = EditorApplication.timeSinceStartup));
            steps.Add(Until("H3-off", () => EditorApplication.timeSinceStartup - offAt > 1.0, 3, "wait"));
            steps.Add(Do("H3-off", () =>
            {
                Expect(Captures() == capturesBefore && !cam.enabled, "OFF: a catch notice takes no photo");
                int cnt = Get<int>(uAlbum, "_count");
                Expect(countBeforeOff > 0 && cnt == countBeforeOff && boardPhotos.Take(cnt).All(b => b.gameObject.activeSelf), "OFF: the " + countBeforeOff + " photos stay viewable (now " + cnt + ")");
                uAlbum.SendCustomEvent("_ToggleCapture");
            }));
            steps.Add(Until("H3-off", () => SettingsReady() && Get<int>(uSettings, "_mode") == 1, 5, "the settings row did not switch back ON"));
            steps.Add(Until("H3-off", () => uDirector.enabled, 2, "ON: the photo director was not enabled again"));
            steps.Add(Do("H3-off", () =>
            {
                Expect(SavedMode() == 1, "ON saved to PlayerData (1)");
                var slots = Slots(); bool same = runtimeSlots != null && slots.Length == runtimeSlots.Length; for (int i = 0; same && i < slots.Length; i++) if (slots[i] != runtimeSlots[i]) same = false;
                Expect(same, "ON again kept the same textures (photos kept, nothing re-created)");
            }));
        }

        static void AddMenuSteps()
        {
            steps.Add(Do("H4-menu", () =>
            {
                tabBefore = Get<int>(uPouch, "_tab");
                var panel = Get<GameObject>(uPouch, "panelRoot");
                if (!panel.activeSelf) uPouch.SendCustomEvent("_Toggle");
            }));
            steps.Add(Until("H4-menu", () => Get<GameObject>(uPouch, "panelRoot").activeSelf, 2, "the hand menu did not open"));
            steps.Add(Do("H4-menu", () => uPouch.SendCustomEvent("_PhotoOpen")));
            steps.Add(Until("H4-menu", () => Get<GameObject>(uAlbum, "menuPage").activeInHierarchy, 2, "[写真] did not open the photo page"));
            steps.Add(Do("H4-menu", () =>
            {
                var order = Order(); var slots = Slots();
                var thumbs = Get<UnityEngine.UI.RawImage[]>(uAlbum, "menuThumbs"); var buttons = Get<GameObject[]>(uAlbum, "menuThumbButtons");
                int cnt = Get<int>(uAlbum, "_count");
                Expect(cnt > 0, "photos exist to show in the menu (" + cnt + ")");
                bool same = cnt > 0;
                for (int k = 0; k < Math.Min(cnt, PageSize); k++) if (order[k] < 0 || order[k] >= slots.Length || thumbs[k].texture != slots[order[k]] || thumbs[k].texture != boardPhotos[k].sharedMaterial.mainTexture || !buttons[k].activeInHierarchy) same = false;
                Expect(same, "page 1 thumbnails show the same RenderTextures as the board, newest first");
                Expect(Get<int>(uAlbum, "_menuPage") == 0, "the page opens on page 1");
                Expect(Get<GameObject>(uAlbum, "menuNewMark").activeInHierarchy, "the 'new' mark is on the newest photo (taken while the page was closed)");
                var pager = Get<GameObject>(uAlbum, "menuPager");
                Expect(pager.activeInHierarchy == (cnt > PageSize), "menu pager shown iff more than " + PageSize + " photos (" + cnt + ")");
                var title = Get<TMPro.TextMeshProUGUI>(uPouch, "sectionTitleLabel");
                Note("section title: '" + (title != null ? title.text : "") + "'");
                if (cnt > PageSize)
                {
                    int pages = (cnt + PageSize - 1) / PageSize;
                    uAlbum.SendCustomEvent("_MenuPageNext");
                    Expect(Get<int>(uAlbum, "_menuPage") == 1, "▶ -> page 2");
                    CheckMenuPage(1);
                    var label = Get<TMPro.TMP_Text>(uAlbum, "menuPageLabel");
                    Expect(label != null && label.text == "2/" + pages, "menu label '2/" + pages + "' ('" + (label != null ? label.text : "") + "')");
                    Expect(!Get<GameObject>(uAlbum, "menuNewMark").activeInHierarchy, "the 'new' mark is only on page 1");
                    uAlbum.SendCustomEvent("_MenuPick1");
                    Expect(Get<GameObject>(uAlbum, "menuLargeRoot").activeInHierarchy && Get<UnityEngine.UI.RawImage>(uAlbum, "menuLarge").texture == slots[order[PageSize + 1]], "page 2 thumbnail 2 opens slot " + order[PageSize + 1]);
                    uAlbum.SendCustomEvent("_MenuPagePrev");
                    Expect(Get<int>(uAlbum, "_menuPage") == 0 && !Get<GameObject>(uAlbum, "menuLargeRoot").activeSelf, "◀ -> page 1 (the large view closes)");
                    uAlbum.SendCustomEvent("_MenuPagePrev");
                    Expect(Get<int>(uAlbum, "_menuPage") == pages - 1, "◀ on page 1 wraps to the last page " + pages);
                    CheckMenuPage(pages - 1);
                    uAlbum.SendCustomEvent("_MenuPageNext");
                    Expect(Get<int>(uAlbum, "_menuPage") == 0, "▶ on the last page wraps to page 1");
                    CheckMenuPage(0);
                }
                else Note("menu pager not tested: " + cnt + " photos (one page; H6 makes 3 pages)");
                uAlbum.SendCustomEvent("_MenuPick" + Math.Min(3, Math.Max(0, cnt - 1)));
            }));
            steps.Add(Do("H4-menu", () =>
            {
                var order = Order(); var slots = Slots();
                int pick = Math.Min(3, Math.Max(0, Get<int>(uAlbum, "_count") - 1));
                bool has = Get<int>(uAlbum, "_count") > 0 && order[pick] >= 0 && order[pick] < slots.Length;
                Expect(has && Get<GameObject>(uAlbum, "menuLargeRoot").activeInHierarchy && Get<UnityEngine.UI.RawImage>(uAlbum, "menuLarge").texture == slots[order[pick]], "thumbnail " + (pick + 1) + " opens large with slot " + order[pick] + (has ? "" : " (no photo)"));
                uAlbum.SendCustomEvent("_MenuLargeClose");
                Expect(!Get<GameObject>(uAlbum, "menuLargeRoot").activeSelf, "large view closes");
                uAlbum.SendCustomEvent("_MenuHelpOpen");
                var body = Get<TMPro.TMP_Text>(uAlbum, "menuHelpBody");
                Expect(Get<GameObject>(uAlbum, "menuHelpRoot").activeInHierarchy && body != null && body.text.Length > 20, "[?] shows how photos work");
                uAlbum.SendCustomEvent("_MenuHelpClose");
                uPouch.SendCustomEvent("_PhotoBack");
            }));
            steps.Add(Until("H4-menu", () => !Get<GameObject>(uAlbum, "menuPage").activeInHierarchy, 2, "[もどる] did not leave the photo page"));
            steps.Add(Do("H4-menu", () =>
            {
                Expect(Get<int>(uPouch, "_tab") == tabBefore && !Get<bool>(uPouch, "_settingsOpen"), "[もどる] returned to the tab that was open (" + tabBefore + ")");
                uPouch.SendCustomEvent("_Close");
            }));

            // ---- board pager (IMP-WLD-007 W1): ▶ shows the next 8, and the board returns to page 1 by itself ----
            steps.Add(Do("H4-menu", () =>
            {
                int cnt = Get<int>(uAlbum, "_count");
                if (cnt <= PageSize) { Note("board pager not tested: " + cnt + " photos (one page; H6 makes 3 pages)"); return; }
                uAlbum.SetProgramVariable("boardPageResetSeconds", 1f);   // runtime only (the scene keeps 60 s)
                uAlbum.SendCustomEvent("_BoardPageNext");
                Expect(Get<int>(uAlbum, "_boardPage") == 1, "board ▶ -> page 2");
                CheckBoardOrder(-1, 1);
                var label = Get<TMPro.TMP_Text>(uAlbum, "boardPageLabel");
                int pages = (cnt + PageSize - 1) / PageSize;
                Expect(label != null && label.text == "2/" + pages, "board label '2/" + pages + "' ('" + (label != null ? label.text : "") + "')");
            }));
            steps.Add(Until("H4-menu", () => Get<int>(uAlbum, "_count") <= PageSize || Get<int>(uAlbum, "_boardPage") == 0, 4, "the board did not return to page 1 after boardPageResetSeconds"));
            steps.Add(Do("H4-menu", () =>
            {
                if (Get<int>(uAlbum, "_count") > PageSize) CheckBoardOrder(-1, 0);
                uAlbum.SetProgramVariable("boardPageResetSeconds", 60f);
            }));
        }

        // ================================================================ evidence

        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }

        static void End(string status, string reason)
        {
            if (report == null || ending) return;
            report.status = status; report.reason = reason; ending = true; SafeSave();
            Debug.Log("[KH Photo Harness] PHOTO_HARNESS_" + status + " " + reason);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode(); else Finish();
        }

        static void Finish()
        {
            if (report == null) return;
            try
            {
                Application.runInBackground = report.originalBackground; report.settingsRestored = Application.runInBackground == report.originalBackground;
                RestorePlayerData(); report.playerDataRestored = true;
                report.hashAfter = Hash(report.scene); report.sceneFileUnchanged = report.hashBefore == report.hashAfter;
                report.endedUtc = DateTime.UtcNow.ToString("o");
                if (report.status == "PASS" && !report.sceneFileUnchanged) { report.status = "FAIL"; report.reason += " | the scene file changed"; }
            }
            catch (Exception e) { report.status = "EVIDENCE_ERROR"; report.reason = "final evidence failed: " + e.Message; }
            finally
            {
                SafeSave();
                Debug.Log("[KH Photo Harness] PHOTO_HARNESS_" + report.status + " finished (" + Dir + "/latest.json) sceneUnchanged " + report.sceneFileUnchanged + " playerDataRestored " + report.playerDataRestored);
                SessionState.SetBool(Key, false); report = null; ending = false; steps = null; step = -1;
            }
        }

        static void BackupPlayerData()
        {
            report.playerDataBackup = Dir + "/" + report.runId + "/playerdata"; Directory.CreateDirectory(report.playerDataBackup);
            string storage = Path.GetFullPath("ClientSimStorage/PlayerData");
            var files = Directory.Exists(storage) ? Directory.GetFiles(storage, "*_VRCDefaultWorldScene.json") : new string[0];
            foreach (var path in files) File.Copy(path, report.playerDataBackup + "/" + Path.GetFileName(path), true);
            File.WriteAllLines(report.playerDataBackup + "/manifest.txt", files.Select(Path.GetFileName));
        }

        static void RestorePlayerData()
        {
            if (string.IsNullOrEmpty(report.playerDataBackup)) return;
            string storage = Path.GetFullPath("ClientSimStorage/PlayerData"); Directory.CreateDirectory(storage);
            var names = File.ReadAllLines(report.playerDataBackup + "/manifest.txt");
            foreach (var name in names)
            {
                if (Path.GetFileName(name) != name || !name.EndsWith("_VRCDefaultWorldScene.json", StringComparison.Ordinal)) throw new InvalidDataException("unsafe backup entry " + name);
                File.Copy(report.playerDataBackup + "/" + name, Path.Combine(storage, name), true);
            }
            foreach (var path in Directory.GetFiles(storage, "*_VRCDefaultWorldScene.json"))
                if (!names.Contains(Path.GetFileName(path))) File.Delete(path);   // only this scene's saves created by this run
        }

        static void SafeSave() { try { Save(); } catch (Exception e) { Debug.LogError("[KH Photo Harness] evidence write failed: " + e.Message); } }
        static void Save()
        {
            Directory.CreateDirectory(Dir + "/" + report.runId);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Dir + "/" + report.runId + "/report.json", json); File.WriteAllText(Journal, json); File.WriteAllText(Dir + "/latest.json", json);
        }
    }
}
