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
    /// IMP-WLD-007 W1 で 24 枠・8 枚ずつのページになった。IMP-WLD-011 で出来事 4 カテゴリ (散歩 0..9 / 撫で 10..13 / 遊び 14..17 / 洗う 18..23)。
    /// 枠の番号は KH_PhotoGalleryEditor.GroupSizes から計算する (下の番号は今の配分での例)。
    ///   H0-ready          入室 → 設定の復元 → 実行時の RT 24 枚が端末の寸法 (Editor = PC 1280x720) で作られ、撮影の見回りが有効、カメラは無効
    ///   H1-capture        きっかけ (フリスビーを取った: KD_PhotoDirector._OnFrisbeeCatch) → 遊びの最初の枠 (14) へ 1 枚。色が入っている (真っ黒・一色でない)。
    ///                     ボードの左上と縮小表示がその RT を出す。撮り終えたらカメラは無効
    ///   H2-wrap           さらに 4 回 → 遊びの 5 枚目で遊びのいちばん古い枠 (14) が置き換わり、新しい順に並ぶ (14, 17, 16, 15)。散歩・撫で・洗うの枠は使わない
    ///   H6-category       散歩の枠へ 11 回 (散歩のきっかけは W2 なので、実行時だけ _hillsideGroupOverride = 0 にして撮る) → 散歩の中だけで上書き (0 が 2 回目)、
    ///                     それまでの写真は残る (H4 のページ数の期待は実際の枚数から計算する)
    /// IMP-WLD-011 (洗いの写真。ラブ = 犬 2 の KH_DogWash へ _LocalContact を直接送る。泡・すすぎのしきい値は実行時だけ 0 にして節目の配線を見る):
    ///   H9-wash           洗っていない端末 (自分のストロークなし) では清潔の合図が来ても撮らない → 自分のスポンジで泡の 1 枚、シャワー 2 s ですすぎの 1 枚、
    ///                     清潔の合図で洗い上がりの 1 枚 = 洗うの枠に 3 枚 → 同じ節目をもう一度起こしても撮らない → スポンジで洗い直すと数え直す →
    ///                     洗われている犬 (洗いの保持) は撫で続けても撫でのバーストが出ない、保持を解くと 2 枚のバーストが撫での枠へ →
    ///                     撫で・遊び・散歩を各枠 1 周以上撮っても、洗うの枠の 3 枚 (RT の中身) は残る
    ///   H3-off            設定の [写真: ON/OFF] (KH_PhotoAlbum._ToggleCapture) で OFF → 見回りが止まり、きっかけが来ても撮らない、写真は残る、保存値 0。
    ///                     もう一度押して ON → 保存値 1、RT は作り直さない (同じ寸法)
    ///   H4-menu           手元メニューの [写真] (KD_TreatPouch._PhotoOpen) → 写真の面がボードと同じ RT を出す、縮小表示の吸着の板 (IMP-WLD-009: 同じ RT、大きい表示・説明の間は隠れる)、
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
        /// IMP-WLD-011: H9 (洗い) は H2 の後 (H2 は H1 の 1 枚だけがある前提で並びを見る) で、H3 / H4 の前 (写真の枚数は実際の数から計算される)。
        public static readonly string[] AllCases = { "H0-ready", "H1-capture", "H2-wrap", "H6-category", "H9-wash", "H6w-walk-trigger", "H7-fade-gate", "H8-selfie", "H3-off", "H4-menu", "H5-no-camera-left" };

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
            // the walk cases (H6w / H7 / H8) and the wash case (H9) need no earlier photo
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
            if (report.casesRun.Contains("H9-wash")) ResolveWash(all);
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
            CheckThumbSnaps(page, true, "menu page " + (page + 1));
        }

        /// <summary>IMP-WLD-009: 縮小表示の吸着の板。shown = 一覧が見えている (大きい表示・説明が閉じている) とき、写真のある位置だけ出て、縮小表示と同じ RT を持つ。
        /// shown = false のときは 8 枚とも隠れている。吸着そのもの (VRChat カメラ) は ClientSim では見ない。</summary>
        static void CheckThumbSnaps(int page, bool shown, string when)
        {
            var order = Order(); var slots = Slots();
            int count = Get<int>(uAlbum, "_count");
            var snaps = Get<Renderer[]>(uAlbum, "menuThumbSnaps");
            bool ok = snaps != null && snaps.Length == PageSize;
            for (int k = 0; ok && k < PageSize; k++)
            {
                int p = page * PageSize + k;
                bool want = shown && p < count;
                if (snaps[k] == null || snaps[k].gameObject.activeSelf != want) ok = false;
                else if (want && snaps[k].sharedMaterial.mainTexture != slots[order[p]]) ok = false;
            }
            Expect(ok, when + ": thumbnail snaps " + (shown ? "shown under the photos (same RT as the thumbnail), hidden where there is none" : "all hidden (large view / help open)"));
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
                Expect(Get<int[]>(uGallery, "slotGroups").SequenceEqual(KH_PhotoGalleryEditor.SlotGroups()) && Get<bool>(uDirector, "hillsideEventGroups"),
                    KominkaDogRun.KD_PhotoDirector.HillsideGroupCount + " event categories (" + string.Join(" / ", KH_PhotoGalleryEditor.GroupNames.Select((n, g) => n + " " + KH_PhotoGalleryEditor.GroupSizes[g])) + ") and the director picks them");
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
                    var want = new List<int> { playFirst }; for (int s = playFirst + playSize - 1; s > playFirst; s--) want.Add(s);   // 14, 17, 16, 15 (IMP-WLD-011 layout)
                    int count = Get<int>(uAlbum, "_count");
                    Expect(count == playSize && order.Take(count).SequenceEqual(want), "after " + (playSize + 1) + " play captures the oldest play slot (" + playFirst + ") was replaced, newest first: [" + string.Join(",", order.Take(count)) + "] want [" + string.Join(",", want) + "]");
                    Expect(order.Take(count).All(s => s >= playFirst && s < playFirst + playSize), "no walk / pet / wash slot was used by play events");
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
                    var want = new List<int> { walkFirst }; for (int s = walkFirst + walkSize - 1; s > walkFirst; s--) want.Add(s);   // 0, 9, 8, … 1 (IMP-WLD-011 layout)
                    want.AddRange(before);                                                                                            // the play photos keep their order behind
                    Expect(count == walkSize + countBefore && order.Take(count).SequenceEqual(want), (walkSize + 1) + " walk captures: walk wraps inside itself (" + walkFirst + " twice), the earlier photos stay: ["
                        + string.Join(",", order.Take(count)) + "] want [" + string.Join(",", want) + "]");
                    Expect(Get<int[]>(uGallery, "_nextInGroup")[KominkaDogRun.KD_PhotoDirector.HillsideGroupWalk] == 1, "the walk category wraps (next = its 2nd slot)");
                    CheckBoardOrder(count);
                    object pagerObj; uAlbum.TryGetProgramVariable("boardPager", out pagerObj);
                    Expect(pagerObj is GameObject pg && pg.activeSelf == (count > PageSize), "board pager shown iff more than " + PageSize + " photos (" + count + ")");
                }));
            }

            // ---- H9: wash photos (IMP-WLD-011) ----
            if (On("H9-wash")) AddWashSteps();

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
        static float fadeEndTime;   // H7: Time.time of the first frame the fade is idle again
        static float eventMark;     // Time.time at the start of a capture window (DescribeCaptures)

        static string SlotCategory(int s)
        {
            for (int g = KominkaDogRun.KD_PhotoDirector.HillsideGroupCount - 1; g >= 0; g--) if (s >= GroupFirst(g)) return KH_PhotoGalleryEditor.GroupNames[g];
            return "?";
        }

        /// <summary>このウィンドウで増えた写真の枠とカテゴリ (並びの新しい方から)、と t0 以後に時刻を残した出来事 (どの撮影か)。
        /// 受け渡し・集合写真は時刻を残さないので、どれにも当たらなければそれ。</summary>
        static string DescribeCaptures(int since, float t0)
        {
            int n = Captures() - since; var order = Order(); var parts = new List<string>();
            for (int k = 0; k < n && k < order.Length; k++) parts.Add("slot " + order[k] + " (" + SlotCategory(order[k]) + ")");
            var events = new List<string>();
            void Ev(UdonBehaviour u, string field, string name)
            {
                object o;
                if (u != null && u.TryGetProgramVariable(field, out o) && o is float f && f >= t0) events.Add(name + " at " + f.ToString("F2"));
            }
            Ev(uDirector, "_lastChaseShot", "chase (a dog started running: KD_DogWanderController.Shared.cs:424 / fetch chase)");
            Ev(uDirector, "_lastPlayShot", "dogs playing");
            Ev(uDirector, "_lastFrisbeeShot", "frisbee catch");
            Ev(uDirector, "_lastPetShot", "pet burst");
            Ev(uWatcher, "_lastWalkShotAt", "walk");
            return n + " new [" + string.Join(", ", parts) + "]; events since Time.time " + t0.ToString("F2") + ": "
                + (events.Count == 0 ? "none with a time (a fetch drop or a group photo)" : string.Join(", ", events));
        }
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
                // Counted by the watcher (_shots), not by all captures: the leashed dog's run edges (KD_DogWanderController.Shared.cs:424 ->
                // _OnDogChase) and the other dogs' play edges take their own "play" photos at any time (run 20261003-102254, H7)
                steps.Add(Do("H6w-walk-trigger", () => { capturesBefore = Captures(); shotsBefore = Get<int>(uWatcher, "_shots"); eventMark = Time.time; uWatcher.SetProgramVariable("_fadeUntil", 0f); PokeTimeTrigger(); }));
                steps.Add(Until("H6w-walk-trigger", () => Get<int>(uWatcher, "_shots") > shotsBefore, 4, "no walk photo after the time + distance trigger (see the notes for the watcher's skip)"));
                steps.Add(Until("H6w-walk-trigger", () => Captures() >= capturesBefore + 1 && !cam.enabled, 1, "the walk photo did not finish (camera still enabled)"));
                steps.Add(Do("H6w-walk-trigger", () =>
                {
                    object skip; uWatcher.TryGetProgramVariable("_lastSkip", out skip);
                    Note("watcher last skip '" + skip + "', director reason '" + WalkReason() + "'");
                    Expect(Get<int>(uWatcher, "_shots") == shotsBefore + 1, "the watcher counted one walk photo");
                    int extra = Captures() - capturesBefore - 1;
                    if (extra != 0) Note("other captures in this window: " + DescribeCaptures(capturesBefore, eventMark));
                    var order = Order();
                    int newest = Math.Max(1, Captures() - capturesBefore);
                    Expect(order.Take(newest).Any(s => s >= walkFirst && s < walkFirst + walkSize), "the walk photo went into a walk slot (newest " + string.Join(",", order.Take(newest)) + ")");
                    int kind = Get<int>(uDirector, "_walkLastKind");
                    if (kind == KominkaDogRun.KD_PhotoDirector.WalkShotDogEye)
                    {
                        Vector3 pos = Get<Vector3>(uDirector, "_walkLastPos"), dir = Get<Vector3>(uDirector, "_walkLastDir"), head = Get<Vector3>(uDirector, "_walkLastHead"), rf = Get<Vector3>(uDirector, "_walkLastRootFwd");
                        if (extra == 0) Expect(Vector3.Distance(cam.transform.position, pos) < 0.001f && Vector3.Angle(cam.transform.forward, dir) < 0.5f, "the camera is where the director put it");
                        else Note("the camera moved on to another photo afterwards: the pose is judged from the director's record only");
                        if (extra == 0) Expect(Mathf.Approximately(cam.fieldOfView, KH_PhotoGalleryEditor.DogEyeFov), "dog's-eye FOV " + cam.fieldOfView);
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
                    // the poke and _Begin are one editor callback: no Udon tick runs between them, so every watcher tick after the poke sees the fade
                    capturesBefore = Captures(); shotsBefore = Get<int>(uWatcher, "_shots"); eventMark = Time.time; PokeTimeTrigger();
                    uFade.SendCustomEvent("_Begin");
                    waitFrom = EditorApplication.timeSinceStartup;   // only for the plain waits below; the judgement uses Time.time (the product's clock)
                }));
                steps.Add(Until("H7-fade-gate", () => EditorApplication.timeSinceStartup - waitFrom > 1.5, 3, "wait"));
                steps.Add(Do("H7-fade-gate", () =>
                {
                    object skip; uWatcher.TryGetProgramVariable("_lastSkip", out skip);
                    // A walk photo = the watcher's count (_shots) or a new capture in a walk slot. The dogs' own event photos (chase / play /
                    // drop: category "play") are not walk photos and the fade does not gate them (design §0: the gate is for walk photos)
                    int during = Captures() - capturesBefore;
                    bool walkSlot = during > 0 && Order().Take(during).Any(s => s >= GroupFirst(KominkaDogRun.KD_PhotoDirector.HillsideGroupWalk) && s < GroupFirst(KominkaDogRun.KD_PhotoDirector.HillsideGroupWalk) + KH_PhotoGalleryEditor.GroupSizes[KominkaDogRun.KD_PhotoDirector.HillsideGroupWalk]);
                    Expect(Get<int>(uWatcher, "_shots") == shotsBefore && !walkSlot, "no walk photo during the fade (watcher " + (Get<int>(uWatcher, "_shots") - shotsBefore) + ", new walk-slot captures " + (walkSlot ? "yes" : "none") + ")");
                    Note("captures during the fade: " + (during == 0 ? "none" : DescribeCaptures(capturesBefore, eventMark)));
                    Expect((skip as string) == "fade", "the watcher says it waits for the fade ('" + skip + "')");
                    capturesBefore = Captures(); eventMark = Time.time;   // the next window starts here
                    uFade.SendCustomEvent("_Release");
                }));
                // The fade's end and the shot are both judged on Time.time (the clock KH_PhotoWalkWatcher uses), not on the Editor clock:
                // the end is sampled on the first frame the fade is idle again (Until is polled every frame), and the shot time is the
                // watcher's own record (_lastWalkShotAt = Time.time when _WalkShoot started the capture). A frame hitch cannot move either.
                steps.Add(Until("H7-fade-gate", () =>
                {
                    if (Get<int>(uFade, "_state") != 0) return false;
                    fadeEndTime = Time.time;
                    return true;
                }, 3, "the fade did not finish after _Release"));
                steps.Add(Until("H7-fade-gate", () => Get<int>(uWatcher, "_shots") > shotsBefore, 5, "no walk photo within 5 s after the fade ended (the 2 s grace should have passed)"));
                steps.Add(Until("H7-fade-gate", () => Captures() >= capturesBefore + 1, 1, "the watcher counted a walk photo but no capture finished"));
                steps.Add(Do("H7-fade-gate", () =>
                {
                    float shotAt = Get<float>(uWatcher, "_lastWalkShotAt");
                    float after = shotAt - fadeEndTime;
                    int n = Captures() - capturesBefore;
                    Expect(Get<int>(uWatcher, "_shots") == shotsBefore + 1, "exactly one walk photo across the fade and its grace (watcher " + (Get<int>(uWatcher, "_shots") - shotsBefore) + ")");
                    if (n != 1) Note("captures after the fade (the walk photo + the dogs' own events): " + DescribeCaptures(capturesBefore, eventMark));
                    Expect(after >= 1.9f, "the walk photo was taken " + after.ToString("F2") + " s (Time.time) after the fade ended: none before the 2 s grace, the first one at >= 1.9 s");
                    Note("fade ended at Time.time " + fadeEndTime.ToString("F2") + ", shot at " + shotAt.ToString("F2") + " (the watcher sees the end on its next 0.5 s tick, so 2.0..2.6 s is normal)");
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
                steps.Add(Until("H8-selfie", () => Captures() >= capturesBefore + 1 || current.status == "FAIL", 3, "the selfie was not captured"));
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

        // ================================================================ wash photos (IMP-WLD-011)

        const int WashDog = 2;   // Lab (the walk cases use dog 0)
        static UdonBehaviour uWash, uWashPet;
        static string washContactEntry, washEndEntry;
        static readonly Dictionary<string, string[]> washParams = new Dictionary<string, string[]>();
        static int washCi = -1;
        static Collider washCol;
        static int washShotsBefore, washAskedBefore, washNext0, petNext0;
        static bool petSuppressedBefore, washTouched;
        static float washFoamAvg0, washRinseFoam0, washHold0;
        static int[] washSlots;
        static string[] washPrints;

        /// <summary>The Lab's KH_DogWash and pet controller, and the U#-renamed _LocalContact / _EndContact with their parameter symbols
        /// (the same lookup as the wash harness, KH_CourseWalkHarnessEditor.Wash.cs WsResolve).</summary>
        static void ResolveWash(UdonBehaviour[] all)
        {
            uWash = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_DogWash" && u.TryGetProgramVariable("dogIndex", out object di) && di is int x && x == WashDog);
            if (uWash == null) throw new InvalidOperationException("H9: no KH_DogWash with dogIndex " + WashDog + " (Wash/11)");
            string petName = "KD_PetSystem_" + KH_PhotoGalleryEditor.Keys[WashDog];
            uWashPet = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_DogPetController" && u.gameObject.name == petName);
            if (uWashPet == null) throw new InvalidOperationException("H9: no KD_DogPetController on " + petName);
            var prog = uWash.programSource.SerializedProgramAsset.RetrieveProgram();
            washContactEntry = WashMangled(prog.EntryPoints, "_LocalContact");
            washEndEntry = WashMangled(prog.EntryPoints, "_EndContact");
            washParams.Clear();
            foreach (var p in new[] { "tool", "colliderIndex", "worldPoint", "seconds", "radiusMeters" })
            {
                var syms = new List<string>();
                for (int n = 0; n <= 200; n++) if (prog.SymbolTable.TryGetAddressFromSymbol("__" + n + "_" + p + "__param", out _)) syms.Add("__" + n + "_" + p + "__param");
                if (syms.Count == 0) throw new InvalidOperationException("H9: KH_DogWash has no parameter symbol for " + p);
                washParams[p] = syms.ToArray();
            }
            object o;
            var cols = uWash.TryGetProgramVariable("partColliders", out o) ? o as Array : null;
            var parts = uWash.TryGetProgramVariable("partOfCollider", out o) ? o as int[] : null;
            washCi = -1; washCol = null;
            if (cols != null && parts != null)
                for (int i = 0; i < cols.Length && i < parts.Length && washCi < 0; i++)
                    if (cols.GetValue(i) is Collider c && c != null && parts[i] >= 0) { washCi = i; washCol = c; }
            if (washCi < 0) throw new InvalidOperationException("H9: KH_DogWash (dog " + WashDog + ") has no usable part collider");
        }

        static string WashMangled(IUdonSymbolTable t, string method)
        {
            if (t.TryGetAddressFromSymbol(method, out _)) return method;
            var m = new List<string>();
            for (int n = 0; n <= 200; n++) if (t.TryGetAddressFromSymbol("__" + n + "_" + method, out _)) m.Add("__" + n + "_" + method);
            if (m.Count != 1) throw new InvalidOperationException("H9: KH_DogWash entry " + method + ": " + m.Count + " matches (" + string.Join(",", m) + ")");
            return m[0];
        }

        /// <summary>Every candidate symbol of the parameter gets the value (U# writes its own arguments on every call, so the others are harmless).</summary>
        static void WashSet(string name, object v)
        {
            foreach (var sym in washParams[name])
            {
                if (v is int i) uWash.SetProgramVariable<int>(sym, i);
                else if (v is float f) uWash.SetProgramVariable<float>(sym, f);
                else if (v is Vector3 x) uWash.SetProgramVariable<Vector3>(sym, x);
            }
        }

        /// <summary>times x _LocalContact(tool, 0.25 s) on one part collider's surface (the entry KH_WashTool calls every frame), then _EndContact.
        /// Each call is one counted own stroke (KH_DogWash.Stroke -> PhotoStroke).</summary>
        static void WashStroke(int tool, int times)
        {
            for (int k = 0; k < times; k++)
            {
                bool closest = !(washCol is MeshCollider mc) || mc.convex;
                Vector3 p = closest ? washCol.ClosestPoint(washCol.bounds.center + Vector3.up) : washCol.bounds.center;
                WashSet("tool", tool); WashSet("colliderIndex", washCi); WashSet("worldPoint", p); WashSet("seconds", 0.25f); WashSet("radiusMeters", -1f);
                uWash.SendCustomEvent(washContactEntry);
            }
            WashSet("tool", tool);
            uWash.SendCustomEvent(washEndEntry);
        }

        static int WashShots() { return Get<int>(uDirector, "_washShots"); }
        static int NextIn(int group) { return Get<int[]>(uGallery, "_nextInGroup")[group]; }
        static int Taken() { return Get<int>(uWash, "_photoTaken"); }
        static string WashWhy()
        {
            object a, b; uDirector.TryGetProgramVariable("_washLastReason", out a); uWash.TryGetProgramVariable("_photoLastReason", out b);
            return "director '" + a + "', wash '" + b + "', taken " + Taken();
        }
        static void WashEdge() { uWash.SetProgramVariable("cleanSerial", Get<int>(uWash, "cleanSerial") + 1); uWash.SendCustomEvent("_PhotoEdges"); }

        /// <summary>A sampled hash of an RT (does the slot still hold the same picture).</summary>
        static string Fingerprint(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                var px = tex.GetPixels32(); long h = 17;
                for (int i = 0; i < px.Length; i += 61) h = unchecked(h * 31 + px[i].r * 65536 + px[i].g * 256 + px[i].b);
                return h.ToString("X");
            }
            finally { RenderTexture.active = prev; UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>Puts back what H9 changed at runtime (under H0, so a failed H9 does not leave a petted, held dog for H3 / H4).</summary>
        static void WashRestore()
        {
            if (!washTouched) return;
            uWashPet.SetProgramVariable("_petting", false); uWashPet.SetProgramVariable("_suppressed", petSuppressedBefore);
            uWash.SetProgramVariable("washShotFoamAvg", washFoamAvg0); uWash.SetProgramVariable("washShotRinseFoam", washRinseFoam0); uWash.SetProgramVariable("holdSeconds", washHold0);
            uDirector.SetProgramVariable("_hillsideGroupOverride", -1);
            washTouched = false;
        }

        static void AddWashSteps()
        {
            const string C = "H9-wash";
            int washG = KominkaDogRun.KD_PhotoDirector.HillsideGroupWash, petG = KominkaDogRun.KD_PhotoDirector.HillsideGroupPet;
            int washFirst = GroupFirst(washG), washSize = KH_PhotoGalleryEditor.GroupSizes[washG], petSize = KH_PhotoGalleryEditor.GroupSizes[petG];
            int burst = KH_PhotoGalleryEditor.PetBurstLength;

            // ---- 1) a client with no own stroke on the dog (= watching someone else wash): the clean edge takes nothing ----
            steps.Add(Do(C, () =>
            {
                uDirector.SetProgramVariable("frisbeeShotCooldown", 0f);   // runtime only (the scene keeps 20 s)
                Expect(Get<int>(uDirector, "petBurstLength") == burst, "pet burst length " + burst + " at runtime (Photo/1; actual " + Get<int>(uDirector, "petBurstLength") + ")");
                washFoamAvg0 = Get<float>(uWash, "washShotFoamAvg"); washRinseFoam0 = Get<float>(uWash, "washShotRinseFoam"); washHold0 = Get<float>(uWash, "holdSeconds");
                petSuppressedBefore = Get<bool>(uWashPet, "_suppressed");
                washTouched = true;
                uWash.SetProgramVariable("_photoActive", false);
                washShotsBefore = WashShots(); washAskedBefore = Get<int>(uWash, "_photoAsked"); washNext0 = NextIn(washG);
                WashEdge();
                waitFrom = EditorApplication.timeSinceStartup;
                Note("dog " + WashDog + " (" + KH_PhotoGalleryEditor.Keys[WashDog] + "), part collider " + washCi + " (" + washCol.name + "); wash category next " + washNext0 + "; thresholds foam " + washFoamAvg0 + " / rinse foam " + washRinseFoam0);
            }));
            steps.Add(Until(C, () => EditorApplication.timeSinceStartup - waitFrom > 1.0, 2, "wait"));
            steps.Add(Do(C, () =>
            {
                Expect(WashShots() == washShotsBefore && Get<int>(uWash, "_photoAsked") == washAskedBefore && NextIn(washG) == washNext0,
                    "no own stroke: a clean edge takes no wash photo (" + WashWhy() + ")");
                // ---- 2) own sponge -> the foam photo. Thresholds 0 at runtime: this checks the milestone path, not the foam amount ----
                uWash.SetProgramVariable("washShotFoamAvg", 0f); uWash.SetProgramVariable("washShotRinseFoam", 0f);
                WashStroke(KH_DogWash.ToolSponge, 4);
                Expect(Get<bool>(uWash, "_photoActive") && Taken() == 0, "own sponge strokes started this client's wash session (" + WashWhy() + ")");
            }));
            // the foam check runs at the next KH_DogWash.EvaluateState (0.5 s after a cell change, 4 s at the latest)
            steps.Add(Until(C, () => WashShots() >= washShotsBefore + 1 && !cam.enabled, 6, "no foam photo within 6 s of the own sponge strokes (director _washShots did not move)"));
            steps.Add(Do(C, () =>
            {
                Expect(Get<int>(uDirector, "_washLastKind") == KominkaDogRun.KD_PhotoDirector.WashShotFoam && Get<int>(uDirector, "_washLastDog") == WashDog && (Taken() & 1) != 0,
                    "the foam photo of dog " + WashDog + " (" + WashWhy() + ")");
                Expect(NextIn(washG) == (washNext0 + 1) % washSize, "the foam photo went into the wash category (next " + NextIn(washG) + ")");
                // ---- 3) own shower, 10 x 0.25 s of contact -> the rinse photo at 2.0 s ----
                WashStroke(KH_DogWash.ToolShower, 10);
            }));
            steps.Add(Until(C, () => WashShots() >= washShotsBefore + 2 && !cam.enabled, 3, "no rinse photo after 2.5 s of own shower contact"));
            steps.Add(Do(C, () =>
            {
                Expect(Get<int>(uDirector, "_washLastKind") == KominkaDogRun.KD_PhotoDirector.WashShotRinse && (Taken() & 2) != 0, "the rinse photo (" + WashWhy() + ")");
                // ---- 4) the clean edge -> the done photo (washShotDoneDelay later) ----
                WashEdge();
            }));
            steps.Add(Until(C, () => WashShots() >= washShotsBefore + 3 && !cam.enabled, 3, "no done photo after the clean edge"));
            steps.Add(Do(C, () =>
            {
                Expect(Get<int>(uDirector, "_washLastKind") == KominkaDogRun.KD_PhotoDirector.WashShotDone && Taken() == 7, "the done photo; all three milestones taken (" + WashWhy() + ")");
                var order = Order(); var slots = Slots(); int count = Get<int>(uAlbum, "_count");
                washSlots = Enumerable.Range(0, 3).Select(k => washFirst + (washNext0 + k) % washSize).ToArray();
                Expect(NextIn(washG) == (washNext0 + 3) % washSize && washSlots.All(s => order.Take(count).Contains(s)), "3 wash photos in the wash slots " + string.Join(",", washSlots) + ", shown in the album");
                foreach (var s in washSlots) { string px = PixelCheck(slots[s]); Expect(px.StartsWith("OK"), "wash slot " + s + " holds a picture (" + px + ")"); }
                washPrints = washSlots.Select(s => Fingerprint(slots[s])).ToArray();
                // ---- 5) the same milestones again in the same wash: nothing ----
                washShotsBefore = WashShots(); washAskedBefore = Get<int>(uWash, "_photoAsked");
                WashStroke(KH_DogWash.ToolShower, 10);
                WashEdge();
                waitFrom = EditorApplication.timeSinceStartup;
            }));
            steps.Add(Until(C, () => EditorApplication.timeSinceStartup - waitFrom > 1.5, 3, "wait"));
            steps.Add(Do(C, () =>
            {
                Expect(WashShots() == washShotsBefore && Get<int>(uWash, "_photoAsked") == washAskedBefore && NextIn(washG) == (washNext0 + 3) % washSize,
                    "the same wash: more shower and another clean edge take no second photo (" + WashWhy() + ")");
                // ---- 6) a sponge after the done photo = a new wash. The scene's thresholds back first, so this one dab takes nothing ----
                uWash.SetProgramVariable("washShotFoamAvg", washFoamAvg0); uWash.SetProgramVariable("washShotRinseFoam", washRinseFoam0);
                WashStroke(KH_DogWash.ToolSponge, 1);
                Expect(Get<bool>(uWash, "_photoActive") && Taken() == 0, "a sponge after the done photo starts a new wash: the milestones count again (" + WashWhy() + ")");
                // ---- 7) a washed dog that is petted: no pet burst ----
                uWashPet.SetProgramVariable("_suppressed", true); uWashPet.SetProgramVariable("_petting", true);   // IsPetting stays true (the sampling is off)
                uWash.SetProgramVariable("holdSeconds", 30f); uWash.SetProgramVariable("washSeen", true);
                uWash.SetProgramVariable("washBy", Networking.LocalPlayer.playerId); uWash.SetProgramVariable("washMs", Networking.GetServerTimeInMilliseconds());
                uDirector.SetProgramVariable("_lastPetShot", -1000f);
                petNext0 = NextIn(petG);
                waitFrom = EditorApplication.timeSinceStartup;
                Note("runtime only: dog " + WashDog + " held as washed (washMs = now, holdSeconds 30) and petted (pet controller _suppressed + _petting)");
            }));
            steps.Add(Until(C, () => EditorApplication.timeSinceStartup - waitFrom > 4.5, 6, "wait"));
            steps.Add(Do(C, () =>
            {
                var acc = Get<float[]>(uDirector, "_petAccum");
                Expect(Get<float>(uDirector, "_lastPetShot") < -999f && NextIn(petG) == petNext0 && acc[WashDog] == 0f,
                    "4.5 s of petting a washed dog: no pet burst (accum " + acc[WashDog].ToString("F2") + ", pet next " + NextIn(petG) + " / " + petNext0 + ")");
                uWash.SendCustomEvent("_ReleaseHold");
                uWash.SetProgramVariable("holdSeconds", washHold0);
            }));
            steps.Add(Until(C, () => Get<float>(uDirector, "_lastPetShot") > -999f, 6, "no pet burst within 6 s after the wash hold ended (the dog is still petted)"));
            steps.Add(Until(C, () => (NextIn(petG) - petNext0 + petSize) % petSize == burst % petSize && Get<int>(uDirector, "_burstStep") < 0 && !cam.enabled, 3,
                "the pet burst after the wash did not end with " + burst + " photos in the pet category"));
            steps.Add(Do(C, () =>
            {
                uWashPet.SetProgramVariable("_petting", false); uWashPet.SetProgramVariable("_suppressed", petSuppressedBefore);
                Note("after the hold: one pet burst of " + burst + " photos into the pet category");
            }));
            // ---- 8) pet / play / walk each one full round and one more after the wash: the wash photos stay ----
            foreach (int g in new[] { petG, KominkaDogRun.KD_PhotoDirector.HillsideGroupPlay, KominkaDogRun.KD_PhotoDirector.HillsideGroupWalk })
            {
                int size = KH_PhotoGalleryEditor.GroupSizes[g], group = g;
                for (int i = 0; i <= size; i++)
                {
                    int dog = (i + 1) % 6;
                    steps.Add(Do(C, () => { capturesBefore = Captures(); uDirector.SetProgramVariable("_hillsideGroupOverride", group); Trigger(dog); }));
                    steps.Add(Until(C, () => Captures() >= capturesBefore + 1 && !cam.enabled, 3, KH_PhotoGalleryEditor.GroupNames[group] + " capture after the wash did not happen"));
                }
            }
            steps.Add(Do(C, () =>
            {
                uDirector.SetProgramVariable("_hillsideGroupOverride", -1);
                var slots = Slots(); var order = Order(); int count = Get<int>(uAlbum, "_count");
                var prints = washSlots.Select(s => Fingerprint(slots[s])).ToArray();
                Expect(NextIn(washG) == (washNext0 + 3) % washSize, "the wash category did not move while pet / play / walk took their photos");
                Expect(prints.SequenceEqual(washPrints) && washSlots.All(s => order.Take(count).Contains(s)),
                    "after a full round of pet / play / walk photos the 3 wash photos (slots " + string.Join(",", washSlots) + ") are unchanged and still in the album");
            }));
            // under H0 (always run) so that a failed H9 does not leave the dog petted / held for H3 / H4
            steps.Add(Do("H0-ready", WashRestore));
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
                CheckThumbSnaps(0, true, "page 1 on open");
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
                    uAlbum.SendCustomEvent("_MenuPick1");
                    Expect(Get<GameObject>(uAlbum, "menuLargeRoot").activeInHierarchy && Get<UnityEngine.UI.RawImage>(uAlbum, "menuLarge").texture == slots[order[PageSize + 1]], "page 2 thumbnail 2 opens slot " + order[PageSize + 1]);
                    CheckThumbSnaps(1, false, "page 2 with the large view open");
                    uAlbum.SendCustomEvent("_MenuPagePrev");
                    Expect(Get<int>(uAlbum, "_menuPage") == 0 && !Get<GameObject>(uAlbum, "menuLargeRoot").activeSelf, "◀ -> page 1 (the large view closes)");
                    uAlbum.SendCustomEvent("_MenuPagePrev");
                    Expect(Get<int>(uAlbum, "_menuPage") == pages - 1, "◀ on page 1 wraps to the last page " + pages);
                    CheckMenuPage(pages - 1);
                    uAlbum.SendCustomEvent("_MenuPageNext");
                    Expect(Get<int>(uAlbum, "_menuPage") == 0, "▶ on the last page wraps to page 1");
                    CheckMenuPage(0);
                }
                else Note("menu pager not tested: " + cnt + " photos (one page; H6 / H9 add photos -> 2 pages or more)");
                uAlbum.SendCustomEvent("_MenuPick" + Math.Min(3, Math.Max(0, cnt - 1)));
            }));
            steps.Add(Do("H4-menu", () =>
            {
                var order = Order(); var slots = Slots();
                int pick = Math.Min(3, Math.Max(0, Get<int>(uAlbum, "_count") - 1));
                bool has = Get<int>(uAlbum, "_count") > 0 && order[pick] >= 0 && order[pick] < slots.Length;
                Expect(has && Get<GameObject>(uAlbum, "menuLargeRoot").activeInHierarchy && Get<UnityEngine.UI.RawImage>(uAlbum, "menuLarge").texture == slots[order[pick]], "thumbnail " + (pick + 1) + " opens large with slot " + order[pick] + (has ? "" : " (no photo)"));
                CheckThumbSnaps(0, false, "page 1 with the large view open");
                uAlbum.SendCustomEvent("_MenuLargeClose");
                Expect(!Get<GameObject>(uAlbum, "menuLargeRoot").activeSelf, "large view closes");
                CheckThumbSnaps(0, true, "page 1 after the large view closed");
                uAlbum.SendCustomEvent("_MenuHelpOpen");
                var body = Get<TMPro.TMP_Text>(uAlbum, "menuHelpBody");
                Expect(Get<GameObject>(uAlbum, "menuHelpRoot").activeInHierarchy && body != null && body.text.Length > 20, "[?] shows how photos work");
                CheckThumbSnaps(0, false, "page 1 with the help open");
                uAlbum.SendCustomEvent("_MenuHelpClose");
                CheckThumbSnaps(0, true, "page 1 after the help closed");
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
                if (cnt <= PageSize) { Note("board pager not tested: " + cnt + " photos (one page; H6 / H9 add photos -> 2 pages or more)"); return; }
                uAlbum.SetProgramVariable("boardPageResetSeconds", 1f);   // runtime only (the scene keeps 60 s)
                Note("the board's return to page 1 is shortened from 60 s to 1 s at runtime only (the scene value stays 60 s; Photo/2 checks it)");
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
