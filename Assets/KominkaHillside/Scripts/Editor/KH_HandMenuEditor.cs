using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDK3.Components;
using KominkaDogRun;

namespace KominkaHillside.Editor
{
    // DES-UI-001 段階 2: 手元メニュー。板・タブ・色・開き方は古民家 (KD_TreatPouch, hand-menu.md, world-ui-spec §3) を継承し、
    // 中身は さんぽ / あそぶ / おもちゃ の 3 タブ。文言は KH_HandMenu.cs の文字列リテラルとここの定数が正本で、
    // Ensure Glyphs が静的フォントへ焼く (vrc-tmp-font §3)。
    public static class KH_HandMenuEditor
    {
        const string WorldScene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        public const string FontPath = "Assets/KominkaHillside/Font/KD_NotoSerif SDF.asset";
        const string RoundPath = "Assets/KominkaHillside/Textures/UI/KD_UiRound.png";
        const string RuntimeSource = "Assets/KominkaHillside/Scripts/KH_HandMenu.cs";
        const string MarkerMaterialPath = "Assets/KominkaHillside/Data/Walking/KH_GatherMarker.mat";
        public const int InputLayer = 17; // Walkthrough: UI pointer input without player collision (KominkaDogRun BUG-ENV-004)
        public const float PanelWidth = 1140f, PanelHeight = 700f;
        // Look tokens (KominkaDogRun KD_UiSpriteEditor)
        static readonly Color SurfacePanel = new Color(.12f, .10f, .08f, .92f);
        static readonly Color ActionPrimary = new Color(.35f, .50f, .30f), ActionEquip = new Color(.30f, .42f, .52f), ActionNeutral = new Color(.34f, .30f, .28f), ActionClose = new Color(.42f, .26f, .24f);
        static readonly Color TabOff = new Color(.20f, .18f, .16f), AccentGold = new Color(.84f, .68f, .32f);
        // Words written by this builder (runtime words live in KH_HandMenu.cs)
        public const string Title = "古民家の裏山", WordClose = "閉じる", WordLauncher = "メニュー";
        const string WordNight = "ナイトモード: OFF";   // initial label only (DrawNight overwrites it every draw; runtime literals in KH_HandMenu.cs own the words and Charset reads them). Longest variant so the width check measures the worst case
        const string WordDev = "試作: 走り込み: 遠く → 走り込み: 並走";   // IMP-DOG-008: initial label only (DrawDev overwrites it; the words are runtime literals). The worst case DrawDev can show (longest name → longest pending name) so the width check measures it
        public static readonly string[] Tabs = { "さんぽ", "あそぶ", "おもちゃ" };
        public static readonly string[] DogNames = { "コーギー", "ポメラニアン", "ラブラドール", "ダックスフンド", "柴犬", "サモエド" };
        public static readonly string[] Formations = { "横一列", "輪になる", "ぎゅっと集合", "V字", "思い思いに", "前後二列" };
        public const string WordLeft = "左につなぐ", WordRight = "右につなぐ", WordOff = "外す", WordPlaza = "広場へ行こう", WordGate = "古民家側へ帰ろう";
        public const string WordGather = "ここに集合", WordEnd = "終わる", WordBuy = "購入画面をひらく", WordRecall = "目の前へ", WordBallSuffix = " のボール", WordOpener = "メニューをひらく", WordBall = "ボール", WordDisc = "フリスビー", WordToySuffix = " のおもちゃ";

        static void Report(string name, string text) { Directory.CreateDirectory("Temp/KH_DogPort"); File.WriteAllText("Temp/KH_DogPort/" + name + ".txt", text); Debug.Log("[KH Menu] " + text); }
        static void Guard() { if (Application.isPlaying || EditorSceneManager.GetActiveScene().path != WorldScene) throw new InvalidOperationException("Open the hillside scene in Edit mode."); }
        static TMP_FontAsset Font() { return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath); }
        static Sprite Round() { return AssetDatabase.LoadAssetAtPath<Sprite>(RoundPath); }

        // ---------------------------------------------------------------- font
        // Every FIXED character the menu can show: string literals of the runtime script + this builder's words + ASCII.
        // Player display names are deliberately never shown (the static font has no fallback for arbitrary glyphs).
        public static string Charset()
        {
            var sb = new StringBuilder();
            string src = File.ReadAllText(RuntimeSource);
            foreach (Match m in Regex.Matches(src, "\"((?:[^\"\\\\]|\\\\.)*)\"")) sb.Append(m.Groups[1].Value.Replace("\\n", "").Replace("\\\"", "\""));
            sb.Append(Title).Append(WordClose).Append(WordLauncher).Append(WordLeft).Append(WordRight).Append(WordOff).Append(WordPlaza).Append(WordGate)
              .Append(WordGather).Append(WordEnd).Append(WordBuy).Append(WordRecall).Append(WordBallSuffix).Append(WordOpener).Append(WordBall).Append(WordDisc).Append(WordToySuffix);
            foreach (string s in Tabs) sb.Append(s); foreach (string s in DogNames) sb.Append(s); foreach (string s in Formations) sb.Append(s);
            sb.Append(KH_FrisbeePrototypeEditor.AimHint); // world-space hint on the disc's aim marker uses the same static font
            for (char c = ' '; c <= '~'; c++) sb.Append(c);
            return sb.ToString();
        }
        static string Distinct(string s)
        {
            var set = new HashSet<char>(); var sb = new StringBuilder();
            foreach (char c in s) if (c >= ' ' && set.Add(c)) sb.Append(c);
            return sb.ToString();
        }
        static string Missing(TMP_FontAsset font, string chars)
        {
            var sb = new StringBuilder();
            foreach (char c in chars) if (!font.HasCharacter(c, false, false)) sb.Append(c);
            return sb.ToString();
        }

