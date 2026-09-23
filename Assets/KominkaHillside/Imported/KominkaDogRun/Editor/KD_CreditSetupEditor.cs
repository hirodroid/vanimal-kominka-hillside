using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-ENV-001: クレジット表記。KanjiFind IMP-UI-004 / DES-UI-001 T2 の
    /// 「スクロールできる掲示板」方式 (実機確認済みの構成) をこのワールドへ移植する。
    ///
    /// 2026-08-11 改訂: PNG 焼きプレート方式を廃止し、TMP + ScrollRect の掲示板へ。
    /// 「文字はなんだかんだ必要」(ユーザー判断) — フォント基盤をここで整える。
    ///
    /// 実行順 (それぞれ別実行):
    ///   0 - Import TMP Essentials  (初回のみ。TMP のシェーダー・設定を導入)
    ///   1 - Build Font Asset       (Noto Serif JP → KD_NotoSerif SDF。Static で焼く)
    ///   2 - Build Credit Board     (旧プレートを撤去してスクロール掲示板を構築)
    ///   3 - Verify Credit Board
    ///
    /// 初回は Static で生成。更新時は GUID を保持し一時的に Dynamic で追加して Static に戻す。
    /// 文言変更時は 1 → 2 と Menu Finish/2 を実行し、宣言した全字形を明示的に追加・検査する。
    /// </summary>
    public static class KD_CreditSetupEditor
    {
        const string TmpEssentialsPackage =
            "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage";
        const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        const string SourceFontPath =
            "Assets/KominkaHillside/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf";
        const string FontAssetPath = "Assets/KominkaHillside/Font/KD_NotoSerif SDF.asset";

        const string BoardName = KD_DogDefs.CreditBoardName;
        const string LegacyPlaqueName = "KD_CreditPlaque";
        const string LegacyPlaqueTexPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures/KD_CreditPlaque.png";
        const string LegacyPlaqueMatPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_CreditPlaque.mat";
        const string PhotoSystemName = KD_DogDefs.PhotoSystemName;   // IMP-INF-003: 正本は KD_DogDefs

        // 許諾条件の文言。**一字も変えない** (VER-INF-001)
        const string DogCredit = "TORINOSUKEの家";

        // ---- 見た目 (古民家に合わせる: 暗い木肌 × 金 × 生成り。キャプション PNG と同系色) ----
        const float CanvasScale = 0.0008f; // 700×900px = 0.56×0.72m
        static readonly Vector2 CanvasSize = new Vector2(700f, 900f);
        const float FrameWidth = 8f;
        static readonly Color PanelColor = new Color(0.13f, 0.11f, 0.09f, 0.97f);
        static readonly Color FrameColor = new Color(0.76f, 0.62f, 0.30f, 1f);
        static readonly Color TextColor = new Color(0.97f, 0.95f, 0.90f, 1f);
        static readonly Color TitleColor = new Color(0.84f, 0.68f, 0.32f, 1f);
        static readonly Color ScrollTrackColor = new Color(0.07f, 0.06f, 0.05f, 1f);

        // スクロール領域 (KanjiFind HowTo board と同じ構成。字は縮めずスクロールで収める)
        const float ScrollTop = 290f;
        const float ScrollBottom = -420f;
        const float ScrollWidth = 640f;
        const float ScrollbarWidth = 14f;
        const float BodyFontSize = 26f;

        // ギャラリーの右脇 (旧プレートと同じ枠)。Gallery の子なので移動に追従する
        static readonly Vector3 BoardLocalPos = new Vector3(1.8f, 0.8f, 0f);

        const string TitleText = "クレジット";
        const string LeadText = "このワールドで使わせていただいた素材";

        /// <summary>クレジット本文の正本。素材を足したらここへ追記して 1 → 2 を再実行する。</summary>
        const string CreditsBody =
            "<color=#D6AD52>■ 犬モデル</color>\n" +
            DogCredit + "\n" +
            "\n" +
            "<color=#D6AD52>■ 舞台モデル</color>\n" +
            "モサンゴ屋「庭付き古民家」\n" +
            "\n" +
            "<color=#D6AD52>■ フォント</color> (SIL Open Font License 1.1)\n" +
            "ライセンス全文: https://openfontlicense.org\n" +
            "Noto Serif JP\n" +
            "Copyright 2012 Google Inc. All Rights Reserved.\n" +
            "\n" +
            "<color=#D6AD52>■ 動画プレイヤー</color>\n" +
            "VizVid (JLChnToZ, MIT License)";

        // ================================================================ 0: TMP Essentials

        [MenuItem("Tools/KominkaHillside/Menu/2a Import TMP Essentials")]
        public static void ImportTmpEssentials()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TmpSettingsPath) != null)
            {
                Debug.Log("[KD_Credit] TMP Essentials は導入済み (" + TmpSettingsPath + ")");
                return;
            }
            if (!File.Exists(Path.GetFullPath(TmpEssentialsPackage)))
            {
                Debug.LogError("[KD_Credit] TMP パッケージが見つからない: " + TmpEssentialsPackage);
                return;
            }
            AssetDatabase.ImportPackage(TmpEssentialsPackage, false);
            Debug.Log("[KD_Credit] TMP Essentials をインポートした (取り込み完了後に 1 - Build Font Asset へ)");
        }

        // ================================================================ 1: Font asset

        [MenuItem("Tools/KominkaHillside/Menu/2 Build Font Asset")]
        public static void BuildFontAsset()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_Credit] Build font asset");

            if (AssetDatabase.LoadMainAssetAtPath(TmpSettingsPath) == null)
            {
                log.AppendLine("FAIL TMP Essentials 未導入。先に 0 - Import TMP Essentials を実行");
                Finish("Logs/imp-env-001-font.txt", log);
                return;
            }
            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                log.AppendLine("FAIL 元フォントが無い: " + SourceFontPath);
                Finish("Logs/imp-env-001-font.txt", log);
                return;
            }

            // 収録文字 = クレジットの全文言 + ASCII 印字可能域。
            // 文言を変えたら CreditsBody を直して再実行 (この関数が正本から拾い直す)
            var chars = new StringBuilder();
            chars.Append(TitleText).Append(LeadText).Append(CreditsBody);
            for (char c = ' '; c <= '~'; c++) chars.Append(c);

            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
            {
                var so = new SerializedObject(existing);
                so.FindProperty("m_SourceFontFile").objectReferenceValue = sourceFont;
                so.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = sourceFont;
                so.ApplyModifiedPropertiesWithoutUndo();
                existing.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                var needed = new StringBuilder();
                foreach (char c in chars.ToString()) if (!char.IsControl(c) && !existing.HasCharacter(c)) needed.Append(c);
                try
                {
                    if (needed.Length > 0)
                    {
                        existing.TryAddCharacters(needed.ToString(), out string absent);
                        if (!string.IsNullOrEmpty(absent)) throw new System.InvalidOperationException("Credit glyphs missing: " + absent);
                    }
                }
                finally { existing.atlasPopulationMode = AtlasPopulationMode.Static; }
                EditorUtility.SetDirty(existing); AssetDatabase.SaveAssets();
                log.AppendLine("RESULT: PASS (existing font GUID and glyphs preserved)");
                Finish("Logs/imp-env-001-font.txt", log);
                return;
            }

            // 2048² / 60pt / padding 6 ≈ 780 グリフ収容 (vrc-tmp-font §1)。今は 100 字程度
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont, 60, 6, GlyphRenderMode.SDFAA, 2048, 2048,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);
            if (fontAsset == null)
            {
                log.AppendLine("FAIL フォントアセット生成に失敗");
                Finish("Logs/imp-env-001-font.txt", log);
                return;
            }
            if (!fontAsset.TryAddCharacters(chars.ToString(), out string missing) && !string.IsNullOrEmpty(missing))
                log.AppendLine($"WARN 字形が無い文字 ({missing.Length} 字): {missing}");
            else
                log.AppendLine("全文字の字形を確認 (欠落なし)");

            // Static へ (元 ttf への参照が外れビルドに含まれない。焼いたグリフは残る)
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;

            // 作り直し (GUID が変わる) は初回のみ許容。以後の文言追加は本メニューを
            // 再実行せず TryAddCharacters 方式へ切り替えること (vrc-tmp-font §2)。
            // 既存があれば SerializedObject 書き換えではなく「参照が板 1 枚だけ」なので
            // 2 - Build Credit Board の再実行で貼り直せる
            string baseName = Path.GetFileNameWithoutExtension(FontAssetPath);
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            fontAsset.material.name = baseName + " Material";
            fontAsset.atlasTextures[0].name = baseName + " Atlas";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontAssetPath);
            log.AppendLine($"created {FontAssetPath} (グリフ {fontAsset.glyphTable.Count})");
            log.AppendLine("RESULT: PASS");
            Finish("Logs/imp-env-001-font.txt", log);
        }

        // ================================================================ 2: Board

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Credit/2 - Build Credit Board")]
        public static void BuildBoard()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_Credit] Build credit board");

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                log.AppendLine("FAIL フォントアセットが無い。先に 1 - Build Font Asset を実行");
                Finish("Logs/imp-env-001-setup.txt", log);
                return;
            }

            // IMP-INF-003: ルート限定の走査をやめ、シーン全体から名前で引く
            var photoGo = KD_SceneObjects.Find(PhotoSystemName);
            var galleryT = photoGo == null ? null : photoGo.transform.Find("Gallery");
            if (galleryT == null)
            {
                log.AppendLine("FAIL " + PhotoSystemName + "/Gallery が見つからない (Photo System を先に構築)");
                Finish("Logs/imp-env-001-setup.txt", log);
                return;
            }

            // 旧プレート方式 (2026-08-11 改訂) の撤去コードは 2026-09-13 に削除 (IMP-INF-019 C4)。
            // 残骸が無いことは Verify (3) が引き続き検査する。

            // 冪等: 作り直す (位置は既存を引き継ぐ — 実機で動かした調整を潰さない)
            Vector3 localPos = BoardLocalPos;
            Quaternion localRot = Quaternion.identity;
            var oldBoard = galleryT.Find(BoardName);
            if (oldBoard != null)
            {
                localPos = oldBoard.localPosition;
                localRot = oldBoard.localRotation;
                Object.DestroyImmediate(oldBoard.gameObject);
                log.AppendLine("rebuilt at kept position " + localPos);
            }

            var root2 = new GameObject(BoardName);
            root2.transform.SetParent(galleryT, false);
            root2.transform.localPosition = localPos;
            root2.transform.localRotation = localRot;

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root2.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<CanvasScaler>();
            // スクロールを掴むため操作面にする (KanjiFind T2 と同じ)。
            // VRCUiShape は VRChat がランタイムで Collider を整える。Layer は Default のまま
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.AddComponent<VRCUiShape>();
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = CanvasSize;
            // 親 (Gallery) のスケール 0.5 を打ち消して実寸を保つ
            var ls = galleryT.lossyScale;
            canvasRect.localScale = new Vector3(
                CanvasScale / Mathf.Max(ls.x, 1e-4f),
                CanvasScale / Mathf.Max(ls.y, 1e-4f),
                CanvasScale / Mathf.Max(ls.z, 1e-4f));

            var frame = CreateUi("PanelFrame", canvasGo.transform, Vector2.zero, CanvasSize);
            var frameImage = frame.AddComponent<Image>();
            frameImage.color = FrameColor;
            frameImage.raycastTarget = false;
            var panel = CreateUi("Panel", canvasGo.transform, Vector2.zero,
                CanvasSize - Vector2.one * (FrameWidth * 2f));
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = false;

            var title = CreateText("Title", canvasGo.transform, new Vector2(0f, 385f),
                new Vector2(640f, 80f), font, 56f, TitleColor, TextAlignmentOptions.Center);
            title.text = TitleText;
            var lead = CreateText("Lead", canvasGo.transform, new Vector2(0f, 322f),
                new Vector2(640f, 44f), font, 24f, TextColor, TextAlignmentOptions.Center);
            lead.text = LeadText;

            BuildScrollBody(canvasGo.transform, font);
            if (KD_SceneObjects.Find("KD_Language") != null) KD_MenuFinishEditor.BuildCreditRows(log);
            KD_MenuFinishEditor.RebindIfPresent(log);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine($"built {BoardName} under Gallery at local {localPos} (0.56×0.72m)");
            log.AppendLine("SETUP RESULT: PASS (scene not saved yet)");
            Finish("Logs/imp-env-001-setup.txt", log);
        }

        /// <summary>本文をスクロール領域として組む (KanjiFind HowTo board の構成)。
        /// 字は縮めない — 素材が増えたらスクロールで収める。バーは常時表示
        /// (「まだ下に続く」を伝える唯一の手掛かり)。スクロール位置はローカル。</summary>
        static void BuildScrollBody(Transform canvasTf, TMP_FontAsset font)
        {
            float height = ScrollTop - ScrollBottom;
            float centerY = (ScrollTop + ScrollBottom) * 0.5f;

            GameObject viewportGo;
            var scrollRect = BuildScrollArea(canvasTf, "ScrollView",
                new Vector2(0f, centerY), new Vector2(ScrollWidth, height), ScrollbarWidth,
                PanelColor, ScrollTrackColor, FrameColor, out viewportGo);
            float viewportWidth = ((RectTransform)viewportGo.transform).sizeDelta.x;

            // 本文そのものを Content にする (TMP は ILayoutElement — ContentSizeFitter が直接効く)
            var body = CreateText("Body", viewportGo.transform, Vector2.zero,
                new Vector2(viewportWidth, height), font, BodyFontSize, TextColor,
                TextAlignmentOptions.TopLeft);
            var bodyRect = (RectTransform)body.transform;
            bodyRect.anchorMin = new Vector2(0f, 1f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0.5f, 1f);
            bodyRect.anchoredPosition = Vector2.zero;
            bodyRect.sizeDelta = new Vector2(0f, height);
            var fitter = body.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            body.text = CreditsBody;

            scrollRect.content = bodyRect;
            LayoutRebuilder.ForceRebuildLayoutImmediate(bodyRect);
        }

        /// <summary>スクロール領域の器 (ScrollView + Viewport + Scrollbar) を作る共通ビルダー。
        /// IMP-ENV-005 でおじいさんメニューと共用化した。
        /// **content は呼び出し側が viewport の子に作って `scrollRect.content` へ差す**
        /// (本文だけの板は TMP そのもの、ボタン列は器の RectTransform を content にする)。
        /// 冪等ではなく毎回作る前提 — 再実行する Setup は同名の既存を先に破棄すること。
        /// ビューポートの Image はドラッグを拾うレイキャスト面なので外さない。</summary>
        public static ScrollRect BuildScrollArea(Transform parent, string name, Vector2 center,
            Vector2 size, float scrollbarWidth, Color viewportColor, Color trackColor,
            Color handleColor, out GameObject viewportGo)
        {
            float viewportWidth = size.x - scrollbarWidth - 6f;

            var scrollGo = CreateUi(name, parent, center, size);
            var scrollRect = scrollGo.AddComponent<ScrollRect>();

            viewportGo = CreateUi("Viewport", scrollGo.transform,
                new Vector2(-(size.x - viewportWidth) * 0.5f, 0f),
                new Vector2(viewportWidth, size.y));
            var viewportImage = viewportGo.AddComponent<Image>();
            viewportImage.color = viewportColor;
            viewportGo.AddComponent<RectMask2D>();

            var scrollbar = BuildScrollbar(scrollGo.transform, size, scrollbarWidth,
                trackColor, handleColor);

            scrollRect.viewport = (RectTransform)viewportGo.transform;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            return scrollRect;
        }

        static Scrollbar BuildScrollbar(Transform scrollTf, Vector2 areaSize,
            float scrollbarWidth, Color trackColor, Color handleColor)
        {
            var barGo = CreateUi("Scrollbar", scrollTf,
                new Vector2((areaSize.x - scrollbarWidth) * 0.5f, 0f),
                new Vector2(scrollbarWidth, areaSize.y));
            var barImage = barGo.AddComponent<Image>();
            barImage.color = trackColor;
            var scrollbar = barGo.AddComponent<Scrollbar>();

            var slidingArea = CreateUi("Sliding Area", barGo.transform, Vector2.zero,
                new Vector2(scrollbarWidth, areaSize.y));
            var slidingRect = (RectTransform)slidingArea.transform;
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.sizeDelta = Vector2.zero;

            var handleGo = CreateUi("Handle", slidingArea.transform, Vector2.zero,
                new Vector2(scrollbarWidth, 60f));
            var handleImage = handleGo.AddComponent<Image>();
            handleImage.color = handleColor; // つまみは目立つ色 — 触れるものだと分かる
            var handleRect = (RectTransform)handleGo.transform;
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.sizeDelta = Vector2.zero;

            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            return scrollbar;
        }

        // ================================================================ 3: Verify

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Credit/3 - Verify Credit Board")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_Credit] Verify");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            Check(AssetDatabase.LoadMainAssetAtPath(TmpSettingsPath) != null, "TMP Essentials imported");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            Check(font != null, "font asset exists: " + FontAssetPath);
            if (font != null)
            {
                Check(font.atlasPopulationMode == AtlasPopulationMode.Static,
                    "font atlas Static (all EN/JP glyphs explicitly baked)");
                // 表示文言の全文字が焼けているか (vrc-tmp-font §3 の機械確認)
                string all = TitleText + LeadText + CreditsBody;
                var missing = new StringBuilder();
                foreach (char c in all)
                    if (c != '\n' && !font.HasCharacter(c) && missing.ToString().IndexOf(c) < 0)
                        missing.Append(c);
                Check(missing.Length == 0, "all credit chars baked" +
                    (missing.Length == 0 ? "" : $" (missing {missing.Length}: {missing})"));
            }

            // IMP-INF-003: ルート限定の走査をやめ、シーン全体から名前で引く
            var photoGo = KD_SceneObjects.Find(PhotoSystemName);
            var galleryT = photoGo == null ? null : photoGo.transform.Find("Gallery");
            var boardT = galleryT == null ? null : galleryT.Find(BoardName);
            Check(boardT != null, BoardName + " exists under Gallery");
            Check(galleryT != null && galleryT.Find(LegacyPlaqueName) == null,
                "legacy plaque removed");
            Check(AssetDatabase.LoadMainAssetAtPath(LegacyPlaqueTexPath) == null &&
                AssetDatabase.LoadMainAssetAtPath(LegacyPlaqueMatPath) == null,
                "legacy plaque assets deleted");

            if (boardT != null)
            {
                var canvasT = boardT.Find("Canvas");
                var canvas = canvasT == null ? null : canvasT.GetComponent<Canvas>();
                Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace, "Canvas is WorldSpace");
                if (canvasT != null)
                {
                    Check(canvasT.GetComponent<GraphicRaycaster>() != null, "GraphicRaycaster on Canvas");
                    Check(canvasT.GetComponent<VRCUiShape>() != null, "VRCUiShape on Canvas");
                    Check(canvasT.gameObject.layer == 0, "Canvas layer == Default (UI だと動かない)");
                    var size = Vector3.Scale((Vector3)((RectTransform)canvasT).sizeDelta, canvasT.lossyScale);
                    Check(Mathf.Abs(size.x - CanvasSize.x * CanvasScale) < 0.02f,
                        $"canvas world width ≈ {CanvasSize.x * CanvasScale:F2}m (actual {size.x:F3})");

                    var scrollT = canvasT.Find("ScrollView");
                    var scroll = scrollT == null ? null : scrollT.GetComponent<ScrollRect>();
                    Check(scroll != null && scroll.vertical && !scroll.horizontal, "ScrollRect vertical only");
                    Check(scroll != null && scroll.content != null && scroll.viewport != null &&
                        scroll.verticalScrollbar != null, "ScrollRect fully wired");
                    var body = scroll == null || scroll.content == null
                        ? null : scroll.content.GetComponent<TextMeshProUGUI>();
                    var labels = boardT.GetComponentsInChildren<TextMeshProUGUI>(true);
                    bool hasAttribution = false;
                    foreach (var label in labels) if (label.text.Contains(DogCredit)) hasAttribution = true;
                    Check(hasAttribution,
                        $"body contains 指定文言「{DogCredit}」");
                    Check((body != null && body.font == font) || (scroll != null && scroll.content.name == "CreditRows"), "credit content is wired");
                }
            }

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail} checks)");
            Finish("Logs/imp-env-001-verify.txt", log);
        }

        // ================================================================ helpers

        static GameObject CreateUi(string name, Transform parent, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
            return go;
        }

        static TextMeshProUGUI CreateText(string name, Transform parent, Vector2 anchoredPos,
            Vector2 size, TMP_FontAsset font, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            var go = CreateUi(name, parent, anchoredPos, size);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.text = "";
            return text;
        }

        static void Finish(string file, StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
