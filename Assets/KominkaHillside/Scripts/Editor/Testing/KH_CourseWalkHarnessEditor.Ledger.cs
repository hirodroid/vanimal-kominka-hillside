using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-PRG-001 台帳の固定手順 — Course/96 (初めて来た人) と Course/96b (96 の PlayerData で再入室)。
    ///
    /// 96 (PlayerData は空から):
    ///   L0-fresh-profile     復元済み・名前空間 KH_Commerce_v1・Bonus.initial / keeper なし・骨型 5 個 (初期在庫)・残高 = 滞在分だけ。滞在の加算を止める (模擬)
    ///   L1-T008-zero-balance 残高 0 のまま さんぽ (つなぐ / はずす)・ボールを呼ぶ・ディスクを呼ぶ・おやつを出して しまう が通り、pt を要求する表示が出ない
    ///   L2-T007-keeper-once  台帳の復元待ち (模擬: _loaded=false) に 3 連打 → 付与なし・要求は残る → [またね] で閉じても残る → 復元後 1 秒の再試行で 200 がちょうど 1 回、
    ///                        Bonus.keeper がファイルに保存。その後の 5 回 (連打含む) は挨拶だけ
    ///   L3-menu-balance      手元メニューのヘッダに「N pt」(N = 残高)
    ///   L4-stay-accrual      滞在の加算を戻す → 1 分ごとに 1 pt (earned = 200 + 経過分)、Earned がファイルに保存
    ///   L5-purchase          かざりの撫でFX [買う] → 確認 [購入] で Own.petfx.paw_cookie と Spent がファイルに保存。もう一度押すと着け替え (二重に払わない)。
    ///                        おやつの [買う] で StockB +5・Spent +1
    ///   L6-saved-snapshot    ファイルとヒープの一致を確かめ、ファイルを写して Logs/kh-course-walk/ledger-last.json に記録 (96b の入力)
    /// 96b (96 の写しを置いて Play = ClientSim の再入室):
    ///   R0-restored          Earned = 写しの値 + 今回の滞在分、Spent・所持・在庫が写しと同じ。初期在庫を二度付けない
    ///   R1-keeper-no-regrant おじいさんに 3 連打 → 付与なし・通常の挨拶
    ///   R2-owned-in-menu     買った撫でFX はメニューで所持 (押すと着け替え・払わない)
    ///   R3-menu-balance      ヘッダに残高
    /// ハーネス対象外 (PCVR / 実機): VRChat のサーバー保存と実際の再入室 (OnPlayerRestored の遅れ)、撫で (手の接触)、集合 (トレーナー特典 = CE)、
    /// 本物の投げ (ボール / ディスクを手で投げる)、CE の所持 (Store)、複数人。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/96 - Walk Harness Ledger (L0-L6, fresh profile)")]
        public static void LedgerHarness() { BeginCommerce(CmSetLedger); }
        [MenuItem("Tools/KominkaHillside/Course/96b - Walk Harness Ledger Reentry (R0-R3, after 96)")]
        public static void LedgerReentryHarness() { BeginCommerce(CmSetLedgerReentry); }

        static void CmLedgerNotCovered()
        {
            cmReport.notCovered.Add("T-007 re-entry on a real instance: VRChat server persistence and the real OnPlayerRestored delay (96b is a new ClientSim Play reading the local file) -> PCVR");
            cmReport.notCovered.Add("T-008 petting (hand contact is not simulated) -> PCVR");
            cmReport.notCovered.Add("T-008 gathering (needs the trainer benefit = CE subscription) -> PCVR with the benefit");
            cmReport.notCovered.Add("T-008 real ball / disc throws (VRC_Pickup in a real hand); the harness uses the menu recall of both -> PCVR");
            cmReport.notCovered.Add("CE ownership through Store.DoesPlayerOwnProduct and the loading contract of T-006 -> PCVR / VER");
            cmReport.notCovered.Add("Several players / late joiner reading another player's Own.* (IsUnlockedFor) -> PCVR");
            cmReport.notCovered.Add("Write order 'flag -> Earned' / 'Own -> Spent' inside one frame is not observable here (code review + check-consume-once)");
        }

        // ============================================================
        // Course/96
        // ============================================================

        static void MakeLedgerCases()
        {
            CmLedgerNotCovered();
            int biscuitRow = -1, fxRow = -1;

            // ---- L0 ----
            var s = CmCase("L0-fresh-profile", true, 60);
            s.Add(CmUntil(CmLoaded, 20, () => "ledger not restored (_loaded)"));
            s.Add(CmUntil(() => Get<bool>(cmCatalog, "_resolved") && CmSeeded(CmBiscuit), 5, () => "catalog not resolved / initial bone biscuits not seeded (_GrantInitialStockOnce)"));
            s.Add(CmDo(() =>
            {
                biscuitRow = CmTreatRow(CmBiscuit);
                CmExpect(CmNs() == KominkaDogRun.KD_ProgressLedger.ProductionNamespace && CmNs() == "KH_Commerce_v1", "namespace KH_Commerce_v1 (" + CmNs() + ")");
                CmExpect(Get<int>(cmLedger, "initialPoints") == 0, "initialPoints 0 (no Bonus.initial in the hillside)");
                CmExpect(Get<int>(cmLedger, "keeperPoints") == 200 && KominkaDogRun.KD_ProgressLedger.HillsideKeeperPoints == 200, "keeperPoints 200");
                CmExpect(!PdHas(CmKey("Bonus", KominkaDogRun.KD_ProgressLedger.BonusKeeper)) && !PdHas(CmKey("Bonus", KominkaDogRun.KD_ProgressLedger.BonusInitial)), "fresh profile: no Bonus.keeper / Bonus.initial");
                CmExpect(CmSpent() == 0, "spent 0 (" + CmSpent() + ")");
                CmExpect(CmEarned() == CmGranted(), "earned = stay minutes only (earned " + CmEarned() + ", minutes " + CmGranted() + ")");
                CmExpect(CmPoints() == 0, "balance 0 before the first minute (" + CmPoints() + "); the harness starts ~15 s after the join");
                CmExpect(CmStockB(CmBiscuit) == 5 && CmStockE(CmBiscuit) == 0, "bone biscuit 5 / 0 (" + CmStockB(CmBiscuit) + " / " + CmStockE(CmBiscuit) + ")");
                CmFreezeStay(true);
                CmNote("fresh ClientSim PlayerData (this scene's file removed after the backup); restored, ns " + CmNs() + ", no bonus flags, bone biscuit 5, balance 0");
                CmNote("simulated: stay accrual frozen (ledger heap minuteSeconds=1e6) so the balance stays 0 for T-008 and grants are exact; L4 puts it back");
            }));
            s.Add(CmUntil(() => CmFileInt(CmFile(), CmKey("StockB", CmBiscuit)) == 5, 3, () => "initial stock not saved to the PlayerData file"));

            // ---- L1 (T-008) ----
            s = CmCase("L1-T008-zero-balance", false, 120);
            s.Add(CmDo(CmToGate));
            s.Add(CmWait(1));
            s.Add(CmDo(() => { CmExpect(CmPoints() == 0, "balance 0"); CmAttach(0, 0); }));
            s.Add(CmUntil(() => CmMine(0) && Get<string[]>(pouch, "_hsNoteKeys")[0] == "KD_TreatPouch.WordWalkAttachedLeft", 5, () => "walk: attach dog 0 at 0 pt (walker " + Get<int[]>(manager, "walkers")[0] + ", note " + Get<string[]>(pouch, "_hsNoteKeys")[0] + ")"));
            s.Add(CmWait(1));
            s.Add(CmDo(() => pouch.SendCustomEvent("_WalkOff0")));
            s.Add(CmUntil(() => Get<int[]>(manager, "walkers")[0] < 0 && Get<string[]>(pouch, "_hsNoteKeys")[0] == "KD_TreatPouch.WordWalkDetached", 5, () => "walk: detach dog 0 (note " + Get<string[]>(pouch, "_hsNoteKeys")[0] + ")"));
            CmToyRecallSteps(s, 0, KH_ToyRecall.KindBall);
            s.Add(CmWait(3));   // KH_ToyRecall の行ごとの間隔 (Cooldown) を空ける
            CmToyRecallSteps(s, 0, KH_ToyRecall.KindDisc);
            int e0 = 0;
            s.Add(CmDo(() => { e0 = CmStockE(CmBiscuit); pouch.SendCustomEvent("_Row" + biscuitRow); }));
            s.Add(CmUntil(() => CmState() == KominkaDogRun.KD_TreatBox.DispOut, 4, () => "treat: dispense at 0 pt (state " + CmState() + ", reason " + CmReason() + ")"));
            s.Add(CmDo(() => { CmExpect(CmStockE(CmBiscuit) == e0 + 1, "one biscuit consumed"); pouch.SendCustomEvent("_PutAway"); }));
            s.Add(CmUntil(() => CmState() == KominkaDogRun.KD_TreatBox.DispIdle, 4, () => "treat: put away"));
            s.Add(CmDo(() =>
            {
                CmExpect(CmPoints() == 0 && CmSpent() == 0 && CmEarned() == 0, "still 0 pt / spent 0 / earned 0 after every basic action");
                CmNote("0 pt: attach and detach dog 0 (menu _WalkLeft0 / _WalkOff0, notes attached / detached), ball and disc of dog 0 called to the feet (_Toy0 / _Disc0, gate Ok, placed), a biscuit out and put away (initial stock). No pt demand on these paths");
                CmNote("not here: petting, gathering (trainer CE), real throws -> PCVR (see notCovered)");
            }));

            // ---- L2 (T-007) ----
            s = CmCase("L2-T007-keeper-once", true, 60);
            int before = 0;
            s.Add(CmDo(CmToKeeper));
            s.Add(CmWait(1));
            s.Add(CmDo(() =>
            {
                before = CmEarned();
                cmLedger.SetProgramVariable("_loaded", false); cmLoadedForced = true;
                cmKeeper.Interact(); cmKeeper.Interact(); cmKeeper.Interact();   // 同じフレームの 3 連打
                CmExpect(Get<bool>(cmKeeper, "_giftPending"), "the request stays while the ledger is not restored");
                CmExpect(CmGo(cmKeeper, "menuRoot") != null && CmGo(cmKeeper, "menuRoot").activeSelf, "the keeper's board is open");
                CmExpect(CmEarned() == before && !PdHas(CmKey("Bonus", "keeper")), "no grant before the restore");
                CmExpect(CmStr(cmKeeper, "_topicBody") == "KD_ShopKeeper.WordGreeting", "plain greeting while waiting (" + CmStr(cmKeeper, "_topicBody") + ")");
                cmKeeper.SendCustomEvent("_Close");   // [またね]
                CmExpect(!CmGo(cmKeeper, "menuRoot").activeSelf && Get<bool>(cmKeeper, "_giftPending"), "[またね] closes the board but keeps the request (Codex D-2)");
            }));
            s.Add(CmWait(2.5));   // _Refresh (1 s) が 2 回以上回っても、復元前は付けない
            s.Add(CmDo(() =>
            {
                CmExpect(CmEarned() == before && !PdHas(CmKey("Bonus", "keeper")) && Get<bool>(cmKeeper, "_giftPending"), "still no grant, request kept, 2.5 s into the delay");
                cmLedger.SetProgramVariable("_loaded", true); cmLoadedForced = false;
            }));
            s.Add(CmUntil(() => CmEarned() == before + 200, 3, () => "200 pt not granted within 3 s after the restore (earned " + CmEarned() + ")"));
            s.Add(CmWait(1.5));
            s.Add(CmUntil(() => CmFileTrue(CmFile(), CmKey("Bonus", "keeper")) && CmFileTotalsMatch(), 3, () => "Bonus.keeper / Earned not saved: " + CmFileSummary()));
            int after = 0;
            s.Add(CmDo(() =>
            {
                CmExpect(CmEarned() == before + 200 && PdBool(CmKey("Bonus", "keeper")) && !Get<bool>(cmKeeper, "_giftPending"), "exactly +200, flag set, request cleared");
                after = CmEarned();
                cmKeeper.Interact();
            }));
            s.Add(CmWait(0.6));
            s.Add(CmDo(() => cmKeeper.Interact()));
            s.Add(CmWait(0.6));
            s.Add(CmDo(() => { cmKeeper.Interact(); cmKeeper.Interact(); cmKeeper.Interact(); }));
            s.Add(CmWait(1.5));
            s.Add(CmDo(() =>
            {
                CmExpect(CmEarned() == after, "5 more talks (3 in one frame) add nothing (earned " + CmEarned() + ", expected " + after + ")");
                CmExpect(CmStr(cmKeeper, "_topicBody") == "KD_ShopKeeper.WordGreeting" && !Get<bool>(cmKeeper, "_giftPending"), "plain greeting after the grant");
                cmKeeper.SendCustomEvent("_Close");
                CmNote("simulated restore delay: ledger heap _loaded=false while 3 talks in one frame and [またね]; no grant, request kept; _loaded=true -> +200 once within 3 s by the 1 s retry; Bonus.keeper and Earned in the file");
                CmNote("after the grant: 5 talks (one triple) -> +0, plain greeting. Re-entry is Course/96b");
            }));

            // ---- L3 ----
            s = CmCase("L3-menu-balance", false, 30);
            s.Add(CmDo(() => CmMenu("_Tab0")));
            s.Add(CmUntil(() => CmText(pouch, "balanceLabel").Contains(" " + CmPoints() + " pt"), 2, () => "header balance '" + CmText(pouch, "balanceLabel") + "' does not show " + CmPoints() + " pt"));
            s.Add(CmDo(() => CmNote("header: '" + CmText(pouch, "balanceLabel") + "' (balance " + CmPoints() + ")")));

            // ---- L4 ----
            s = CmCase("L4-stay-accrual", true, 150);
            int g1 = 0, en1 = 0;
            s.Add(CmDo(() => CmFreezeStay(false)));
            s.Add(CmUntil(() => CmGranted() == CmMinutesNow(), 20, () => "catch-up of the frozen minutes (granted " + CmGranted() + ", elapsed " + CmMinutesNow() + ")"));
            s.Add(CmDo(() =>
            {
                g1 = CmGranted(); en1 = CmEarned();
                CmExpect(en1 == 200 + g1, "earned = 200 + stay minutes (" + en1 + " vs 200 + " + g1 + ")");
            }));
            s.Add(CmUntil(() => CmGranted() == g1 + 1, 80, () => "no +1 within the next minute (granted " + CmGranted() + ")"));
            s.Add(CmWait(0.5));
            s.Add(CmUntil(CmFileTotalsMatch, 3, () => "stay point not saved: " + CmFileSummary()));
            s.Add(CmDo(() =>
            {
                CmExpect(CmEarned() == en1 + 1 && CmEarned() == 200 + CmGranted(), "exactly +1 for the minute (earned " + CmEarned() + ")");
                CmNote("stay accrual restored (minuteSeconds " + cmMinuteSeconds + "): the frozen minutes arrived at once (" + g1 + " min), then +1 at the next minute boundary; Earned " + CmEarned() + " saved");
            }));

            // ---- L5 ----
            s = CmCase("L5-purchase", false, 60);
            int sp0 = 0, b0 = 0;
            s.Add(CmDo(() =>
            {
                CmMenu("_Tab5");
                CmExpect(Get<int>(pouch, "_accSetMenu") == 0 && Get<int>(pouch, "_accProduct") < 0, "かざり opens at the catalog level");
                fxRow = CmFxRow(CmFxPawCookie);
                CmExpect(!CmOwned(CmFxPawCookie) && CmPoints() >= 8, "paw-cookie FX not owned, balance >= 8 (" + CmPoints() + ")");
                sp0 = CmSpent();
                pouch.SendCustomEvent("_Fx" + fxRow);
                CmExpect(Get<int>(pouch, "_fxConfirmRow") == fxRow && CmConfirmKind() == KominkaDogRun.KD_TreatPouch.ConfirmFx && CmConfirmYesShown(), "[買う] opens the shared confirm card with [購入] (kind " + CmConfirmKind() + ")");
                CmExpect(CmSpent() == sp0, "opening the card pays nothing");
                pouch.SendCustomEvent("_ConfirmYes");
                CmExpect(CmOwned(CmFxPawCookie) && CmSpent() == sp0 + 8 && CmFeedback() == "KD_ShopShelf.WordBought", "bought: owned, spent +8 (" + CmSpent() + "), '" + CmFeedback() + "'");
            }));
            s.Add(CmUntil(() => CmFileTrue(CmFile(), CmKey("Own", CmFxPawCookie)) && CmFileTotalsMatch(), 3, () => "Own." + CmFxPawCookie + " / Spent not saved: " + CmFileSummary()));
            s.Add(CmDo(() =>
            {
                pouch.SendCustomEvent("_Fx" + fxRow);   // 所持済み = 着け替え
                CmExpect(CmSpent() == sp0 + 8 && Get<int>(pouch, "_fxConfirmRow") < 0 && CmFeedback() == "KD_TreatPouch.WordApplied", "pressing the owned row equips, pays nothing ('" + CmFeedback() + "')");
                CmMenu("_Tab0");
                b0 = CmStockB(CmBiscuit); sp0 = CmSpent();
                pouch.SendCustomEvent("_TreatBuy" + biscuitRow);
                CmExpect(CmConfirmKind() == KominkaDogRun.KD_TreatPouch.ConfirmTreat && CmConfirmYesShown(), "treat [買う] opens the card with [購入]");
                pouch.SendCustomEvent("_ConfirmYes");
                CmExpect(CmStockB(CmBiscuit) == b0 + 5 && CmSpent() == sp0 + 1, "treat pack: StockB +5, spent +1 (" + CmStockB(CmBiscuit) + ", " + CmSpent() + ")");
            }));
            s.Add(CmUntil(() => CmFileInt(CmFile(), CmKey("StockB", CmBiscuit)) == CmStockB(CmBiscuit) && CmFileTotalsMatch(), 3, () => "StockB / Spent not saved: " + CmFileSummary()));
            s.Add(CmDo(() => CmNote("menu purchase: FX '" + CmFxPawCookie + "' 8 pt -> Own + Spent saved, second press equips (no double pay); treat pack via [買う] -> StockB " + CmStockB(CmBiscuit) + ", Spent " + CmSpent() + " saved")));

            // ---- L6 ----
            s = CmCase("L6-saved-snapshot", false, 30);
            s.Add(CmDo(() => { CmFreezeStay(true); pouch.SendCustomEvent("_Close"); }));
            s.Add(CmWait(1));
            s.Add(CmDo(() =>
            {
                var f = CmFile();
                CmExpect(CmFileTotalsMatch(), "file = heap: " + CmFileSummary());
                CmExpect(CmFileTrue(f, CmKey("Bonus", "keeper")) && CmFileTrue(f, CmKey("Own", CmFxPawCookie)), "Bonus.keeper and Own." + CmFxPawCookie + " in the file");
                // StockE は最初の消費 (_TryConsume) まで書かれず、台帳は無いキーを 0 と読む (KD_ProgressLedger.cs ReadTotal / _TryConsume)。無ければ 0
                CmExpect(CmFileInt(f, CmKey("StockB", CmBiscuit)) == CmStockB(CmBiscuit) && CmFileIntOr0(f, CmKey("StockE", CmBiscuit)) == CmStockE(CmBiscuit),
                    "biscuit StockB / StockE in the file (file " + CmFileInt(f, CmKey("StockB", CmBiscuit)) + "/" + CmFileIntOr0(f, CmKey("StockE", CmBiscuit)) + ", heap " + CmStockB(CmBiscuit) + "/" + CmStockE(CmBiscuit) + ")");
                string dir = Dir + "/" + report.runId + "/ledger-after";
                Directory.CreateDirectory(dir);
                string name = Path.GetFileName(CmStorageFile()), copy = dir + "/" + name;
                File.Copy(CmStorageFile(), copy, true);
                File.WriteAllText(CmLastLedgerPath, JsonUtility.ToJson(new CmLedgerLast { runId = report.runId, status = cmFailed ? "FAIL" : "PASS", file = copy, name = name, localId = CmMe() }, true));
                CmNote("snapshot " + copy + " (earned " + CmEarned() + ", spent " + CmSpent() + ", biscuit " + CmStockB(CmBiscuit) + "/" + CmStockE(CmBiscuit) + "); " + CmLastLedgerPath + " points to it for Course/96b; stay frozen until Play ends");
            }));
        }

        /// <summary>ボール / ディスクを手元メニューで呼び、足元に置かれたことを見る (T-008)。</summary>
        static void CmToyRecallSteps(List<Func<double, bool>> s, int dog, int kind)
        {
            string evt = (kind == KH_ToyRecall.KindBall ? "_Toy" : "_Disc") + dog, what = kind == KH_ToyRecall.KindBall ? "ball" : "disc";
            s.Add(CmDo(() =>
            {
                cmRecall.SetProgramVariable("_lastResult", -1);   // 読み出し用の値を空にする (前の結果と取り違えない)
                pouch.SendCustomEvent(evt);
                CmExpect(Get<int>(pouch, "_hsLastGate") == KH_ToyRecall.Ok, what + ": menu gate Ok at 0 pt (gate " + Get<int>(pouch, "_hsLastGate") + ")");
            }));
            s.Add(CmUntil(() => Get<int>(cmRecall, "_lastResult") == 0 && Get<int>(cmRecall, "_lastRow") == dog && Get<int>(cmRecall, "_lastKind") == kind, 6,
                () => what + ": not placed (result " + Get<int>(cmRecall, "_lastResult") + ")"));
            s.Add(CmUntil(() => Get<string[]>(pouch, "_hsNoteKeys")[1] == "KD_TreatPouch.WordToyPlaced", 2, () => what + ": note '" + Get<string[]>(pouch, "_hsNoteKeys")[1] + "'"));
            s.Add(CmDo(() =>
            {
                var t = CmToyTransform(dog, kind);
                float d = TvFlat(t.position, Networking.LocalPlayer.GetPosition());
                CmExpect(d <= 3.5f, what + " by the player (" + d.ToString("F2") + " m)");
            }));
        }

        static Transform CmToyTransform(int dog, int kind)
        {
            // 裏山の手元メニューはおもちゃを KH_ToyRecall に頼む (KD_TreatPouch.Toy.cs HillsideToy → recall._RecallToy)。
            // pouch.balls は裏山では配線しない (KD_TreatSetupEditor.Hillside.cs:253「balls / toyFetchers / live は探さない」) ので、
            // 置いた物は KH_ToyRecall の balls / discs (行 = 犬) から引く
            var toys = CmRefs(cmRecall, kind == KH_ToyRecall.KindBall ? "balls" : "discs");
            if (dog < 0 || dog >= toys.Length || toys[dog] == null) throw new InvalidOperationException("KH_ToyRecall." + (kind == KH_ToyRecall.KindBall ? "balls" : "discs") + "[" + dog + "] missing (length " + toys.Length + ")");
            return toys[dog].transform;
        }

        static int CmFxRow(string productKey)
        {
            var fx = CmRefs(pouch, "fxItems");
            for (int i = 0; i < fx.Length; i++) if (fx[i] != null && CmStr(fx[i], "productKey") == productKey) return i;
            throw new InvalidOperationException("FX row not found: " + productKey);
        }

        /// <summary>台帳の _Tick と同じ式の経過分 ((Time.time - _clockBase) / minuteSeconds の切り捨て)。</summary>
        static int CmMinutesNow() { return (int)((Time.time - Get<float>(cmLedger, "_clockBase")) / Mathf.Max(Get<float>(cmLedger, "minuteSeconds"), 1f)); }

        // ============================================================
        // Course/96b
        // ============================================================

        static void MakeLedgerReentryCases()
        {
            CmLedgerNotCovered();
            if (string.IsNullOrEmpty(report.commerceSource) || !File.Exists(report.commerceSource)) throw new InvalidOperationException("96 snapshot lost: " + report.commerceSource);
            var snap = CmFileValues(report.commerceSource);
            string ns = CmNs();
            int fileEarned = CmFileIntOr0(snap, ns + ".Earned"), fileSpent = CmFileIntOr0(snap, ns + ".Spent");

            var s = CmCase("R0-restored", false, 60);
            s.Add(CmUntil(CmLoaded, 20, () => "ledger not restored (_loaded)"));
            s.Add(CmDo(() =>
            {
                CmExpect(snap.Count > 0 && CmFileTrue(snap, ns + ".Bonus.keeper"), "the 96 snapshot has Bonus.keeper (" + report.commerceSource + ")");
                CmExpect(CmEarned() == fileEarned + CmGranted(), "earned = saved " + fileEarned + " + this stay " + CmGranted() + " (heap " + CmEarned() + ")");
                CmExpect(CmSpent() == fileSpent, "spent restored (" + CmSpent() + " vs " + fileSpent + ")");
                CmExpect(PdBool(CmKey("Bonus", "keeper")), "Bonus.keeper restored");
                var owned = Get<bool[]>(cmLedger, "_owned");
                var ownValid = Get<bool[]>(cmLedger, "_ownValid");
                var bought = Get<int[]>(cmLedger, "_stockBought");
                var eaten = Get<int[]>(cmLedger, "_stockEaten");
                int ownN = 0, stockN = 0;
                for (int i = 0; i < cmProducts.Length; i++)
                {
                    string key = CmStr(cmProducts[i], "productKey");
                    bool fileOwn = CmFileTrue(snap, ns + ".Own." + key);
                    CmExpect(owned[i] == fileOwn && ownValid[i], "Own." + key + " restored (" + owned[i] + " vs file " + fileOwn + ")");
                    if (fileOwn) ownN++;
                    if (snap.ContainsKey(ns + ".StockB." + key))
                    {
                        CmExpect(bought[i] == CmFileInt(snap, ns + ".StockB." + key) && eaten[i] == CmFileIntOr0(snap, ns + ".StockE." + key), key + " stock restored (" + bought[i] + "/" + eaten[i] + ")");
                        stockN++;
                    }
                }
                CmExpect(ownN >= 1 && CmOwned(CmFxPawCookie), "the FX bought in 96 is owned");
                CmExpect(CmSeeded(CmBiscuit) && CmStockB(CmBiscuit) == CmFileInt(snap, ns + ".StockB." + CmBiscuit), "initial biscuits not granted twice");
                CmFreezeStay(true);
                CmNote("installed " + report.commerceSource + " and entered Play (ClientSim re-entry): earned " + CmEarned() + " = " + fileEarned + " + " + CmGranted() + " min, spent " + CmSpent() + ", owned " + ownN + ", stocked rows " + stockN + " as saved");
                CmNote("simulated: stay accrual frozen after this check (minuteSeconds=1e6) so R1 compares exact totals");
            }));

            s = CmCase("R1-keeper-no-regrant", false, 40);
            int before = 0;
            s.Add(CmDo(CmToKeeper));
            s.Add(CmWait(1));
            s.Add(CmDo(() => { before = CmEarned(); cmKeeper.Interact(); cmKeeper.Interact(); cmKeeper.Interact(); }));
            s.Add(CmWait(1.5));
            s.Add(CmDo(() =>
            {
                CmExpect(CmEarned() == before && !Get<bool>(cmKeeper, "_giftPending"), "no second 200 after re-entry (earned " + CmEarned() + " vs " + before + ")");
                CmExpect(CmStr(cmKeeper, "_topicBody") == "KD_ShopKeeper.WordGreeting", "plain greeting");
                cmKeeper.SendCustomEvent("_Close");
                CmNote("re-entry: 3 talks in one frame -> +0, plain greeting (T-007 across a re-entry, ClientSim level)");
            }));

            s = CmCase("R2-owned-in-menu", false, 30);
            s.Add(CmDo(() =>
            {
                CmMenu("_Tab5");
                int row = CmFxRow(CmFxPawCookie), sp = CmSpent();
                pouch.SendCustomEvent("_Fx" + row);
                CmExpect(Get<int>(pouch, "_fxConfirmRow") < 0 && CmSpent() == sp && CmFeedback() == "KD_TreatPouch.WordApplied", "the restored FX equips without a card or payment ('" + CmFeedback() + "')");
                CmNote("menu reads the restored ownership: the FX row equips (no purchase card)");
            }));

            s = CmCase("R3-menu-balance", false, 30);
            s.Add(CmDo(() => CmMenu("_Tab0")));
            s.Add(CmUntil(() => CmText(pouch, "balanceLabel").Contains(" " + CmPoints() + " pt"), 2, () => "header balance '" + CmText(pouch, "balanceLabel") + "' does not show " + CmPoints() + " pt"));
            s.Add(CmDo(() => { CmNote("header: '" + CmText(pouch, "balanceLabel") + "'"); pouch.SendCustomEvent("_Close"); }));
        }
    }
}
