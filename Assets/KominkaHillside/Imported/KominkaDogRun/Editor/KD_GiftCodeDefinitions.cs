using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KominkaDogRun.Editor
{
    /// <summary>ギフトコードの定義表 (IMP-PRG-010)。**ハッシュだけを持ち、平文はリポジトリに置かない。**
    ///
    /// 額と段構成の正本は business-strategy §5.7、機構は docs/development/design/gift-code.md。
    /// 平文 → ハッシュは `Tools/KominkaHillside/Menu/Imported-Unused/Gift Code/0 - Hash Tool` で算出して貼る
    /// (式は <see cref="Hash"/> = Udon 側 <c>KD_ProgressLedger.Fnv1a31(KD_GiftCodePanel.HashSalt + NormalizeCode(平文))</c> と同じ)。
    /// codeKey は productKey と同じ文法。PlayerData の印は <c>KD_Commerce_v1.Bonus.gift.{codeKey}</c>。</summary>
    public static class KD_GiftCodeDefinitions
    {
        public sealed class Row
        {
            public readonly string codeKey, nameKey, nameJa, nameEn;
            public readonly int points, hash;
            public Row(string codeKey, string nameJa, string nameEn, int points, int hash)
            { this.codeKey = codeKey; nameKey = "gift.name." + codeKey; this.nameJa = nameJa; this.nameEn = nameEn; this.points = points; this.hash = hash; }
        }

        public const string Salt = "KD_Gift_v1:";
        public const int MinPoints = 1, MaxPoints = 1000;
        static readonly Regex KeyRule = new Regex(@"^[a-z][a-z0-9_]*$");

        /// <summary>公開時点の 7 本 (2026-09-15 ユーザー決定)。ハッシュは 2026-09-15 に発行した平文のもの。
        /// 平文を作り直したら Hash Tool の値でここを差し替え、Setup を再実行する。</summary>
        public static readonly Row[] All =
        {
            new Row("launch",      "公開記念",              "Launch celebration", 100, 1035458386),
            new Row("fav_1000",    "お気に入り 1,000 記念", "1,000 favorites",    100, 168334114),
            new Row("visits_10k",  "Visits 10,000 記念",    "10,000 visits",      100, 89270511),
            new Row("visits_20k",  "Visits 20,000 記念",    "20,000 visits",      150, 1145814640),
            new Row("visits_30k",  "Visits 30,000 記念",    "30,000 visits",      200, 1348994261),
            new Row("visits_50k",  "Visits 50,000 記念",    "50,000 visits",      300, 1278086590),
            new Row("visits_100k", "Visits 100,000 記念",   "100,000 visits",     500, 226814889),
        };

        /// <summary>入力の正規化。Udon 側 <c>KD_ProgressLedger.NormalizeCode</c> と同じ (ASCII 英数字のみ・大文字)。</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.ToUpperInvariant()) if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z')) sb.Append(c);
            return sb.ToString();
        }
        /// <summary>FNV-1a 32bit を 31bit に畳む。Udon 側 <c>KD_ProgressLedger.Fnv1a31</c> と同じ式。</summary>
        public static int Fnv1a31(string text)
        {
            long h = 2166136261L;
            if (text != null) foreach (char c in text) { h = ((h ^ (long)c) * 16777619L) & 0xFFFFFFFFL; }
            return (int)(h & 0x7FFFFFFFL);
        }
        /// <summary>平文 → 定義表に貼るハッシュ。</summary>
        public static int Hash(string plaintext) { return Fnv1a31(Salt + Normalize(plaintext)); }

        /// <summary>表の自己検査。重複・文法・額の範囲・ハッシュ 0 (未発行) を拒む。</summary>
        public static void Validate()
        {
            var keys = new HashSet<string>(); var hashes = new HashSet<int>();
            foreach (var r in All)
            {
                if (r == null || !KeyRule.IsMatch(r.codeKey) || !keys.Add(r.codeKey)) throw new InvalidOperationException("Invalid/duplicate gift codeKey: " + (r == null ? "null" : r.codeKey));
                if (r.codeKey.StartsWith("test")) throw new InvalidOperationException("test codeKey in production table: " + r.codeKey);
                if (r.points < MinPoints || r.points > MaxPoints) throw new InvalidOperationException("Gift points out of range: " + r.codeKey);
                if (r.hash <= 0 || !hashes.Add(r.hash)) throw new InvalidOperationException("Gift hash missing/duplicate: " + r.codeKey);
                if (string.IsNullOrWhiteSpace(r.nameJa) || string.IsNullOrWhiteSpace(r.nameEn)) throw new InvalidOperationException("Gift name missing: " + r.codeKey);
            }
        }
    }
}
