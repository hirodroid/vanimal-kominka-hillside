using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon;
using KominkaDogRun;
using KominkaDogRun.Editor;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-054 §3・§4.5: 芸のガイド KH_TrickGuide (本人だけのローカル表示) の Setup / Verify。
    /// 前例は KH_WalkIntroEditor (専用マテリアル 5000 / 5001 で夜の暗化 4999 の後に描く、読む面に Collider なし)。
    /// 5 は Program Asset が無ければ作って止まる (コンパイル後にもう一度 5)。5a は文言表の取り込みと ja/en の字形だけ。
    /// Setup・5a はシーンを dirty にするだけで保存しない (Verify を見てから統括が保存する)。</summary>
    public static class KH_TrickGuideEditor
    {
        const string Program = "Assets/KominkaHillside/Scripts/KH_TrickGuide";
        const string World = "Assets/Scenes/VRCDefaultWorldScene.unity";
        const string Root = "KH_TrickGuide";
        const string RingMaterialPath = "Assets/KominkaHillside/Materials/KH_TrickGuide_Ring.mat";
        const string TextMaterialPath = "Assets/KominkaHillside/Materials/KH_TrickGuide_Text.mat";
        const string RingMeshPath = "Assets/KominkaHillside/Models/WalkIntro/Ring.asset";   // KH_WalkIntro の輪 (参照だけ。変更しない)
        const string ReportDir = "Temp/kh-tricks";
        const float CanvasScale = .002f;
        const float RingMeshRadius = .075f;
        const int RingQueue = 5000, TextQueue = 5001;   // KH_NightMode.OverlayQueue (4999) の後
        public static readonly string[] Keys =
        {
            "call.trick.bang", "call.trick.jump", "call.trick.hint.bang", "call.trick.hint.jump", "call.trick.playing",
            "call.trick.wait", "call.trick.trainer", "call.trick.trainerTag", "call.trick.hint.menu",
        };
        static readonly Color Ink = new Color(.97f, .96f, .89f), Surface = new Color(.08f, .095f, .085f, .78f);
        static readonly Color BangColor = new Color(1f, .55f, .25f), JumpColor = new Color(.35f, .85f, 1f);
        static readonly Vector2 ZoneSize = new Vector2(360, 130), StatusSize = new Vector2(360, 70), MenuSize = new Vector2(560, 80);

        static void Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != World)
                throw new InvalidOperationException("Open the hillside scene in Edit mode");
        }
        static T Single<T>(string what) where T : UnityEngine.Object
        {
            var all = UnityEngine.Object.FindObjectsOfType<T>(true);
            if (all.Length != 1) throw new InvalidOperationException("Exactly one " + what + " required (found " + all.Length + ")");
            return all[0];
        }
        static void Save(UdonSharpBehaviour b)
        { UdonSharpEditorUtility.CopyProxyToUdon(b); EditorUtility.SetDirty(b); EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(b)); }

        [MenuItem("Tools/KominkaHillside/Tricks/5 Setup Trick Guide")]
        public static void SetupMenu() { Setup(); }
        public static string Setup()
        {
            Guard();
            var log = new StringBuilder();
            // vrc-udonsharp §2: 作った直後の Program Asset へ AddUdonSharpComponent すると同期方式が焼き付く。作ったら止まる
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Program + ".asset") == null)
            {
                var a = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                a.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(Program + ".cs");
                if (a.sourceCsScript == null) throw new InvalidOperationException("Missing " + Program + ".cs");
                AssetDatabase.CreateAsset(a, Program + ".asset"); AssetDatabase.SaveAssets();
                return Report("setup", "PROGRAM_CREATED " + Program + ".asset — compile U# (scripts/unity-compile.sh --udon), then run Tricks/5 again.");
            }
            var callPose = Single<KD_CallPose>("KD_CallPose");
            var language = Single<KD_Language>("KD_Language");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (font == null) throw new InvalidOperationException("Shared UI font missing: " + KD_ShopSetupEditor.SharedFontPath);
            var ringMesh = AssetDatabase.LoadAssetAtPath<Mesh>(RingMeshPath);
            if (ringMesh == null) throw new InvalidOperationException("Ring mesh missing (run Course/Walk Intro/2 Setup first): " + RingMeshPath);
            var catalog = KD_LocalizationEditor.Load();

            var rootGo = GameObject.Find(Root);
            if (rootGo == null) rootGo = new GameObject(Root);
            if (rootGo.transform.parent != null) throw new InvalidOperationException(Root + " must be a scene root (same as KH_WalkIntro)");
            rootGo.SetActive(true);
            var guide = rootGo.GetComponent<KH_TrickGuide>();
            if (guide == null) guide = rootGo.AddUdonSharpComponent<KH_TrickGuide>();
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(guide);
            if (udon == null) throw new InvalidOperationException("KH_TrickGuide backing missing (compile U# and run again)");
            if (udon.SyncMethod != Networking.SyncType.None) { udon.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(udon); }

            var ringMat = RingMaterial();
            var textMat = TextMaterial(font);
            guide.callPose = callPose; guide.language = language;
            guide.bangColor = BangColor; guide.jumpColor = JumpColor;
            guide.ringMeshRadius = RingMeshRadius;
            guide.fadeSeconds = 2f; guide.flashSeconds = .6f; guide.showDistance = 6f; guide.showHysteresis = .5f; guide.menuScaleVr = .7f;

            guide.bangRing = Ring(rootGo.transform, "BangRing", ringMesh, ringMat, out var bangRenderer); guide.bangRingRenderer = bangRenderer;
            guide.jumpRing = Ring(rootGo.transform, "JumpRing", ringMesh, ringMat, out var jumpRenderer); guide.jumpRingRenderer = jumpRenderer;

            // 芸名 + 合図の案内。輪の右に左端を合わせて並べる (犬の頭・手元を隠さない)
            guide.bangText = TextCanvas(rootGo.transform, "BangText", ZoneSize, new Vector2(0f, .5f));
            guide.bangFade = Fade(guide.bangText);
            guide.bangLabel = Label(guide.bangText, ZoneSize, font, textMat, T(catalog, "call.trick.bang") + "\n<size=62%>" + T(catalog, "call.trick.hint.bang") + "</size>", 40, 18, TextAlignmentOptions.Left);
            guide.jumpText = TextCanvas(rootGo.transform, "JumpText", ZoneSize, new Vector2(0f, .5f));
            guide.jumpFade = Fade(guide.jumpText);
            guide.jumpLabel = Label(guide.jumpText, ZoneSize, font, textMat, T(catalog, "call.trick.jump") + "\n<size=62%>" + T(catalog, "call.trick.hint.jump") + "</size>", 40, 18, TextAlignmentOptions.Left);
            guide.statusText = TextCanvas(rootGo.transform, "StatusText", StatusSize, new Vector2(.5f, .5f));
            guide.statusFade = Fade(guide.statusText);
            guide.statusLabel = Label(guide.statusText, StatusSize, font, textMat, T(catalog, "call.trick.playing"), 36, 18, TextAlignmentOptions.Center);
            guide.menuText = TextCanvas(rootGo.transform, "MenuText", MenuSize, new Vector2(.5f, .5f));
            guide.menuFade = Fade(guide.menuText);
            guide.menuLabel = Label(guide.menuText, MenuSize, font, textMat, T(catalog, "call.trick.hint.menu"), 30, 16, TextAlignmentOptions.Center);

            // 保存時は全部隠す (根は有効のまま: Update が要る)
            foreach (var t in new[] { guide.bangRing, guide.jumpRing, guide.bangText, guide.jumpText, guide.statusText, guide.menuText })
                t.gameObject.SetActive(false);
            Save(guide);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("Setup complete: " + Root + " (sync None) callPose=" + callPose.name + " language=" + language.name);
            log.AppendLine("Materials: " + RingMaterialPath + " q" + ringMat.renderQueue + ", " + TextMaterialPath + " q" + textMat.renderQueue);
            log.AppendLine("Scene is dirty. Run Tricks/5a (text table + glyphs) if UITranslations.json changed, then Tricks/6 Verify, then save.");
            return Report("setup", log.ToString());
        }

        /// <summary>文言表 (UITranslations.json) を KD_Language へ取り込み、call.trick.* の ja/en (glyphs=self) の字を共有フォントへ焼く。
        /// KH_WalkIntroEditor.RefreshText と同じ狭い取り込み。全体の取り込みは Menu/11 Setup Localization (広い Setup)。</summary>
        [MenuItem("Tools/KominkaHillside/Tricks/5a Apply Trick Text")]
        public static void ApplyTextMenu() { ApplyText(); }
        public static string ApplyText()
        {
            Guard();
            var language = Single<KD_Language>("KD_Language");
            var catalog = KD_LocalizationEditor.Load();
            var log = new StringBuilder();
            KD_LocalizationEditor.Configure(language, catalog, log);
            string glyphs = string.Concat(catalog.languages.Where(KD_LocalizationEditor.Bundled).SelectMany(l => l.texts).Where(t => t.key.StartsWith("call.trick.")).Select(t => t.text));
            KD_ShopSetupEditor.AddGlyphs(glyphs, log);
            Save(language);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            string missing = KD_ShopSetupEditor.MissingGlyphs(glyphs);
            log.AppendLine("call.trick.* bundled glyphs missing: " + (missing.Length == 0 ? "none" : missing));
            return Report("apply-text", log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Tricks/6 Verify Trick Guide")]
        public static void VerifyMenu() { Verify(); }
        public static string Verify()
        {
            Guard();
            int total = 0, fail = 0; var log = new StringBuilder();
            Action<bool, string> check = (ok, msg) => { total++; if (!ok) fail++; log.AppendLine((ok ? "PASS " : "FAIL ") + msg); };
            var all = UnityEngine.Object.FindObjectsOfType<KH_TrickGuide>(true);
            check(all.Length == 1, "exactly one KH_TrickGuide (found " + all.Length + ")");
            if (all.Length != 1) return Finish(log, total, fail);
            var s = all[0]; var u = UdonSharpEditorUtility.GetBackingUdonBehaviour(s);
            check(u != null, "backing UdonBehaviour");
            if (u == null) return Finish(log, total, fail);
            check(s.transform.parent == null && s.name == Root, "scene root " + Root);
            check(s.gameObject.activeInHierarchy && s.enabled && u.enabled, "observer always enabled");
            check(u.SyncMethod == Networking.SyncType.None, "actual Udon sync None");
            check(u.programSource == AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Program + ".asset"), "program source");
            check(s.GetComponent<VRCObjectSync>() == null, "no network transform");
            check(s.GetComponentsInChildren<Collider>(true).Length == 0, "no colliders (read-only guide)");
            check(s.GetComponentsInChildren<GraphicRaycaster>(true).Length == 0 && s.GetComponentsInChildren<VRCUiShape>(true).Length == 0, "no raycaster / UiShape");

            var callPose = UnityEngine.Object.FindObjectsOfType<KD_CallPose>(true);
            var language = UnityEngine.Object.FindObjectsOfType<KD_Language>(true);
            check(callPose.Length == 1 && s.callPose == callPose[0], "callPose wired to the scene KD_CallPose");
            check(language.Length == 1 && s.language == language[0], "language wired to the scene KD_Language");
            foreach (string field in new[] { "callPose", "language" })
            {
                object stored; u.publicVariables.TryGetVariableValue(field, out stored);
                var proxy = typeof(KH_TrickGuide).GetField(field).GetValue(s) as UdonSharpBehaviour;
                check(proxy != null && stored as UnityEngine.Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy), "serialized backing " + field);
            }
            var parts = new Transform[] { s.bangRing, s.jumpRing, s.bangText, s.jumpText, s.statusText, s.menuText };
            check(parts.All(t => t != null && t.IsChildOf(s.transform)), "6 visual parts under the root");
            check(parts.All(t => t == null || !t.gameObject.activeSelf), "visuals hidden in saved scene");
            check(s.bangFade != null && s.jumpFade != null && s.statusFade != null && s.menuFade != null, "fade groups");
            check(new[] { s.bangFade, s.jumpFade, s.statusFade, s.menuFade }.All(f => f == null || (!f.blocksRaycasts && !f.interactable)), "fade groups do not take input");
            check(s.bangLabel != null && s.jumpLabel != null && s.statusLabel != null && s.menuLabel != null, "labels");

            var ringMat = AssetDatabase.LoadAssetAtPath<Material>(RingMaterialPath);
            var textMat = AssetDatabase.LoadAssetAtPath<Material>(TextMaterialPath);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            check(ringMat != null && ringMat.renderQueue == RingQueue, "ring material " + RingMaterialPath + " queue " + RingQueue + " (after night overlay 4999)");
            check(textMat != null && textMat.renderQueue == TextQueue, "text material " + TextMaterialPath + " queue " + TextQueue);
            foreach (var pair in new[] { (s.bangRing, s.bangRingRenderer), (s.jumpRing, s.jumpRingRenderer) })
            {
                var r = pair.Item2;
                check(r != null && pair.Item1 != null && r.transform == pair.Item1 && r.sharedMaterial == ringMat
                    && r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off, (pair.Item1 != null ? pair.Item1.name : "ring") + " renderer uses the guide ring material");
                var mf = pair.Item1 != null ? pair.Item1.GetComponent<MeshFilter>() : null;
                check(mf != null && mf.sharedMesh == AssetDatabase.LoadAssetAtPath<Mesh>(RingMeshPath) && Mathf.Abs(mf.sharedMesh.bounds.extents.x - s.ringMeshRadius) < .002f,
                    (pair.Item1 != null ? pair.Item1.name : "ring") + " mesh = WalkIntro Ring, ringMeshRadius matches (" + s.ringMeshRadius + ")");
            }
            foreach (var label in new[] { s.bangLabel, s.jumpLabel, s.statusLabel, s.menuLabel })
                check(label != null && label.font == font && label.fontSharedMaterial == textMat && !label.raycastTarget, (label != null ? label.transform.parent.name : "label") + " shared font + text material");
            check(Mathf.Approximately(s.fadeSeconds, 2f) && Mathf.Approximately(s.showDistance, 6f) && s.showHysteresis > 0f && s.showHysteresis < 1f, "fade 2.0 s, show within 6 m");

            // 文言 9 件 × 13 言語 (AI 作成、校正なし) と、シーンの実行時の表 (Tricks/5a か Menu/11 で取り込む)
            var catalog = KD_LocalizationEditor.Load();
            check(catalog.languages.Length == 13, "13 languages (found " + catalog.languages.Length + ")");
            foreach (var lang in catalog.languages)
            {
                var missingKeys = Keys.Where(k => !lang.texts.Any(t => t.key == k && !string.IsNullOrWhiteSpace(t.text))).ToArray();
                check(missingKeys.Length == 0, lang.code + " has the 9 trick keys" + (missingKeys.Length == 0 ? "" : " (missing " + string.Join(",", missingKeys) + ")"));
                var wait = lang.texts.FirstOrDefault(t => t.key == "call.trick.wait");
                check(wait != null && wait.text != null && wait.text.Contains("{0}"), lang.code + " call.trick.wait keeps {0}");
                if (language.Length == 1)
                    check(Keys.All(k => language[0]._TextForLanguage(k, lang.code) == KD_LocalizationEditor.Text(catalog, lang.code, k)), lang.code + " serialized runtime table (run Tricks/5a if FAIL)");
                if (KD_LocalizationEditor.Bundled(lang))
                {
                    string missing = KD_ShopSetupEditor.MissingGlyphs(string.Concat(lang.texts.Where(t => t.key.StartsWith("call.trick.")).Select(t => t.text)));
                    check(missing.Length == 0, lang.code + " bundled glyphs" + (missing.Length == 0 ? "" : " (missing " + missing + ")"));
                }
                else log.AppendLine("DEVICE_CHECK " + lang.code + " uses the VRChat client font fallback (glyphs=client)");
            }
            log.AppendLine("NOTE translations are AI-made, not proofread (IMP-DOG-054 §2.8)");
            return Finish(log, total, fail);
        }

        static string Finish(StringBuilder log, int total, int fail)
        {
            log.AppendLine("TOTAL " + total + " FAILED " + fail);
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(ReportDir + "/guide-verify.txt", log.ToString());
            Debug.Log(fail == 0 ? "TRICKGUIDE_OK " + total : "TRICKGUIDE_NG " + fail);
            return log.ToString();
        }
        static string Report(string name, string body)
        {
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(ReportDir + "/guide-" + name + ".txt", body);
            Debug.Log("[KH TrickGuide] " + body);
            return body;
        }

        static string T(KD_LocalizationEditor.Catalog c, string key) { return KD_LocalizationEditor.Text(c, c.fallbackLanguage, key); }
        static T Comp<T>(GameObject g) where T : Component { var c = g.GetComponent<T>(); return c != null ? c : g.AddComponent<T>(); }
        static Transform Child(Transform p, string name)
        { var t = p.Find(name); if (t != null) return t; var g = new GameObject(name); g.transform.SetParent(p, false); return g.transform; }

        static Transform Ring(Transform parent, string name, Mesh mesh, Material mat, out Renderer renderer)
        {
            var t = Child(parent, name);
            foreach (var c in t.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            Comp<MeshFilter>(t.gameObject).sharedMesh = mesh;
            var r = Comp<MeshRenderer>(t.gameObject); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one * 2f;
            renderer = r; return t;
        }
        static Transform TextCanvas(Transform parent, string name, Vector2 size, Vector2 pivot)
        {
            var t = parent.Find(name);
            if (t == null) { var g = new GameObject(name, typeof(RectTransform)); t = g.transform; t.SetParent(parent, false); }
            var rt = (RectTransform)t; rt.sizeDelta = size; rt.pivot = pivot; rt.localScale = Vector3.one * CanvasScale;
            t.gameObject.layer = 0;
            Comp<Canvas>(t.gameObject).renderMode = RenderMode.WorldSpace;
            // 読む専用: Raycaster / UiShape / Collider を持たせない (前回の Setup が付けていても外す)
            var raycaster = t.GetComponent<GraphicRaycaster>(); if (raycaster != null) UnityEngine.Object.DestroyImmediate(raycaster);
            var shape = t.GetComponent<VRCUiShape>(); if (shape != null) UnityEngine.Object.DestroyImmediate(shape);
            foreach (var c in t.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            var bg = KD_ShopSetupEditor.EnsureUiChild(t, "Background", Vector2.zero, size);
            var img = Comp<Image>(bg); img.color = Surface; img.raycastTarget = false; bg.transform.SetAsFirstSibling();
            return t;
        }
        static CanvasGroup Fade(Transform canvas)
        { var g = Comp<CanvasGroup>(canvas.gameObject); g.blocksRaycasts = false; g.interactable = false; g.alpha = 1f; return g; }
        static TextMeshProUGUI Label(Transform canvas, Vector2 size, TMP_FontAsset font, Material mat, string text, float max, float min, TextAlignmentOptions align)
        {
            var g = KD_ShopSetupEditor.EnsureUiChild(canvas, "Text", Vector2.zero, size - new Vector2(20, 12));
            var label = Comp<TextMeshProUGUI>(g);
            label.font = font; label.fontSharedMaterial = mat; label.text = text;
            label.fontSize = max; label.fontSizeMax = max; label.fontSizeMin = min; label.enableAutoSizing = true;
            label.enableWordWrapping = true; label.richText = true; label.alignment = align; label.color = Ink; label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }
        static Material RingMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(RingMaterialPath);
            if (m == null) { m = new Material(Shader.Find("Unlit/Color")); AssetDatabase.CreateAsset(m, RingMaterialPath); }
            m.color = Color.white;   // 実際の色は KH_TrickGuide が MaterialPropertyBlock で入れる (バーン / ジャンプ / 薄く / 光る)
            m.renderQueue = RingQueue;
            EditorUtility.SetDirty(m); return m;
        }
        static Material TextMaterial(TMP_FontAsset font)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(TextMaterialPath);
            if (m == null) { m = new Material(font.material); AssetDatabase.CreateAsset(m, TextMaterialPath); }
            m.CopyPropertiesFromMaterial(font.material); m.renderQueue = TextQueue;
            EditorUtility.SetDirty(m); return m;
        }
    }
}
