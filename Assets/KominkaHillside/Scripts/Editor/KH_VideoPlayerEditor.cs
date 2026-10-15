using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using Object = UnityEngine.Object;
using VCore = JLChnToZ.VRC.VVMW.Core;
using VFrontend = JLChnToZ.VRC.VVMW.FrontendHandler;
using VPlayerHandler = JLChnToZ.VRC.VVMW.VideoPlayerHandler;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-UI-011: VizVid (idv.jlchntoz.vvmw 1.8.2) の動画プレイヤーを手元メニューの副画面 (Page_Settings/VideoPage) に組み込む。
    /// 設計: docs/development/issues/IMP-UI-011-menu-video-player.md §3 (2・4・6) / §4。
    ///
    /// 実行順 (統括が直列に。サブエージェントは実行しない):
    ///   Player/0 - Create Program Asset              KH_VideoPage.asset を作る → U# をコンパイル (unity-compile.sh --udon)
    ///   Player/1 - Build Video Player and Menu Page  冪等。VizVid の `VVMW (No Controls)` を毎回 削除 → 展開 (Unpack Completely) し直し、
    ///                                                Core の 7 配列に面の RawImage を mode 4 で足す、enableQueueList=false、音源を 2D に、
    ///                                                面 Page_Settings/VideoPage、ヘッダの [動画]、KH_VideoPage・pouch・BGM の配線。保存前に Player/2 と同じ検査
    ///   Player/2 - Verify                            読むだけ (Temp/imp-ui-011/verify.json)
    ///   Player/9 - Remove                            VizVid・面・[動画]・配線を外す (資産は無い)
    ///
    /// VizVid の Editor 側クラス (JLChnToZ.VVMW.Editor asmdef) は参照しない。internal なフィールドは SerializedObject で読み書きし、
    /// U# (Core / FrontendHandler) は書いたら CopyProxyToUdon (vrc-udonsharp §4。VizVid の CoreConfigPreprocessor もビルド時に同じことをする)。
    /// 展開してから配線するので、プレハブ内 → 外の参照をオーバーライドで持たない (vrc-udonsharp §3)。
    /// KH_VideoPage は VizVid ルートの子 `KH_VideoPage` (常に有効、sync None) に置く。Core の GameObject は Manual なので同居させない。
    /// 文言は UITranslations.json の video.player.* (面の KH_VideoPage が描く)。Bind (UILocalizationBindings.json) には登録しない。
    /// 注意: ヘッダの VideoButton (PhotoButton も) は KD_TreatSetupEditor.PrunePanelChildren の keep に無い。板を旧来の手順で作り直すと消えるので Player/1 を回し直す (周回シーンでは RejectLegacyRebuild が先に拒否する)。
    /// </summary>
    public static partial class KH_VideoPlayerEditor
    {
        const string Menu = "Tools/KominkaHillside/Player/";
        public const string OutDir = "Temp/imp-ui-011";
        public const string RootName = "KH_VideoPlayer (VizVid)";
        public const string PrefabPath = "Packages/idv.jlchntoz.vvmw/VVMW (No Controls).prefab";
        public const string PageName = "VideoPage";
        public const string PageBehaviourName = "KH_VideoPage";
        public const string ProgramPath = "Assets/KominkaHillside/Scripts/KH_VideoPage";
        public const string HeaderKey = "video.player.menu", TitleKey = "video.player.title";

        // ---------------------------------------------------------------- 面の配置 (1140 x 700、本文の帯 Y 148〜-232。Issue §3-4)
        // Issue §3-4 の右の列の値 (URL 欄 330 x 50 at (230,130) ほか) は画面・上段・[もどる] と重なるので、右の列 x 110〜550 に収めて組み直した (報告に記載)
        public const float BandTop = 148f, BandBottom = -232f, ColumnLeft = 110f, ColumnRight = 550f;
        public static readonly Vector2 StatePos = new Vector2(-170f, 170f), StateSize = new Vector2(700f, 44f);
        public static readonly Vector2 BackPos = new Vector2(450f, 170f), BackSize = new Vector2(220f, 44f);
        public static readonly Vector2 ScreenPos = new Vector2(-230f, -46f), ScreenSize = new Vector2(640f, 360f);
        public const float ScreenBorder = 2f;
        public static readonly Vector2 UrlPos = new Vector2(270f, 120f), UrlSize = new Vector2(320f, 50f);
        public static readonly Vector2 SubmitPos = new Vector2(500f, 120f), ButtonSize = new Vector2(100f, 50f);
        public const float ControlRowY = 60f;
        public static readonly string[] ControlNames = { "Play", "Pause", "Stop", "Resync" };
        public static readonly string[] ControlEvents = { "_Play", "_Pause", "_Stop", "_Resync" };
        public static readonly string[] ControlKeys = { "video.player.play", "video.player.pause", "video.player.stop", "video.player.resync" };
        /// <summary>2 段目のボタン i の中心 X (4 個を x 110〜550 に等間隔。160 / 273.3 / 386.7 / 500)。</summary>
        public static float ControlX(int i) { return ColumnLeft + ButtonSize.x * 0.5f + i * (ColumnRight - ColumnLeft - ButtonSize.x) / 3f; }
        public static readonly Vector2 SliderPos = new Vector2(220f, -10f), SliderSize = new Vector2(220f, 40f);
        public static readonly Vector2 VolumeLabelPos = new Vector2(390f, -10f), VolumeLabelSize = new Vector2(100f, 40f);
        public static readonly Vector2 MutePos = new Vector2(500f, -10f);
        // Codex 中 (UX、2026-10-15): 注意は 3 行の短文。440 x 110 at (330,-105) = y -50..-160 (帯の下端 -232 の内側、[消音] の下端 -35 と 15 px 空ける)。折り返し + 18 -> 14pt
        public static readonly Vector2 NotePos = new Vector2(330f, -105f), NoteSize = new Vector2(440f, 110f);
        public const float NoteFontMax = 18f, NoteFontMin = 14f;

        /// <summary>面の子と期待する矩形 (Verify が見る。ScreenFrame は Screen の 2 px 外側)。</summary>
        public static IEnumerable<(string name, Vector2 pos, Vector2 size)> PageLayout()
        {
            yield return ("State", StatePos, StateSize);
            yield return ("Back", BackPos, BackSize);
            yield return ("ScreenFrame", ScreenPos, ScreenSize + Vector2.one * (ScreenBorder * 2f));
            yield return ("Screen", ScreenPos, ScreenSize);
            yield return ("UrlInput", UrlPos, UrlSize);
            yield return ("Submit", SubmitPos, ButtonSize);
            for (int i = 0; i < ControlNames.Length; i++) yield return (ControlNames[i], new Vector2(ControlX(i), ControlRowY), ButtonSize);
            yield return ("VolumeSlider", SliderPos, SliderSize);
            yield return ("VolumeLabel", VolumeLabelPos, VolumeLabelSize);
            yield return ("Mute", MutePos, ButtonSize);
            yield return ("Note", NotePos, NoteSize);
        }

        static readonly Color Ink = new Color(0.97f, 0.95f, 0.90f);
        static readonly Color PlaceholderInk = new Color(0.62f, 0.60f, 0.56f);
        static readonly Color UrlBack = new Color(0.10f, 0.09f, 0.08f, 1f);
        static readonly Color ScreenIdle = new Color(0.06f, 0.06f, 0.07f, 1f);

        /// <summary>KH_VideoPage が描く文言 (opus-runtime 2026-10-15 の対応表)。gift.back は既存。</summary>
        public static readonly string[] NeededKeys =
        {
            HeaderKey, TitleKey, "video.player.submit", "video.player.play", "video.player.pause", "video.player.stop", "video.player.resync",
            "video.player.mute", "video.player.unmute", "video.player.volume", "video.player.note", "video.player.url",
            "video.player.state.idle", "video.player.state.loading", "video.player.state.playing", "video.player.state.playing.anon",
            "video.player.state.paused", "video.player.state.error", "video.player.state.untrusted", "video.player.state.ratelimited", "video.player.state.wait",
            "gift.back", "video.player.state.locked",
        };
        /// <summary>NeededKeys のうち、無くても止めないキー (opus-runtime が後から足す。無ければ Note に落とし FAIL にしない)。</summary>
        public static readonly string[] OptionalKeys = { "video.player.state.locked" };
        public static readonly string[] StateKeys =
        {
            "video.player.state.idle", "video.player.state.loading", "video.player.state.playing", "video.player.state.playing.anon",
            "video.player.state.paused", "video.player.state.error", "video.player.state.untrusted", "video.player.state.ratelimited", "video.player.state.wait",
            "video.player.state.locked",
        };
        /// <summary>{0} の最悪値 (playing = 入れた人の名前 16 字、wait = 秒)。</summary>
        public const string LongPlayerName = "ABCDEFGHIJKLMNOP";

        // ================================================================ context

        class Ctx
        {
            public UnityEngine.SceneManagement.Scene scene;
            public KD_TreatPouch pouch; public KD_Language language; public TMP_FontAsset font; public UdonBehaviour pouchUdon;
            public KD_LocalizationEditor.Catalog catalog;
            public List<KD_BgmPlayer> bgms = new List<KD_BgmPlayer>();
            public List<string> missingKeys = new List<string>();
            public List<string> notes = new List<string>();
        }

        static Ctx Resolve()
        {
            var c = new Ctx { scene = EditorSceneManager.GetActiveScene() };
            var pouches = Object.FindObjectsOfType<KD_TreatPouch>(true).Where(p => p.gameObject.scene == c.scene).ToArray();
            if (pouches.Length != 1) throw new InvalidOperationException("exactly one KD_TreatPouch required (" + pouches.Length + "); nothing changed");
            c.pouch = pouches[0];
            c.language = c.pouch.language;
            if (c.language == null) throw new InvalidOperationException("KD_TreatPouch.language not wired (Menu/11); nothing changed");
            c.font = c.pouch.sectionTitleLabel != null ? c.pouch.sectionTitleLabel.font : null;
            if (c.font == null) throw new InvalidOperationException("the menu's shared font (sectionTitleLabel.font) is missing; nothing changed");
            if (c.pouch.settingsPage == null || c.pouch.panelRoot == null) throw new InvalidOperationException("menu panel / settings page missing; nothing changed");
            c.pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(c.pouch);
            if (c.pouchUdon == null) throw new InvalidOperationException("KD_TreatPouch backing missing; nothing changed");
            c.catalog = KD_LocalizationEditor.Load();
            var fallback = c.catalog.languages.First(l => l.code == c.catalog.fallbackLanguage);
            foreach (var key in NeededKeys)
            {
                var e = fallback.texts.FirstOrDefault(x => x.key == key);
                if (e != null && !string.IsNullOrWhiteSpace(e.text)) continue;
                if (OptionalKeys.Contains(key)) c.notes.Add("NOTE optional text " + key + " not in the catalog yet (not a failure; KH_VideoPage falls back)");
                else c.missingKeys.Add(key);
            }
            c.bgms = Object.FindObjectsOfType<KD_BgmPlayer>(true).Where(b => b.gameObject.scene == c.scene).ToList();
            if (c.bgms.Count == 0) c.notes.Add("NOTE no KD_BgmPlayer in the scene: BGM is not ducked while a video plays (KD_BgmPlayer.videoCore not wired)");
            if (c.bgms.Count > 1) c.notes.Add("NOTE " + c.bgms.Count + " KD_BgmPlayer in the scene: every one gets videoCore");
            return c;
        }

        static string ProgramMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") == null)
                return ProgramPath + ".asset missing: run Player/0, compile U# (unity-compile.sh --udon), then this menu";
            return null;
        }

        /// <summary>文言表の文 (無いキーは null。KD_LocalizationEditor.Text は未知のキーで投げる)。</summary>
        static string TextOrNull(KD_LocalizationEditor.Catalog catalog, string code, string key)
        {
            try { return KD_LocalizationEditor.Text(catalog, code, key); } catch (InvalidOperationException) { return null; }
        }

        static string Ja(Ctx c, string key) { return TextOrNull(c.catalog, c.catalog.fallbackLanguage, key) ?? key; }

        static VRCSceneDescriptor Descriptor() { return Object.FindObjectOfType<VRCSceneDescriptor>(); }

        /// <summary>今 GameObject を失っている NetworkIDs の ID (Player/1 の保存前検査で、元からあった残骸を区別する)。</summary>
        static HashSet<int> NullNetworkIds(VRCSceneDescriptor d)
        {
            var s = new HashSet<int>();
            if (d == null || d.NetworkIDCollection == null) return s;
            foreach (var p in d.NetworkIDCollection) if (p != null && p.gameObject == null) s.Add(p.ID);
            return s;
        }

        // ================================================================ Player/0

        [MenuItem(Menu + "0 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") != null)
            { Debug.Log("[KH Player] PLAYER_PROGRAM_OK already " + ProgramPath + ".asset"); return; }
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ProgramPath + ".cs");
            if (script == null) { Debug.LogError("[KH Player] PLAYER_PROGRAM_FAIL " + ProgramPath + ".cs not imported yet (refresh first)"); return; }
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, ProgramPath + ".asset");
            AssetDatabase.SaveAssets();
            Debug.Log("[KH Player] PLAYER_PROGRAM_OK created " + ProgramPath + ".asset. Compile U# (unity-compile.sh --udon) before Player/1.");
        }

        // ================================================================ Player/1

        [Serializable] class BuildReport
        {
            public string utc, scene, status, backup, menu;
            public List<string> log = new List<string>();
        }

        class Parts { public GameObject root; public VCore core; public VFrontend frontend; public VPlayerHandler[] handlers; public KH_VideoPage page; public UdonBehaviour pageUdon; }

        class PageUi
        {
            public GameObject page; public TextMeshProUGUI state; public GameObject back; public RawImage screen;
            public VRCUrlInputField url; public TextMeshProUGUI placeholder;
            public GameObject submit, mute; public GameObject[] controls; public Slider slider; public TextMeshProUGUI volumeLabel, note;
        }

        [MenuItem(Menu + "1 - Build Video Player and Menu Page")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            string missing = ProgramMissing();
            if (missing != null) { Debug.LogError("[KH Player] PLAYER_BUILD_FAIL " + missing + "; nothing changed"); return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError("[KH Player] PLAYER_BUILD_FAIL " + PrefabPath + " not found (VizVid package missing?); nothing changed"); return; }
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Player/1" };
            Ctx c;
            try
            {
                c = Resolve();
                if (c.missingKeys.Count > 0) throw new InvalidOperationException("texts missing in " + KD_MenuFinishEditor.CatalogPath + " (" + c.catalog.fallbackLanguage + "): " + string.Join(", ", c.missingKeys) + "; add the video.player.* texts first; nothing changed");
            }
            catch (Exception e) { Debug.LogError("[KH Player] PLAYER_BUILD_FAIL " + e.Message); return; }
            r.log.AddRange(c.notes);
            var descriptor = Descriptor();
            var baselineNull = NullNetworkIds(descriptor);
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-build.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                var parts = RebuildRoot(c, prefab, descriptor, r.log);
                EnsurePageBehaviour(parts, r.log);
                var ui = BuildPage(c, parts.pageUdon, r.log);
                var header = PlaceHeaderButton(c, r.log);
                AddScreen(parts.core, ui.screen, r.log);
                ConfigureFrontend(parts, r.log);
                ConfigureAudio(parts, r.log);
                Wire(c, parts, ui, header, r.log);
                RefreshLanguage(c, r.log);
                CopyAll(c, parts);
                // ---- the same checks as Player/2 before saving (Photo/1 style: a failure leaves the scene unsaved) ----
                var v = RunVerify(baselineNull);
                WriteJson(OutDir + "/verify.json", v);
                if (v.fails > 0)
                {
                    r.status = "VERIFY_FAILED"; r.log.Add("NOT SAVED: " + v.fails + " checks failed (" + OutDir + "/verify.json)");
                    r.log.AddRange(v.lines.Where(l => l.StartsWith("FAIL")).Take(20));
                    WriteJson(OutDir + "/build.json", r);
                    Debug.LogError("[KH Player] PLAYER_BUILD_FAIL verify fails=" + v.fails + " (not saved). First: " + string.Join(" | ", v.lines.Where(l => l.StartsWith("FAIL")).Take(4))
                        + ". The scene before this run is at " + r.backup + "; reopen the scene without saving to discard the changes.");
                    return;
                }
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("scene save failed");
                AssetDatabase.SaveAssets();
                r.status = "BUILT";
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message);
                WriteJson(OutDir + "/build.json", r);
                Debug.LogError("[KH Player] PLAYER_BUILD_FAIL " + e.Message + ". The scene before this run is at " + r.backup + "; reopen the scene without saving to discard partial changes.");
                throw;
            }
            WriteJson(OutDir + "/build.json", r);
            Debug.Log("[KH Player] PLAYER_BUILD_OK VizVid unpacked, page " + PageName + " / header [動画] wired, saved. " + r.log.Count + " log lines in " + OutDir + "/build.json. Next: Player/2 (after the save the backing heaps are populated).");
        }

        // ---------------------------------------------------------------- VizVid root (delete → instantiate → unpack)

        /// <summary>既存の VizVid ルートを消して作り直す。消す前に、その木の GameObject を指す NetworkIDs の ID を控えておき、消した後にその ID だけを外す
        /// (Core / FrontendHandler は同期するので型名つきの残骸になり、Photo/9 の「型名なしだけ消す」では残る。vrc-scene-edit-safety)。</summary>
        static Parts RebuildRoot(Ctx c, GameObject prefab, VRCSceneDescriptor descriptor, List<string> log)
        {
            RemoveRoots(c, descriptor, log);
            var go = PrefabUtility.InstantiatePrefab(prefab, c.scene) as GameObject;
            if (go == null) throw new InvalidOperationException("InstantiatePrefab returned null for " + PrefabPath);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = RootName;
            go.transform.SetParent(null, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); go.transform.localScale = Vector3.one;
            go.SetActive(true);
            var p = new Parts { root = go };
            p.core = go.GetComponentInChildren<VCore>(true);
            p.frontend = go.GetComponentInChildren<VFrontend>(true);
            p.handlers = go.GetComponentsInChildren<VPlayerHandler>(true);
            if (p.core == null || p.frontend == null || p.handlers.Length == 0)
                throw new InvalidOperationException(PrefabPath + " did not contain Core / FrontendHandler / VideoPlayerHandler (core " + (p.core != null) + ", frontend " + (p.frontend != null) + ", handlers " + p.handlers.Length + ")");
            log.Add("VizVid: instantiated " + PrefabPath + ", unpacked completely, renamed " + RootName + " at the origin (Core on " + p.core.name + ", FrontendHandler on " + p.frontend.name
                + ", " + p.handlers.Length + " VideoPlayerHandler: " + string.Join(", ", p.handlers.Select(h => h.name)) + ")");
            return p;
        }

        /// <summary>シーン直下の VizVid ルートを全部消す (NetworkIDs の自分の分も)。外から木の中を指す既知の参照 (BGM / pouch) は呼び手が配線し直すか null にする。</summary>
        static void RemoveRoots(Ctx c, VRCSceneDescriptor descriptor, List<string> log)
        {
            var olds = c.scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray();
            KD_SceneObjects.Find(RootName, out int anywhere);
            if (anywhere != olds.Length) throw new InvalidOperationException(RootName + " exists below another object (" + anywhere + " in the scene, " + olds.Length + " at the root); move or delete it by hand");
            if (olds.Length == 0) return;
            var ids = new HashSet<int>();
            if (descriptor != null && descriptor.NetworkIDCollection != null)
                foreach (var pair in descriptor.NetworkIDCollection)
                    if (pair != null && pair.gameObject != null && olds.Any(o => pair.gameObject.transform.IsChildOf(o.transform))) ids.Add(pair.ID);
            foreach (var o in olds) Object.DestroyImmediate(o);
            log.Add("removed " + olds.Length + " old " + RootName);
            if (ids.Count > 0)
            {
                int dropped = descriptor.NetworkIDCollection.RemoveAll(p => p != null && ids.Contains(p.ID));
                EditorUtility.SetDirty(descriptor);
                log.Add("NetworkIDs: dropped " + dropped + " entries of the old VizVid tree (IDs " + string.Join(",", ids.OrderBy(i => i)) + "); the new tree gets IDs from the SDK (at the latest at build)");
            }
        }

        static void EnsurePageBehaviour(Parts p, List<string> log)
        {
            var t = p.root.transform.Find(PageBehaviourName);
            GameObject go;
            if (t == null) { go = new GameObject(PageBehaviourName); go.transform.SetParent(p.root.transform, false); }
            else go = t.gameObject;
            go.layer = 0; go.SetActive(true);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var vp = go.GetComponent<KH_VideoPage>();
            if (vp == null) vp = go.AddUdonSharpComponent<KH_VideoPage>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(vp);
            if (backing == null) throw new InvalidOperationException("KH_VideoPage backing missing (run Player/0, compile U#, run again)");
            if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); log.Add("KH_VideoPage sync -> None"); }
            vp.enabled = true;
            p.page = vp; p.pageUdon = backing;
            log.Add("KH_VideoPage on " + RootName + "/" + PageBehaviourName + " (always active, sync None; not on Core's GameObject, which is Manual)");
        }

        // ---------------------------------------------------------------- Core / Frontend / audio (SerializedObject: the fields are internal / private)

        static readonly string[] ScreenArrays = { "screenTargets", "screenTargetModes", "screenTargetIndeces", "screenTargetPropertyNames", "screenTargetDefaultTextures", "avProPropertyNames", "rtScreenTargetSTs" };
        public const int RawImageMode = 4;

        /// <summary>CoreEditor.AppendScreen の RawImage の分岐 (CoreEditor.cs 1034-1037 / AppendScreenUnchecked 1107-1115) と同じ 7 本:
        /// target = RawImage / mode 4 / index -1 / "_MainTex" / 既定テクスチャ = rawImage.texture (null なら Core.defaultTexture に落ちる) / "_IsAVProVideo" / ST = 0。</summary>
        static void AddScreen(VCore core, RawImage raw, List<string> log)
        {
            using (var so = new SerializedObject(core))
            {
                var props = ScreenArrays.Select(n => so.FindProperty(n)).ToArray();
                for (int i = 0; i < props.Length; i++) if (props[i] == null || !props[i].isArray) throw new InvalidOperationException("VizVid Core." + ScreenArrays[i] + " not found (VizVid version changed?)");
                int n0 = props[0].arraySize;
                if (props.Any(p => p.arraySize != n0)) throw new InvalidOperationException("VizVid Core screen arrays differ in length: " + string.Join(" / ", props.Select(p => p.name + " " + p.arraySize)));
                for (int i = 0; i < n0; i++)
                    if (props[0].GetArrayElementAtIndex(i).objectReferenceValue == raw) { log.Add("Core.screenTargets already holds the page's RawImage (index " + i + ")"); return; }
                Append(props[0]).objectReferenceValue = raw;
                Append(props[1]).intValue = RawImageMode;
                Append(props[2]).intValue = -1;
                Append(props[3]).stringValue = "_MainTex";
                Append(props[4]).objectReferenceValue = raw.texture;
                Append(props[5]).stringValue = "_IsAVProVideo";
                Append(props[6]).vector4Value = Vector4.zero;
                so.ApplyModifiedPropertiesWithoutUndo();
                log.Add("Core.screenTargets += " + PageName + "/Screen (RawImage, mode " + RawImageMode + ", index -1, _MainTex, _IsAVProVideo, ST 0); " + (n0 + 1) + " screens");
            }
            UdonSharpEditorUtility.CopyProxyToUdon(core); EditorUtility.SetDirty(core);
        }

        static SerializedProperty Append(SerializedProperty array)
        {
            int i = array.arraySize;
            array.arraySize = i + 1;
            return array.GetArrayElementAtIndex(i);
        }

        static void ConfigureFrontend(Parts p, List<string> log)
        {
            if (p.frontend.core != p.core) throw new InvalidOperationException("FrontendHandler.core is not the Core of the same prefab (" + (p.frontend.core != null ? p.frontend.core.name : "null") + ")");
            using (var so = new SerializedObject(p.frontend))
            {
                var q = so.FindProperty("enableQueueList");
                if (q == null) throw new InvalidOperationException("FrontendHandler.enableQueueList not found (VizVid version changed?)");
                q.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            UdonSharpEditorUtility.CopyProxyToUdon(p.frontend); EditorUtility.SetDirty(p.frontend);
            log.Add("FrontendHandler: core = Core (prefab wiring kept), enableQueueList = false (a new URL plays at once)");
        }

        /// <summary>各 VideoPlayerHandler の primaryAudioSource (と R) を集める (AVPro / Builtin は同じ Default Audio Source を共有する)。</summary>
        static List<AudioSource> HandlerAudio(VPlayerHandler[] handlers, List<string> problems)
        {
            var list = new List<AudioSource>();
            foreach (var h in handlers)
                using (var so = new SerializedObject(h))
                {
                    var main = so.FindProperty("primaryAudioSource");
                    var a = main != null ? main.objectReferenceValue as AudioSource : null;
                    if (a == null) { problems?.Add(h.name + ".primaryAudioSource is empty"); continue; }
                    if (!list.Contains(a)) list.Add(a);
                    var right = so.FindProperty("primaryAudioSourceR");
                    var ar = right != null ? right.objectReferenceValue as AudioSource : null;
                    if (ar != null && !list.Contains(ar)) list.Add(ar);
                }
            return list;
        }

        static void ConfigureAudio(Parts p, List<string> log)
        {
            var problems = new List<string>();
            var sources = HandlerAudio(p.handlers, problems);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join("; ", problems));
            foreach (var a in sources)
            {
                a.spatialBlend = 0f; a.spatialize = false;
                EditorUtility.SetDirty(a);
                var sp = a.GetComponent<VRCSpatialAudioSource>();
                if (sp == null) { sp = a.gameObject.AddComponent<VRCSpatialAudioSource>(); log.Add("added VRCSpatialAudioSource on " + a.name); }
                sp.EnableSpatialization = false;   // Gain / Near / Far stay at VizVid's values
                EditorUtility.SetDirty(sp);
            }
            int added = 0;
            using (var so = new SerializedObject(p.core))
            {
                var arr = so.FindProperty("audioSources");
                if (arr == null) throw new InvalidOperationException("VizVid Core.audioSources not found");
                foreach (var a in sources)
                {
                    bool has = false;
                    for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == a) has = true;
                    if (!has) { Append(arr).objectReferenceValue = a; added++; }
                }
                if (added > 0) so.ApplyModifiedPropertiesWithoutUndo();
            }
            if (added > 0) { UdonSharpEditorUtility.CopyProxyToUdon(p.core); EditorUtility.SetDirty(p.core); }
            log.Add("audio: " + string.Join(", ", sources.Select(a => a.name)) + " -> 2D (spatialBlend 0, spatialize off, VRCSpatialAudioSource.EnableSpatialization off; Gain / Near / Far kept)"
                + (added > 0 ? ", added " + added + " to Core.audioSources" : ", already in Core.audioSources"));
        }

        // ---------------------------------------------------------------- the page (Page_Settings/VideoPage)

        static PageUi BuildPage(Ctx c, UdonBehaviour pageUdon, List<string> log)
        {
            var p = c.pouch;
            var parent = p.settingsPage.transform;
            // rebuilt from scratch every run (no stale children; everything on it is the builder's)
            var old = parent.Find(PageName);
            if (old != null)
            {
                p.extraSettingsPages = (p.extraSettingsPages ?? new GameObject[0]).Where(e => e != null && e != old.gameObject).ToArray();
                Object.DestroyImmediate(old.gameObject);
                log.Add("removed old Page_Settings/" + PageName);
            }
            var ui = new PageUi();
            var page = KD_ShopSetupEditor.EnsureUiChild(parent, PageName, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            ui.page = page;
            ui.state = Label(page.transform, "State", StatePos, StateSize, Ja(c, "video.player.state.idle"), c.font, 22f, 14f, TextAlignmentOptions.MidlineLeft, false);
            ui.back = Btn(page.transform, "Back", BackPos, BackSize, Ja(c, "gift.back"), c, c.pouchUdon, "_VideoBack", log);
            // ---- the screen: a black frame 2 px outside, then the RawImage (Core mode 4 writes its texture; KH_VideoPage tints it while idle) ----
            var frame = KD_ShopSetupEditor.EnsureUiChild(page.transform, "ScreenFrame", ScreenPos, ScreenSize + Vector2.one * (ScreenBorder * 2f));
            var frameImg = Comp<Image>(frame); frameImg.sprite = null; frameImg.type = Image.Type.Simple; frameImg.color = Color.black; frameImg.raycastTarget = false;
            var screenGo = KD_ShopSetupEditor.EnsureUiChild(page.transform, "Screen", ScreenPos, ScreenSize);
            var raw = Comp<RawImage>(screenGo);
            raw.texture = null; raw.color = ScreenIdle; raw.raycastTarget = false; raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            ui.screen = raw;
            // ---- right column ----
            BuildUrlInput(page.transform, c, pageUdon, ui, log);
            ui.submit = Btn(page.transform, "Submit", SubmitPos, ButtonSize, Ja(c, "video.player.submit"), c, pageUdon, "_SubmitUrl", log);
            ui.controls = new GameObject[ControlNames.Length];
            for (int i = 0; i < ControlNames.Length; i++)
                ui.controls[i] = Btn(page.transform, ControlNames[i], new Vector2(ControlX(i), ControlRowY), ButtonSize, Ja(c, ControlKeys[i]), c, pageUdon, ControlEvents[i], log);
            var sb = new StringBuilder();
            ui.slider = KD_DogVoiceSettingsEditor.BuildSlider(page.transform, "VolumeSlider", SliderPos.y, KD_UiSpriteEditor.LoadRound(), pageUdon, "_VolumeChanged", sb);
            if (sb.Length > 0) log.Add(sb.ToString().Trim());
            FitSlider(ui.slider);
            ui.volumeLabel = Label(page.transform, "VolumeLabel", VolumeLabelPos, VolumeLabelSize, Ja(c, "video.player.volume") + " 50%", c.font, 20f, 14f, TextAlignmentOptions.Center, false);
            ui.mute = Btn(page.transform, "Mute", MutePos, ButtonSize, Ja(c, "video.player.mute"), c, pageUdon, "_ToggleMute", log);
            ui.note = Label(page.transform, "Note", NotePos, NoteSize, Ja(c, "video.player.note"), c.font, NoteFontMax, NoteFontMin, TextAlignmentOptions.TopLeft, true);
            page.SetActive(false);

            var pages = new List<GameObject>();
            foreach (var e in p.extraSettingsPages ?? new GameObject[0]) if (e != null && !pages.Contains(e)) pages.Add(e);
            if (!pages.Contains(page)) pages.Add(page);
            p.extraSettingsPages = pages.ToArray();
            EditorUtility.SetDirty(p);
            log.Add("page: Page_Settings/" + PageName + " (hidden, registered in extraSettingsPages: " + pages.Count + " pages), screen " + ScreenSize + " at " + ScreenPos
                + ", right column x " + ColumnLeft + ".." + ColumnRight + ": URL " + UrlSize + " + [submit], [play][pause][stop][resync] at y " + ControlRowY + ", volume " + SliderSize + " + label + [mute], note " + NoteSize
                + "; [back] -> pouch _VideoBack. Layer " + LayerMask.LayerToName(page.layer));
            return ui;
        }

        /// <summary>KD_DogVoiceSettingsEditor.BuildSlider は 290 幅・(0, y) で作る。この面の大きさへ置き直し、レールとつまみの可動域も幅に合わせる。</summary>
        static void FitSlider(Slider s)
        {
            var rt = (RectTransform)s.transform;
            rt.anchoredPosition = SliderPos; rt.sizeDelta = SliderSize;
            float inner = SliderSize.x - 40f;   // BuildSlider keeps 20 px each side for the 40 px handle (290 / 250)
            foreach (var name in new[] { "Track", "FillArea", "HandleArea" })
            {
                var t = rt.Find(name) as RectTransform;
                if (t != null) t.sizeDelta = new Vector2(inner, t.sizeDelta.y);
            }
            EditorUtility.SetDirty(s);
        }

        /// <summary>URL 欄。VRCUrlInputField は uGUI の InputField 派生 (TMP_InputField ではない): 文字は旧 UnityEngine.UI.Text、
        /// 置き換え文字 (m_Placeholder) は Graphic なら何でもよいので TMP (KH_VideoPage.urlPlaceholder が描く)。
        /// VizVid の Default UI (Narrow) の URL Input (Image + VRCUrlInputField、子 Text / Placeholder を全面 − 余白) を写した。onEndEdit → _SubmitUrl (文字列固定)。</summary>
        static void BuildUrlInput(Transform page, Ctx c, UdonBehaviour target, PageUi ui, List<string> log)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(page, "UrlInput", UrlPos, UrlSize);
            var img = Comp<Image>(go); img.color = UrlBack; img.raycastTarget = true;
            KD_UiSpriteEditor.ApplyRoundSkin(img, KD_UiSpriteEditor.LoadRound());
            Comp<RectMask2D>(go);
            var field = Comp<VRCUrlInputField>(go);

            var textGo = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Text", Vector2.zero, UrlSize);
            Stretch((RectTransform)textGo.transform, 12f, 4f);
            foreach (var g in textGo.GetComponents<Graphic>()) if (!(g is UnityEngine.UI.Text)) Object.DestroyImmediate(g);
            var text = Comp<UnityEngine.UI.Text>(textGo);
            text.font = LegacyFont();
            text.fontSize = 22; text.color = Ink; text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false; text.text = "";
            EditorUtility.SetDirty(text);

            var phGo = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Placeholder", Vector2.zero, UrlSize);
            Stretch((RectTransform)phGo.transform, 12f, 4f);
            foreach (var g in phGo.GetComponents<Graphic>()) if (!(g is TextMeshProUGUI)) Object.DestroyImmediate(g);
            var ph = Comp<TextMeshProUGUI>(phGo);
            ph.font = c.font; ph.text = Ja(c, "video.player.url"); ph.color = PlaceholderInk; ph.alignment = TextAlignmentOptions.MidlineLeft; ph.raycastTarget = false;
            ph.enableWordWrapping = false; ph.overflowMode = TextOverflowModes.Overflow; ph.richText = false;
            ph.enableAutoSizing = true; ph.fontSizeMax = 20f; ph.fontSizeMin = 14f; ph.fontSize = 20f;
            EditorUtility.SetDirty(ph);

            using (var so = new SerializedObject(field))
            {
                SetRef(so, "m_TextComponent", text);
                SetRef(so, "m_Placeholder", ph);
                SetRef(so, "m_TargetGraphic", img);
                var line = so.FindProperty("m_LineType"); if (line != null) line.intValue = 0;   // SingleLine
                var limit = so.FindProperty("m_CharacterLimit"); if (limit != null) limit.intValue = 0;
                var ro = so.FindProperty("m_ReadOnly"); if (ro != null) ro.boolValue = false;
                var send = so.FindProperty("AllowSendingOnEndEdit"); if (send != null) send.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            for (int i = field.onEndEdit.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(field.onEndEdit, i);
            UnityEventTools.AddStringPersistentListener(field.onEndEdit, new UnityEngine.Events.UnityAction<string>(target.SendCustomEvent), "_SubmitUrl");
            EditorUtility.SetDirty(field);
            ui.url = field; ui.placeholder = ph;
            log.Add("URL: VRCUrlInputField " + UrlSize + " at " + UrlPos + " (legacy Text " + (text.font != null ? text.font.name : "NO FONT") + " + TMP placeholder, RectMask2D), onEndEdit -> _SubmitUrl");
        }

        static void SetRef(SerializedObject so, string name, Object value)
        {
            var p = so.FindProperty(name);
            if (p == null) throw new InvalidOperationException("VRCUrlInputField." + name + " not found (SDK changed?)");
            p.objectReferenceValue = value;
        }

        static void Stretch(RectTransform rt, float padX, float padY)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(padX, padY); rt.offsetMax = new Vector2(-padX, -padY);
        }

        /// <summary>旧 UI.Text の内蔵フォント (Unity 2022.3 は LegacyRuntime.ttf、古い版は Arial.ttf)。null だと何も描かれないので無ければ止める。</summary>
        public static Font LegacyFont()
        {
            foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
            {
                try { var f = Resources.GetBuiltinResource<Font>(name); if (f != null) return f; } catch (Exception) { }
            }
            throw new InvalidOperationException("no built-in font for the URL field's legacy Text (LegacyRuntime.ttf / Arial.ttf)");
        }

        // ---------------------------------------------------------------- header [動画]

        static GameObject PlaceHeaderButton(Ctx c, List<string> log)
        {
            var panel = c.pouch.panelRoot.transform;
            var go = Btn(panel, KD_HandMenuScrollEditor.VideoHeaderButtonName, new Vector2(-65f, KD_HandMenuScrollEditor.HeaderRowY),
                new Vector2(KD_HandMenuScrollEditor.HeaderMediaButtonWidth, KD_HandMenuScrollEditor.HeaderRowHeight), Ja(c, HeaderKey), c, c.pouchUdon, "_VideoOpen", log);
            // keep it among the header controls (a new child is appended last and would draw over the purchase confirm card)
            var anchor = panel.Find(KD_HandMenuScrollEditor.PhotoHeaderButtonName);
            if (anchor == null) anchor = panel.Find("SettingsButton");
            if (anchor != null)
            {
                int a = anchor.GetSiblingIndex();
                go.transform.SetSiblingIndex(go.transform.GetSiblingIndex() > a ? a + 1 : a);
            }
            KD_HandMenuScrollEditor.LayoutFrame(c.pouch);
            bool photo = panel.Find(KD_HandMenuScrollEditor.PhotoHeaderButtonName) != null;
            KD_HandMenuScrollEditor.HeaderLayout(photo, true, out Vector2 titlePos, out Vector2 titleSize, out _, out float videoX, out _, out _, out _);
            var bt = (RectTransform)go.transform;
            var title = (RectTransform)c.pouch.sectionTitleLabel.transform;
            if (Mathf.Abs(bt.anchoredPosition.x - videoX) > 0.5f || Mathf.Abs(title.anchoredPosition.x - titlePos.x) > 0.5f || Mathf.Abs(title.sizeDelta.x - titleSize.x) > 0.5f)
                throw new InvalidOperationException("LayoutFrame did not place the header for [動画] (button x " + bt.anchoredPosition.x + " want " + videoX + ", title " + title.anchoredPosition.x + " / " + title.sizeDelta.x + " want " + titlePos.x + " / " + titleSize.x + ")");
            EditorUtility.SetDirty(go);
            log.Add("header [動画] " + KD_HandMenuScrollEditor.VideoHeaderButtonName + " at x " + videoX + " -> pouch _VideoOpen; title " + titleSize.x + " wide at x " + titlePos.x + (photo ? " (with [写真])" : " (photo's seat)"));
            return go;
        }

        // ---------------------------------------------------------------- wiring

        static void Wire(Ctx c, Parts p, PageUi ui, GameObject header, List<string> log)
        {
            var v = p.page;
            v.frontend = p.frontend; v.core = p.core; v.language = c.language;
            v.urlInput = ui.url; v.screen = ui.screen;
            v.submitButton = ui.submit.GetComponent<Button>();
            v.playButton = ui.controls[0].GetComponent<Button>(); v.pauseButton = ui.controls[1].GetComponent<Button>();
            v.stopButton = ui.controls[2].GetComponent<Button>(); v.resyncButton = ui.controls[3].GetComponent<Button>();
            v.muteButton = ui.mute.GetComponent<Button>();
            v.volumeSlider = ui.slider;
            v.stateLabel = ui.state; v.noteLabel = ui.note; v.volumeLabel = ui.volumeLabel; v.urlPlaceholder = ui.placeholder;
            v.submitLabel = LabelOf(ui.submit); v.playLabel = LabelOf(ui.controls[0]); v.pauseLabel = LabelOf(ui.controls[1]);
            v.stopLabel = LabelOf(ui.controls[2]); v.resyncLabel = LabelOf(ui.controls[3]); v.muteLabel = LabelOf(ui.mute); v.backLabel = LabelOf(ui.back);
            v.page = ui.page; v.headerLabel = LabelOf(header);   // opus-runtime 2026-10-15: two optional fields beyond the contract
            EditorUtility.SetDirty(v);

            c.pouch.videoPage = ui.page; c.pouch.videoPageBehaviour = v;
            EditorUtility.SetDirty(c.pouch);
            foreach (var b in c.bgms) { b.videoCore = p.core; EditorUtility.SetDirty(b); }
            log.Add("wired: KH_VideoPage (frontend / core / language / URL / screen / 6 buttons / slider / 4 texts / 7 button labels / page / header label), pouch.videoPage / videoPageBehaviour, "
                + c.bgms.Count + " KD_BgmPlayer.videoCore");
        }

        /// <summary>文言表 (UITranslations.json) を KD_Language へ入れ直す (Menu/11 の Configure と同じ。束縛 Bind は触らない) と、動画の文字の字形を共通フォントへ焼く。</summary>
        static void RefreshLanguage(Ctx c, List<string> log)
        {
            var sb = new StringBuilder();
            KD_LocalizationEditor.Configure(c.language, c.catalog, sb);
            string glyphs = BundledGlyphs(c);
            string missing = KD_ShopSetupEditor.MissingGlyphs(glyphs);
            if (missing.Length > 0) { KD_ShopSetupEditor.AddGlyphs(missing, sb); log.Add("glyphs added: " + missing); }
            log.Add("language tables refreshed (Configure; bindings untouched)");
        }

        /// <summary>自前で焼く言語 (glyphs=self) の動画の文言 + 数字と % と {0} の最悪値の文字。</summary>
        static string BundledGlyphs(Ctx c)
        {
            var glyphs = new StringBuilder();
            foreach (var lang in c.catalog.languages)
                if (KD_LocalizationEditor.Bundled(lang))
                    foreach (var key in NeededKeys)
                    {
                        var t = TextOrNull(c.catalog, lang.code, key);
                        if (t != null) glyphs.Append(System.Text.RegularExpressions.Regex.Replace(t, "<[^<>]+>", ""));
                    }
            glyphs.Append("0123456789% ").Append(LongPlayerName);
            return glyphs.ToString();
        }

        static void CopyAll(Ctx c, Parts p)
        {
            foreach (UdonSharpBehaviour b in new UdonSharpBehaviour[] { p.page, c.pouch, c.language, p.core, p.frontend })
            { UdonSharpEditorUtility.CopyProxyToUdon(b); EditorUtility.SetDirty(b); }
            foreach (var b in c.bgms) { UdonSharpEditorUtility.CopyProxyToUdon(b); EditorUtility.SetDirty(b); }
        }

        // ================================================================ Player/9

        [MenuItem(Menu + "9 - Remove")]
        public static void Remove()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Player/9" };
            Ctx c;
            try { c = Resolve(); } catch (Exception e) { Debug.LogError("[KH Player] PLAYER_REMOVE_FAIL " + e.Message); return; }
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-remove.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                var p = c.pouch;
                // references into the VizVid tree first (they would turn into {fileID: 0} silently)
                foreach (var b in c.bgms) { b.videoCore = null; UdonSharpEditorUtility.CopyProxyToUdon(b); EditorUtility.SetDirty(b); }
                p.videoPage = null; p.videoPageBehaviour = null;
                RemoveRoots(c, Descriptor(), r.log);
                var page = p.settingsPage.transform.Find(PageName);
                p.extraSettingsPages = (p.extraSettingsPages ?? new GameObject[0]).Where(e => e != null && (page == null || e != page.gameObject)).ToArray();
                if (page != null) { Object.DestroyImmediate(page.gameObject); r.log.Add("removed Page_Settings/" + PageName); }
                var button = p.panelRoot.transform.Find(KD_HandMenuScrollEditor.VideoHeaderButtonName);
                if (button != null) { Object.DestroyImmediate(button.gameObject); r.log.Add("removed header [動画]"); }
                KD_HandMenuScrollEditor.LayoutFrame(p);   // the header goes back to the photo-only / plain layout
                UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
                r.log.Add("cleared KD_TreatPouch.videoPage / videoPageBehaviour, " + c.bgms.Count + " KD_BgmPlayer.videoCore; header re-laid out");
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("scene save failed");
                r.status = "REMOVED";
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message); WriteJson(OutDir + "/remove.json", r);
                Debug.LogError("[KH Player] PLAYER_REMOVE_FAIL " + e.Message + ". Backup: " + r.backup + " (reopen the scene without saving to discard partial changes)");
                throw;
            }
            WriteJson(OutDir + "/remove.json", r);
            Debug.Log("[KH Player] PLAYER_REMOVE_OK " + string.Join(" | ", r.log));
        }

        // ================================================================ small helpers

        /// <summary>Unity の偽 null (エディタの GetComponent) に ?? を使わない。</summary>
        static T Comp<T>(GameObject go) where T : Component { var x = go.GetComponent<T>(); if (x == null) x = go.AddComponent<T>(); return x; }

        static TextMeshProUGUI LabelOf(GameObject button) { if (button == null) return null; var t = button.transform.Find("Label"); return t != null ? t.GetComponent<TextMeshProUGUI>() : null; }

        static GameObject Btn(Transform parent, string name, Vector2 pos, Vector2 size, string text, Ctx c, UdonBehaviour target, string evt, List<string> log)
        {
            var sb = new StringBuilder();
            var go = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size, KD_UiSpriteEditor.ActionNeutral, c.font, text, target, evt, sb);
            if (sb.Length > 0) log.Add(sb.ToString().Trim());
            var label = LabelOf(go);
            if (label != null) { label.fontSize = 20f; label.enableAutoSizing = true; label.fontSizeMax = 20f; label.fontSizeMin = KD_LocalizationEditor.ButtonFontSizeMin; EditorUtility.SetDirty(label); }
            return go;
        }

        static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size, string text, TMP_FontAsset font, float max, float min, TextAlignmentOptions align, bool wrap)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            foreach (var g in go.GetComponents<Graphic>()) if (!(g is TextMeshProUGUI)) Object.DestroyImmediate(g);
            var t = Comp<TextMeshProUGUI>(go);
            t.font = font; t.text = text; t.color = Ink; t.alignment = align; t.raycastTarget = false;
            t.enableWordWrapping = wrap; t.overflowMode = TextOverflowModes.Overflow;   // never Truncate / Ellipsis (vrc-world-ui §10.1)
            t.enableAutoSizing = true; t.fontSizeMax = max; t.fontSizeMin = min; t.fontSize = max; t.richText = true;
            EditorUtility.SetDirty(t);
            return t;
        }

        static void WriteJson(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(o, true));
        }
    }
}
