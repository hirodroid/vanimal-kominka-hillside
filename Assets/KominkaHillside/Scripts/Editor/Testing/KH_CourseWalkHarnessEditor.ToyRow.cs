using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>おもちゃタブの上段の ClientSim ハーネス — Course/126 (全部) / 126a (投げ方と取り方の権限、DES-UI-002) / 126b (ボールの柄、DES-DOG-002)。
    /// 枠は周回コースの歩行ハーネスと同じ (Begin / Course/41 Stop / 締切 / PlayerData の退避と復旧 / シーンは保存しない /
    /// 結果は Logs/kh-course-walk/&lt;runId&gt;/report.json、ケースの記録は toy-row.json)。PlayerData は空から (初めて来た人、0 pt)。
    ///
    /// 126a 投げ方 (ローカル) と取り方の権限:
    ///   TT0-setup           台帳の復元・6 匹を外す・滞在の加算を止める (模擬)・門の道へ。6 匹の取り方 0 / 予約なし、投げ方はまっすぐ
    ///   TT1-throw-curve     おもちゃタブの [曲げる] (_ThrowCurve) → KH_ToyRecall._throwCurve、ボタンの点灯と下線。犬 0 の円盤を投げる (_CheckThrow) →
    ///                       円盤の _curve と犬が受けた _curve が CurveDefault。6 匹の取り方 (variant) は 0 のまま (他人の遊びを変えない)
    ///   TT2-throw-straight  [まっすぐ] → 投げ直し → 円盤・犬とも _curve 0
    ///   TT3-style-refused   (トレーナーでない) あそぶの「フリスビーの取り方」(_Row2) は何も頼まず「トレーナーになると使えます」。
    ///                       犬の Owner へ ReqVariant(3) を SendCustomNetworkEvent で送る (UI を通らない送信) → 6 匹とも 0 のまま・受け手の拒否ログ
    ///   TT4-style-trainer   (模擬: KH_TrainerPass のヒープに自分を所有者として書く) ReqVariant(5) は世界では拒否。_Row2 で 0 → 3 → 4 → 0 (5 を通らない)。
    ///                       犬 0 だけ 4 にして行の副文が「全部の犬: 切替中」、戻してそろうと「全部の犬: …」。最後に 0 へ戻しヒープを空へ戻す
    /// 126b ボールの柄 (全員共通 1 つ):
    ///   BP0-setup           (126b 単独なら TT0 と同じ準備) 同期値は未設定・6 個のボールとも基本ボールだけ表示
    ///   BP1-buy-short       0 pt で ▶ (テニス) → 中央 → 確認カードに [購入] が出ない。強制の [購入] は払わず・選ばず・「買えません」
    ///   BP2-keeper-grant    おじいさん → 200 pt
    ///   BP3-buy             [やめる] は払わない。[購入] で 10 pt 減・所有・同期値テニス・6 個ともテニスだけ表示・「6 匹のボールを…」
    ///   BP4-use-owned       ◀ (基本) → 中央 → カード無しで基本へ。▶ → テニスへ戻る (支払いなし)
    ///   BP5-forged          (模擬: 受け手の検査) 同期値を直接書いて OnDeserialization: 持っていない有料 (手毬) / 表に無いキー / CE のぬいぐるみ / 壊れた文字列 → 基本ボール・例外なし。持っているテニス → テニス
    ///   BP6-saved           所有 (Own.toy.tennis_ball) と Spent が PlayerData に入っている
    ///   BP7-owner-to-non-owner (模擬) KD_ToyBox の Owner を柄を持たない人形へ → この端末も確かめ直して基本ボール。Owner を戻すとテニス
    ///   BP8-late-owner-data    (模擬) 有料の柄が所有データより先に届き、見直しの回数を使い切った後で所有データが届く → OnPlayerDataUpdated で出る
    ///   (BP1〜BP8 はそれぞれ前提を自分で整える。前のケースが落ちても巻き込まない)
    /// ハーネス対象外 (2 人 / PCVR / 実機): 他人の画面の見え方・途中参加 (同期値の初回受信と、Owner の PlayerData が後から届く見直し)・Owner の退室と
    /// 新 Owner の書き直し、本物の投げ (手・照準・強さ)、曲がって飛ぶ見た目と犬の取り方の自然さ、ボタンの押し心地、端末のフォント。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/126 - Harness Toy Tab (throw style, catch-style permission, ball patterns)")]
        public static void ToyRowHarness() { BeginToyRow("throw,pattern"); }
        [MenuItem("Tools/KominkaHillside/Course/126a - Harness Toy Tab throw style + catch-style permission (DES-UI-002)")]
        public static void ToyRowHarnessThrow() { BeginToyRow("throw"); }
        [MenuItem("Tools/KominkaHillside/Course/126b - Harness Toy Tab ball patterns (DES-DOG-002)")]
        public static void ToyRowHarnessPattern() { BeginToyRow("pattern"); }

        [Serializable] class ToyRowCase { public string name, status = "PENDING"; public bool simulated; public List<string> notes = new List<string>(); }
        [Serializable] class ToyRowReport { public string runId, groups; public List<ToyRowCase> cases = new List<ToyRowCase>(); public List<string> notCovered = new List<string>(); public List<string> logs = new List<string>(); }

        static List<List<Func<double, bool>>> trScripts;
        static List<double> trLimits;
        static ToyRowReport trReport;
        static int trStep, trRefusedLogs, trCurveRefusedLogs, trAcceptLogs;
        static double trStepAt, trCaseAt;
        static bool trFailed;
        static float trMinuteSeconds = 60f;
        static UdonBehaviour trRecall, trBox, trCatalog, trLedger, trKeeper, trActMenu, trPass, trDisc;
        static UdonBehaviour[] trCatchers;

        static void BeginToyRow(string groups)
        {
            Begin(false);
            report.toyRowOnly = true;
            report.toyRowGroups = groups;
            report.deadline = EditorApplication.timeSinceStartup + 600;
            report.limitations = "Single local ClientSim; the local player is instance master and every Owner. Menu operations go through the real Udon events (_Throw*, _Row2, _Pattern*, _ConfirmYes/No); "
                + "the throws are the disc's own _CheckThrow after the harness writes the velocity (no hand). Heap writes (trainer pass owners, stay accrual freeze, forged synced pattern values) are SIMULATED and listed per case. "
                + "Not covered: other players' views, late join, Owner leaving, real throws / curve feel, device fonts (2 players / PCVR / devices).";
            try { TrPrepareStorage(); }
            catch (Exception e) { End("BLOCKED", "PlayerData preparation failed (nothing tested): " + e.Message); return; }
            Save();
        }

        /// <summary>Begin が退避を済ませた後で、この場の PlayerData を消して「初めて来た人」で Play に入る (Course/97 と同じ手順。戻すのは Finish)。</summary>
        static void TrPrepareStorage()
        {
            string storage = Path.GetFullPath("ClientSimStorage/PlayerData");
            Directory.CreateDirectory(storage);
            var manifest = File.ReadAllLines(report.playerDataBackup + "/manifest.txt");
            foreach (var name in manifest)
                if (!File.Exists(report.playerDataBackup + "/" + name) || Hash(report.playerDataBackup + "/" + name) != Hash(Path.Combine(storage, name)))
                    throw new IOException("Backup of " + name + " is not identical; nothing deleted");
            foreach (var path in Directory.GetFiles(storage, "*_VRCDefaultWorldScene.json"))
            {
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), storage, StringComparison.OrdinalIgnoreCase)) throw new IOException("PlayerData path outside storage");
                File.Delete(path);
            }
        }

        static bool TrGroup(string g) { return report.toyRowGroups != null && report.toyRowGroups.Split(',').Contains(g); }

        // ============================================================
        // 組み立て
        // ============================================================

        static void MakeToyRowCases()
        {
            Application.logMessageReceived -= TrLog; Application.logMessageReceived += TrLog;
            trScripts = new List<List<Func<double, bool>>>(); trLimits = new List<double>();
            trStep = 0; trCaseAt = -1; trFailed = false; trRefusedLogs = 0; trCurveRefusedLogs = 0; trAcceptLogs = 0;
            trReport = new ToyRowReport { runId = report.runId, groups = report.toyRowGroups };
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true);
            trRecall = TrOne(all, "KH_ToyRecall"); trCatalog = TrOne(all, "KD_ShopCatalog"); trLedger = TrOne(all, "KD_ProgressLedger");
            trKeeper = TrOne(all, "KD_ShopKeeper"); trActMenu = TrOne(all, "KD_ActivitiesMenu"); trPass = TrOne(all, "KH_TrainerPass");
            trCatchers = new UdonBehaviour[6];
            foreach (var u in all.Where(u => u.programSource != null && u.programSource.name == "KH_FrisbeeCatch"))
                for (int i = 0; i < 6; i++) if (Get<Transform>(u, "dogRoot") == roots[i]) trCatchers[i] = u;
            if (trCatchers.Any(c => c == null)) throw new InvalidOperationException("Every dog needs its KH_FrisbeeCatch");
            object dv; trDisc = trCatchers[0].TryGetProgramVariable("frisbee", out dv) ? dv as UdonBehaviour : null;
            if (trDisc == null) throw new InvalidOperationException("Dog 0's catcher has no disc");
            trMinuteSeconds = Get<float>(trLedger, "minuteSeconds");
            trReport.notCovered.Add("Other players' views and late join: the synced pattern's first receipt and the re-check when the Owner's PlayerData arrives later (OnPlayerRestored / _Revalidate) -> 2 players");
            trReport.notCovered.Add("Owner leaving: the new Owner rewrites an unowned pattern to the basic ball (_ReconcileAsNewOwner) -> 2 players");
            trReport.notCovered.Add("A real hand throw with the aim preview, the curve's look and the dog's catch on a curving disc -> PCVR (the curve group of the prototype harness covers the planner)");
            trReport.notCovered.Add("The catch style change seen by a second player, and a non-trainer's forged event over the real network -> 2 players (TT3 sends it through ClientSim's network event path)");
            MakeToySetupCase(TrGroup("throw") ? "TT0-setup" : "BP0-setup");
            if (TrGroup("throw")) MakeThrowCases();
            if (TrGroup("pattern")) MakePatternCases();
            TrWrite();
        }

        static void MakeToySetupCase(string name)
        {
            var s = TrCase(name, true, 60);
            s.Add(TrUntil(() => Get<bool>(trLedger, "_loaded"), 25, () => "ledger not restored (_loaded)"));
            s.Add(TrUntil(() => Get<bool>(trCatalog, "_resolved"), 5, () => "catalog not resolved"));
            s.Add(TrDo(() => { for (int i = 0; i < 6; i++) if (Get<int[]>(manager, "walkers")[i] >= 0) pouch.SendCustomEvent("_WalkOff" + i); }));
            s.Add(TrUntil(() => Get<int[]>(manager, "walkers").All(w => w < 0), 5, () => "detach before setup"));
            s.Add(TrDo(() =>
            {
                trLedger.SetProgramVariable("minuteSeconds", 1000000f);   // 模擬: 滞在の加算を止める (残高を固定)
                TrToGate();
                TrExpect(trCatchers.All(c => Get<int>(c, "variant") == KH_FrisbeeCatch.VariantNormal && Get<int>(c, "pendingVariant") == -1), "six catch styles 0, nothing queued");
                TrExpect(!Get<bool>(trRecall, "_throwCurve"), "throw style starts straight");
                TrNote("fresh profile, 0 pt, six dogs detached, catch style 0 x6, throw style straight; simulated: stay accrual frozen (ledger heap minuteSeconds=1e6)");
                var box = TrToyBox();
                if (box != null)
                {
                    TrExpect((Get<string>(box, "_syncedProduct") ?? "") == "", "ball pattern unset (" + Get<string>(box, "_syncedProduct") + ")");
                    TrExpect(TrOnlyPattern(0), "every ball shows the basic ball only");
                    TrNote("ball pattern unset, basic ball on all six balls");
                }
            }));
        }

        static void MakeThrowCases()
        {
            // ---- TT1 ----
            var s = TrCase("TT1-throw-curve", false, 60);
            s.Add(TrDo(() =>
            {
                TrMenu("_Tab1");
                pouch.SendCustomEvent("_ThrowCurve");
                TrExpect(Get<bool>(trRecall, "_throwCurve"), "_ThrowCurve sets KH_ToyRecall._throwCurve");
                TrExpect(TrImageColor("throwCurveImage") == Get<Color>(pouch, "equipOnColor") && TrImageColor("throwStraightImage") == Get<Color>(pouch, "hillsideNeutralColor"), "curve lit, straight dark");
                TrExpect(TrGoActive("throwCurveUnderline") && !TrGoActive("throwStraightUnderline"), "underline under [曲げる] only");
            }));
            s.Add(TrUntil(TrDogFree, 30, () => "dog 0 catcher not at rest (state " + Get<int>(trCatchers[0], "state") + ")"));
            s.Add(TrDo(() => { trAcceptLogs = 0; TrThrow(); }));
            s.Add(TrUntil(() => trAcceptLogs >= 1 && Mathf.Abs(Get<float>(trCatchers[0], "_curve") - KH_FrisbeeCatch.CurveDefault) < 1e-5f, 3, () => "the dog did not accept the throw with the curve (catcher _curve " + Get<float>(trCatchers[0], "_curve") + ")"));
            s.Add(TrDo(() =>
            {
                TrExpect(Mathf.Abs(Get<float>(trDisc, "_curve") - KH_FrisbeeCatch.CurveDefault) < 1e-5f, "the disc flies with CurveDefault (" + Get<float>(trDisc, "_curve") + ")");
                TrExpect(trCatchers.All(c => Get<int>(c, "variant") == KH_FrisbeeCatch.VariantNormal), "the shared catch style stays 0 on all six (the curve is local)");
                TrNote("[曲げる]: recall flag on, button lit + underline; throw: disc _curve = catcher _curve = " + KH_FrisbeeCatch.CurveDefault + " (via ReqCatch); variant 0 x6");
                trDisc.SendCustomEvent("_Recall");
            }));
            s.Add(TrUntil(TrDogFree, 30, () => "dog 0 did not come back to rest after the recall"));

            // ---- TT2 ----
            s = TrCase("TT2-throw-straight", false, 60);
            s.Add(TrDo(() =>
            {
                TrMenu("_Tab1");
                pouch.SendCustomEvent("_ThrowStraight");
                TrExpect(!Get<bool>(trRecall, "_throwCurve"), "_ThrowStraight clears the flag");
                TrExpect(TrGoActive("throwStraightUnderline") && !TrGoActive("throwCurveUnderline"), "underline under [まっすぐ] only");
                trAcceptLogs = 0;
                TrThrow();
            }));
            s.Add(TrUntil(() => trAcceptLogs >= 1 && Mathf.Abs(Get<float>(trCatchers[0], "_curve")) < 1e-6f, 3, () => "the dog did not accept the straight throw (_curve " + Get<float>(trCatchers[0], "_curve") + ")"));
            s.Add(TrDo(() =>
            {
                TrExpect(Mathf.Abs(Get<float>(trDisc, "_curve")) < 1e-6f, "the disc flies straight");
                TrNote("[まっすぐ]: disc _curve = catcher _curve = 0");
                trDisc.SendCustomEvent("_Recall");
            }));
            s.Add(TrUntil(TrDogFree, 30, () => "dog 0 did not come back to rest after the recall"));

            // ---- TT3 ----
            s = TrCase("TT3-style-refused", true, 40);
            s.Add(TrDo(() =>
            {
                TrSetTrainer(false);
                TrMenu("_Tab6");
            }));
            s.Add(TrWait(.6));
            s.Add(TrDo(() =>
            {
                trActMenu.SendCustomEvent("_Row2");
                TrExpect(Get<string>(trActMenu, "_noteKey") == "activities.needTrainer", "menu refuses a non-trainer ('" + Get<string>(trActMenu, "_noteKey") + "')");
                trRefusedLogs = 0;
                foreach (var c in trCatchers) c.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqVariant", KH_FrisbeeCatch.VariantRunInBeside);
            }));
            s.Add(TrWait(1.5));
            s.Add(TrDo(() =>
            {
                TrExpect(trCatchers.All(c => Get<int>(c, "variant") == KH_FrisbeeCatch.VariantNormal && Get<int>(c, "pendingVariant") == -1), "a forged ReqVariant(3) from a non-trainer changes nothing (variants " + string.Join(",", trCatchers.Select(c => Get<int>(c, "variant"))) + ")");
                TrExpect(trRefusedLogs >= 6, "the receiver logged the refusal for each dog (" + trRefusedLogs + ")");
                TrNote("simulated: trainer pass heap emptied (_owners / _ownerCount); menu _Row2 -> 'activities.needTrainer', no request; network ReqVariant(3) x6 -> refused by the receiver (" + trRefusedLogs + " log lines), variant 0 x6");
            }));

            // ---- TT4 ----
            s = TrCase("TT4-style-trainer", true, 60);
            s.Add(TrDo(() =>
            {
                TrSetTrainer(true);
                trCurveRefusedLogs = 0;
                trCatchers[0].SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqVariant", KH_FrisbeeCatch.VariantCurve);
            }));
            s.Add(TrWait(1));
            s.Add(TrDo(() =>
            {
                TrExpect(Get<int>(trCatchers[0], "variant") == KH_FrisbeeCatch.VariantNormal && trCurveRefusedLogs >= 1, "ReqVariant(5) from a trainer is refused in the world (variant " + Get<int>(trCatchers[0], "variant") + ", logs " + trCurveRefusedLogs + ")");
                TrMenu("_Tab6");
            }));
            foreach (int want in new[] { KH_FrisbeeCatch.VariantRunInBeside, KH_FrisbeeCatch.VariantTwist, KH_FrisbeeCatch.VariantNormal })
            {
                int w = want;
                s.Add(TrWait(1.8));   // past the menu's 1.5 s optimistic window, so each press starts from the applied value
                s.Add(TrDo(() => trActMenu.SendCustomEvent("_Row2")));
                s.Add(TrUntil(() => trCatchers.All(c => Get<int>(c, "variant") == w), 3, () => "menu press did not set " + w + " on all six (" + string.Join(",", trCatchers.Select(c => Get<int>(c, "variant"))) + ")"));
            }
            s.Add(TrDo(() =>
            {
                TrNote("simulated trainer: menu cycle 0 -> 3 -> 4 -> 0 on all six (never 5); ReqVariant(5) refused in the world");
                trCatchers[0].SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqVariant", KH_FrisbeeCatch.VariantTwist);   // dog 0 alone -> mixed
            }));
            s.Add(TrUntil(() => Get<int>(trCatchers[0], "variant") == KH_FrisbeeCatch.VariantTwist, 3, () => "dog 0 alone did not switch to 4"));
            s.Add(TrWait(.6));   // the menu redraws every 0.25 s while visible
            s.Add(TrDo(() =>
            {
                string row = TrRowText(KominkaDogRun.KD_ActivitiesMenu.RowFrisbee);
                TrExpect(TrCourseTexts("course.frisbee.mixed").Any(t => row.Contains(t)), "dogs differ -> the row says 'all dogs: switching' ('" + row.Replace("\n", " / ") + "')");
                trCatchers[0].SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqVariant", KH_FrisbeeCatch.VariantNormal);
            }));
            s.Add(TrUntil(() => Get<int>(trCatchers[0], "variant") == KH_FrisbeeCatch.VariantNormal, 3, () => "dog 0 did not return to 0"));
            s.Add(TrWait(.6));
            s.Add(TrDo(() =>
            {
                string row = TrRowText(KominkaDogRun.KD_ActivitiesMenu.RowFrisbee);
                TrExpect(TrCourseTexts("course.frisbee.allDogs").Any(t => row.Contains(t.Split('{')[0])) && !TrCourseTexts("course.frisbee.mixed").Any(t => row.Contains(t)), "all equal -> 'all dogs: <style>' ('" + row.Replace("\n", " / ") + "')");
                TrSetTrainer(false);
                TrNote("mixed (dog 0 at 4, others 0) -> 'all dogs: switching'; back to 0 -> 'all dogs: normal'; trainer heap emptied again");
                pouch.SendCustomEvent("_Tab1");
            }));
        }

        static void MakePatternCases()
        {
            // 2026-10-03 (run 20261002-225708): each case sets up what it needs itself (cursor, closed card, points, owned tennis),
            // so one failure no longer cascades. The cursor is set on the toy tab's first draw (RedrawToyTopRow), so it is not -1 after TT*.

            // ---- BP1 ----
            var s = TrCase("BP1-buy-short", false, 30);
            s.Add(TrDo(() =>
            {
                TrExpect(TrToyBox() != null, "KD_ToyBox in the scene (Course/124)");
                TrExpect(Get<int>(trLedger, "_earned") - Get<int>(trLedger, "_spent") < 10 && !TrOwned("toy.tennis_ball"), "precondition: under 10 pt and tennis not owned (BP1 runs before the keeper grant)");
                TrPatternStart();
                int world = TrWorldPatternIndex(), cur = Get<int>(pouch, "_ptCursor");
                TrExpect(cur < 0 || cur == world, "the cursor starts on the world's pattern (cursor " + cur + ", world " + world + ")");
                TrCursorTo(1);
                pouch.SendCustomEvent("_PatternAct");
                TrExpect(Get<int>(pouch, "_confirmKind") == KominkaDogRun.KD_TreatPouch.ConfirmToyPattern && !TrGoActive("confirmYes"), "under 10 pt: the card opens without [購入] (kind " + Get<int>(pouch, "_confirmKind") + ")");
                int sp = Get<int>(trLedger, "_spent"); string synced = Get<string>(TrToyBox(), "_syncedProduct") ?? "";
                pouch.SendCustomEvent("_ConfirmYes");
                TrExpect(Get<int>(trLedger, "_spent") == sp && !TrOwned("toy.tennis_ball") && (Get<string>(TrToyBox(), "_syncedProduct") ?? "") == synced && TrNoteKey() == "KD_ShopShelf.WordCantBuy" && Get<int>(pouch, "_confirmKind") == 0,
                    "forced [購入] pays nothing, selects nothing, says 'cannot buy' ('" + TrNoteKey() + "')");
                TrNote("under 10 pt: card without [購入]; forced press -> no spend, no pattern, 'cannot buy'");
            }));

            // ---- BP2 ----
            s = TrCase("BP2-keeper-grant", false, 30);
            TrAddGrantSteps(s);
            s.Add(TrDo(() => { TrExpect(Get<int>(trLedger, "_earned") >= 200, "earned 200 pt"); TrNote("keeper: 200 pt earned"); }));

            // ---- BP3 ----
            s = TrCase("BP3-buy", false, 40);
            TrAddGrantSteps(s);
            int sp3 = 0;
            s.Add(TrDo(() =>
            {
                TrExpect(!TrOwned("toy.tennis_ball"), "precondition: tennis not owned yet");
                TrPatternStart();
                TrCursorTo(1);
                sp3 = Get<int>(trLedger, "_spent");
                pouch.SendCustomEvent("_PatternAct");
                TrExpect(Get<int>(pouch, "_confirmKind") == KominkaDogRun.KD_TreatPouch.ConfirmToyPattern && TrGoActive("confirmYes"), "200 pt: the card shows [購入] (kind " + Get<int>(pouch, "_confirmKind") + ")");
                pouch.SendCustomEvent("_ConfirmNo");
                TrExpect(Get<int>(trLedger, "_spent") == sp3 && !TrOwned("toy.tennis_ball") && Get<int>(pouch, "_confirmKind") == 0, "[やめる] pays nothing");
                pouch.SendCustomEvent("_PatternAct");
                TrExpect(TrGoActive("confirmYes"), "the card reopens with [購入]");
                pouch.SendCustomEvent("_ConfirmYes");
            }));
            s.Add(TrUntil(() => Get<string>(TrToyBox(), "_syncedProduct") == "toy.tennis_ball", 3, () => "the synced pattern did not become tennis (" + Get<string>(TrToyBox(), "_syncedProduct") + ")"));
            s.Add(TrDo(() =>
            {
                TrExpect(Get<int>(trLedger, "_spent") == sp3 + 10 && TrOwned("toy.tennis_ball"), "10 pt spent, tennis owned (spent " + sp3 + " -> " + Get<int>(trLedger, "_spent") + ")");
                TrExpect(Get<string>(TrToyBox(), "_applied") == "toy.tennis_ball" && TrOnlyPattern(1), "all six balls show the tennis ball only");
                TrExpect(TrNoteKey() == "course.toy.pattern.changed", "note 'six balls are now ...' ('" + TrNoteKey() + "')");
                TrNote("[やめる] -> no spend; [購入] -> 10 pt, owned, synced 'toy.tennis_ball', six balls tennis only");
            }));

            // ---- BP4 ----
            s = TrCase("BP4-use-owned", false, 60);
            TrAddOwnTennisSteps(s);
            int sp4 = 0;
            s.Add(TrDo(() =>
            {
                TrPatternStart();
                sp4 = Get<int>(trLedger, "_spent");
                TrCursorTo(0);
                pouch.SendCustomEvent("_PatternAct");
                TrExpect(Get<int>(pouch, "_confirmKind") == 0, "the free basic ball needs no card");
            }));
            s.Add(TrUntil(() => Get<string>(TrToyBox(), "_syncedProduct") == "toy.basic_ball" && TrOnlyPattern(0), 3, () => "basic ball not applied"));
            s.Add(TrDo(() =>
            {
                TrCursorTo(1);
                pouch.SendCustomEvent("_PatternAct");
                TrExpect(Get<int>(pouch, "_confirmKind") == 0, "an owned pattern needs no card");
            }));
            s.Add(TrUntil(() => Get<string>(TrToyBox(), "_syncedProduct") == "toy.tennis_ball" && TrOnlyPattern(1), 3, () => "tennis not applied again"));
            s.Add(TrDo(() => { TrExpect(Get<int>(trLedger, "_spent") == sp4, "no payment for switching"); TrNote("basic (free) and tennis (owned) switch without a card or payment"); }));

            // ---- BP5 ----
            s = TrCase("BP5-forged", true, 60);
            TrAddOwnTennisSteps(s);
            s.Add(TrDo(() =>
            {
                var box = TrToyBox();
                TrExpect(!TrOwned("toy.temari_claude"), "precondition: temari not owned");
                int rejects = Get<int>(box, "_dbgRejectCount");
                foreach (var forged in new[] { "toy.temari_claude", "toy.no_such_pattern", "toy.paw_cushion", "\u0000../x" + new string('z', 300) })
                {
                    box.SetProgramVariable("_syncedProduct", forged);
                    box.SendCustomEvent("_onDeserialization");
                    TrExpect(Get<string>(box, "_applied") == "toy.basic_ball" && TrOnlyPattern(0), "forged '" + (forged.Length > 24 ? forged.Substring(0, 24) + "..." : forged) + "' -> basic ball (applied " + Get<string>(box, "_applied") + ")");
                }
                TrExpect(Get<int>(box, "_dbgRejectCount") >= rejects + 4, "each forged value counted as rejected");
                box.SetProgramVariable("_syncedProduct", "toy.tennis_ball");
                box.SendCustomEvent("_onDeserialization");
                TrExpect(Get<string>(box, "_applied") == "toy.tennis_ball" && TrOnlyPattern(1), "an owned pattern written the same way is shown (applied " + Get<string>(box, "_applied") + ")");
                TrExpect(report.errors.Count == 0, "no runtime error");
                TrNote("simulated receiver: heap _syncedProduct + _onDeserialization; unowned paid (temari) / unknown key / CE plush not in the table / broken 300+ char string -> basic ball, no exception; owned tennis -> tennis");
            }));

            // ---- BP6 ----
            s = TrCase("BP6-saved", false, 60);
            TrAddOwnTennisSteps(s);
            s.Add(TrUntil(() =>
            {
                string ns = Get<string>(trLedger, "storageNamespace");
                bool owned;
                return VRC.SDK3.Persistence.PlayerData.TryGetBool(Networking.LocalPlayer, ns + ".Own.toy.tennis_ball", out owned) && owned;
            }, 3, () => "Own.toy.tennis_ball not in PlayerData"));
            s.Add(TrDo(() => TrNote("PlayerData: Own.toy.tennis_ball true (re-entry restore is the ledger's, Course/96b)")));

            // ---- BP7 (独立レビュー 2026-10-03 中 1) ----
            // 模擬: Udon の無い人形 (遠隔プレイヤー、柄を持っていない) へ KD_ToyBox の Owner を移す。人形は書き直さないので、
            // 「新 Owner が書き直さなかった」場合そのもの。この (Owner でなくなった) クライアントも OnOwnershipTransferred で確かめ直し、基本ボールへそろうこと。
            s = TrCase("BP7-owner-to-non-owner", true, 60);
            TrAddOwnTennisSteps(s);
            VRCPlayerApi puppet = null;
            s.Add(TrDo(() =>
            {
                var box = TrToyBox();
                if (Get<string>(box, "_syncedProduct") != "toy.tennis_ball") { box.SetProgramVariable("_syncedProduct", "toy.tennis_ball"); box.SendCustomEvent("_onDeserialization"); }
                TrExpect(Get<string>(box, "_applied") == "toy.tennis_ball", "precondition: tennis shown");
                VRC.SDK3.ClientSim.ClientSimMain.SpawnRemotePlayer("KH_ToyNeighbor");
            }));
            s.Add(TrUntil(() =>
            {
                var all = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; VRCPlayerApi.GetPlayers(all);
                puppet = all.FirstOrDefault(p => p != null && !p.isLocal); return puppet != null;
            }, 5, () => "remote puppet did not spawn"));
            s.Add(TrDo(() => Networking.SetOwner(puppet, TrToyBox().gameObject)));
            s.Add(TrUntil(() => { var o = Networking.GetOwner(TrToyBox().gameObject); return o != null && !o.isLocal; }, 3, () => "toy box Owner did not move to the puppet"));
            s.Add(TrUntil(() => Get<string>(TrToyBox(), "_applied") == "toy.basic_ball" && TrOnlyPattern(0), 3, () => "after the Owner moved to a non-owner this client still shows " + Get<string>(TrToyBox(), "_applied")));
            s.Add(TrDo(() =>
            {
                TrExpect(Get<string>(TrToyBox(), "_syncedProduct") == "toy.tennis_ball", "the synced value itself was not rewritten (the puppet has no Udon) — the fallback comes from re-validation");
                Networking.SetOwner(Networking.LocalPlayer, TrToyBox().gameObject);
            }));
            s.Add(TrUntil(() => Networking.IsOwner(TrToyBox().gameObject) && Get<string>(TrToyBox(), "_applied") == "toy.tennis_ball" && TrOnlyPattern(1), 3, () => "Owner back to me did not show tennis again (" + Get<string>(TrToyBox(), "_applied") + ")"));
            s.Add(TrDo(() =>
            {
                TrNote("simulated: Owner -> Udon-less puppet that owns nothing: this client re-validated on OnOwnershipTransferred and fell back to basic (synced value unchanged); Owner back to me -> tennis");
                // 人形の片付けは検査の外 (run 20261003-004156: ClientSim の RemovePlayer が NullReference を投げて、合格した検査がケースごと FAIL になった)。
                // 失敗しても記録だけ残して先へ進む。残った人形は柄の検査に影響しない (Owner は自分へ戻してある)
                try { if (puppet != null && Utilities.IsValid(puppet) && puppet.gameObject != null) VRC.SDK3.ClientSim.ClientSimMain.RemovePlayer(puppet); }
                catch (Exception e) { TrNote("cleanup only: ClientSim RemovePlayer threw " + e.GetType().Name + ": " + e.Message + " at " + TvWhere(e) + " (the puppet stays; not part of the check)"); }
            }));

            // ---- BP8 (独立レビュー 2026-10-03 中 2) ----
            // 模擬: 有料の柄 (手毬) の同期が、Owner の所有データより先に届き、見直しの回数も使い切った後で所有データが届く。
            // 所有データの到着 = 台帳のヒープ _owned / _ownValid を立ててから PlayerData にその Own キーを書く (OnPlayerDataUpdated を起こす)。
            s = TrCase("BP8-late-owner-data", true, 40);
            TrAddOwnTennisSteps(s);
            s.Add(TrDo(() =>
            {
                var box = TrToyBox();
                TrExpect(Networking.IsOwner(box.gameObject) && !TrOwned("toy.temari_claude"), "precondition: I am the Owner and do not own temari");
                box.SetProgramVariable("_syncedProduct", "toy.temari_claude");
                box.SendCustomEvent("_onDeserialization");
                TrExpect(Get<string>(box, "_applied") == "toy.basic_ball", "temari withheld while ownership is unknown");
                box.SetProgramVariable("_revalidateLeft", 0);   // 見直しの窓を使い切った状態
            }));
            s.Add(TrWait(2.5));   // 予約済みの見直しが 1 回走って終わるまで (まだ持っていないので基本ボールのまま)
            s.Add(TrDo(() =>
            {
                TrExpect(Get<string>(TrToyBox(), "_applied") == "toy.basic_ball" && !Get<bool>(TrToyBox(), "_revalidateScheduled"), "retry window over, still basic, nothing scheduled");
                int i = TrLedgerIndex("toy.temari_claude");
                var owned = Get<bool[]>(trLedger, "_owned"); var valid = Get<bool[]>(trLedger, "_ownValid");
                owned[i] = true; valid[i] = true;
                trLedger.SetProgramVariable("_owned", owned); trLedger.SetProgramVariable("_ownValid", valid);
                VRC.SDK3.Persistence.PlayerData.SetBool(Get<string>(trLedger, "storageNamespace") + ".Own.toy.temari_claude", true);
            }));
            s.Add(TrUntil(() => Get<string>(TrToyBox(), "_applied") == "toy.temari_claude" && TrOnlyPattern(5), 3, () => "late ownership data did not bring temari back (" + Get<string>(TrToyBox(), "_applied") + ")"));
            s.Add(TrDo(() =>
            {
                // 片付け: 手毬の所有を戻し、テニスへ
                int i = TrLedgerIndex("toy.temari_claude");
                var owned = Get<bool[]>(trLedger, "_owned"); owned[i] = false; trLedger.SetProgramVariable("_owned", owned);
                VRC.SDK3.Persistence.PlayerData.SetBool(Get<string>(trLedger, "storageNamespace") + ".Own.toy.temari_claude", false);
                var box = TrToyBox(); box.SetProgramVariable("_syncedProduct", "toy.tennis_ball"); box.SendCustomEvent("_onDeserialization");
                TrNote("simulated: temari synced before the Owner's ownership, retry window exhausted -> basic; ledger heap + PlayerData Own key written -> OnPlayerDataUpdated -> temari shown; ownership and pattern restored afterwards");
            }));
        }

        static int TrLedgerIndex(string key)
        {
            var products = ((Array)Get<object>(trLedger, "products")).Cast<object>().Select(x => x as UdonBehaviour).ToArray();
            for (int i = 0; i < products.Length; i++) if (products[i] != null && Get<string>(products[i], "productKey") == key) return i;
            throw new InvalidOperationException("Product not in the ledger: " + key);
        }

        // ---- 柄のケースの前提 (各ケースが自分で整える) ----
        static bool trGrantNeeded, trBuyNeeded;
        /// <summary>おもちゃタブを開き、開いている確認カードを閉じる。</summary>
        static void TrPatternStart()
        {
            TrMenu("_Tab1");
            if (Get<int>(pouch, "_confirmKind") != 0) pouch.SendCustomEvent("_ConfirmNo");
            TrExpect(Get<int>(pouch, "_confirmKind") == 0, "no confirm card open");
        }
        static int TrWorldPatternIndex()
        {
            string applied = Get<string>(TrToyBox(), "_applied") ?? "";
            int i = Array.IndexOf(Get<string[]>(TrToyBox(), "productKeys"), applied);
            return i < 0 ? 0 : i;
        }
        /// <summary>見ている柄を index へ (▶ を最大 7 回)。</summary>
        static void TrCursorTo(int index)
        {
            for (int k = 0; k < 7 && Get<int>(pouch, "_ptCursor") != index; k++) pouch.SendCustomEvent("_PatternNext");
            TrExpect(Get<int>(pouch, "_ptCursor") == index, "cursor on pattern " + index + " (" + Get<int>(pouch, "_ptCursor") + ")");
        }
        /// <summary>前提: おじいさんの 200 pt (まだなら本物の Interact で受け取る。受け取り済みなら飛ばす)。</summary>
        static void TrAddGrantSteps(List<Func<double, bool>> s)
        {
            s.Add(TrDo(() => { trGrantNeeded = Get<int>(trLedger, "_earned") < 200; if (trGrantNeeded) TrToKeeper(); }));
            s.Add(now => !trGrantNeeded || now - trStepAt >= 1);
            s.Add(TrDo(() => { if (trGrantNeeded) trKeeper.Interact(); }));
            s.Add(TrUntil(() => !trGrantNeeded || Get<int>(trLedger, "_earned") >= 200, 3, () => "keeper 200 pt (earned " + Get<int>(trLedger, "_earned") + ")"));
            s.Add(TrDo(() => { if (trGrantNeeded) { trKeeper.SendCustomEvent("_Close"); TrToGate(); TrNote("precondition: keeper grant (+200 pt)"); } }));
        }
        /// <summary>前提: テニスを持っている (まだなら 200 pt を受け取り、手元メニューで買う = BP3 と同じ入口)。</summary>
        static void TrAddOwnTennisSteps(List<Func<double, bool>> s)
        {
            TrAddGrantSteps(s);
            s.Add(TrDo(() =>
            {
                trBuyNeeded = !TrOwned("toy.tennis_ball");
                if (!trBuyNeeded) return;
                TrPatternStart(); TrCursorTo(1);
                pouch.SendCustomEvent("_PatternAct");
                TrExpect(TrGoActive("confirmYes"), "precondition purchase: the card shows [購入]");
                pouch.SendCustomEvent("_ConfirmYes");
            }));
            s.Add(TrUntil(() => TrOwned("toy.tennis_ball"), 3, () => "precondition: tennis not owned after the purchase"));
            s.Add(TrDo(() => { if (trBuyNeeded) TrNote("precondition: bought tennis through the menu"); }));
        }

        // ============================================================
        // 実行
        // ============================================================

        static void ToyRowTick(double now)
        {
            if (trScripts == null || caseIndex < 0 || caseIndex >= trScripts.Count) throw new InvalidOperationException("Toy row script lost (domain reload?)");
            if (trStep == 0 && trCaseAt != Current.startedAt) { trCaseAt = Current.startedAt; trStepAt = now; }
            try
            {
                if (now - Current.startedAt > trLimits[caseIndex]) throw new TimeoutException("Case deadline (" + trLimits[caseIndex] + " s)");
                var steps = trScripts[caseIndex];
                while (trStep < steps.Count)
                {
                    if (!steps[trStep](now)) return;
                    trStep++; trStepAt = now;
                }
                Current.status = "PASS"; Current.endedAt = now; Current.reason = string.Join(" | ", trReport.cases[caseIndex].notes);
                trReport.cases[caseIndex].status = "PASS";
            }
            catch (Exception e)
            {
                trFailed = true;
                Current.status = "FAIL"; Current.endedAt = now; Current.reason = "step " + trStep + ": " + e.GetType().Name + ": " + e.Message;
                trReport.cases[caseIndex].status = "FAIL";
                trReport.cases[caseIndex].notes.Add("FAIL at step " + trStep + ": " + e.Message);
                trReport.cases[caseIndex].notes.Add("at: " + TvWhere(e));
                TrRecover();
            }
            trStep = 0;
            TrWrite(); Save();
            if (caseIndex + 1 < report.cases.Count) { Next(); return; }
            try { trLedger.SetProgramVariable("minuteSeconds", trMinuteSeconds); } catch (Exception) { }
            End(trFailed ? "TOY_ROW_FAIL" : "TOY_ROW_PASS", trFailed ? "One or more cases failed; see toy-row.json"
                : "Local ClientSim toy tab top row cases passed (" + report.toyRowGroups + "). Not other players, late join, Owner leaving, real throws or devices.");
        }

        static void TrRecover()
        {
            try { TrSetTrainer(false); } catch (Exception) { }
            try { if (Get<int>(pouch, "_confirmKind") != 0) pouch.SendCustomEvent("_ConfirmNo"); } catch (Exception) { }
            try { trKeeper.SendCustomEvent("_Close"); } catch (Exception) { }
            try { trDisc.SendCustomEvent("_Recall"); } catch (Exception) { }
        }

        static void TrLog(string condition, string stack, LogType type)
        {
            if (report == null || !report.toyRowOnly || trReport == null || !EditorApplication.isPlaying) return;
            if (condition.StartsWith("[KH_FrisbeeCatch] variant request refused: player")) trRefusedLogs++;
            if (condition.StartsWith("[KH_FrisbeeCatch] variant request refused: 5 (curve)")) trCurveRefusedLogs++;
            if (condition.StartsWith("[KH_FrisbeeCatch] req accept") && roots != null && condition.Contains(" dog=" + roots[0].name)) trAcceptLogs++;
            if (trReport.logs.Count < 200 && (condition.StartsWith("[KH_FrisbeeCatch] variant") || condition.StartsWith("[KH_Frisbee] launch") || condition.StartsWith("[KD_Commerce]"))) trReport.logs.Add(condition);
        }

        static void TrWrite()
        {
            if (report == null || trReport == null) return;
            Directory.CreateDirectory(Dir + "/" + report.runId);
            File.WriteAllText(Dir + "/" + report.runId + "/toy-row.json", JsonUtility.ToJson(trReport, true));
        }

        // ---- 手順の部品 ----
        static List<Func<double, bool>> TrCase(string name, bool simulated, double limit)
        {
            report.cases.Add(new Case { name = name });
            trReport.cases.Add(new ToyRowCase { name = name, simulated = simulated });
            trLimits.Add(limit);
            var s = new List<Func<double, bool>>(); trScripts.Add(s); return s;
        }
        static Func<double, bool> TrDo(Action a) { return now => { a(); return true; }; }
        static Func<double, bool> TrWait(double seconds) { return now => now - trStepAt >= seconds; }
        static Func<double, bool> TrUntil(Func<bool> condition, double limit, Func<string> what)
        { return now => { if (condition()) return true; if (now - trStepAt > limit) throw new TimeoutException(what() + " (" + limit + " s)"); return false; }; }
        static void TrExpect(bool ok, string what) { if (!ok) throw new InvalidOperationException("Expected: " + what); }
        static void TrNote(string text) { trReport.cases[caseIndex].notes.Add(text); }

        // ---- 読み取り・操作 ----
        static UdonBehaviour TrOne(UdonBehaviour[] all, string program)
        {
            var found = all.Where(u => u.programSource != null && u.programSource.name == program).ToArray();
            if (found.Length != 1) throw new InvalidOperationException(program + " in scene: " + found.Length + " (expected 1)");
            return found[0];
        }
        static UdonBehaviour TrToyBox()
        {
            if (trBox != null) return trBox;
            object o; trBox = pouch.TryGetProgramVariable("ballPatterns", out o) ? o as UdonBehaviour : null;
            return trBox;
        }
        /// <summary>6 個のボールで、柄 i の器だけが出ている (KD_ToyBox.meshNodes[i * 6 + d])。</summary>
        static bool TrOnlyPattern(int index)
        {
            var box = TrToyBox(); if (box == null) return false;
            var nodes = Get<GameObject[]>(box, "meshNodes"); int n = nodes.Length / 6;
            for (int i = 0; i < n; i++) for (int d = 0; d < 6; d++) if (nodes[i * 6 + d] == null || nodes[i * 6 + d].activeSelf != (i == index)) return false;
            return true;
        }
        static bool TrOwned(string key)
        {
            var products = ((Array)Get<object>(trLedger, "products")).Cast<object>().Select(x => x as UdonBehaviour).ToArray();
            for (int i = 0; i < products.Length; i++)
                if (products[i] != null && Get<string>(products[i], "productKey") == key) return Get<bool[]>(trLedger, "_owned")[i] && Get<bool[]>(trLedger, "_ownValid")[i];
            throw new InvalidOperationException("Product not in the ledger: " + key);
        }
        static string TrNoteKey() { var keys = Get<string[]>(pouch, "_hsNoteKeys"); return keys != null && keys.Length > 1 ? keys[1] : ""; }   // slot 1 = おもちゃ
        static Color TrImageColor(string field) { object o; return pouch.TryGetProgramVariable(field, out o) && o is Image img && img != null ? img.color : Color.clear; }
        static bool TrGoActive(string field) { object o; return pouch.TryGetProgramVariable(field, out o) && o is GameObject g && g != null && g.activeSelf; }
        static string TrRowText(int row)
        {
            object o; if (!trActMenu.TryGetProgramVariable("rowLabels", out o) || !(o is Array a) || row >= a.Length) return "";
            var t = a.GetValue(row) as TMP_Text; return t == null ? "" : t.text ?? "";
        }
        static string[] TrCourseTexts(string key)
        {
            var c = JsonUtility.FromJson<KominkaDogRun.Editor.KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath));
            return c.languages.Select(l => l.texts.FirstOrDefault(x => x.key == key)).Where(x => x != null && !string.IsNullOrEmpty(x.text)).Select(x => x.text).ToArray();
        }
        /// <summary>模擬: KH_TrainerPass のヒープの所有者表 (ClientSim の Store は自分を所有者にしない前提。TT3 は拒否ログで確かめる)。</summary>
        static void TrSetTrainer(bool on)
        {
            trPass.SetProgramVariable("_owners", on ? new[] { Networking.LocalPlayer.playerId } : new int[0]);
            trPass.SetProgramVariable("_ownerCount", on ? 1 : 0);
            trPass.SetProgramVariable("_loaded", true);
        }
        static void TrMenu(string tabEvent)
        {
            object o; var root = pouch.TryGetProgramVariable("panelRoot", out o) ? o as GameObject : null;
            if (root == null || !root.activeSelf) pouch.SendCustomEvent("_Toggle");
            if (Get<bool>(pouch, "_welcome")) pouch.SendCustomEvent("_WelcomeMenu");
            if (tabEvent != null) pouch.SendCustomEvent(tabEvent);
        }
        static void TrToGate()
        {
            var face = nodes[1] - nodes[0]; face.y = 0;
            Networking.LocalPlayer.TeleportTo(nodes[0], Quaternion.LookRotation(face));
        }
        static void TrToKeeper()
        {
            var t = trKeeper.transform; var fwd = t.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            UnityEngine.AI.NavMeshHit hit;
            var want = t.position + fwd.normalized * 1.6f;
            var p = UnityEngine.AI.NavMesh.SamplePosition(want, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas) ? hit.position : want;
            var face = t.position - p; face.y = 0;
            Networking.LocalPlayer.TeleportTo(p, face.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(face) : Networking.LocalPlayer.GetRotation());
        }
        static bool TrDogFree() { int st = Get<int>(trCatchers[0], "state"); return st == KH_FrisbeeCatch.StateIdle || st == KH_FrisbeeCatch.StateReady; }
        /// <summary>門の道で、犬 0 を投げる人の横 1.5 m に置き、円盤を手の位置から前へ 7 m/s・10° で出す (Course/94 の FbThrow と同じ入口)。</summary>
        static void TrThrow()
        {
            TrToGate();
            var lp = Networking.LocalPlayer; var fwd = lp.GetRotation() * Vector3.forward; fwd.y = 0; fwd.Normalize();
            var side = Vector3.Cross(Vector3.up, fwd);
            UnityEngine.AI.NavMeshHit hit;
            var dogAt = UnityEngine.AI.NavMesh.SamplePosition(lp.GetPosition() + side * 1.5f, out hit, 2f, UnityEngine.AI.NavMesh.AllAreas) ? hit.position : lp.GetPosition() + side * 1.5f;
            roots[0].SetPositionAndRotation(dogAt, Quaternion.LookRotation(fwd));
            var sync = roots[0].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
            var p0 = lp.GetPosition() + Vector3.up * 1.2f + fwd * .4f;
            float rad = 10f * Mathf.Deg2Rad;
            var v0 = fwd * (7f * Mathf.Cos(rad)) + Vector3.up * (7f * Mathf.Sin(rad));
            trDisc.transform.position = p0;
            trDisc.SetProgramVariable("_pendingThrowVelocity", v0);
            trDisc.SetProgramVariable("_hasPendingThrow", true);
            trDisc.SendCustomEvent("_CheckThrow");
            TrExpect(Get<bool>(trDisc, "_flying"), "the disc launched (_CheckThrow)");
        }
    }
}
