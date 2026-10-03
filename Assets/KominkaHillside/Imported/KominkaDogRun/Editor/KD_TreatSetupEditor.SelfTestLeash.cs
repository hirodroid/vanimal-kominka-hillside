using System.Text;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-DOG-006 (2026-10-03 ユーザー決定): 接続中の犬をおやつで呼ぶ割当の机上テスト (Play なし)。
    /// <c>KD_TreatBox.BuildAllowWith</c> + <c>ResolveClaimsAllowWith</c> を名前つきの固定ケースで当てる。Menu/6d Verify Treats から呼ぶ。
    /// 期待値は実装の写しではなく決定の文から書く (docs/plans/2026-10-03-treat-call.md §割当)。</summary>
    public static partial class KD_TreatSetupEditor
    {
        static int SelfTestLeashCall(KD_TreatBox m, StringBuilder log)
        {
            int fail = 0, n = 0;
            const int Out = KD_TreatBox.SlotOut, Free = KD_TreatBox.SlotFree;
            const int G = 7, H = 8;   // 出した人 G、ほかの人 H

            // 6 匹 × 枠 3。枠 0 だけ G が出している (出した順は枠 0 が先)
            int[] Solve(int[] walker, int[] eligible, int[] dist0, int[] prevSlot, int[] prevGen, int[] lastGiver, int[] lastSeq,
                int[] slotState = null, int[] slotGiver = null, int[] distAll = null)
            {
                int dogs = 6, slots = 3;
                slotState = slotState ?? new[] { Out, Free, Free };
                slotGiver = slotGiver ?? new[] { G, -1, -1 };
                int[] gen = { 100, 200, 300 };
                int[] dist = distAll;
                if (dist == null)
                {
                    dist = new int[dogs * slots];
                    for (int d = 0; d < dogs; d++) for (int i = 0; i < slots; i++) dist[d * slots + i] = i == 0 ? dist0[d] : 9000;
                }
                var allow = new int[dogs * slots];
                m.BuildAllowWith(walker, slotGiver, slotState, eligible, lastGiver ?? new[] { -1, -1, -1, -1, -1, -1 },
                    lastSeq ?? new int[6], dogs, slots, allow);
                var outSlot = new int[dogs]; var outGen = new int[dogs];
                m.ResolveClaimsAllowWith(prevSlot ?? new[] { -1, -1, -1, -1, -1, -1 }, prevGen ?? new int[6], slotState, gen,
                    eligible, dist, allow, dogs, slots, outSlot, outGen);
                return outSlot;
            }
            int Who(int[] outSlot, int slot) { for (int d = 0; d < outSlot.Length; d++) if (outSlot[d] == slot) return d; return -1; }
            void Eq(int actual, int expected, string what)
            {
                n++;
                if (actual == expected) return;
                fail++;
                log.AppendLine($"  FAIL [leash-call {n}] {what}: expected {expected}, got {actual}");
            }
            int[] all = { 1, 1, 1, 1, 1, 1 };

            // 1. G が 0,1,2 をつなぎ、未接続の 3 のほうが近い → 接続犬の最寄り (1)
            Eq(Who(Solve(new[] { G, G, G, -1, -1, -1 }, all, new[] { 3000, 1200, 2000, 300, 5000, 5000 }, null, null, null, null), 0), 1,
                "接続犬 3 匹とより近い未接続犬 → 接続犬の最寄り");
            // 2. 他人 H の接続犬は (いちばん近くても) 選ばない。G は接続犬 0 匹 → 未接続の最寄り (4)
            Eq(Who(Solve(new[] { H, H, -1, -1, -1, -1 }, all, new[] { 100, 200, 3000, 2500, 900, 4000 }, null, null, null, null), 0), 4,
                "他人の接続犬は選ばない (G は接続 0 匹 → 未接続の最寄り)");
            // 3. 接続 0 匹 → 従来どおり最寄り (2)
            Eq(Who(Solve(new[] { -1, -1, -1, -1, -1, -1 }, all, new[] { 900, 800, 100, 700, 600, 500 }, null, null, null, null), 0), 2,
                "接続 0 匹 → 最寄りの犬 (従来どおり)");
            // 4. 同じ距離 → 犬番号の小さい方
            Eq(Who(Solve(new[] { -1, G, -1, G, -1, -1 }, all, new[] { 100, 1000, 100, 1000, 100, 100 }, null, null, null, null), 0), 1,
                "同じ距離の接続犬 → 犬番号の小さい方");
            // 5. 掴んだ後に解除 (G にはまだ接続犬が居る) → Claim を保つ
            Eq(Who(Solve(new[] { -1, G, G, -1, -1, -1 }, all, new[] { 2000, 100, 100, 100, 100, 100 },
                new[] { 0, -1, -1, -1, -1, -1 }, new[] { 100, 0, 0, 0, 0, 0 }, null, null), 0), 0,
                "Claim の後に解除された犬は Claim を保つ (選び直さない)");
            // 6. 掴んだ後に他人 H がつないだ → Claim が外れ、G の接続犬 (2) へ
            {
                var o = Solve(new[] { H, -1, G, -1, -1, -1 }, all, new[] { 100, 900, 800, 50, 50, 50 },
                    new[] { 0, -1, -1, -1, -1, -1 }, new[] { 100, 0, 0, 0, 0, 0 }, null, null);
                Eq(o[0], -1, "Claim の後に他人がつないだ犬は Claim が外れる");
                Eq(Who(o, 0), 2, "外れた枠は G の接続犬へ");
            }
            // 7. 続けて呼ばない: G が最後に呼んだ 1 は、ほかに来られる接続犬が居れば外す → 2
            Eq(Who(Solve(new[] { G, G, G, -1, -1, -1 }, all, new[] { 3000, 100, 900, 50, 50, 50 }, null, null,
                new[] { G, G, -1, -1, -1, -1 }, new[] { 1, 2, 0, 0, 0, 0 }), 0), 2,
                "G が最後に呼んだ犬 (1) は飛ばして次の接続犬の最寄り (2)");
            // 8. 来られる接続犬が最後に呼んだ犬だけ → その犬がまた来てよい
            Eq(Who(Solve(new[] { G, G, -1, -1, -1, -1 }, new[] { 0, 1, 1, 1, 1, 1 }, new[] { 3000, 100, 50, 50, 50, 50 }, null, null,
                new[] { -1, G, -1, -1, -1, -1 }, new[] { 0, 5, 0, 0, 0, 0 }), 0), 1,
                "来られる接続犬が最後に呼んだ 1 匹だけなら、その犬がまた来る");
            // 8b. 最後に呼んだ犬 (3) はもう外れている → 2 つ前に呼んだ接続犬 (1) も飛ばさず、接続犬の最寄り (1)
            Eq(Who(Solve(new[] { G, G, G, -1, -1, -1 }, all, new[] { 3000, 100, 900, 50, 50, 50 }, null, null,
                new[] { -1, G, -1, G, -1, -1 }, new[] { 0, 1, 0, 2, 0, 0 }), 0), 1,
                "最後に呼んだ犬が外れていれば誰も飛ばさない (2 つ前の犬も最寄りなら来る)");
            // 9. 接続犬は居るが 1 匹も来られない → 未接続へは回さない (枠は待つ)
            Eq(Who(Solve(new[] { G, G, -1, -1, -1, -1 }, new[] { 0, 0, 1, 1, 1, 1 }, new[] { 100, 100, 50, 50, 50, 50 }, null, null, null, null), 0), -1,
                "接続犬が全頭来られない → 未接続の犬へは回さない");
            // 10. 距離が測れない組 (int.MaxValue) は選ばない
            Eq(Who(Solve(new[] { G, -1, -1, -1, -1, -1 }, all, new[] { int.MaxValue, 50, 50, 50, 50, 50 }, null, null, null, null), 0), -1,
                "距離が測れない接続犬は選ばない (未接続へも回さない)");
            // 11. 2 人が同時に出す: 枠 0 = G、枠 1 = H。各自の接続犬だけ
            {
                var dist = new int[18];
                for (int d = 0; d < 6; d++) { dist[d * 3 + 0] = 1000 + d * 10; dist[d * 3 + 1] = 1000 - d * 10; dist[d * 3 + 2] = 9000; }
                var o = Solve(new[] { G, G, H, H, -1, -1 }, all, null, null, null, null, null,
                    new[] { Out, Out, Free }, new[] { G, H, -1 }, dist);
                Eq(Who(o, 0), 0, "2 人同時: G の枠は G の接続犬の最寄り");
                Eq(Who(o, 1), 3, "2 人同時: H の枠は H の接続犬の最寄り");
            }
            // 12. 許可なし (null) は従来の ResolveClaimsWith と同じ答え
            {
                int[] ps = { -1, -1, -1, -1 }, pg = { 0, 0, 0, 0 }, st = { Out, Out, Free }, sg = { 10, 20, 30 }, el = { 1, 1, 1, 1 };
                int[] di = { 100, 900, 900, 900, 100, 900, 900, 900, 100, 800, 800, 800 };
                var a = new int[4]; var ag = new int[4]; var b = new int[4]; var bg = new int[4];
                m.ResolveClaimsWith(ps, pg, st, sg, el, di, 4, 3, a, ag);
                m.ResolveClaimsAllowWith(ps, pg, st, sg, el, di, null, 4, 3, b, bg);
                int diff = 0; for (int d = 0; d < 4; d++) if (a[d] != b[d] || ag[d] != bg[d]) diff++;
                Eq(diff, 0, "許可なしの版と同じ答え");
            }

            log.AppendLine($"  self-test (leash call, IMP-DOG-006) {n - fail}/{n} passed");
            return fail;
        }
    }
}