        // TMP Essential Resources (TMP_Settings, shaders) are git-ignored; a fresh clone needs this once (vrc-tmp-font §0).
        [MenuItem("Tools/KominkaHillside/Menu/0 Import TMP Essentials")]
        public static void ImportTmpEssentials()
        {
            string pkg = Path.GetFullPath("Library/PackageCache/com.unity.textmeshpro@3.0.6/Package Resources/TMP Essential Resources.unitypackage");
            if (!File.Exists(pkg)) throw new InvalidOperationException("TMP package not found: " + pkg);
            AssetDatabase.ImportPackage(pkg, false);
            Report("tmp-import", "imported " + pkg);
        }

        [MenuItem("Tools/KominkaHillside/Menu/1 Measure Font")]
        public static void MeasureFont()
        {
            var font = Font(); if (font == null) throw new InvalidOperationException("Font asset missing: " + FontPath);
            var so = new SerializedObject(font);
            int w = so.FindProperty("m_AtlasWidth").intValue, pad = so.FindProperty("m_AtlasPadding").intValue;
            float pt = font.faceInfo.pointSize;
            int capacity = (int)Math.Pow(Math.Floor(w / (pt + 2 * pad)), 2);
            string need = Distinct(Charset()); string missing = Missing(font, need);
            var log = new StringBuilder();
            log.AppendLine("atlas=" + w + "x" + so.FindProperty("m_AtlasHeight").intValue + " pt=" + pt + " padding=" + pad + " mode=" + font.atlasPopulationMode + " glyphs=" + font.glyphTable.Count + " chars=" + font.characterTable.Count);
            log.AppendLine("menu distinct=" + need.Length + " missing=" + missing.Length + " [" + missing + "]");
            log.AppendLine("estimated capacity=" + capacity + " after add=" + (font.characterTable.Count + missing.Length) + (font.characterTable.Count + missing.Length > capacity * .95f ? " WARN >95%" : ""));
            Report("font", log.ToString());
        }

        // Bakes missing glyphs into the static font (Dynamic for the call, Static after). Rebakes at 48 pt when the atlas is full.
        [MenuItem("Tools/KominkaHillside/Menu/2 Ensure Glyphs")]
        public static void EnsureGlyphs()
        {
            var font = Font(); if (font == null) throw new InvalidOperationException("Font asset missing: " + FontPath);
            var log = new StringBuilder();
            string need = Distinct(Charset()); string missing = Missing(font, need);
            if (missing.Length == 0) { Report("font-ensure", "nothing to add (" + need.Length + " chars present)"); return; }
            EnsureSourceFont(font, log);
            string absent = ""; string still;
            try
            {
                font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                font.TryAddCharacters(missing, out absent);
                still = Missing(font, need);
                if (still.Length > 0)
                {
                    // Anything still missing = atlas full (or a glyph outside the TTF). Rebuild the same asset (GUID kept) at 48 pt with everything.
                    string all = Distinct(new string(font.characterTable.Select(c => (char)c.unicode).ToArray()) + need);
                    log.AppendLine("still missing [" + still + "]: rebaking at 48pt with " + all.Length + " chars");
                    Rebake(font, 48, all, out absent);
                }
            }
            finally
            {
                font.atlasPopulationMode = AtlasPopulationMode.Static; // never leave the asset Dynamic (vrc-tmp-font §2)
                EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
            }
            still = Missing(font, need);
            log.AppendLine("added " + (missing.Length - still.Length) + " / " + missing.Length + " still missing=[" + still + "] absent=[" + absent + "]");
            Report("font-ensure", log.ToString());
            if (still.Length > 0) throw new InvalidOperationException("Font still lacks " + still.Length + " chars: " + still);
        }
        static void EnsureSourceFont(TMP_FontAsset font, StringBuilder log)
        {
            var so = new SerializedObject(font);
            var src = so.FindProperty("m_SourceFontFile");
            if (src.objectReferenceValue != null) return;
            var editorRef = so.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue;
            if (editorRef == null) throw new InvalidOperationException("Font has no source TTF reference; cannot add glyphs.");
            src.objectReferenceValue = editorRef; so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("source font restored from editor ref: " + editorRef.name);
        }
        static void Rebake(TMP_FontAsset font, int pointSize, string all, out string absent)
        {
            var so = new SerializedObject(font);
            so.FindProperty("m_FaceInfo").FindPropertyRelative("m_PointSize").floatValue = pointSize;
            so.ApplyModifiedPropertiesWithoutUndo();
            font.ClearFontAssetData(false);
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.TryAddCharacters(all, out absent);
        }

