using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-001 段階 4: 表示名の補助フォント (設計書 §A.6)。**Static** (2026-09-27 方式変更)。
    ///
    /// 共通フォント KD_NotoSerif SDF は宣言した字だけを焼いた Static なので、VRChat の表示名の字の多くが □ になる。
    /// 表示名を出すラベル (人の一覧 16 行・通知板の名前) に限って、同じ書体の Static フォント KH_DisplayName SDF を使う。
    /// - 最初の版は Dynamic だったが、元の TTF (7.33 MiB) がビルドに入り Android が +3.53 MB になったので採らない。
    ///   Static で焼いてから元のフォントの参照を外す (Course/27 が共通フォントへ字形を足すのと同じ作法。vrc-world-ui §8)。
    /// - 文字集合: scripts/kh-gen-name-charset.js が作る KH_DisplayName_Charset.txt (ASCII・Latin-1・Latin 拡張 A・かな・全角英数・
    ///   JIS 第 1 水準 2,965 字・記号)。簡体字の多く・ハングル・絵文字・第 2 水準以降は対象外 (□ で出る。世界側の TMP に自動の fallback は無い前提)。
    /// - Noto Serif JP に無い Latin 拡張 A などは、TMP 付属の LiberationSans から作る小さな Static の fallback (KH_DisplayName Latin SDF) で出す。
    /// - 大きさ: サンプリング 18 pt・余白 2・SDFAA・Alpha8。本体 1024×2048、fallback 512²。Verify がバイト数と使用率を JSON に出す。
    ///   (2026-09-27: 20 pt・余白 3・2048² は Android +1.23 MB で目標 1.0 MB を超えたので、アトラスを半分にした)
    /// - 焼いた後は元フォントの参照を 2 本とも外す (実行時の m_SourceFontFile と Editor 用の m_SourceFontFile_EditorRef)。作り直しは 86 が毎回最初から行う。
    /// Course/86 がフォントを作り直して配線し、シーンを保存する。Course/85 を流し直したときも ApplyIfPresent で配線を戻す。</summary>
    public static class KH_CourseTravelNameFontEditor
    {
        public const string FontPath = "Assets/KominkaHillside/Font/KH_DisplayName SDF.asset";
        public const string LatinPath = "Assets/KominkaHillside/Font/KH_DisplayName Latin SDF.asset";
        public const string CharsetPath = "Assets/KominkaHillside/Font/KH_DisplayName_Charset.txt";
        public const string SourcePath = "Assets/KominkaHillside/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf";
        public const string LatinSourcePath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
        // 1 マス約 22 px → 約 4,200 マスに 3,680 字 (見込み使用率 7 割)。入り切らない字は 86 の lacking と 81 の not baked に出る
        public const int AtlasWidth = 1024, AtlasHeight = 2048, LatinAtlas = 512, SamplingPoints = 18, Padding = 2;
        const string OutDir = "Temp/imp-wld-001";

        // 表示名の見本 (Verify)。対象集合の字は欠字 0、対象外の字 (簡体字・ハングル・絵文字・第 2 水準) は記録だけ
        public static readonly (string name, string text)[] Samples =
        {
            ("en", "Alice_99 Bob-X Zoë Ñandú Øystein Łukasz Straße"),
            ("ja", "さくら ヤマト 渡辺 高橋 斎藤 ヴィ ー 〜 ﾔﾏﾀﾞ ＶＲ"),
            ("zh", "陳 劉 黄 王 李 张伟 黃"),
            ("symbol", "★☆♪※・！？（）「」【】#@&+=~^_|♡"),
            ("ko", "김민준 서연"),
            ("emoji", "😀🐶🌸"),
        };

        [Serializable] class BuildReport { public string utc, scene, status, reason; public int labelsWired, charset, baked, latinBaked, fontLacks; public string lacking; }

        [MenuItem("Tools/KominkaHillside/Course/86 - Build Display Name Font")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p = KH_CourseTravelEditor.FindPouch();
            var notice = UnityEngine.Object.FindObjectsOfType<KH_TravelNotice>(true).FirstOrDefault();
            if (p.travelPeopleLabels == null || p.travelPeopleLabels.Length == 0 || notice == null || notice.titleLabel == null)
                throw new InvalidOperationException("People list / notice missing: run Course/85 first");
            string charset = Charset();
            if (charset.Length == 0) throw new InvalidOperationException(CharsetPath + " missing: run node scripts/kh-gen-name-charset.js");
            var report = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = p.gameObject.scene.path, charset = charset.Length };
            // 本体 (Noto Serif JP) → 本体に無い字だけを fallback (LiberationSans) へ
            var font = Bake(FontPath, SourcePath, "KH_DisplayName SDF", AtlasWidth, AtlasHeight, charset, out string missing);
            report.baked = font.characterTable.Count;
            var latin = Bake(LatinPath, LatinSourcePath, "KH_DisplayName Latin SDF", LatinAtlas, LatinAtlas, missing, out string stillMissing);
            report.latinBaked = latin.characterTable.Count;
            font.fallbackFontAssetTable = new List<TMP_FontAsset> { latin };
            EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
            report.fontLacks = stillMissing.Length; report.lacking = stillMissing;
            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-name-font.unity", true);
            report.labelsWired = Apply(p, notice, font);
            EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
            if (!EditorSceneManager.SaveScene(p.gameObject.scene)) throw new IOException("Name font save failed");
            report.status = "BUILT";
            report.reason = "Static name font (" + AtlasWidth + "x" + AtlasHeight + " / " + SamplingPoints + " pt / padding " + Padding + " + Latin " + LatinAtlas + "²). Run Course/81, then Build Size.";
            File.WriteAllText(OutDir + "/name-font-build.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH Name Font] BUILT glyphs=" + report.baked + "+" + report.latinBaked + " lacking=" + report.fontLacks + " labels=" + report.labelsWired);
        }

        /// <summary>対象の文字集合 (区分ごとの行を 1 本に)。</summary>
        public static string Charset()
        {
            if (!File.Exists(CharsetPath)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var line in File.ReadAllLines(CharsetPath)) { int tab = line.IndexOf('\t'); if (tab >= 0) sb.Append(line.Substring(tab + 1)); }
            return sb.ToString();
        }

        /// <summary>Static のフォントを作り直して文字を焼く。Dynamic の間に焼き、Static へ戻して元フォントの参照 (ビルドに入る側) を外す。
        /// 大きさ・サンプリングが違う旧版 (Dynamic 版を含む) は消して作り直す (ラベルは Apply で配線し直す)。</summary>
        static TMP_FontAsset Bake(string path, string sourcePath, string name, int w, int h, string wanted, out string missing)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (source == null) throw new InvalidOperationException("Source font missing: " + sourcePath);
            var old = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            var font = TMP_FontAsset.CreateFontAsset(source, SamplingPoints, Padding, GlyphRenderMode.SDFAA, w, h, AtlasPopulationMode.Dynamic, false);
            if (font == null) throw new InvalidOperationException("TMP could not load " + sourcePath + " (Include Font Data?)");
            font.name = name;
            AssetDatabase.CreateAsset(font, path);
            font.atlasTextures[0].name = name + " Atlas";
            font.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
            AssetDatabase.AddObjectToAsset(font.material, font);
            missing = "";
            string need = string.Concat(wanted.Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c)).Distinct());
            try
            {
                if (need.Length > 0) font.TryAddCharacters(need, out missing);
                missing = missing ?? "";
            }
            finally
            {
                // **必ず Static へ戻す**。Dynamic のまま残ると元の TTF がビルドへ入る (段階 4 の最初の版の +3.5 MB)
                font.atlasPopulationMode = AtlasPopulationMode.Static;
                font.isMultiAtlasTexturesEnabled = false;
                // Editor 用の参照も外す (ビルドには入らないが、TTF への参照 0 を 81 で機械的に確かめるため)。Dynamic へ戻すと TMP はこの参照から元フォントを戻す
                var so = new SerializedObject(font);
                so.FindProperty("m_SourceFontFile").objectReferenceValue = null;
                so.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(font);
                AssetDatabase.SaveAssets();
            }
            return font;
        }

        /// <summary>表示名のラベル (人の一覧 16 行・通知板の名前) にだけ補助フォントを付ける。フォントがまだ無ければ何もしない (Course/85 から呼ぶ)。</summary>
        public static int ApplyIfPresent(KD_TreatPouch p, KH_TravelNotice notice)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            return font == null ? 0 : Apply(p, notice, font);
        }

        static int Apply(KD_TreatPouch p, KH_TravelNotice notice, TMP_FontAsset font)
        {
            int n = 0;
            foreach (var t in Labels(p, notice))
            {
                t.font = font; t.fontSharedMaterial = font.material; t.overflowMode = TextOverflowModes.Ellipsis;
                EditorUtility.SetDirty(t); n++;
            }
            return n;
        }

        static IEnumerable<TMP_Text> Labels(KD_TreatPouch p, KH_TravelNotice notice)
        {
            if (p.travelPeopleLabels != null) foreach (var t in p.travelPeopleLabels) if (t != null) yield return t;
            if (notice != null && notice.titleLabel != null) yield return notice.titleLabel;
        }

        // ============================================================ Course/81 から呼ぶ検査
        public static void Validate(KD_TreatPouch p, List<string> e, KH_CourseTravelEditor.Report report)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var latin = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LatinPath);
            if (font == null || latin == null) { e.Add("FAIL display-name fonts missing (run Course/86)"); return; }
            foreach (var f in new[] { font, latin })
            {
                if (f.atlasPopulationMode != AtlasPopulationMode.Static) e.Add("FAIL " + f.name + " must be Static (a Dynamic font puts its TTF in the build)");
                if (f.sourceFontFile != null) e.Add("FAIL " + f.name + " still references its source font (it would be built)");
                if (f.isMultiAtlasTexturesEnabled || f.atlasTextures == null || f.atlasTextures.Length != 1) e.Add("FAIL " + f.name + " must have exactly one atlas");
                var so = new SerializedObject(f);
                if (so.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue != null) e.Add("FAIL " + f.name + " keeps m_SourceFontFile_EditorRef (run Course/86)");
                // アトラスはフォントの中の Texture2D (取り込み設定が無い) なので、形式はアセットに書かれた 1 つで Android / iOS / PC 共通
                var tex = f.atlasTextures != null && f.atlasTextures.Length > 0 ? f.atlasTextures[0] : null;
                if (tex == null || tex.format != TextureFormat.Alpha8) e.Add("FAIL " + f.name + " atlas must be Alpha8 (is " + (tex == null ? "none" : tex.format.ToString()) + ")");
                else if (AssetDatabase.GetAssetPath(tex) != AssetDatabase.GetAssetPath(f) || AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) is TextureImporter)
                    e.Add("FAIL " + f.name + " atlas must be a sub-asset of the font (a separate texture file gets per-platform import settings)");
            }
            if (font.atlasWidth != AtlasWidth || font.atlasHeight != AtlasHeight || latin.atlasWidth != LatinAtlas || (int)font.faceInfo.pointSize != SamplingPoints || font.atlasPadding != Padding)
                e.Add("FAIL display-name font size (" + AtlasWidth + "x" + AtlasHeight + " / " + SamplingPoints + " pt / padding " + Padding + ", Latin " + LatinAtlas + "²): run Course/86");
            report.nameFontBuildRefs = BuildFontRefs(p.gameObject.scene, e);
            if (font.fallbackFontAssetTable == null || !font.fallbackFontAssetTable.Contains(latin)) e.Add("FAIL Latin fallback not wired");
            var notice = UnityEngine.Object.FindObjectsOfType<KH_TravelNotice>(true).FirstOrDefault();
            foreach (var t in Labels(p, notice)) if (t.font != font) { e.Add("FAIL display-name label not using the name font: " + t.name); break; }
            // 対象集合: 元のフォントのどちらかに字形がある字は、全部アトラスにあること
            string charset = Charset();
            var target = new HashSet<char>(charset);
            var src = AssetDatabase.LoadAssetAtPath<Font>(SourcePath); var latinSrc = AssetDatabase.LoadAssetAtPath<Font>(LatinSourcePath);
            var inSource = new HashSet<char>();
            foreach (var s in new[] { src, latinSrc })
            {
                if (s == null || FontEngine.LoadFontFace(s, SamplingPoints) != FontEngineError.Success) continue;
                foreach (char c in target) if (FontEngine.TryGetGlyphIndex(c, out uint g) && g != 0) inSource.Add(c);
            }
            var notBaked = target.Where(c => inSource.Contains(c) && !KD_LocalizationEditor.HasGlyph(font, c)).ToArray();
            var lacking = target.Where(c => !inSource.Contains(c)).ToArray();
            if (notBaked.Length > 0) e.Add("FAIL " + notBaked.Length + " target characters not baked: " + new string(notBaked.Take(40).ToArray()));
            // 見本: 対象集合の字は欠字 0、対象外は記録だけ
            var coverage = new List<string>();
            foreach (var (name, text) in Samples)
            {
                var missIn = new List<string>(); var outOfScope = new List<string>();
                for (int i = 0; i < text.Length; i++)
                {
                    bool pair = char.IsSurrogatePair(text, i);
                    string ch = pair ? text.Substring(i, 2) : text.Substring(i, 1);
                    if (pair) i++;
                    if (ch == " ") continue;
                    if (pair || !target.Contains(ch[0]) || !inSource.Contains(ch[0])) { outOfScope.Add(ch); continue; }
                    if (!KD_LocalizationEditor.HasGlyph(font, ch[0])) missIn.Add(ch);
                }
                coverage.Add(name + ": missing " + missIn.Count + (missIn.Count > 0 ? " (" + string.Join("", missIn) + ")" : "") + " / out of scope " + outOfScope.Count + (outOfScope.Count > 0 ? " (" + string.Join("", outOfScope) + ")" : ""));
                if (missIn.Count > 0) e.Add("FAIL display-name sample " + name + " has target glyphs missing: " + string.Join("", missIn));
            }
            report.nameFontCoverage = coverage.ToArray();
            report.nameFontCharset = target.Count;
            report.nameFontGlyphs = font.characterTable.Count + latin.characterTable.Count;
            report.nameFontLacking = new string(lacking);
            report.nameFontAtlasBytes = (long)font.atlasWidth * font.atlasHeight + (long)latin.atlasWidth * latin.atlasHeight;   // Alpha8 = 1 byte/px
            report.nameFontUsage = Usage(font) + " / latin " + Usage(latin);
            report.nameFontAssetBytes = new FileInfo(FontPath).Length + new FileInfo(LatinPath).Length;
        }

        /// <summary>ビルドに入る元フォント (TTF / OTF) の参照を数える。シーンの部品が Font を直に持つもの (UI.Text など) と、
        /// シーンが使う TMP フォント (fallback を辿る) のうち実行時の参照 m_SourceFontFile を持つもの・Dynamic のもの。
        /// Editor 用の参照 (m_SourceFontFile_EditorRef。共通フォントは残している) はビルドに入らないので数えない (Build Size/6 の editor-only と同じ扱い)。</summary>
        static string BuildFontRefs(UnityEngine.SceneManagement.Scene scene, List<string> e)
        {
            var refs = new List<string>();
            var fonts = new HashSet<TMP_FontAsset>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform) continue;
                    var it = new SerializedObject(c).GetIterator();
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var r = it.objectReferenceValue;
                        if (r is Font) refs.Add(c.GetType().Name + "(" + c.name + ")." + it.propertyPath + " -> " + AssetDatabase.GetAssetPath(r));
                        else if (r is TMP_FontAsset fa) fonts.Add(fa);
                    }
                }
            var queue = new Queue<TMP_FontAsset>(fonts);
            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                if (f.fallbackFontAssetTable != null) foreach (var fb in f.fallbackFontAssetTable) if (fb != null && fonts.Add(fb)) queue.Enqueue(fb);
            }
            foreach (var f in fonts)
            {
                var src = new SerializedObject(f).FindProperty("m_SourceFontFile").objectReferenceValue;
                if (src != null) refs.Add(f.name + ".m_SourceFontFile -> " + AssetDatabase.GetAssetPath(src));
                if (f.atlasPopulationMode != AtlasPopulationMode.Static) refs.Add(f.name + " is " + f.atlasPopulationMode + " (its source font is built)");
            }
            foreach (var r in refs) e.Add("FAIL source font reaches the build: " + r);
            return refs.Count + " refs / " + fonts.Count + " TMP fonts checked (" + string.Join(", ", fonts.Select(x => x.name).OrderBy(x => x)) + ")";
        }

        /// <summary>アトラスの使用率 (グリフの矩形 + 余白の面積 ÷ アトラスの面積)。</summary>
        static string Usage(TMP_FontAsset f)
        {
            double used = 0; int pad = f.atlasPadding;
            foreach (var g in f.glyphTable) used += (double)(g.glyphRect.width + pad * 2) * (g.glyphRect.height + pad * 2);
            double area = (double)f.atlasWidth * f.atlasHeight;
            return (area > 0 ? used / area * 100.0 : 0).ToString("F1") + "% of " + f.atlasWidth + "x" + f.atlasHeight + " (" + f.glyphTable.Count + " glyphs)";
        }
    }
}
