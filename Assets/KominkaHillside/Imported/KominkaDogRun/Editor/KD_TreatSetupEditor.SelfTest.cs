using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-TRT-001 おやつ: 純関数の自己テスト (Gate / Give / Dispense / Claims)。Verify から呼ぶ。本体は KD_TreatSetupEditor.cs。</summary>
    public static partial class KD_TreatSetupEditor
    {
        // ================================================================ 自己テスト
        //
        // **Play を 1 秒も使わない。** 二重消費も二重 Claim も「実機で 2 人そろえて
        // 同時に押す」でしか出ない類の事故だが、判断を純関数へ出しておけば机上で全部落ちる
        // (台帳の算術 37 件 / 選択の 5 ケース / ルート沈みの 18 件と同じ手)。

        /// <summary>出せるか / 出せない理由の自己テスト。**全 32 組合せ = 本当に全数。**</summary>
        static int SelfTestGate(KD_TreatBox m, StringBuilder log)
        {
            int fail = 0, n = 0;
            void Eq(int actual, int expected, string what)
            {
                n++;
                if (actual == expected) return;
                fail++;
                log.AppendLine($"  FAIL [gate {n}] {what}: expected {expected}, got {actual}");
            }

            // loaded 2 × 在庫あり 2 × 自分が出している 2 × 空き枠 0..3 = **32 通りを全数**
            for (int li = 0; li < 2; li++)
                for (int si = 0; si < 2; si++)
                    for (int ai = 0; ai < 2; ai++)
                        for (int free = 0; free < 4; free++)
                        {
                            bool loaded = li == 1;
                            int stock = si == 1 ? 3 : 0;
                            bool already = ai == 1;

                            // **優先順位はここが正本。** 順序が変わると文言が変わるので、
                            // 「たまたま今の実装がこうだった」ではなく期待値として固定する
                            int want;
                            if (!loaded) want = KD_TreatBox.GateLoading;
                            else if (already) want = KD_TreatBox.GateAlreadyOut;
                            else if (stock <= 0) want = KD_TreatBox.GateNoStock;
                            else if (free <= 0) want = KD_TreatBox.GateSlotsFull;
                            else want = KD_TreatBox.GateOk;

                            Eq(m.DispenseGateWith(loaded, stock, already, free), want,
                                $"loaded={loaded} stock={stock} already={already} free={free}");
                        }

            // 代表例を名前つきでも押さえる (回帰したときにログが読めるように)
            Eq(m.DispenseGateWith(false, 5, false, 3), KD_TreatBox.GateLoading,
                "復元前は「よみこみ中」— **在庫 0 と区別する**");
            Eq(m.DispenseGateWith(true, 0, false, 3), KD_TreatBox.GateNoStock, "在庫 0 は出せない");
            Eq(m.DispenseGateWith(true, 5, true, 3), KD_TreatBox.GateAlreadyOut, "1 人 1 個");
            Eq(m.DispenseGateWith(true, 5, false, 0), KD_TreatBox.GateSlotsFull,
                "**4 人目は無言で失敗しない**");
            Eq(m.DispenseGateWith(true, 5, true, 0), KD_TreatBox.GateAlreadyOut,
                "自分の理由を他人の都合より先に出す (優先順位の固定)");

            log.AppendLine($"  self-test (gate) {n - fail}/{n} passed");
            return fail;
        }

        /// <summary>あげる判定の自己テスト。**全 16 組合せ = 本当に全数**
        /// (attaching × armed × panelOpen × isDown)。
        ///
        /// 狙いは 1 つ: **`[出す]` を押したトリガーで手のおやつが落ちないこと。**
        /// VR の UI 押下はトリガーそのものなので、押し下がりは吸着より前・
        /// 離しは吸着より後に届く。**`armed` を要求することで順序で塞ぐ** —
        /// 猶予時間ではないので、押し方の速さに依らず成立する。</summary>
        static int SelfTestGive(KD_TreatBox m, StringBuilder log)
        {
            int fail = 0, n = 0;
            void Eq(int actual, int expected, string what)
            {
                n++;
                if (actual == expected) return;
                fail++;
                log.AppendLine($"  FAIL [give {n}] {what}: expected {expected}, got {actual}");
            }

            for (int at = 0; at < 2; at++)
                for (int ar = 0; ar < 2; ar++)
                    for (int po = 0; po < 2; po++)
                        for (int dn = 0; dn < 2; dn++)
                        {
                            bool attaching = at == 1;
                            bool armed = ar == 1;
                            bool panelOpen = po == 1;
                            bool isDown = dn == 1;

                            // **優先順位はここが正本。** 実装の写しではなく期待値として固定する
                            int want;
                            if (!attaching) want = KD_TreatBox.GiveDisarm;
                            else if (panelOpen) want = KD_TreatBox.GiveIgnore;
                            else if (isDown) want = KD_TreatBox.GiveArm;
                            else if (!armed) want = KD_TreatBox.GiveIgnore;
                            else want = KD_TreatBox.GiveRelease;

                            Eq(m.GiveStepWith(attaching, armed, panelOpen, isDown), want,
                                $"attaching={attaching} armed={armed} panelOpen={panelOpen} down={isDown}");
                        }

            // ★ 事故そのものを名前つきで押さえる (回帰したときにログで読めるように)
            Eq(m.GiveStepWith(true, false, false, false), KD_TreatBox.GiveIgnore,
                "[出す] を押したトリガーの離し (吸着後だが armed が立っていない) では放さない");
            Eq(m.GiveStepWith(true, true, false, false), KD_TreatBox.GiveRelease,
                "吸着してから引いて離す = あげる");
            Eq(m.GiveStepWith(true, true, true, false), KD_TreatBox.GiveIgnore,
                "板が開いている間は放さない (板のボタン操作と混ざらない)");
            Eq(m.GiveStepWith(false, true, false, false), KD_TreatBox.GiveDisarm,
                "吸着していないおやつは Use に反応しない (3 枠へ同じイベントが飛ぶため)");

            log.AppendLine($"  self-test (give) {n - fail}/{n} passed");
            return fail;
        }

        /// <summary>出す手順の自己テスト。**狙いは「在庫が 2 回減らないこと」の証明。**</summary>
        static int SelfTestDispense(KD_TreatBox m, StringBuilder log)
        {
            int fail = 0, n = 0;
            void Step(int state, int ev, int evSeq, int curSeq,
                      int wantNext, bool wantConsume, string what)
            {
                n++;
                int packed = m.StepDispenseWith(state, ev, evSeq, curSeq);
                int next = m.DispenseNextOf(packed);
                bool consume = m.DispenseConsumesOf(packed);
                if (next == wantNext && consume == wantConsume) return;
                fail++;
                log.AppendLine($"  FAIL [disp {n}] {what}: expected ({wantNext},{wantConsume}), "
                    + $"got ({next},{consume})");
            }

            const int Idle = KD_TreatBox.DispIdle;
            const int Reserving = KD_TreatBox.DispReserving;
            const int Reserved = KD_TreatBox.DispReserved;
            const int Committing = KD_TreatBox.DispCommitting;
            const int Out = KD_TreatBox.DispOut;
            const int Recalling = KD_TreatBox.DispRecalling;

            // ---- 正常系: 押す → 枠が取れた → 実体が出た → 減った → 出ている ----
            Step(Idle, KD_TreatBox.EvPress, 0, 1, Reserving, false, "押すと予約に入る");
            Step(Reserving, KD_TreatBox.EvOwnerGranted, 1, 1, Reserved, false, "枠が取れた");
            Step(Reserved, KD_TreatBox.EvPlaced, 1, 1, Committing, true,
                "★ **在庫が減る唯一の辺** (実体が出た後)");
            Step(Committing, KD_TreatBox.EvConsumeOk, 1, 1, Out, false, "減らせた");
            Step(Out, KD_TreatBox.EvRecalled, 1, 1, Idle, false, "回収されたら次が出せる");

            // ---- 二重消費の経路をすべて塞ぐ ----
            Step(Committing, KD_TreatBox.EvPlaced, 1, 1, Committing, false,
                "**同じ Placed が 2 回来ても 2 度目は減らない** (状態が動かない)");
            Step(Out, KD_TreatBox.EvPlaced, 1, 1, Out, false, "出た後の Placed も減らさない");
            Step(Reserved, KD_TreatBox.EvPlaced, 0, 1, Reserved, false,
                "**古い世代の Placed は捨てる** (遅延イベントの重複)");
            Step(Reserving, KD_TreatBox.EvOwnerGranted, 0, 1, Reserving, false,
                "**古い世代の移譲通知も捨てる** (OnOwnershipTransferred の再通知)");
            Step(Reserving, KD_TreatBox.EvPress, 0, 1, Reserving, false, "手続き中の連打は無視");
            Step(Reserved, KD_TreatBox.EvPress, 0, 1, Reserved, false, "同上 (予約済み)");
            Step(Out, KD_TreatBox.EvPress, 0, 1, Out, false, "出ている間の連打も無視");

            // ---- 「減ったのに出ていない」= プレイヤーの損 を塞ぐ ----
            Step(Reserving, KD_TreatBox.EvOwnerFailed, 1, 1, Idle, false,
                "**枠の取り合いに負けても在庫は減らない**");
            Step(Reserving, KD_TreatBox.EvTimeout, 1, 1, Idle, false, "移譲が来なくても減らない");
            Step(Reserved, KD_TreatBox.EvTimeout, 1, 1, Recalling, false,
                "実体を出せないまま時間切れ → 回収 (減らさない)");
            Step(Committing, KD_TreatBox.EvConsumeFailed, 1, 1, Recalling, false,
                "**減らせなかったら実体を回収する** (無料で出たままにしない)");
            Step(Recalling, KD_TreatBox.EvRecalled, 1, 1, Idle, false, "回収し終えたら Idle");

            // ---- 全 (状態 × イベント × 世代) を走査して、減る辺が 1 本だけであることを示す ----
            //
            // **「辺が 1 本」は設計の主張で、実装がそうなっている保証は無い。**
            // 数え上げて初めて主張になる (2026-08-24 Codex 指摘)
            int consumeEdges = 0, movedWithoutSeq = 0;
            for (int st = 0; st <= Recalling; st++)
                for (int ev = 0; ev <= KD_TreatBox.EvTimeout; ev++)
                    for (int seq = 0; seq < 2; seq++)
                    {
                        int packed = m.StepDispenseWith(st, ev, seq, 1);
                        if (m.DispenseConsumesOf(packed))
                        {
                            consumeEdges++;
                            // 減る辺は**必ず状態を動かす** — 動かない辺で副作用を起こすと、
                            // 呼び出し側の「状態が動いたときだけ実行する」ガードが効かない
                            if (m.DispenseNextOf(packed) == st) movedWithoutSeq++;
                        }
                        // 押す以外で世代が合わないものは、状態を動かしてはいけない
                        if (ev != KD_TreatBox.EvPress && seq != 1
                            && m.DispenseNextOf(packed) != st)
                        {
                            n++; fail++;
                            log.AppendLine($"  FAIL [disp {n}] 世代違いなのに状態が動いた "
                                + $"(state={st} ev={ev})");
                        }
                    }
            n++;
            if (consumeEdges != 1)
            {
                fail++;
                log.AppendLine($"  FAIL [disp {n}] **在庫が減る辺がちょうど 1 本ではない** "
                    + $"(actual {consumeEdges})");
            }
            n++;
            if (movedWithoutSeq != 0)
            {
                fail++;
                log.AppendLine($"  FAIL [disp {n}] 在庫が減るのに状態が動かない辺がある "
                    + $"(actual {movedWithoutSeq})");
            }

            log.AppendLine($"  self-test (dispense) {n - fail}/{n} passed "
                + $"[在庫が減る辺 {consumeEdges} 本]");
            return fail;
        }

        /// <summary>
        /// Claim の自己テスト。**健全性 7 + 完全性 4 の不変条件を、3 段構えで当てる。**
        ///
        /// <code>
        /// 縮小空間 (犬 2 × 枠 2) の**全列挙**       ← ここだけが「全数」
        /// 実寸 (犬 4 × 枠 3) の固定ケース            ← 手で選んだ代表
        /// 実寸の**決定的生成テスト** (LCG 200 件)    ← 乱数ではなく再現可能な生成
        /// </code>
        ///
        /// **「200 件のランダム」を全数検査と呼ばない** (2026-08-24 Codex 指摘)。
        /// 全数と言えるのは縮小空間の列挙だけで、残りはサンプル。
        /// </summary>
        static int SelfTestClaims(KD_TreatBox m, StringBuilder log)
        {
            int fail = 0, cases = 0;
            var firstFailures = new StringBuilder();
            int reported = 0;

            void Violate(string what, string detail)
            {
                fail++;
                if (reported++ < 8) firstFailures.AppendLine($"  FAIL [claim] {what}: {detail}");
            }

            // 1 ケースぶんを解いて、不変条件をすべて当てる
            void Run(int[] prevSlot, int[] prevOutMs, int[] slotState, int[] slotOutMs,
                     int[] eligible, int[] distMm, string tag)
            {
                cases++;
                int dogCount = prevSlot.Length;
                int slotCount = slotState.Length;
                var outSlot = new int[dogCount];
                var outOutMs = new int[dogCount];
                m.ResolveClaimsWith(prevSlot, prevOutMs, slotState, slotOutMs,
                    eligible, distMm, dogCount, slotCount, outSlot, outOutMs);

                var owner = new int[slotCount];
                for (int i = 0; i < slotCount; i++) owner[i] = -1;

                for (int d = 0; d < dogCount; d++)
                {
                    int s = outSlot[d];
                    // 6: 出力が範囲内
                    if (s < -1 || s >= slotCount) { Violate("6 範囲外", $"{tag} dog{d}→{s}"); continue; }
                    if (s < 0) continue;
                    // 1 / 7: 単射 (壊れた入力から来ても)
                    if (owner[s] >= 0) Violate("1 単射", $"{tag} slot{s} を dog{owner[s]} と dog{d} が持つ");
                    owner[s] = d;
                    // 4: Out の枠だけ
                    if (slotState[s] != KD_TreatBox.SlotOut)
                        Violate("4 Out 以外を掴んだ", $"{tag} dog{d}→slot{s} state={slotState[s]}");
                    // 5: 不適格な犬は持たない
                    if (eligible[d] == 0) Violate("5 不適格な犬が掴んだ", $"{tag} dog{d}");
                    // 3: 世代を持ち帰っているか (これが無いと枠の再利用で継承が起きる)
                    if (outOutMs[d] != slotOutMs[s])
                        Violate("3 世代が一致しない", $"{tag} dog{d} {outOutMs[d]}!={slotOutMs[s]}");
                }

                for (int d = 0; d < dogCount; d++)
                {
                    int prev = prevSlot[d];
                    bool prevValid = eligible[d] != 0 && prev >= 0 && prev < slotCount
                        && slotState[prev] == KD_TreatBox.SlotOut && prevOutMs[d] == slotOutMs[prev];
                    // 2 / 10: 有効な Claim は保たれる (先着順。奪われない)
                    // **ただし壊れた入力で重複していた場合は、犬番号の小さい方だけが保つ**
                    if (prevValid && outSlot[d] != prev)
                    {
                        bool stolenByLowerIndex = false;
                        for (int e = 0; e < d; e++)
                            if (prevSlot[e] == prev && eligible[e] != 0
                                && prevOutMs[e] == slotOutMs[prev]) stolenByLowerIndex = true;
                        if (!stolenByLowerIndex)
                            Violate("2 先着順が壊れた", $"{tag} dog{d} {prev}→{outSlot[d]}");
                    }
                }

                // 8: 空きの Out と手の空いた適格な犬が両方あるなら、放置しない
                int freeOut = 0, freeDog = 0;
                for (int i = 0; i < slotCount; i++)
                    if (slotState[i] == KD_TreatBox.SlotOut && owner[i] < 0) freeOut++;
                for (int d = 0; d < dogCount; d++)
                    if (eligible[d] != 0 && outSlot[d] < 0) freeDog++;
                if (freeOut > 0 && freeDog > 0)
                    Violate("8 放置した", $"{tag} 空き Out {freeOut} / 手の空いた犬 {freeDog}");

                // ---- 9: **選択そのものを当てる** (2026-08-24 Codex 指摘で追加) ----
                //
                // ここまでの検査は**構造的な性質** (単射・世代一致・既存 Claim 維持・
                // 放置しない) しか見ていなかった。**「どの枠へどの犬を配ったか」を
                // 当てていない**ので、空き枠を適当な順・適当な犬へ配っても全ケースが
                // 通ってしまう = **検査が vacuous** だった。
                //
                // 参照実装は**本体からコピーしない** — コピーすると同じ間違いを 2 回
                // 書くだけで、検査が意味を失う。**仕様の言葉から独立に書く**:
                //
                //   「先に出された順に枠を見て、その枠へ最も近い手の空いた犬を配る。
                //    既に有効な Claim を持っている犬と枠はそのまま残す。」
                var refSlot = ReferenceClaims(prevSlot, prevOutMs, slotState, slotOutMs,
                    eligible, distMm, dogCount, slotCount);
                for (int d = 0; d < dogCount; d++)
                    if (outSlot[d] != refSlot[d])
                        Violate("9 選択が参照実装と違う",
                            $"{tag} dog{d} 実装→{outSlot[d]} / 参照→{refSlot[d]}");

                // 11: 決定的 — もう一度解いて同じ答えになるか
                var again = new int[dogCount];
                var againMs = new int[dogCount];
                m.ResolveClaimsWith(prevSlot, prevOutMs, slotState, slotOutMs,
                    eligible, distMm, dogCount, slotCount, again, againMs);
                for (int d = 0; d < dogCount; d++)
                    if (again[d] != outSlot[d])
                        Violate("11 非決定的", $"{tag} dog{d} {outSlot[d]}!={again[d]}");
            }

            // ---- (a) 縮小空間の全列挙: 犬 2 × 枠 2。**ここだけが「全数」** ----
            //
            // 枠の状態 4 × 世代 2 × 適格 2 × prev 3 × 距離の順 2 を、2 匹 2 枠ぶん全部
            int[] genChoices = { 100, 200 };
            for (int s0 = 0; s0 < 4; s0++)
                for (int s1 = 0; s1 < 4; s1++)
                    for (int g0 = 0; g0 < 2; g0++)
                        for (int g1 = 0; g1 < 2; g1++)
                            for (int e0 = 0; e0 < 2; e0++)
                                for (int e1 = 0; e1 < 2; e1++)
                                    for (int p0 = -1; p0 < 2; p0++)
                                        for (int p1 = -1; p1 < 2; p1++)
                                            for (int dist = 0; dist < 2; dist++)
                                            {
                                                var slotState = new[] { s0, s1 };
                                                var slotOutMs = new[] { genChoices[g0], genChoices[g1] };
                                                var eligible = new[] { e0, e1 };
                                                var prevSlot = new[] { p0, p1 };
                                                // **世代をわざと半分ずらす** — 枠の再利用を
                                                // 見抜けているかは、ここでしか当たらない
                                                var prevOutMs = new[]
                                                {
                                                    p0 < 0 ? 0 : (dist == 0 ? slotOutMs[p0] : 999),
                                                    p1 < 0 ? 0 : slotOutMs[p1],
                                                };
                                                var distMm = dist == 0
                                                    ? new[] { 1000, 2000, 3000, 500 }
                                                    : new[] { 2000, 1000, 500, 3000 };
                                                Run(prevSlot, prevOutMs, slotState, slotOutMs,
                                                    eligible, distMm, "enum");
                                            }

            // ---- (a2) サーバー時刻のラップ ----
            //
            // `GetServerTimeInMilliseconds()` は int なので**約 24.8 日で一周する。**
            // 生の大小で比べると**ラップ前後で新旧が逆転**し、先着順が壊れる
            // (2026-08-24 Codex 指摘)。**ここは机上で検査できる。**
            {
                int n0 = cases;
                // ラップ直前に出した枠 0 と、直後に出した枠 1。**枠 0 のほうが先**
                int before = int.MaxValue - 1000;   // 一周する 1 秒前
                int after = int.MinValue + 1000;    // 一周した 1 秒後
                // 犬 0 は枠 1 のほうが近い。**それでも先に出された枠 0 が優先される**のが正
                Run(new[] { -1, -1, -1, -1 }, new[] { 0, 0, 0, 0 },
                    new[] { KD_TreatBox.SlotOut, KD_TreatBox.SlotOut, KD_TreatBox.SlotFree },
                    new[] { before, after, 0 }, new[] { 1, 0, 0, 0 },
                    new[] { 900, 100, 900, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, "wrap");
                // 上の Run が単射などを見るのに加えて、**どちらを選んだか**を直接当てる
                // (ここの「犬 4 匹 × 枠 3」は**純関数の検査用の縮小空間**で、実際の頭数とは無関係。
                //  IMP-DOG-020 で 6 匹になっても変えない — 検査する性質は頭数に依らない)
                var outSlot = new int[4];
                var outGen = new int[4];
                m.ResolveClaimsWith(new[] { -1, -1, -1, -1 }, new[] { 0, 0, 0, 0 },
                    new[] { KD_TreatBox.SlotOut, KD_TreatBox.SlotOut, KD_TreatBox.SlotFree },
                    new[] { before, after, 0 }, new[] { 1, 0, 0, 0 },
                    new[] { 900, 100, 900, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    4, 3, outSlot, outGen);
                cases++;
                if (outSlot[0] != 0)
                    Violate("2 先着順がラップで壊れた",
                        $"ラップ直前の枠 0 を選ぶべきだが dog0→{outSlot[0]} " +
                        "(生の大小で比べていないか)");
                // 比較そのものの単体確認
                cases++;
                if (!m.EarlierMs(before, after))
                    Violate("EarlierMs がラップを跨げない", $"{before} は {after} より先のはず");
                cases++;
                if (m.EarlierMs(after, before))
                    Violate("EarlierMs が逆を返す", $"{after} は {before} より後のはず");
                if (reported == 0 && cases > n0) { /* 通った */ }
            }

            // ---- (b) 実寸 (犬 4 × 枠 3) の固定ケース ----
            var allOut = new[] { KD_TreatBox.SlotOut, KD_TreatBox.SlotOut, KD_TreatBox.SlotOut };
            var noPrev = new[] { -1, -1, -1, -1 };
            var noPrevMs = new[] { 0, 0, 0, 0 };
            var allEligible = new[] { 1, 1, 1, 1 };
            // 犬 d から枠 i まで: d が近い枠ほど小さく
            var dist4x3 = new[]
            {
                100, 900, 900,   // dog0 は slot0 が近い
                900, 100, 900,   // dog1 は slot1
                900, 900, 100,   // dog2 は slot2
                800, 800, 800,   // dog3 はどこも遠い
            };
            Run(noPrev, noPrevMs, allOut, new[] { 10, 20, 30 }, allEligible, dist4x3, "4x3 全部空き");
            // 枠 3 < 犬 4 なので 1 匹余る (設計 1 の根拠。余ることが仕様)
            Run(noPrev, noPrevMs,
                new[] { KD_TreatBox.SlotOut, KD_TreatBox.SlotReserved, KD_TreatBox.SlotFree },
                new[] { 10, 20, 30 }, allEligible, dist4x3, "4x3 Reserved は対象外");
            Run(new[] { 2, -1, -1, -1 }, new[] { 30, 0, 0, 0 }, allOut,
                new[] { 10, 20, 30 }, allEligible, dist4x3,
                "4x3 dog0 は遠い slot2 を掴んだまま (先着順で切り替えない)");
            Run(new[] { 0, 0, -1, -1 }, new[] { 10, 10, 0, 0 }, allOut,
                new[] { 10, 20, 30 }, allEligible, dist4x3, "4x3 壊れた入力 (2 匹が同じ枠)");
            Run(noPrev, noPrevMs, allOut, new[] { 10, 20, 30 },
                new[] { 0, 0, 0, 0 }, dist4x3, "4x3 全員が仕事中");

            // ---- (c) 実寸の決定的生成テスト (LCG。**乱数ではない** — 毎回同じ 200 件) ----
            uint seed = 20260824u;
            for (int t = 0; t < 200; t++)
            {
                var slotState = new int[3];
                var slotOutMs = new int[3];
                var prevSlot = new int[4];
                var prevOutMs = new int[4];
                var eligible = new int[4];
                var distMm = new int[12];
                for (int i = 0; i < 3; i++)
                {
                    slotState[i] = (int)(Next(ref seed) % 4);
                    slotOutMs[i] = (int)(Next(ref seed) % 5) * 10;
                }
                for (int d = 0; d < 4; d++)
                {
                    eligible[d] = (int)(Next(ref seed) % 2);
                    int p = (int)(Next(ref seed) % 5) - 1;       // -1..3 (範囲外の 3 も混ぜる)
                    prevSlot[d] = p;
                    prevOutMs[d] = (int)(Next(ref seed) % 5) * 10;
                    for (int i = 0; i < 3; i++) distMm[d * 3 + i] = (int)(Next(ref seed) % 4000);
                }
                Run(prevSlot, prevOutMs, slotState, slotOutMs, eligible, distMm, "gen" + t);
            }

            if (fail > 0) log.Append(firstFailures);
            log.AppendLine($"  self-test (claims) {cases} cases / 違反 {fail} 件 "
                + "[縮小空間の全列挙 + 実寸の固定 5 + 決定的生成 200]");
            return fail;
        }

        /// <summary>
        /// **Claim の参照実装。** 本体 (`KD_TreatBox.ResolveClaimsWith`) の写しではなく、
        /// **仕様の言葉から独立に書いたもの。**
        ///
        /// > 「先に出された順に枠を見て、その枠へ最も近い手の空いた犬を配る。
        /// >  既に有効な Claim を持っている犬と枠はそのまま残す。」
        ///
        /// **写したら意味が無い** — 同じ間違いを 2 回書くだけで、
        /// 「実装と参照が一致する」が「両方同じように壊れている」と区別できなくなる。
        /// だから本体を見ずに、上の 2 文だけから書いてある。
        ///
        /// Editor 側なので `List` も LINQ も使えるが、**あえて素朴に書く** —
        /// 参照実装が凝っていると、参照実装のバグを疑う羽目になる。
        /// </summary>
        static int[] ReferenceClaims(int[] prevSlot, int[] prevOutMs,
            int[] slotState, int[] slotOutMs, int[] eligible, int[] distMm,
            int dogCount, int slotCount)
        {
            var res = new int[dogCount];
            var used = new bool[slotCount];
            for (int d = 0; d < dogCount; d++) res[d] = -1;

            // 「既に有効な Claim を持っている犬と枠はそのまま残す」
            for (int d = 0; d < dogCount; d++)
            {
                if (eligible[d] == 0) continue;
                int s = prevSlot[d];
                if (s < 0 || s >= slotCount) continue;
                if (slotState[s] != KD_TreatBox.SlotOut) continue;
                if (prevOutMs[d] != slotOutMs[s]) continue;
                if (used[s]) continue;          // 壊れた入力で重複していたら先の犬が勝つ
                used[s] = true;
                res[d] = s;
            }

            // 「先に出された順に枠を見て、その枠へ最も近い手の空いた犬を配る」
            for (int round = 0; round < slotCount; round++)
            {
                int slot = -1;
                for (int i = 0; i < slotCount; i++)
                {
                    if (used[i] || slotState[i] != KD_TreatBox.SlotOut) continue;
                    // **時刻は循環する** — 差の符号で「先か」を見る (本体と同じ理屈だが、
                    // ここは「先に出された順」という仕様語からの直訳)
                    if (slot < 0 || (slotOutMs[i] - slotOutMs[slot]) < 0) slot = i;
                }
                if (slot < 0) break;

                int dog = -1;
                for (int d = 0; d < dogCount; d++)
                {
                    if (eligible[d] == 0 || res[d] >= 0) continue;
                    int di = distMm[d * slotCount + slot];
                    if (dog < 0 || di < distMm[dog * slotCount + slot]) dog = d;
                }
                used[slot] = true;              // 配れなくてもこの枠はもう見ない
                if (dog >= 0) res[dog] = slot;
            }
            return res;
        }

        /// <summary>決定的な生成テスト用の LCG。**乱数ではない** —
        /// 種を固定してあるので毎回まったく同じ 200 件が出る (回帰が再現できる)。</summary>
        static uint Next(ref uint state)
        {
            state = state * 1664525u + 1013904223u;
            return state >> 8;
        }
    }
}
