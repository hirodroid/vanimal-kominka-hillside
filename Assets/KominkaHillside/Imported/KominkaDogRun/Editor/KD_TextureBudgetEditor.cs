using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-ENV-010: **モバイル (Android / iOS) のテクスチャ上限を表で持って再適用する。**
    ///
    /// 動機は見た目でも性能でもなく **100MB 制限の余裕** — アクセサリーやパーティクルを
    /// 今後足していくと、処理負荷ではなくビルドサイズがキャップになる。
    ///
    /// **なぜツールが要るのか (設定を Inspector で入れて終わりにしない理由):**
    /// 対象は購入アセット (Allsky) で、`.gitignore` 除外なので **`.meta` がコミットされない**。
    /// 新しいクローンや再インポートで Override が黙って消える。
    /// **設定の正本はこのファイルの <see cref="Table"/>** で、`.meta` はその出力にすぎない。
    ///
    /// **Standalone (PCVR) には触らない** — 「モバイル対応のために PCVR の体験品質を落とさない」
    /// (CLAUDE.md)。Verify は Standalone が既定のままであることも数える。
    ///
    /// 将来 [IMP-INF-004](../../../docs/development/issues/IMP-INF-004-platform-support-harness.md) の
    /// 「テクスチャ Override 漏れ」検出がこの表を吸収する想定。
    /// </summary>
    public static class KD_TextureBudgetEditor
    {
        const string LogFile = "Logs/kd-texture-budget.txt";

        /// <summary>Unity の platform 名。Android は Quest とスマホの両方を兼ねる。</summary>
        const string AndroidPlatform = "Android";
        const string IosPlatform = "iPhone";

        /// <summary>PCVR の上限。ここを下げないことが Verify の検査項目。</summary>
        const int StandaloneMax = 2048;

        struct Entry
        {
            public string path;
            public int mobileMax;
            public string why;
        }

        /// <summary>
        /// **モバイル上限の正本。** 足すときは「なぜその値か」を <c>why</c> に書く。
        /// </summary>
        static readonly Entry[] Table = new Entry[]
        {
            new Entry
            {
                path = "Assets/Allsky/Day Sun High/Day Sun High Clear/Day Sun High Clear.png",
                mobileMax = 1024,
                why = "空 (Cubemap 6 面)。PC は 2048 のまま。2048 で約 12.6MB / 1024 で約 3.1MB。"
                    + "512 まで落とせば約 1.5MB だが、空は画面占有面積が大きく Quest の解像度では帯が見える",
            },
        };

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Texture Budget/1 - Apply Mobile Overrides")]
        public static void Apply()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_TextureBudget] Apply (Android / iOS のテクスチャ上限)");
            log.AppendLine();

            int changed = 0, fail = 0;
            for (int i = 0; i < Table.Length; i++)
            {
                var e = Table[i];
                log.AppendLine("- " + e.path);
                log.AppendLine("  why: " + e.why);
                var ti = AssetImporter.GetAtPath(e.path) as TextureImporter;
                if (ti == null)
                {
                    log.AppendLine("  FAIL TextureImporter が取れない (購入アセット未インポート?)");
                    fail++;
                    continue;
                }

                bool touched = false;
                touched |= ApplyOne(ti, AndroidPlatform, e.mobileMax, log);
                touched |= ApplyOne(ti, IosPlatform, e.mobileMax, log);
                if (touched)
                {
                    ti.SaveAndReimport();
                    changed++;
                    log.AppendLine("  reimported");
                }
                else
                {
                    log.AppendLine("  すでに希望どおり");
                }
                log.AppendLine("  " + Measure(e.path));
            }

            log.AppendLine();
            log.AppendLine("changed=" + changed + " fail=" + fail);
            Write(log);
            Debug.Log("[KD_TextureBudget] Apply 完了 changed=" + changed + " fail=" + fail + " -> " + LogFile);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Texture Budget/2 - Verify")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_TextureBudget] Verify");
            log.AppendLine();

            int fail = 0;
            for (int i = 0; i < Table.Length; i++)
            {
                var e = Table[i];
                log.AppendLine("- " + e.path);
                var ti = AssetImporter.GetAtPath(e.path) as TextureImporter;
                if (ti == null)
                {
                    log.AppendLine("  FAIL TextureImporter が取れない");
                    fail++;
                    continue;
                }

                CheckPlatform(ti, AndroidPlatform, e.mobileMax, log, ref fail);
                CheckPlatform(ti, IosPlatform, e.mobileMax, log, ref fail);

                // PCVR を落としていないこと (モバイル対応のために PCVR の品質を落とさない)
                var pc = ti.GetPlatformTextureSettings("Standalone");
                bool pcOk = !pc.overridden || pc.maxTextureSize >= StandaloneMax;
                if (!pcOk) fail++;
                log.AppendLine("  " + (pcOk ? "OK  " : "FAIL") + " Standalone は " + StandaloneMax
                    + " 以上のまま (overridden=" + pc.overridden + " max=" + pc.maxTextureSize + ")");
                log.AppendLine("  " + Measure(e.path));
            }

            log.AppendLine();
            log.AppendLine(fail == 0 ? "PASS" : "FAIL " + fail + " 件");
            Write(log);
            if (fail == 0) Debug.Log("[KD_TextureBudget] Verify PASS -> " + LogFile);
            else Debug.LogError("[KD_TextureBudget] Verify FAIL " + fail + " 件 -> " + LogFile);
        }

        // ============================================================

        static bool ApplyOne(TextureImporter ti, string platform, int max, StringBuilder log)
        {
            var s = ti.GetPlatformTextureSettings(platform);
            if (s.overridden && s.maxTextureSize == max
                && s.format == TextureImporterFormat.Automatic
                && s.textureCompression == TextureImporterCompression.Compressed)
                return false;

            s.overridden = true;
            s.maxTextureSize = max;
            // 形式は Automatic のまま (Android は ASTC、iOS も ASTC へ解決される)。
            // 上限だけを動かすのが目的で、形式を手で決めると SDK 側の既定と食い違う
            s.format = TextureImporterFormat.Automatic;
            s.textureCompression = TextureImporterCompression.Compressed;
            ti.SetPlatformTextureSettings(s);
            log.AppendLine("  " + platform + " -> overridden=true max=" + max);
            return true;
        }

        static void CheckPlatform(TextureImporter ti, string platform, int max, StringBuilder log, ref int fail)
        {
            var s = ti.GetPlatformTextureSettings(platform);
            bool ok = s.overridden && s.maxTextureSize == max;
            if (!ok) fail++;
            log.AppendLine("  " + (ok ? "OK  " : "FAIL") + " " + platform + " overridden=" + s.overridden
                + " max=" + s.maxTextureSize + " (期待 overridden=True max=" + max + ")");
        }

        /// <summary>今エディタに載っている実サイズ。数字を持たない議論をしないため。</summary>
        static string Measure(string path)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (t == null) return "size: (読めない)";
            long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
            return "size: " + t.GetType().Name + " " + t.width + "x" + t.height
                + " editor mem " + (b / 1048576.0).ToString("F2") + "MB"
                + " (現在の Editor プラットフォームでの値。ビルド後の実測は SDK のビルドパネルで見る)";
        }

        static void Write(StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile));
            File.WriteAllText(LogFile, log.ToString());
        }
    }
}
