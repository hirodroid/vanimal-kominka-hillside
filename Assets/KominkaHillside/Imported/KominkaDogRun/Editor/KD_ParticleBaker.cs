using System.IO;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// **implicit 関数からパーティクル用テクスチャを焼く**共通ヘルパ。
    ///
    /// 元は <see cref="KD_PetHeartsSetupEditor"/> の `BakeHeartTexture` の中身で、
    /// [IMP-TRT-001](../../../docs/development/issues/IMP-TRT-001-treat-system.md)
    /// 設計 5「撫でエフェクトとは**テクスチャの作り方だけ共有する**」を実装するために
    /// **コピーではなく移動**した。♡ 側は <see cref="Heart"/> を渡して同じ絵を焼く。
    ///
    /// ## 共有するのは「作り方」だけ
    ///
    /// 発火の口 (撫では持続 / おやつは単発)・同期の経路・付き先は共有しない。
    /// ここが持つのは **「f(x,y) &lt; 0 の内側を塗り、外側へ指数で減衰させ、
    /// 中心ほど明るくする」** という 1 つの型だけ。
    ///
    /// ## なぜ implicit 関数なのか
    ///
    /// 外部素材を持ち込まずにプロジェクトの中で完結し、しかも
    /// **絵の違いが式の違いとして残る** — 「なぜこの絵なのか」が後から読める。
    /// </summary>
    public static class KD_ParticleBaker
    {
        /// <summary>implicit 関数。**戻り値が負の領域が「内側」。**</summary>
        public delegate float Field(float x, float y);

        /// <summary>焼き方の指定。既定値は ♡ (2026-08-11 に実機で確認されたもの) に合わせてある。</summary>
        public struct Spec
        {
            /// <summary>1 辺のピクセル数。</summary>
            public int size;
            /// <summary>正規化座標の幅 (px 全体が [-span/2, span/2] に写る)。</summary>
            public float span;
            /// <summary>絵の中心 (0..1)。**♡ は下がとがっているので 0.42 と偏らせてある。**</summary>
            public float centerU, centerV;
            /// <summary>形。<c>f &lt; 0</c> が内側。</summary>
            public Field field;
            /// <summary>縁の外への減衰の強さ。**大きいほど輪郭がはっきりする。**</summary>
            public float edgeFalloff;
            /// <summary>縁の色 / 中心の色。</summary>
            public Color edgeColor, coreColor;
            /// <summary>中心の位置と締まり (グローの芯)。</summary>
            public float coreX, coreY, coreTightness;
            /// <summary>芯から外れたところの不透明度の下限 (0..1)。</summary>
            public float coreAlphaBase;
        }

        /// <summary>♡ の implicit 関数 `(x²+y²−1)³ − x²y³`。
        /// **撫での ♡ とおやつの骨型ビスケットが共有する唯一の式。**</summary>
        public static float Heart(float x, float y)
        {
            float a2 = x * x + y * y - 1f;
            return a2 * a2 * a2 - x * x * y * y * y;
        }

        /// <summary>♡ 用の既定値 (<see cref="KD_PetHeartsSetupEditor"/> が焼いていたものと同一)。</summary>
        public static Spec HeartSpec()
        {
            var s = new Spec();
            s.size = 256;
            s.span = 2.9f;
            s.centerU = 0.5f;
            s.centerV = 0.42f;
            s.field = Heart;
            s.edgeFalloff = 6f;
            s.edgeColor = new Color(1f, 0.45f, 0.6f);
            s.coreColor = Color.white;
            s.coreX = 0f;
            s.coreY = 0.1f;
            s.coreTightness = 1.4f;
            s.coreAlphaBase = 0.75f;
            return s;
        }

        /// <summary>焼いて PNG として書き出し、パーティクル向けの importer 設定を当てる。</summary>
        public static void Bake(string path, Spec spec)
        {
            int size = spec.size > 0 ? spec.size : 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float x = (px / (float)(size - 1) - spec.centerU) * spec.span;
                    float y = (py / (float)(size - 1) - spec.centerV) * spec.span;
                    float f = spec.field(x, y);                 // < 0 が内側
                    float alpha = f < 0f
                        ? 1f
                        : Mathf.Exp(-f * spec.edgeFalloff);     // 縁の外へ柔らかく減衰
                    float dx = x - spec.coreX;
                    float dy = y - spec.coreY;
                    float core = Mathf.Exp(-(dx * dx + dy * dy) * spec.coreTightness);
                    var c = Color.Lerp(spec.edgeColor, spec.coreColor, core);
                    c.a = alpha * (spec.coreAlphaBase + (1f - spec.coreAlphaBase) * core);
                    tex.SetPixel(px, py, c);
                }
            }
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// **「同じ絵が 2 枚焼かれていないか」を機械検査するための指紋。**
        ///
        /// 見ているのは **アルファだけ** — 色だけ変えて形が同じものを
        /// 「違う絵」と数えないため。ベイク関数の引数を間違えると
        /// **黙って同じ形が並ぶ**ので、そこを型で守るのがこの関数の存在理由。
        ///
        /// 読み込みは PNG のバイト列から自前の <see cref="Texture2D"/> へ展開する
        /// (importer の `isReadable` に依存しない)。読めなければ空文字。
        /// </summary>
        public static string AlphaFingerprint(string path, int cells = 8)
        {
            if (!File.Exists(path)) return "";
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(tex);
                return "";
            }
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            var sb = new System.Text.StringBuilder(cells * cells * 2);
            for (int cy = 0; cy < cells; cy++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    int x0 = cx * w / cells, x1 = (cx + 1) * w / cells;
                    int y0 = cy * h / cells, y1 = (cy + 1) * h / cells;
                    long sum = 0, n = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++) { sum += px[y * w + x].a; n++; }
                    int avg = n == 0 ? 0 : (int)(sum / n);
                    sb.Append(((avg >> 4) & 0xF).ToString("x")); // 16 段階に丸める
                }
            }
            Object.DestroyImmediate(tex);
            return sb.ToString();
        }
    }
}