        // ---------------------------------------------------------------- build
        static GameObject Child(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var t = parent.Find(name); GameObject go = t != null ? t.gameObject : null;
            if (go == null) { go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); }
            go.layer = parent.gameObject.layer;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, .5f); r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = pos; r.sizeDelta = size; r.localScale = Vector3.one; r.localRotation = Quaternion.identity;
            return go;
        }
        static TextMeshProUGUI Text(Transform parent, string name, Vector2 pos, Vector2 size, string text, float pt, TextAlignmentOptions align, TMP_FontAsset font)
        {
            var go = Child(parent, name, pos, size);
            var tmp = go.GetComponent<TextMeshProUGUI>(); if (tmp == null) tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = font; tmp.fontSize = pt; tmp.alignment = align; tmp.color = Color.white; tmp.raycastTarget = false; tmp.enableWordWrapping = true; tmp.overflowMode = TextOverflowModes.Overflow; tmp.text = text;
            return tmp;
        }
        static Image Button(Transform parent, string name, Vector2 pos, Vector2 size, Color color, string label, float pt, TMP_FontAsset font, Sprite round, VRC.Udon.UdonBehaviour target, string customEvent, bool flat = false)
        {
            var go = Child(parent, name, pos, size);
            var img = go.GetComponent<Image>(); if (img == null) img = go.AddComponent<Image>();
            img.color = color; img.raycastTarget = true;
            if (flat) { img.sprite = null; img.type = Image.Type.Simple; } else { img.sprite = round; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1f; }
            var btn = go.GetComponent<UnityEngine.UI.Button>(); if (btn == null) btn = go.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.24f, 1.24f, 1.24f, 1f); colors.pressedColor = new Color(.72f, .72f, .72f, 1f);
            colors.selectedColor = Color.white; colors.disabledColor = new Color(.65f, .65f, .65f, .65f); colors.fadeDuration = .08f;
            btn.transition = Selectable.Transition.ColorTint; btn.colors = colors;
            var tmp = Text(go.transform, "Label", Vector2.zero, size - new Vector2(flat ? 12f : 24f, 4f), label, pt, TextAlignmentOptions.Center, font);
            tmp.enableWordWrapping = false;
            for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEditor.Events.UnityEventTools.RemovePersistentListener(btn.onClick, i);
            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(btn.onClick, new UnityEngine.Events.UnityAction<string>(target.SendCustomEvent), customEvent);
            return img;
        }

        [MenuItem("Tools/KominkaHillside/Menu/3 Build Menu")]
        public static void Build()
        {
            Guard(); var scene = EditorSceneManager.GetActiveScene();
            var font = Font(); var round = Round();
            if (font == null || round == null) throw new InvalidOperationException("Font or round sprite missing.");
            string missing = Missing(font, Distinct(Charset()));
            if (missing.Length > 0) throw new InvalidOperationException("Run Menu/2 Ensure Glyphs first; missing " + missing.Length + " chars: " + missing);
            var dogs = GameObject.Find("KH_Dogs"); if (dogs == null) throw new InvalidOperationException("Run Walking/3 Build first.");
            var manager = dogs.GetComponentInChildren<KH_LeashManager>(true);
            var activities = dogs.GetComponentInChildren<KD_GroupActivities>(true);
            var pass = dogs.GetComponentInChildren<KH_TrainerPass>(true);
            var recall = dogs.GetComponentInChildren<KH_ToyRecall>(true);
            if (manager == null || activities == null || pass == null || recall == null) throw new InvalidOperationException("Walking/3 Build has not created the authorities.");

            var rootT = dogs.transform.Find("KH_HandMenu");
            GameObject rootGo = rootT != null ? rootT.gameObject : null;
            if (rootGo == null) { rootGo = new GameObject("KH_HandMenu"); rootGo.transform.SetParent(dogs.transform, false); }
            rootGo.layer = InputLayer;
            var menu = rootGo.GetComponent<KH_HandMenu>(); if (menu == null) menu = rootGo.AddUdonSharpComponent<KH_HandMenu>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(menu);
            backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None; EditorUtility.SetDirty(backing);
            menu.manager = manager; menu.activities = activities; menu.pass = pass; menu.recall = recall;
            menu.dogRoots = manager.dogRoots; menu.dogNames = DogNames; menu.formationNames = Formations;

            // Panel: World Space Canvas, 1px = 1mm (scale 0.001), VRCUiShape on Walkthrough.
            var pt = rootGo.transform.Find("Panel"); GameObject panel = pt != null ? pt.gameObject : null;
            if (panel == null) { panel = new GameObject("Panel", typeof(RectTransform)); panel.transform.SetParent(rootGo.transform, false); }
            panel.layer = InputLayer;
            panel.transform.localPosition = Vector3.zero; panel.transform.localRotation = Quaternion.identity;
            var canvas = panel.GetComponent<Canvas>(); if (canvas == null) canvas = panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (panel.GetComponent<CanvasScaler>() == null) panel.AddComponent<CanvasScaler>();
            if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();
            if (panel.GetComponent<VRCUiShape>() == null) panel.AddComponent<VRCUiShape>();
            var prect = (RectTransform)panel.transform; prect.sizeDelta = new Vector2(PanelWidth, PanelHeight); prect.localScale = Vector3.one * .001f;
            var bg = Child(panel.transform, "Bg", Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            var bgImg = bg.GetComponent<Image>(); if (bgImg == null) bgImg = bg.AddComponent<Image>();
            bgImg.color = SurfacePanel; bgImg.raycastTarget = false; bgImg.sprite = null; bgImg.type = Image.Type.Simple;
            menu.panelRoot = panel;

            // Header
            menu.titleText = Text(panel.transform, "Title", new Vector2(-380f, 312f), new Vector2(340f, 40f), Title, 26f, TextAlignmentOptions.Left, font);
            Button(panel.transform, "Close", new Vector2(485f, 312f), new Vector2(110f, 44f), ActionClose, WordClose, 22f, font, round, backing, "_Close");
            // Night mode toggle (IMP-ENV-002): one header button visible on every tab. Wired to KH_NightMode when Night/1 Setup has run.
            menu.nightImage = Button(panel.transform, "Night", new Vector2(290f, 312f), new Vector2(250f, 44f), ActionNeutral, WordNight, 22f, font, round, backing, "_Night");
            menu.nightLabel = menu.nightImage.GetComponentInChildren<TextMeshProUGUI>(true);
            menu.night = UnityEngine.Object.FindObjectOfType<KH_NightMode>(true);
            // IMP-DOG-008 (development only): cycles the prototype preset on the Lab catcher.
            // Free header span is x -210..165 (375 wide); 350 at -22 spans -197..153. 18 pt keeps the worst pending label (311 px) inside the 326 px box.
            menu.devImage = Button(panel.transform, "Dev", new Vector2(-22f, 312f), new Vector2(350f, 44f), ActionNeutral, WordDev, 18f, font, round, backing, "_DevVariant");
            menu.devLabel = menu.devImage.GetComponentInChildren<TextMeshProUGUI>(true);
            var labCatchT = dogs.transform.Find("KH_FrisbeeCatch_Lab");
            menu.devCatcher = labCatchT == null ? null : labCatchT.GetComponent<KH_FrisbeeCatch>();
            // Tabs (flat + gold underline for the active one)
            menu.tabImages = new Image[3]; menu.tabUnderlines = new GameObject[3]; menu.pages = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                var img = Button(panel.transform, "Tab" + i, new Vector2(-487f + i * 156f, 248f), new Vector2(150f, 50f), TabOff, Tabs[i], 22f, font, round, backing, "_Tab" + i, true);
                var ul = Child(img.transform, "Underline", new Vector2(0f, -22f), new Vector2(150f, 6f));
                var ulImg = ul.GetComponent<Image>(); if (ulImg == null) ulImg = ul.AddComponent<Image>();
                ulImg.color = AccentGold; ulImg.raycastTarget = false; ulImg.sprite = null; ulImg.type = Image.Type.Simple;
                menu.tabImages[i] = img; menu.tabUnderlines[i] = ul;
                menu.pages[i] = Child(panel.transform, "Page" + i, new Vector2(0f, -60f), new Vector2(1120f, 560f));
            }
            // さんぽ
            var walk = menu.pages[0].transform;
            menu.walkState = new TextMeshProUGUI[6]; menu.walkLeft = new Image[6]; menu.walkRight = new Image[6]; menu.walkOff = new Image[6];
            for (int i = 0; i < 6; i++)
            {
                var cell = Child(walk, "Dog" + i, new Vector2(i % 2 == 0 ? -285f : 285f, 210f - (i / 2) * 120f), new Vector2(540f, 110f));
                Text(cell.transform, "Name", new Vector2(-150f, 30f), new Vector2(240f, 34f), DogNames[i], 22f, TextAlignmentOptions.Left, font).enableWordWrapping = false;
                menu.walkState[i] = Text(cell.transform, "State", new Vector2(110f, 30f), new Vector2(300f, 30f), "", 18f, TextAlignmentOptions.Right, font);
                menu.walkLeft[i] = Button(cell.transform, "Left", new Vector2(-115f, -28f), new Vector2(150f, 40f), ActionEquip, WordLeft, 20f, font, round, backing, "_Left" + i);
                menu.walkRight[i] = Button(cell.transform, "Right", new Vector2(45f, -28f), new Vector2(150f, 40f), ActionEquip, WordRight, 20f, font, round, backing, "_Right" + i);
                menu.walkOff[i] = Button(cell.transform, "Off", new Vector2(170f, -28f), new Vector2(100f, 40f), ActionNeutral, WordOff, 20f, font, round, backing, "_Off" + i);
            }
            menu.destPlazaImage = Button(walk, "DestPlaza", new Vector2(-270f, -190f), new Vector2(240f, 44f), ActionNeutral, WordPlaza, 20f, font, round, backing, "_DestPlaza");
            menu.destGateImage = Button(walk, "DestGate", new Vector2(10f, -190f), new Vector2(280f, 44f), ActionNeutral, WordGate, 20f, font, round, backing, "_DestGate");
            menu.destStatus = Text(walk, "DestStatus", new Vector2(0f, -245f), new Vector2(1100f, 36f), "", 18f, TextAlignmentOptions.Center, font);
            // あそぶ
            var gather = menu.pages[1].transform;
            menu.gatherStatus = Text(gather, "Status", new Vector2(0f, 245f), new Vector2(1100f, 36f), "", 22f, TextAlignmentOptions.Center, font);
            menu.formImages = new Image[6];
            for (int i = 0; i < 6; i++) menu.formImages[i] = Button(gather, "Form" + i, new Vector2(-370f + (i % 3) * 370f, 170f - (i / 3) * 70f), new Vector2(330f, 56f), ActionNeutral, Formations[i], 22f, font, round, backing, "_Form" + i);
            menu.startImage = Button(gather, "Start", new Vector2(-250f, 30f), new Vector2(240f, 48f), ActionPrimary, WordGather, 22f, font, round, backing, "_GatherStart");
            menu.endImage = Button(gather, "End", new Vector2(-30f, 30f), new Vector2(160f, 48f), ActionNeutral, WordEnd, 22f, font, round, backing, "_GatherEnd");
            menu.buyImage = Button(gather, "Buy", new Vector2(250f, 30f), new Vector2(280f, 48f), ActionPrimary, WordBuy, 22f, font, round, backing, "_Buy");
            menu.gatherNote = Text(gather, "Note", new Vector2(0f, -60f), new Vector2(1080f, 90f), "", 18f, TextAlignmentOptions.Center, font);
            // おもちゃ
            var toys = menu.pages[2].transform;
            menu.toyState = new TextMeshProUGUI[6]; menu.toyImages = new Image[6]; menu.discImages = new Image[6];
            for (int i = 0; i < 6; i++)
            {
                // One row per dog, one button per toy kind (IMP-DOG-005: the disc is a second toy with its own behaviour).
                var cell = Child(toys, "Ball" + i, new Vector2(i % 2 == 0 ? -285f : 285f, 210f - (i / 2) * 120f), new Vector2(540f, 110f));
                var nameText = cell.transform.Find("Name"); if (nameText != null) UnityEngine.Object.DestroyImmediate(nameText.gameObject);
                var oldRecall = cell.transform.Find("Recall"); if (oldRecall != null) UnityEngine.Object.DestroyImmediate(oldRecall.gameObject);
                Text(cell.transform, "Name", new Vector2(-130f, 22f), new Vector2(280f, 34f), DogNames[i] + WordToySuffix, 22f, TextAlignmentOptions.Left, font).enableWordWrapping = false;
                menu.toyState[i] = Text(cell.transform, "State", new Vector2(-130f, -20f), new Vector2(280f, 30f), "", 16f, TextAlignmentOptions.Left, font);
                menu.toyImages[i] = Button(cell.transform, "Ball", new Vector2(70f, 0f), new Vector2(110f, 44f), ActionPrimary, WordBall, 20f, font, round, backing, "_Toy" + i);
                menu.discImages[i] = Button(cell.transform, "Disc", new Vector2(195f, 0f), new Vector2(130f, 44f), ActionPrimary, WordDisc, 20f, font, round, backing, "_Disc" + i);
            }
            menu.toyNote = Text(toys, "Note", new Vector2(0f, -220f), new Vector2(1100f, 36f), "", 18f, TextAlignmentOptions.Center, font);
            // Touch launcher (Screen Space Overlay; inactive by default, runtime shows it for Touch input only)
            var lt = rootGo.transform.Find("Launcher"); GameObject launcher = lt != null ? lt.gameObject : null;
            if (launcher == null) { launcher = new GameObject("Launcher", typeof(RectTransform)); launcher.transform.SetParent(rootGo.transform, false); }
            launcher.layer = InputLayer;
            var lc = launcher.GetComponent<Canvas>(); if (lc == null) lc = launcher.AddComponent<Canvas>();
            lc.renderMode = RenderMode.ScreenSpaceOverlay; lc.sortingOrder = 100;
            var scaler = launcher.GetComponent<CanvasScaler>(); if (scaler == null) scaler = launcher.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = .5f;
            if (launcher.GetComponent<GraphicRaycaster>() == null) launcher.AddComponent<GraphicRaycaster>();
            if (launcher.GetComponent<VRCUiShape>() == null) launcher.AddComponent<VRCUiShape>();
            var lb = Button(launcher.transform, "Open", Vector2.zero, new Vector2(220f, 72f), ActionNeutral, WordLauncher, 26f, font, round, backing, "_Toggle");
            var lr = (RectTransform)lb.transform; lr.anchorMin = lr.anchorMax = new Vector2(.5f, 1f); lr.pivot = new Vector2(.5f, 1f); lr.anchoredPosition = new Vector2(0f, -24f); lr.sizeDelta = new Vector2(220f, 72f);
            launcher.SetActive(false);
            menu.launcher = launcher;
            // Formation preview markers (6 flat discs, inactive)
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MarkerMaterialPath);
            if (mat == null) { mat = new Material(Shader.Find("VRChat/Mobile/Standard Lite")); mat.color = AccentGold; AssetDatabase.CreateAsset(mat, MarkerMaterialPath); }
            var mt = rootGo.transform.Find("Markers"); GameObject markersGo = mt != null ? mt.gameObject : null;
            if (markersGo == null) { markersGo = new GameObject("Markers"); markersGo.transform.SetParent(rootGo.transform, false); }
            menu.markers = new GameObject[6];
            for (int i = 0; i < 6; i++)
            {
                var m = markersGo.transform.Find("Marker" + i); GameObject mg = m != null ? m.gameObject : null;
                if (mg == null) { mg = GameObject.CreatePrimitive(PrimitiveType.Cylinder); mg.name = "Marker" + i; mg.transform.SetParent(markersGo.transform, false); UnityEngine.Object.DestroyImmediate(mg.GetComponent<Collider>()); }
                mg.layer = 14; mg.GetComponent<Renderer>().sharedMaterial = mat; mg.SetActive(false);
                menu.markers[i] = mg;
            }
            foreach (var t in rootGo.GetComponentsInChildren<Transform>(true)) if (t.GetComponent<Renderer>() == null || t.GetComponent<Graphic>() != null) t.gameObject.layer = InputLayer;
            // One-handed / seated VR entry (review U2): a small plate on each leash station opens the menu by Interact.
            var plateMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/KominkaHillside/Data/Walking/KH_SignPlaza.mat");
            foreach (string st in new[] { "KH_LeashRack", "KH_Station_Viewpoint", "KH_Station_Plaza" })
            {
                var station = dogs.transform.Find(st); if (station == null) continue;
                var plate = station.Find("MenuPlate"); GameObject pg = plate != null ? plate.gameObject : null;
                if (pg == null) { pg = GameObject.CreatePrimitive(PrimitiveType.Cube); pg.name = "MenuPlate"; pg.transform.SetParent(station, false); }
                pg.layer = 14; pg.transform.localPosition = new Vector3(.45f, .095f, .11f); pg.transform.localScale = new Vector3(.16f, .10f, .03f);
                if (plateMat != null) pg.GetComponent<Renderer>().sharedMaterial = plateMat;
                var col = pg.GetComponent<BoxCollider>(); if (col == null) col = pg.AddComponent<BoxCollider>();
                var opener = pg.GetComponent<KH_MenuOpener>(); if (opener == null) opener = pg.AddUdonSharpComponent<KH_MenuOpener>();
                var ob = UdonSharpEditorUtility.GetBackingUdonBehaviour(opener); ob.SyncMethod = VRC.SDKBase.Networking.SyncType.None; ob.InteractionText = WordOpener; ob.proximity = 2.5f;
                opener.menu = menu; UdonSharpEditorUtility.CopyProxyToUdon(opener); EditorUtility.SetDirty(opener);
            }
            panel.SetActive(false);
            UdonSharpEditorUtility.CopyProxyToUdon(menu); EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            Report("menu-build", "menu built: tabs=3 walkRows=6 forms=6 toys=6 night=" + (menu.night != null ? "wired" : "MISSING (run Night/1 Setup)") + " dev=" + (menu.devCatcher != null ? "wired" : "MISSING (run Walking/3 Build)") + " launcher=inactive panel=inactive");
        }

        // Button name (and its cell/parent) -> the handler Build wires. Kept next to Build so a renamed button fails Verify.
        static string ExpectedEvent(UnityEngine.UI.Button b)
        {
            string n = b.name, p = b.transform.parent.name;
            if (n == "Close") return "_Close"; if (n == "Open") return "_Toggle"; if (n == "Night") return "_Night";
            if (n == "Dev") return "_DevVariant";
            if (n.StartsWith("Tab")) return "_Tab" + n.Substring(3);
            if (n.StartsWith("Form")) return "_Form" + n.Substring(4);
            if (n == "Start") return "_GatherStart"; if (n == "End") return "_GatherEnd"; if (n == "Buy") return "_Buy";
            if (n == "DestPlaza") return "_DestPlaza"; if (n == "DestGate") return "_DestGate";
            if (p.StartsWith("Dog")) { string i = p.Substring(3); return n == "Left" ? "_Left" + i : n == "Right" ? "_Right" + i : n == "Off" ? "_Off" + i : "?"; }
            if (p.StartsWith("Ball") && n == "Ball") return "_Toy" + p.Substring(4);
            if (p.StartsWith("Ball") && n == "Disc") return "_Disc" + p.Substring(4);
            return "?";
        }

        [MenuItem("Tools/KominkaHillside/Menu/4 Verify Menu")]
        public static void Verify()
        {
            Guard(); var log = new StringBuilder(); int total = 0, failed = 0;
            void Check(bool ok, string what) { total++; if (!ok) failed++; log.AppendLine((ok ? "PASS " : "FAIL ") + what); }
            var dogs = GameObject.Find("KH_Dogs"); var menu = dogs == null ? null : dogs.GetComponentInChildren<KH_HandMenu>(true);
            Check(menu != null, "hand menu exists under KH_Dogs");
            if (menu == null) { Report("menu-verify", log + "TOTAL " + total + " FAILED " + failed); return; }
            var font = Font(); string need = Distinct(Charset()); string missing = Missing(font, need);
            Check(missing.Length == 0, "font has every menu character (" + need.Length + " distinct, missing " + missing.Length + ")");
            Check(font.atlasPopulationMode == AtlasPopulationMode.Static, "font is Static");
            Check(menu.manager != null && menu.activities != null && menu.pass != null && menu.recall != null && menu.dogRoots != null && menu.dogRoots.Length == 6, "authorities wired");
            Check(menu.panelRoot != null && !menu.panelRoot.activeSelf && menu.launcher != null && !menu.launcher.activeSelf, "panel and launcher start inactive");
            var canvas = menu.panelRoot == null ? null : menu.panelRoot.GetComponent<Canvas>();
            Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace && menu.panelRoot.GetComponent<VRCUiShape>() != null && menu.panelRoot.GetComponent<GraphicRaycaster>() != null && menu.panelRoot.layer == InputLayer, "panel: WorldSpace canvas, VRCUiShape, raycaster, Walkthrough layer");
            var prect = menu.panelRoot == null ? null : (RectTransform)menu.panelRoot.transform;
            Check(prect != null && prect.sizeDelta == new Vector2(PanelWidth, PanelHeight) && Mathf.Abs(prect.localScale.x - .001f) < 1e-6f, "panel 1140x700 at 1mm/px");
            Check(menu.pages != null && menu.pages.Length == 3 && menu.tabImages.Length == 3 && menu.tabUnderlines.Length == 3, "three tabs and pages");
            Check(menu.walkState.Length == 6 && menu.walkLeft.Length == 6 && menu.walkRight.Length == 6 && menu.walkOff.Length == 6 && menu.walkState.All(x => x != null) && menu.walkLeft.All(x => x != null), "walk rows 6");
            Check(menu.formImages.Length == 6 && menu.formImages.All(x => x != null) && menu.startImage != null && menu.endImage != null && menu.buyImage != null && menu.markers.Length == 6 && menu.markers.All(x => x != null && !x.activeSelf), "gather buttons and 6 inactive markers");
            Check(menu.toyImages.Length == 6 && menu.toyState.Length == 6 && menu.toyImages.All(x => x != null), "toy rows 6");
            Check(menu.discImages != null && menu.discImages.Length == 6 && menu.discImages.All(x => x != null), "disc buttons 6");
            var buttons = menu.panelRoot.GetComponentsInChildren<UnityEngine.UI.Button>(true).Concat(menu.launcher.GetComponentsInChildren<UnityEngine.UI.Button>(true)).ToArray();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(menu);
            int wired = 0, sliced = 0;
            foreach (var b in buttons)
            {
                if (b.onClick.GetPersistentEventCount() == 1 && b.onClick.GetPersistentTarget(0) == backing && b.onClick.GetPersistentMethodName(0) == "SendCustomEvent") wired++;
                var img = b.GetComponent<Image>(); if (img != null && (img.sprite == null || img.type == Image.Type.Sliced)) sliced++;
            }
            Check(buttons.Length == 48 && wired == buttons.Length, "48 buttons (47 panel incl. 6 disc + night + dev + launcher) all wired to the menu Udon (" + wired + ")");
            Check(menu.nightImage != null && menu.nightLabel != null && menu.nightLabel.transform.IsChildOf(menu.nightImage.transform), "night button image and label");
            Check(menu.night != null, "night wired to KH_NightMode (run Night/1 Setup then Build Menu)");
            Check(menu.devImage != null && menu.devLabel != null && menu.devLabel.transform.IsChildOf(menu.devImage.transform), "dev variant button image and label");
            Check(sliced == buttons.Length, "rounded buttons use Sliced sprites");
            // Every onClick string must be the handler expected for that button (name -> event), and the handler must exist.
            var runtimeMethods = new HashSet<string>(typeof(KH_HandMenu).GetMethods().Select(m => m.Name));
            bool allExist = true, allExpected = true;
            foreach (var b in buttons)
            {
                string arg = ""; var so = new SerializedObject(b); var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                if (calls.arraySize == 1) arg = calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
                if (!runtimeMethods.Contains(arg)) { allExist = false; log.AppendLine("  missing handler " + arg); }
                string expected = ExpectedEvent(b);
                if (expected != arg) { allExpected = false; log.AppendLine("  " + b.transform.parent.name + "/" + b.name + " -> " + arg + " (expected " + expected + ")"); }
            }
            Check(allExist, "every onClick names an existing _ handler");
            Check(allExpected, "every button calls the handler its name implies");
            object mv; var backingVars = backing.publicVariables;
            bool refs = backingVars.TryGetVariableValue("manager", out mv) && mv != null && backingVars.TryGetVariableValue("activities", out mv) && mv != null
                && backingVars.TryGetVariableValue("pass", out mv) && mv != null && backingVars.TryGetVariableValue("recall", out mv) && mv != null && backingVars.TryGetVariableValue("panelRoot", out mv) && mv != null;
            Check(refs, "backing Udon holds manager/activities/pass/recall/panelRoot references");
            // A U# reference is stored in the backing heap as the target's backing UdonBehaviour, not its proxy.
            bool nightRefs = menu.night != null && backingVars.TryGetVariableValue("night", out mv) && mv == (object)UdonSharpEditorUtility.GetBackingUdonBehaviour(menu.night)
                && backingVars.TryGetVariableValue("nightImage", out mv) && mv == (object)menu.nightImage && backingVars.TryGetVariableValue("nightLabel", out mv) && mv == (object)menu.nightLabel;
            Check(nightRefs, "backing Udon holds night/nightImage/nightLabel (same objects as the proxy)");
            // Same shape as the night check: the button's own Image/Label must be in the heap too, or the header
            // draws nothing at runtime while the Inspector looks wired (Codex M).
            bool devRefs = menu.devCatcher != null && backingVars.TryGetVariableValue("devCatcher", out mv) && mv == (object)UdonSharpEditorUtility.GetBackingUdonBehaviour(menu.devCatcher)
                && backingVars.TryGetVariableValue("devImage", out mv) && mv == (object)menu.devImage && backingVars.TryGetVariableValue("devLabel", out mv) && mv == (object)menu.devLabel;
            Check(devRefs, "backing Udon holds devCatcher/devImage/devLabel (KH_FrisbeeCatch_Lab; run Walking/3 Build then Build Menu)");
            bool arrays = new UnityEngine.Object[][] { menu.pages, menu.tabImages, menu.tabUnderlines, menu.walkState, menu.walkLeft, menu.walkRight, menu.walkOff, menu.formImages, menu.markers, menu.toyState, menu.toyImages, menu.discImages }
                .All(a => a != null && a.Length > 0 && a.All(x => x != null));
            Check(arrays, "all reference arrays are allocated with no null element");
            var openers = dogs.GetComponentsInChildren<KH_MenuOpener>(true);
            Check(openers.Length == 3 && openers.All(o => o.menu == menu && o.GetComponent<BoxCollider>() != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(o).InteractionText == WordOpener), "three station plates open the menu by Interact");
            var texts = menu.panelRoot.GetComponentsInChildren<TextMeshProUGUI>(true);
            Check(texts.All(t => t.font == font), "all " + texts.Length + " texts use the shared font");
            // One-line fit: a Japanese line must fit its box height (vrc-tmp-font §4.1)
            float ascentDescent = (font.faceInfo.ascentLine - font.faceInfo.descentLine) / font.faceInfo.pointSize;
            int cramped = 0; foreach (var t in texts) if (!t.enableWordWrapping && t.rectTransform.sizeDelta.y < ascentDescent * t.fontSize) cramped++;
            Check(cramped == 0, "single-line texts tall enough for one line (cramped=" + cramped + ")");
            // Width fit for fixed labels (vrc-tmp-font §4). Only awake UGUI texts measure correctly (isOrthographic true after a scene load).
            int overflow = 0, measured = 0;
            foreach (var t in texts)
            {
                if (t.enableWordWrapping || string.IsNullOrEmpty(t.text) || !t.isOrthographic) continue;
                measured++;
                float w = t.GetPreferredValues(t.text, t.rectTransform.rect.width, 0f).x;
                if (w > t.rectTransform.rect.width + .5f) { overflow++; log.AppendLine("  overflow " + t.transform.parent.name + "/" + t.name + " " + w.ToString("F0") + ">" + t.rectTransform.rect.width); }
            }
            Check(overflow == 0, "fixed labels fit their width (measured " + measured + ", overflow " + overflow + (measured == 0 ? "; reopen the scene to measure" : "") + ")");
            Report("menu-verify", log + "TOTAL " + total + " FAILED " + failed);
        }
    }
}
