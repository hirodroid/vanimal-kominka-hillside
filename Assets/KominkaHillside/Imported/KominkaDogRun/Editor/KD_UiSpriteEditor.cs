using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-TRT-004: **角丸 + 枠の 9-slice スプライトを焼く。**
    /// 正本は [hand-menu.md §5](../../../docs/development/design/hand-menu.md)。
    ///
    /// **Avatar Idle Arena `IMP-UI-009` の `AR_UiSpriteEditor` の移植であって、発明ではない。**
    /// 半径・枠幅・明度比・9-slice ボーダーはあちらが Play 5 回で確定させた値をそのまま使う。
    ///
    /// ## Image 1 枚で角丸と枠を両立させる
    ///
    /// UI/Default は <c>最終色 = spriteRGB × Image.color</c> の乗算なので、
    /// **スプライトの RGB に「枠 = 明るい / 面 = 暗い」を焼いておけば、
    /// `Image.color` を 1 色渡すだけで枠付きのボタンが描かれる。**
    ///
    /// - **層が増えないのでドローコールも増えない** (枠用の子 Image を足す案より安い)
    /// - **色を塗り分ける既存コードが 1 行も変わらない**
    /// - 枠は常に面の <c>1 / FaceLevel</c> = 1.82 倍明るいので、どの状態でも枠として読める
    ///
    /// ## 使うときの必須設定 (`vrc-udonsharp` §5)
    ///
    /// <code>
    /// img.sprite = KD_UiSpriteEditor.LoadRound();
    /// img.type   = Image.Type.Sliced;      // **Filled も Simple も不可**
    /// img.pixelsPerUnitMultiplier = 1f;    // 書き忘れると角丸半径が canvas 上で化ける
    /// </code>
    ///
    /// `Filled` は 9-slice のボーダーを無視して全体を引き伸ばすので、
    /// 24px の角丸が数百 px へ伸びて破綻する (「角がびよーん」)。
    /// `Simple` も同じ。**Verify が `Sliced` であることを機械検査する。**
    ///
    /// ## 丸みと枠は押せるものだけに使う
    ///
    /// タブ・ラベル・枠インジケータには当てない (hand-menu §5 / Avatar Idle Arena 一般則③)。
    /// 押せないものへ広げると、板の中で「操作するもの」と「読むもの」が混ざる。
    /// </summary>
    public static class KD_UiSpriteEditor
    {
        // Normal/hover/press are explicit across every builder. Selection is shown
        // by tabs/underlines, so EventSystem focus must not look like another selected tab.
        public static void ApplyButtonFeedback(UnityEngine.UI.Selectable button)
        {
            var colors = UnityEngine.UI.ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.24f, 1.24f, 1.24f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.65f);
            colors.fadeDuration = 0.08f;
            button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            button.colors = colors;
        }

        public const string RoundSpritePath = "Assets/KominkaHillside/Textures/UI/KD_UiRound.png";

        /// <summary>色スウォッチ用の円 (IMP-TRT-005)。角丸スプライトの「玉」流用は
        /// 半径を下げた時点で角丸四角に化けたので、円は専用スプライトに分離した。
        /// **`Simple` で貼る** — 円を 9-slice すると辺だけ伸びて楕円でも円でもない形になる。</summary>
        public const string CircleSpritePath = "Assets/KominkaHillside/Textures/UI/KD_UiCircle.png";

        // ================================================================
        // 役割トークン (IMP-INF-006 F-08)
        //
        // **色の正本はここ 1 か所。** 以前は同じ値が
        // `KD_TreatSetupEditor` と `KD_ShopSetupEditor` に独立したリテラルで
        // 書かれていて、片方だけ動かすと板と確認パネルの地が黙って割れた。
        // 意味は docs/development/design/world-ui-spec.md §3 が持つ。
        //
        // **役割で呼ぶ。** 「緑」「赤茶」ではなく「肯定」「閉じる」で呼ぶことで、
        // 同じ見た目に 2 つの意味が乗ったときに気付ける
        // (Touch の [メニュー] が「閉じる」の色で開いていた = F-06)。
        // ================================================================

        /// <summary>板・確認パネルの地。**暗い木の面 + 白文字**の 1 方向だけ。</summary>
        public static readonly Color SurfacePanel = new Color(0.12f, 0.10f, 0.08f, 0.92f);

        /// <summary>確認帯の地 (板の上に重なる面)。</summary>
        public static readonly Color SurfaceRaised = new Color(0.20f, 0.17f, 0.14f, 0.95f);

        /// <summary>**肯定の実行** — 出す / 呼ぶ / 一緒に寝る / 開催 / かざる / 購入。</summary>
        public static readonly Color ActionPrimary = new Color(0.35f, 0.50f, 0.30f);

        /// <summary>**装備** ([つける])。肯定だが**資源を消費しない**ことを色で分けている
        /// (IMP-INF-006 F-07 のユーザー判断 2026-09-07: 青のまま残す)。</summary>
        public static readonly Color ActionEquip = new Color(0.30f, 0.42f, 0.52f);

        /// <summary>取り消し・退避 — しまう / やめる / もどる / **Touch の [メニュー]**。</summary>
        public static readonly Color ActionNeutral = new Color(0.34f, 0.30f, 0.28f);

        /// <summary>**板を消す** ([とじる])。開く操作には使わない (F-06)。</summary>
        public static readonly Color ActionClose = new Color(0.42f, 0.26f, 0.24f);

        /// <summary>タブの消灯色 = 板の中で最も暗い押せる面。</summary>
        public static readonly Color TabOff = new Color(0.20f, 0.18f, 0.16f);

        /// <summary>[みんなにも開放] の消灯色 (トグルの off)。</summary>
        public static readonly Color ToggleOff = new Color(0.30f, 0.28f, 0.24f);

        /// <summary>区切り・下線・資源バーの満タン。**棚と板で同じ金**。</summary>
        public static readonly Color AccentGold = new Color(0.84f, 0.68f, 0.32f);

        /// <summary>資源バーの溝。</summary>
        public static readonly Color BarTrough = new Color(0.20f, 0.17f, 0.14f, 1f);

        /// <summary>資源バーの中身 (満たない間)。</summary>
        public static readonly Color BarFill = new Color(0.55f, 0.42f, 0.10f, 1f);

        // ---- Avatar Idle Arena IMP-UI-009 P1 で確定した値。**推測で動かさない** ----
        // (Radius だけはこのワールドの Play で差し戻されて離れた。下記)

        /// <summary>1 辺の px。9-slice なので実寸ではなく「角の解像度」を決める値。</summary>
        public const int Size = 96;

        /// <summary>9-slice のボーダー (px)。**角丸半径より大きく取る** —
        /// 小さいと角の内側が引き伸ばし領域に入って丸みが歪む。</summary>
        public const int Border = 30;

        /// <summary>角丸半径 (px)。PPU 100 + `pixelsPerUnitMultiplier = 1` なので
        /// **canvas 上でもそのまま 12px**。
        ///
        /// **移植値 24 からの明示的な逸脱 (IMP-TRT-005 / 2026-09-03 Play 差し戻し)** —
        /// 高さ 40〜52px のボタンに半径 24 は上下ほぼ半円 (ピル形) で、
        /// ユーザー評「ふにゃーってしすぎ」。「推測で動かさない」の逆 =
        /// **実測 (Play の差し戻し) でだけ動かす**の 1 例目。
        /// 変えたら `Hand Menu/1 - Bake UI Sprite` → `Treat System/2` → `Shop Keeper/2`。</summary>
        public const float Radius = 12f;

        /// <summary>枠の太さ (px)。</summary>
        public const float RingWidth = 6f;

        /// <summary>枠の明度。<c>Image.color</c> をそのまま出す = 1.00。</summary>
        public const float RingLevel = 1.00f;

        /// <summary>面の明度。**枠 : 面 = 1.00 : 0.55。**
        ///
        /// ⚠ **暗いからといって色定数を持ち上げないこと。** 線形で約 3.8 倍が必要になり、
        /// 点灯色 (金・緑) が 1.0 で頭打ちして白へ潰れ、色分けそのものが壊れる
        /// (Avatar Idle Arena が実測して取り下げた)。**一律に暗くなるぶんには
        /// 明暗の順序が全部保たれる**ので、明るさを変えたいときはこの 1 定数を動かす。</summary>
        public const float FaceLevel = 0.55f;

        /// <summary>PPU。`Canvas.referencePixelsPerUnit` の既定と揃える。</summary>
        public const float PixelsPerUnit = 100f;

        [MenuItem("Tools/KominkaHillside/Menu/1 Bake UI Sprite")]
        public static void BakeMenu()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_UiSprite] Bake");
            Bake(log);
            log.AppendLine("RESULT: "
                + (LoadRound() != null && LoadCircle() != null ? "OK" : "FAIL"));
            Debug.Log(log.ToString());
        }

        /// <summary>角丸 + 枠のスプライト (ボタン用) と円 (スウォッチ用) を焼く。
        /// **既にあっても毎回焼き直す** —
        /// 上の定数を変えたときに追随しないと、定数と実物が黙ってずれる。</summary>
        public static void Bake(StringBuilder log)
        {
            // ボタン: 角丸 + 9-slice。円: 半径 = 1/2 辺で角丸矩形がそのまま円になる
            // (ボーダー 0 = 9-slice しない。`Simple` で貼るので不要)
            BakeOne(RoundSpritePath, Radius, Border, log);
            BakeOne(CircleSpritePath, Size * 0.5f, 0, log);
        }

        static void BakeOne(string path, float radius, int border, StringBuilder log)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            for (int py = 0; py < Size; py++)
            {
                for (int px = 0; px < Size; px++)
                {
                    // 角丸矩形の符号付き距離。**負が内側**
                    float d = RoundedBoxDistance(px + 0.5f, py + 0.5f, radius);

                    // 外側は透明。**1px ぶんアンチエイリアスする** (角がギザつくと
                    // 「角丸にした」効果がそのぶん削れる)
                    float alpha = Mathf.Clamp01(0.5f - d);
                    // 縁から RingWidth までが枠、その内側が面。境目も 1px でぼかす
                    float ring = Mathf.Clamp01(d + RingWidth + 0.5f);
                    float level = Mathf.Lerp(FaceLevel, RingLevel, ring);

                    tex.SetPixel(px, py, new Color(level, level, level, alpha));
                }
            }
            tex.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                log.AppendLine("FAIL importer が取れない: " + path);
                return;
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            // **9-slice のボーダー。ここが 0 だと Sliced にしても角が伸びる** (円は 0 でよい)
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            // **非圧縮。** 96x96 は 36KB しかないのに、圧縮すると枠の 1 段差が滲む
            var settings = importer.GetDefaultPlatformTextureSettings();
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();

            log.AppendLine("baked " + path + " (" + Size + "px / border " + border
                + " / r" + radius + " / ring " + RingWidth + " / 面 " + FaceLevel + ")");
        }

        /// <summary>角丸矩形の符号付き距離 (px)。**負が内側・0 が縁。**
        ///
        /// 教科書どおりの形 (`min(max(q.x,q.y),0) + length(max(q,0)) - r`) をそのまま書く。
        /// **角だけを別扱いする分岐を書かない** — 分岐で作ると、角と辺の継ぎ目に
        /// 1px の段が出て「角丸なのに角がギザつく」という一番みっともない絵になる。</summary>
        static float RoundedBoxDistance(float x, float y, float radius)
        {
            float half = Size * 0.5f;
            // 中心を原点に取り、角丸の中心が作る内側の矩形からの距離を測る
            float qx = Mathf.Abs(x - half) - (half - radius);
            float qy = Mathf.Abs(y - half) - (half - radius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f)
                                     + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }

        /// <summary>焼いたスプライト。無ければ null (呼び出し側が WARN を出す)。</summary>
        public static Sprite LoadRound()
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(RoundSpritePath);
        }

        /// <summary>スウォッチ用の円。無ければ null (呼び出し側が WARN を出す)。</summary>
        public static Sprite LoadCircle()
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);
        }

        /// <summary>**押せるものへ角丸 + 枠を当てる。** タブやラベルへは当てない
        /// (hand-menu §5 — 丸みと枠は押せるものだけ)。</summary>
        public static void ApplyRoundSkin(UnityEngine.UI.Image img, Sprite round)
        {
            if (img == null || round == null) return;
            img.sprite = round;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            // **書き忘れると角丸半径が canvas 上で化ける** (`vrc-udonsharp` §5)
            img.pixelsPerUnitMultiplier = 1f;
        }

        /// <summary>**色スウォッチなど「円に見せたい」面。** `Sliced` にしない —
        /// 円を 9-slice すると辺だけ伸びて楕円でも円でもない形になる。
        /// 貼る先の Rect は正方形であること (Simple は Rect なりに伸びる)。</summary>
        public static void ApplyCircleSkin(UnityEngine.UI.Image img, Sprite circle)
        {
            if (img == null || circle == null) return;
            img.sprite = circle;
            img.type = UnityEngine.UI.Image.Type.Simple;
        }

        /// <summary>**タブ・見出しなど「押して切り替えるが操作対象ではない」面。**
        /// 角のある矩形が下線とつながって紙のインデックスのように読める。</summary>
        public static void ApplyFlatSkin(UnityEngine.UI.Image img)
        {
            if (img == null) return;
            img.sprite = null;
            img.type = UnityEngine.UI.Image.Type.Simple;
        }
    }
}
