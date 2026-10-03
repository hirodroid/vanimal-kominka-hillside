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
    /// IMP-WLD-005 Photo/3 - Harness: 自動写真の小さいケース群 (ClientSim、1 クライアント、約 1 分。歩行・洗浄のハーネスは回さない)。
    ///   H0-ready          入室 → 設定の復元 → 実行時の RT が端末の寸法 (Editor = PC 1280x720) で作られ、撮影の見回りが有効、カメラは無効
    ///   H1-capture        きっかけ (フリスビーを取った: KD_PhotoDirector._OnFrisbeeCatch) → 次の枠 (0) へ 1 枚。色が入っている (真っ黒・一色でない)。
    ///                     ボードの左上と縮小表示がその RT を出す。撮り終えたらカメラは無効
    ///   H2-wrap           さらに 8 回 → 9 枚目でいちばん古い枠 (0) が置き換わり、新しい順に並ぶ (0, 7, 6, … 1)
    ///   H3-off            設定の [写真: ON/OFF] (KH_PhotoAlbum._ToggleCapture) で OFF → 見回りが止まり、きっかけが来ても撮らない、写真は残る、保存値 0。
    ///                     もう一度押して ON → 保存値 1、RT は作り直さない (同じ寸法)
    ///   H4-menu           手元メニューの [写真] (KD_TreatPouch._PhotoOpen) → 写真の面がボードと同じ RT を出す、「新しい」の印、
    ///                     押すと大きく、[?] の説明、[もどる] で元のタブへ
    ///   H5-no-camera-left 最後にカメラが無効で、撮りかけの枠が残っていない
    /// 疑似入力 (Udon のイベントを直接送る) なので、実際のフリスビーの跳び・ボタンの光線・VRChat カメラの吸着・端末差 (Quest の一瞬の引っかかり、
    /// iOS のタップ表示) は見ない。PlayerData は実行前に退避し、終わったら戻す。シーンは保存しない (前後のハッシュを比べる)。
    /// </summary>
    [InitializeOnLoad]
    public static class KH_PhotoHarnessEditor
    {
        const string Key = "KH.PhotoHarness", Dir = "Logs/kh-photo-harness", Journal = Dir + "/journal.json";

        [Serializable] public class Case { public string name, status = "PENDING", reason; public List<string> notes = new List<string>(); }
        [Serializable] public class Report
        {
            public string runId, status = "RUNNING", reason, utc, endedUtc, scene, hashBefore, hashAfter, buildTarget, playerDataBackup;
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

        static UdonBehaviour uAlbum, uSettings, uCapture, uGallery, uDirector, uPouch;
        static Camera cam;
        static MeshRenderer[] boardPhotos;
        static RenderTexture[] runtimeSlots;
        static Case current;
        static int capturesBefore, tabBefore, countBeforeOff;
        static string frisbeeEntry, frisbeeParam;

        static void Resolve()
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            UdonBehaviour One(string program) { var m = all.Where(u => u.programSource != null && u.programSource.name == program).ToArray(); if (m.Length != 1) throw new InvalidOperationException(program + " x" + m.Length); return m[0]; }
            uAlbum = One("KH_PhotoAlbum"); uSettings = One("KD_PhotoSettings"); uCapture = One("KD_PhotoCapture"); uGallery = One("KD_GalleryController"); uDirector = One("KD_PhotoDirector"); uPouch = One("KD_TreatPouch");
            var sys = SceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == KH_PhotoGalleryEditor.SystemName);
            cam = sys.transform.Find("PhotoCamera").GetComponent<Camera>();
            var board = SceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == KH_PhotoGalleryEditor.BoardRootName);
            boardPhotos = Enumerable.Range(0, 8).Select(k => board.transform.Find("Photos/Photo" + k).GetComponent<MeshRenderer>()).ToArray();
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

        static void CheckBoardOrder(int expectCount)
        {
            var order = Order(); var slots = Slots();
            int count = Get<int>(uAlbum, "_count");
            Expect(count == expectCount, "album holds " + expectCount + " photos (actual " + count + ")");
            if (count != expectCount) return;
            for (int k = 0; k < 8; k++)
            {
                bool show = k < expectCount;
                Expect(boardPhotos[k].gameObject.activeSelf == show, "board position " + k + (show ? " shown" : " hidden"));
                if (!show) continue;
                Expect(boardPhotos[k].sharedMaterial.mainTexture == slots[order[k]], "board position " + k + " shows slot " + order[k]);
                var tap = boardPhotos[k].GetComponent<UdonBehaviour>();
                Expect(tap != null && Get<int>(tap, "slotIndex") == order[k], "board position " + k + " tap opens slot " + order[k]);
            }
        }

        // ================================================================ cases

        static void MakeSteps()
        {
            steps = new List<Step>();
            foreach (var name in new[] { "H0-ready", "H1-capture", "H2-wrap", "H3-off", "H4-menu", "H5-no-camera-left" }) report.cases.Add(new Case { name = name });
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
                Expect(slots.Length == 8 && runtime, "8 runtime slots " + wantW + "x" + wantH + " depth 0 (build target " + report.buildTarget + "), not the templates");
                var capRt = Get<RenderTexture>(uCapture, "captureTexture");
                Expect(capRt != null && capRt.width == wantW && capRt.height == wantH && capRt.depth >= 16 && cam.targetTexture == capRt, "runtime capture RT " + wantW + "x" + wantH + " with depth, the camera's target");
                Expect(!cam.enabled, "photo camera disabled while idle");
                Expect(Get<int>(uAlbum, "_count") == 0 && boardPhotos.All(b => !b.gameObject.activeSelf), "no photo yet: board positions hidden");
                var state = Get<TMPro.TMP_Text>(uAlbum, "boardState");
                Expect(state != null && !string.IsNullOrEmpty(state.text), "board shows the empty state: '" + (state != null ? state.text : "") + "'");
                Note("PlayerData " + KominkaDogRun.KD_PhotoSettings.SaveKey + " = " + SavedMode());
            }));

            // ---- H1 ----
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
                Expect(order[0] == 0, "the first photo went into slot 0 (the next slot)");
                Expect(Get<RenderTexture>(uCapture, "_lastSlot") == slots[0], "capture reports slot 0 as the last one copied");
                string px = PixelCheck(slots[0]);
                Expect(px.StartsWith("OK"), "slot 0 holds a picture (" + px + ")");
                CheckBoardOrder(1);
                var thumbs = Get<UnityEngine.UI.RawImage[]>(uAlbum, "menuThumbs");
                Expect(thumbs[0].texture == slots[0], "menu thumbnail 0 is the same RenderTexture as the board");
                Expect(Get<int[]>(uGallery, "_nextInGroup")[0] == 1, "the gallery moved on to slot 1");
            }));

            // ---- H2 ----
            for (int i = 1; i <= 8; i++)
            {
                int dog = i % 6;
                steps.Add(Do("H2-wrap", () => { capturesBefore = Captures(); Trigger(dog); }));
                steps.Add(Until("H2-wrap", () => Captures() == capturesBefore + 1, 3, "capture " + (i + 1) + " did not happen"));
                steps.Add(Until("H2-wrap", () => !cam.enabled, 1, "camera stayed enabled after capture " + (i + 1)));
            }
            steps.Add(Do("H2-wrap", () =>
            {
                var order = Order();
                Expect(order.SequenceEqual(new[] { 0, 7, 6, 5, 4, 3, 2, 1 }), "after 9 captures the oldest slot (0) was replaced and the board reads newest first: [" + string.Join(",", order) + "]");
                Expect(Get<int[]>(uGallery, "_nextInGroup")[0] == 1, "the gallery wraps (next slot 1)");
                CheckBoardOrder(8);
                Expect(Captures() == 9, "9 captures in total");
                var slots = Slots(); bool same = runtimeSlots != null && slots.Length == runtimeSlots.Length; for (int i = 0; same && i < slots.Length; i++) if (slots[i] != runtimeSlots[i]) same = false;
                Expect(same, "captures reuse the same 8 runtime textures (no allocation per capture)");
            }));

            // ---- H3 ----
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

            // ---- H4 ----
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
                for (int k = 0; k < cnt; k++) if (order[k] < 0 || order[k] >= slots.Length || thumbs[k].texture != slots[order[k]] || thumbs[k].texture != boardPhotos[k].sharedMaterial.mainTexture || !buttons[k].activeInHierarchy) same = false;
                Expect(same, "the 8 thumbnails show the same RenderTextures as the board, newest first");
                Expect(Get<GameObject>(uAlbum, "menuNewMark").activeInHierarchy, "the 'new' mark is on the newest photo (taken while the page was closed)");
                var title = Get<TMPro.TextMeshProUGUI>(uPouch, "sectionTitleLabel");
                Note("section title: '" + (title != null ? title.text : "") + "'");
                uAlbum.SendCustomEvent("_MenuPick3");
            }));
            steps.Add(Do("H4-menu", () =>
            {
                var order = Order(); var slots = Slots();
                bool has4 = Get<int>(uAlbum, "_count") > 3 && order[3] >= 0 && order[3] < slots.Length;
                Expect(has4 && Get<GameObject>(uAlbum, "menuLargeRoot").activeInHierarchy && Get<UnityEngine.UI.RawImage>(uAlbum, "menuLarge").texture == slots[order[3]], "thumbnail 4 opens large with slot " + order[3] + (has4 ? "" : " (no 4th photo)"));
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

            // ---- H5 ----
            steps.Add(Do("H5-no-camera-left", () =>
            {
                Expect(!cam.enabled, "the photo camera is disabled at the end");
                // _pendingSlot is null once a capture finishes; Get<T> treats a null value as missing (null is not T), so read it directly.
                // A symbol that is absent from the heap and a null value both mean "nothing pending".
                object pending; uCapture.TryGetProgramVariable("_pendingSlot", out pending);
                Expect(!(pending is RenderTexture), "no capture left half-done (_pendingSlot " + (pending == null ? "null" : pending.ToString()) + ")");
                Note("captures " + Captures() + ", runtime errors " + report.errors.Count);
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
