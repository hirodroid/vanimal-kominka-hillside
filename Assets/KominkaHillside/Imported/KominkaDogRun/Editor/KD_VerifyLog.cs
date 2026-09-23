using System.IO;
using System.Text;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-INF-019 (C2): Editor の Verify / Setup メニューが書く記録の共通基盤。
    ///
    /// 以前は各 Editor が `void Check(bool, string)` (43 変種) と `static void Finish(` (25 変種) を
    /// 写経していた。**新しい Verify を書くときはこれを使う** (`scripts/check-editor-helpers.js` が
    /// 写経の定義数を数えていて、増えると落ちる)。
    ///
    /// 使い方:
    /// <code>
    /// var v = new KD_VerifyLog("[KD_FooSetup] Verify");     // 見出し行
    /// v.Check(x != null, "KD_Foo exists");                    // "  ok   label" / "  FAIL label" と数える
    /// v.Info("-- section --");                                // 素の 1 行
    /// v.Note("scene not saved yet");                          // "  NOTE ..." (落とさない)
    /// v.AddFail(KD_FooFxSetupEditor.Verify(box, v.Text));     // 外の helper が返した FAIL 件数を足す
    /// v.Finish("Logs/imp-xxx-verify.txt");                    // "VERIFY RESULT: PASS|FAIL (n checks)" を足して書く
    /// </code>
    ///
    /// - 出力行の書式は旧 Check / Finish と**同一** (`VERIFY RESULT: PASS` / `FAIL (n checks)` は
    ///   `docs/development/add-dog-runbook.md` の拾い方と、過去の Logs との比較が前提)。
    ///   結果行の接頭辞を変えたいときだけ <see cref="Finish(string, string)"/> の第 2 引数で渡す
    /// - 外の helper がまだ `StringBuilder log` を受け取る間は <see cref="Text"/> を渡す
    ///   (書式が揃っている限り、同じ本文へ続けて書けばよい)
    /// - Setup / Program Asset 生成のように Check を使わないメニューは、自分で RESULT 行を書いてから
    ///   <see cref="Write(string, StringBuilder)"/> で保存する (旧 `Finish(file, log)` と同じ)
    /// - 保存先は `Logs/` 直下のまま。`add-dog-runbook.md` が `Logs/*.txt` を舐めるので、
    ///   サブフォルダへ移すときは先にそちらを直す
    /// </summary>
    public sealed class KD_VerifyLog
    {
        const string PassMark = "  ok   ";
        const string FailMark = "  FAIL ";
        const string NoteMark = "  NOTE ";
        const string DefaultResultPrefix = "VERIFY RESULT";

        readonly StringBuilder _sb = new StringBuilder();

        /// <summary>見出し行を書いて始める。</summary>
        public KD_VerifyLog(string title)
        {
            _sb.AppendLine(title);
        }

        /// <summary>FAIL の件数 (<see cref="Check"/> の失敗 + <see cref="AddFail"/>)。</summary>
        public int Fail { get; private set; }

        /// <summary><see cref="Check"/> が通った件数。</summary>
        public int Pass { get; private set; }

        /// <summary>本文。`StringBuilder log` を受け取る既存 helper へ渡す用。</summary>
        public StringBuilder Text { get { return _sb; } }

        /// <summary>1 検査。ok なら "  ok   label"、違えば "  FAIL label" を書いて数える。戻り値は ok。</summary>
        public bool Check(bool ok, string label)
        {
            _sb.AppendLine((ok ? PassMark : FailMark) + label);
            if (ok) Pass++; else Fail++;
            return ok;
        }

        /// <summary>素の 1 行 (見出し・値の記録)。数えない。</summary>
        public void Info(string line)
        {
            _sb.AppendLine(line);
        }

        /// <summary>注意書き。"  NOTE " を付けて書く。数えない (落とさない)。</summary>
        public void Note(string line)
        {
            _sb.AppendLine(NoteMark + line);
        }

        /// <summary>外の helper が返した FAIL 件数や、SKIP を落としたい 1 件を足す。負数は無視。</summary>
        public void AddFail(int n = 1)
        {
            if (n > 0) Fail += n;
        }

        /// <summary>結果行 ("&lt;prefix&gt;: PASS" / "&lt;prefix&gt;: FAIL (n checks)") を足して
        /// <paramref name="file"/> へ書き、Console にも出す。戻り値は本文全体。</summary>
        public string Finish(string file, string resultPrefix = DefaultResultPrefix)
        {
            _sb.AppendLine(Fail == 0
                ? resultPrefix + ": PASS"
                : resultPrefix + ": FAIL (" + Fail + " checks)");
            string text = _sb.ToString();
            Write(file, text);
            return text;
        }

        /// <summary>Check を使わないメニュー (Setup / Program Asset 生成) 用。
        /// 本文をそのまま <paramref name="file"/> へ書いて Console にも出す (旧 `Finish(file, log)`)。</summary>
        public static void Write(string file, StringBuilder log)
        {
            Write(file, log.ToString());
        }

        /// <summary>UTF-8 (BOM 無し) で書く。フォルダが無ければ作る。
        /// Console ログ解析には偽陰性があるので、ファイルにも書いて裏を取る。</summary>
        public static void Write(string file, string text)
        {
            string dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(file, text, new UTF8Encoding(false));
            Debug.Log(text);
        }
    }
}
