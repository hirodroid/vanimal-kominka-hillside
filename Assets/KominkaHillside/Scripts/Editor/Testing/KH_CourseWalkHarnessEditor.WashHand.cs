using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-039 手洗い (シャンプーのボトル + 素手) — Course/100g (群 hand)。設計は docs/plans/2026-10-03-hand-wash.md §8.3 (§13.8 が優先)。
    ///
    /// 枠・部品は Wash.cs (WsCase / WsDo / WsWait / WsUntil / WsDrive / WsMask / WsLocal ...)、犬を主洗い場へ呼んで留めるのは WashCall.cs
    /// (WcSetup / WcCallAndArrive / WcPlayerAtStation)。犬は柴。各ケースは最初に「柴が主洗い場に留められ着いている」ことを確かめ、違えば呼び直す。
    ///
    /// 疑似入力 (どのケースも notes に書く):
    ///   素手 = KH_HandWash のハーネス専用フィールドをヒープへ毎 tick 書く (debugHands 1・debugLeft / debugRight・debugLeftFree / debugRightFree・
    ///          debugVr)。手の位置は柴の部位 Collider の表面の点 (上 / 右からの Raycast の当たり) + 法線方向へ 0.3 × handContactRadius。
    ///          KH_HandWash は自分のループ (_HandIdle / _HandFrame) のまま動き、_HandContact を呼ぶのは本番の経路。
    ///          本物の追跡・アバターの手の大きさ・IK の遅れ・GetPickupInHand・振動・Quest のハンドトラッキングは通らない。
    ///   ボトル = KH_WashTool._DebugSquirt(origin, direction) (Use の後と同じ狙い = SphereCast bottleAimRadius・bottleReach、1 回分)。Pickup と Use は通らない。
    ///   ClientSim は 1 クライアント: NetStroke は自分のエコー (Owner = 自分) だけ。結果は「単体の判定」か「1 人の端末の通し」で、
    ///   実通信・2 人・実機の合格ではない。
    /// 状態は _DebugClear と、ケースによってはヒープへの書き込み (セルの泡の上限・maxBlitsPerFrame・言語の番号など。notes に明記し、ケースの終わり
    /// と失敗時の WsRecover → HwRecover で戻す)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        const string HwStationName = "KH_WashStation", HwBottleName = "KH_WashBottle_0", HwHandName = "HandWash", HwGuideName = "Guide", HwBoardName = "Board";
        const int HwChBottle = 3, HwChL = 4, HwChR = 5;   // KH_DogWash channels (bottle, left hand, right hand)
        const float HwRubSpeed = 0.3f;                     // m/s along the surface (design §8.3 H-hand-nofoam)

        static UdonBehaviour hwHand, hwBottle, hwBoard, hwLang;
        static Transform hwStation, hwGuide;
        static bool hwActive, hwVr = true, hwLFree = true, hwRFree = true;
        static Func<Vector3> hwL, hwR;
        static int hwMaxBlits0 = -1, hwLang0 = -1, hwWasher0 = int.MinValue;
        static int hwRiver0 = -1;   // -1 = not changed, else 0 / 1
        static float hwGap = 0.02f;
        static bool hwSendHeld;              // round 2: _nextSend held (H-hand-local-inert B, H-queue-protect)
        static Transform hwMovedCol;         // round 2: a part collider moved by H-hand-still-on-moving
        static Vector3 hwMovedBase;
        // tuning, read from the scene (HwBind)
        static float hwHandR, hwHandMin, hwStart, hwBottleR, hwBottleRate, hwCellReach, hwMaxSeg, hwVerifyR, hwReachM, hwContactR, hwNearM, hwSquirtSec, hwBottleReach, hwMinSpeed, hwMaxSpeed;
        static readonly Dictionary<string, string> hwSyms = new Dictionary<string, string>();
        static readonly Dictionary<string, string[]> hwParams = new Dictionary<string, string[]>();

        struct HwDose { public int ci, part; public Vector3 mesh; }

        static void MakeWashHandCases()
        {
            hwSyms.Clear(); hwParams.Clear(); hwActive = false; hwL = hwR = null; hwMaxBlits0 = -1; hwLang0 = -1; hwWasher0 = int.MinValue; hwRiver0 = -1;
            var n = wsReport.notCovered;
            n.Add("100g hands: real tracked hands (GetTrackingData), GetPickupInHand (the free-hand test), avatar hand size / palm offset, IK lag, the hand haptic, Quest hand tracking -> PCVR / Quest (the harness writes KH_HandWash.debug* every tick)");
            n.Add("100g H-hand-pet (dropped, design §13.8): lathering and petting the head together - the simulated hands never reach KD_DogPetController -> Play");
            n.Add("100g other clients: another player's hand strokes accepted / refused by the Owner (HandOwnerOk with the sender's real IK hands, 0.5 m), the non-Owner's synced checks, the visual-only extra foam when the Owner refuses, late join / Owner handover with hand strokes -> 2 people on PCVR (H-hand-reject is an Owner-side predicate unit check only)");
            n.Add("100g bottle: the pickup, Use down / hold-repeat (bottleRepeatSeconds), the nozzle aim from a real hand, the particles' look and the one-shot haptic -> Play / PCVR (the harness calls _DebugSquirt)");
            n.Add("100g ring-only lathering on a NON-Owner (its cells wait for the Owner's sync): ClientSim is the Owner, so H-hand-local-inert (B) holds its own send slot (_nextSend) to keep the dose out of the cells - the same local state, not the remote timing -> 2 people on PCVR");
            n.Add("100g guide: the picture's legibility, the 14 pt fit / glyphs in the 11 other languages, day / night, seated view (Wash/12 and the user)");

            // ---- H-bottle: one dose on the back ----
            var s = WsCase("H-bottle", 200);
            HwBegin(s, false);
            WsAnchor bB = default; HwDose bD = default; int bSeq0 = 0; float[] bF0 = null; float bSum0 = 0f;
            s.Add(WsDo(() => { bB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)"); wsWash.SendCustomEvent("_DebugClear"); }));
            s.Add(WsWait(1.2));
            HwWaitNoHold(s);
            s.Add(WsDo(() => { hwTrace.Clear(); wsFailDump = HwTraceDump; }));   // round 2: the full trace only on a FAIL (dump), a summary on PASS
            // 2026-10-03 diagnostic (run 20261003-075811: the dose reached the cells but the mask read 0 at +0.8 s). Control first: one
            // sponge dab through the same path as the bottle (_LocalContact(0, ci, point, 0.25, -1) + _EndContact(0), same frame, same
            // point) on the idle, clean dog, traced on the same schedule. The trace lines are kept in hwTrace: all of them go into the failure dump,
            // the PASS note carries one short line per sample
            Vector3 bCtlMesh = Vector3.zero; int bCtlPart = -1;
            HwTraced(s, "TRACE sponge-control", () =>
            {
                var h = WsAt(bB); bCtlMesh = WsToMesh(h.ci, h.p); bCtlPart = wsColPart[h.ci];
                WsContact(WsSponge, h.ci, h.p, 0.25f, -1f);
                WsEnd(WsSponge);
            }, () => bCtlMesh, () => bCtlPart);
            s.Add(WsDo(() => wsWash.SendCustomEvent("_DebugClear")));
            s.Add(WsWait(1.2));
            HwWaitNoHold(s);   // the control set the hold
            HwTraced(s, "TRACE bottle", () => { bF0 = HwFoam(); bSum0 = bF0.Sum(); bSeq0 = HwInt("_seq"); bD = HwSquirt(bB); }, () => bD.mesh, () => bD.part);   // ends at +0.8 s
            s.Add(WsDo(() =>
            {
                var f1 = HwFoam(); var m1 = WsMask();
                int up = HwCountUp(bF0, f1, 1e-4f), seq = HwInt("_seq") - bSeq0;
                float at = HwMaskAt(m1, bD.mesh, bD.part), expect = hwBottleRate * hwSquirtSec, thr = Mathf.Min(0.5f, 0.7f * expect);
                float cmax = HwCellMax(f1, bD.mesh, hwBottleR + hwCellReach);
                bool hold = WsHold();
                WsExpect(up > 0 && f1.Sum() > bSum0, "the Owner's cells gained foam from the dose (" + up + " cells up, sum " + bSum0.ToString("F3") + " -> " + f1.Sum().ToString("F3") + ")");
                WsExpect(at >= thr, "mask foam at the hit point >= " + thr.ToString("F2") + " (0.7 x bottleFoamRate " + hwBottleRate + " x bottleSquirtSeconds " + hwSquirtSec + "; got " + at.ToString("F3") + ")");
                WsExpect(hold, "the wash hold is set by the dose (_WashHold)");
                WsExpect(seq == 1, "exactly one NetStroke sent for the dose (_seq +" + seq + ")");
                WsNote("simulated: KH_WashTool._DebugSquirt from " + (0.4f * hwBottleReach).ToString("F2") + " m above the back, cast down (no pickup / Use). Dose on part " + bD.part + " (" + wsCols[bD.ci].name + "): mask G at the point " + at.ToString("F3") + " (expected ~" + expect.ToString("F2") + "), cells up " + up + ", max cell near " + cmax.ToString("F3") + ", cell sum " + bSum0.ToString("F3") + " -> " + f1.Sum().ToString("F3") + ", hold " + hold + ", _seq +" + seq + " (own echo integrated by the Owner = me)");
                WsNote("trace summary (_cur hit6 / max, restores): " + string.Join("; ", hwTrace.Select(x => x.Value)));
            }));
            HwEnd(s);
            // Course/100g1 (group "hand-bottle"): H-bottle only, for iterating on it
            if (wsGroups != null && Array.IndexOf(wsGroups, "hand") < 0) { n.Add("Partial run 100g1: H-bottle only (the other 100g cases were not run)"); return; }

            // ---- H-hand-nofoam: rubbing clean fur does nothing ----
            s = WsCase("H-hand-nofoam", 200);
            HwBegin(s, true);
            List<WsAnchor> nfLine = null; float[] nfD = null, nfF = null, nfW = null; Color[] nfM = null; int nfIss = 0, nfSeq = 0, nfCc = 0;
            s.Add(WsDo(() => { nfLine = HwTopLine(WsTorsoCenter(), 0.15f, 7, p => true, "the back"); wsWash.SendCustomEvent("_DebugClear"); }));
            s.Add(WsWait(1.2));
            HwWaitNoHold(s);
            s.Add(WsDo(() =>
            {
                nfD = (float[])Get<float[]>(wsWash, "_dirt").Clone(); nfF = HwFoam(); nfW = (float[])Get<float[]>(wsWash, "_wet").Clone(); nfM = WsMask();
                nfIss = HwInt("handStrokesIssued"); nfSeq = HwInt("_seq"); nfCc = Get<int>(hwHand, "contactCalls");
                hwL = HwRub(nfLine, HwUp, HwRubSpeed); HwPush();
            }));
            s.Add(WsWait(2.0));
            s.Add(WsDo(() => { hwL = null; HwPush(); }));
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                int cc = Get<int>(hwHand, "contactCalls") - nfCc, iss = HwInt("handStrokesIssued") - nfIss, seq = HwInt("_seq") - nfSeq, cells = WsCellDiff(nfD, nfF, nfW);
                float md = WsMaxDiff(nfM, WsMask()); bool hold = WsHold();
                WsExpect(cc >= 20, "control: the hand touched the back (_HandContact calls " + cc + " in 2 s)");
                WsExpect(iss == 0, "no lather stroke issued (handStrokesIssued +" + iss + ")");
                WsExpect(seq == 0, "nothing sent (_seq +" + seq + ")");
                WsExpect(cells == 0, "cells unchanged (" + cells + " cells changed)");
                WsExpect(md < 1e-3f, "mask unchanged (max diff " + md.ToString("F4") + ")");
                WsExpect(!hold, "no wash hold");
                WsNote("simulated: left hand (debugLeft) rubbing the clean back along " + HwLen(nfLine).ToString("F2") + " m at " + HwRubSpeed + " m/s for 2 s (debugVr, debugHands 1): " + cc + " contacts, issued +" + iss + ", _seq +" + seq + ", cells changed " + cells + ", mask max diff " + md.ToString("F4") + ", hold " + hold);
            }));
            HwEnd(s);

            // ---- H-hand-lather: after the bottle, rubbing over the blob and 10 cm beyond spreads it ----
            s = WsCase("H-hand-lather", 200);
            HwBegin(s, true);
            WsAnchor laB = default; List<WsAnchor> laLine = null; HwDose laD = default; float[] laF0 = null; Color[] laM0 = null; int laIss = 0; float laHalf = 0f, laAvg0 = 0f;
            s.Add(WsDo(() =>
            {
                laB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)");
                laHalf = hwBottleR + 0.10f;
                laLine = HwTopLine(WsAt(laB).p, laHalf, 9, p => true, "the back");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => laD = HwSquirt(laB)));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                laF0 = HwFoam(); laM0 = WsMask(); laIss = HwInt("handStrokesIssued"); laAvg0 = WsAvgFoam();
                hwL = HwRub(laLine, HwUp, HwRubSpeed); HwPush();
            }));
            s.Add(WsWait(4.0));
            s.Add(WsDo(() => { hwL = null; HwPush(); }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                var f1 = HwFoam(); var m1 = WsMask();
                var meshLine = HwMesh(laLine);
                int n0, n1; float band = hwHandR;
                float mean0 = HwMaskMean(laM0, meshLine, band, out n0), mean1 = HwMaskMean(m1, meshLine, band, out n1);
                int c0 = HwCountAbove(laF0, hwHandMin), c1 = HwCountAbove(f1, hwHandMin);
                float blobR = hwBottleR + hwCellReach; int outside = 0;
                var cc = Get<Vector3[]>(wsWash, "cellCenter"); var valid = Get<bool[]>(wsWash, "cellValid");
                for (int c = 0; c < f1.Length && c < valid.Length; c++)
                    if (valid[c] && laF0[c] < 0.05f && f1[c] >= 0.05f && (cc[c] - laD.mesh).magnitude * wsMts > blobR) outside++;
                int iss = HwInt("handStrokesIssued") - laIss;
                WsExpect(iss > 0, "lather strokes were issued (handStrokesIssued +" + iss + ")");
                WsExpect(mean1 > mean0 + 0.02f, "mask foam along the rubbed band grew (mean G " + mean0.ToString("F3") + " -> " + mean1.ToString("F3") + " over " + n1 + " texels within " + band.ToString("F2") + " m of the path)");
                WsExpect(c1 > c0, "more cells with foam >= handFoamMin " + hwHandMin + " (" + c0 + " -> " + c1 + ")");
                WsExpect(outside >= 1, "cells beyond the blob's reach (bottleRadius + cellReach = " + blobR.ToString("F2") + " m from the dose) got foam (" + outside + ")");
                WsNote("simulated: dose (_DebugSquirt) then the left hand rubbing back and forth " + HwLen(laLine).ToString("F2") + " m (the blob +-" + laHalf.ToString("F2") + " m) at " + HwRubSpeed + " m/s for 4 s: issued +" + iss + "; band mean G " + mean0.ToString("F3") + " -> " + mean1.ToString("F3") + "; cells >= " + hwHandMin + ": " + c0 + " -> " + c1 + "; newly foamed cells beyond " + blobR.ToString("F2") + " m: " + outside + "; cell sum " + laF0.Sum().ToString("F2") + " -> " + f1.Sum().ToString("F2") + "; _AverageFoam " + laAvg0.ToString("F3") + " -> " + WsAvgFoam().ToString("F3"));
            }));
            HwEnd(s);

            // ---- H-hand-rest: a hand resting on the foam (speed 0) does nothing ----
            s = WsCase("H-hand-rest", 200);
            HwBegin(s, true);
            WsAnchor reB = default; HwDose reD = default; float[] reF0 = null; Color[] reM0 = null; int reIss = 0, reCc = 0;
            s.Add(WsDo(() => { reB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)"); wsWash.SendCustomEvent("_DebugClear"); }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => reD = HwSquirt(reB)));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() => { reF0 = HwFoam(); reM0 = WsMask(); reIss = HwInt("handStrokesIssued"); reCc = Get<int>(hwHand, "contactCalls"); hwL = HwFixed(reB, HwUp); HwPush(); }));
            s.Add(WsWait(2.0));
            s.Add(WsDo(() => { hwL = null; HwPush(); }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                var f1 = HwFoam(); var m1 = WsMask();
                int cc = Get<int>(hwHand, "contactCalls") - reCc, iss = HwInt("handStrokesIssued") - reIss, up = HwCountUp(reF0, f1, 1e-4f);
                float mup = HwMaxUp(reM0, m1, 1);
                WsExpect(cc >= 20, "control: the hand touched the foam (" + cc + " contacts)");
                WsExpect(iss == 0, "no lather stroke from a resting hand (issued +" + iss + ")");
                WsExpect(up == 0, "no cell gained foam (" + up + ")");
                WsExpect(mup < 0.01f, "no texel gained foam (max dG +" + mup.ToString("F4") + ")");
                WsNote("simulated: dose, then the left hand held still on the dose point (" + wsCols[reD.ci].name + ") for 2 s: " + cc + " contacts, issued +" + iss + ", cells up " + up + ", mask max dG +" + mup.ToString("F4") + " (handMinSpeed " + hwMinSpeed + " m/s)");
            }));
            HwEnd(s);

            // ---- H-hand-rinsed: dose -> shower -> rub the rinsed centre: no foam comes back ----
            s = WsCase("H-hand-rinsed", 200);
            HwBegin(s, true);
            const float RiRad = 0.11f;   // the shower's real range at about 1 m (W-clean)
            WsAnchor rnB = default; List<WsAnchor> rnLine = null; HwDose rnD = default; int rnIss = 0, rnInert = 0; float rnCell1 = 0f, rnMask1 = 0f;
            s.Add(WsDo(() =>
            {
                rnB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)");
                rnLine = HwTopLine(WsAt(rnB).p, 0.03f, 3, p => true, "the back");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => rnD = HwSquirt(rnB)));
            s.Add(WsWait(0.8));
            s.Add(WsDrive(1.5f, 1, u => WsAt(rnB), WsShower, RiRad, null));
            s.Add(WsDo(() => WsEnd(WsShower)));
            s.Add(WsWait(wsRunoffSeconds + 0.8));
            s.Add(WsDo(() =>
            {
                var f = HwFoam(); rnCell1 = HwCellMax(f, rnD.mesh, hwHandR + hwCellReach); rnMask1 = HwMaskAt(WsMask(), rnD.mesh, rnD.part);
                WsExpect(rnCell1 < hwStart && rnMask1 < hwStart, "precondition: the shower rinsed the blob (cells near " + rnCell1.ToString("F3") + ", mask " + rnMask1.ToString("F3") + " < handStartFoam " + hwStart + ")");
                rnIss = HwInt("handStrokesIssued"); rnInert = HwInt("handStrokesInert");
                hwL = HwRub(rnLine, HwUp, HwRubSpeed); HwPush();
            }));
            s.Add(WsWait(2.0));
            s.Add(WsDo(() => { hwL = null; HwPush(); }));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                var f = HwFoam(); float cell = HwCellMax(f, rnD.mesh, hwHandR + hwCellReach), mask = HwMaskAt(WsMask(), rnD.mesh, rnD.part);
                int near = HwNearestCell(rnD.mesh); float nearF = near >= 0 ? f[near] : -1f;
                int iss = HwInt("handStrokesIssued") - rnIss, inert = HwInt("handStrokesInert") - rnInert;
                WsExpect(nearF < hwStart, "the rinsed centre's cell stays below " + hwStart + " (" + nearF.ToString("F3") + ")");
                WsExpect(cell < hwStart, "every cell within the hand's reach (" + (hwHandR + hwCellReach).ToString("F2") + " m) stays below " + hwStart + " (max " + cell.ToString("F3") + ")");
                WsExpect(mask < hwStart, "the mask at the rinsed centre stays below " + hwStart + " (" + mask.ToString("F3") + ")");
                WsNote("simulated: dose, shower r " + RiRad + " m 1.5 s on the dose point, runoff over, then the left hand rubbing +-0.03 m on the rinsed centre for 2 s: centre cell " + nearF.ToString("F3") + ", max near " + rnCell1.ToString("F3") + " -> " + cell.ToString("F3") + ", mask " + rnMask1.ToString("F3") + " -> " + mask.ToString("F3") + "; hand strokes issued +" + iss + " (sender gate), Owner inert +" + inert);
            }));
            HwEnd(s);

            // ---- H-hand-two: left on the head, right on the hip at once; the back between is untouched; lifting the right closes only channel 5 ----
            s = WsCase("H-hand-two", 220);
            HwBegin(s, true);
            WsAnchor twH = default, twP = default, twMid = default; List<WsAnchor> twHL = null, twPL = null; HwDose twDH = default, twDP = default;
            float[] twF0 = null; Color[] twM0 = null; int twIss1 = 0; string twCh = "";
            s.Add(WsDo(() =>
            {
                twH = HwTop(HwPartCenter(7), p => p == 7, "the head (part 7)");
                bool fh, fc; Vector3 hip = WsPartCenter(0, out fh), chest = WsPartCenter(1, out fc);
                twP = HwTop(Vector3.Lerp(hip, chest, 0.3f), p => p == 0, "the hip (part 0)");
                twHL = HwTopLine(WsAt(twH).p, 0.03f, 3, p => p == 7, "the head");
                twPL = HwTopLine(WsAt(twP).p, 0.04f, 3, p => p == 0, "the hip");
                twMid = HwTop(Vector3.Lerp(WsAt(twH).p, WsAt(twP).p, 0.5f), p => true, "the back between the head and the hip");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { twDH = HwSquirt(twH); twDP = HwSquirt(twP); }));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() => { twF0 = HwFoam(); twM0 = WsMask(); hwL = HwRub(twHL, HwUp, HwRubSpeed * 0.8f); hwR = HwRub(twPL, HwUp, HwRubSpeed); HwPush(); }));
            s.Add(WsWait(2.0));
            s.Add(WsDo(() => { hwR = null; HwPush(); }));   // lift the right hand (out of reach)
            s.Add(WsWait(0.4));
            s.Add(WsDo(() =>
            {
                var hv = Get<bool[]>(wsWash, "_hValid"); var so = Get<bool[]>(wsWash, "_segOpen");
                twCh = "after lifting the right: _hValid[4] " + hv[HwChL] + ", [5] " + hv[HwChR] + "; _segOpen[5] " + so[HwChR];
                WsExpect(hv.Length > HwChR && !hv[HwChR] && !so[HwChR], "the right hand's channel 5 closed (" + twCh + ")");
                WsExpect(hv[HwChL], "the left hand's channel 4 still open (" + twCh + ")");
                twIss1 = HwInt("handStrokesIssued");
            }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                int more = HwInt("handStrokesIssued") - twIss1; var hv = Get<bool[]>(wsWash, "_hValid");
                WsExpect(more > 0 && hv[HwChL], "the left hand kept lathering alone (issued +" + more + " in 1 s, _hValid[4] " + hv[HwChL] + ")");
                twCh += "; left alone +" + more + " strokes in 1 s";
                hwL = null; HwPush();
            }));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                var f1 = HwFoam(); var m1 = WsMask();
                float reachC = hwHandR + hwCellReach;
                float h0 = HwCellMax(twF0, twDH.mesh, reachC), h1 = HwCellMax(f1, twDH.mesh, reachC), p0 = HwCellMax(twF0, twDP.mesh, reachC), p1 = HwCellMax(f1, twDP.mesh, reachC);
                var hm = HwMesh(twHL); var pm = HwMesh(twPL); var both = hm.Concat(pm).ToList();
                float farC = 0.04f + hwHandR + hwCellReach + 0.02f;   // rub half-length + the hand's cell reach + margin
                var far = HwCellsFar(both, farC); int grew = 0; float worst = 0f;
                foreach (int c in far) { float d = f1[c] - twF0[c]; if (d > 1e-3f) grew++; worst = Mathf.Max(worst, d); }
                var mh = WsAt(twMid); var mid = WsToMesh(mh.ci, mh.p); int midPart = wsColPart[mh.ci];
                float midDist = WsMinDist(mid, both) * wsMts, farM = 0.04f + hwHandR * (1f + wsEdgeNoise) + 2f * wsTexelHiM + 0.02f;
                float dm, midG = WsLocal(mid, midPart, twM0, m1, 1, out dm);
                int midCell = HwNearestCell(mid);
                WsExpect(h1 > h0 + 0.02f, "the head's cells gained foam (max " + h0.ToString("F3") + " -> " + h1.ToString("F3") + ")");
                WsExpect(p1 > p0 + 0.02f, "the hip's cells gained foam (max " + p0.ToString("F3") + " -> " + p1.ToString("F3") + ")");
                WsExpect(far.Count > 0 && grew == 0, "no cell farther than " + farC.ToString("F2") + " m from both rubbed lines gained foam (" + grew + " of " + far.Count + ", worst +" + worst.ToString("F4") + ")");
                if (midDist > farM) WsExpect(midG < 0.01f, "the mask at the back between them did not gain foam (dG " + midG.ToString("F4") + ", " + midDist.ToString("F2") + " m from the rubbed lines)");
                WsNote("simulated: doses on the head (" + wsCols[twDH.ci].name + ") and the hip (" + wsCols[twDP.ci].name + "); left hand rubbing the head +-0.03 m and right hand the hip +-0.04 m for 2 s at once: head cells " + h0.ToString("F3") + " -> " + h1.ToString("F3") + ", hip cells " + p0.ToString("F3") + " -> " + p1.ToString("F3") + "; " + far.Count + " cells beyond " + farC.ToString("F2") + " m from both: " + grew + " grew (worst +" + worst.ToString("F4") + "); back between (part " + midPart + ", " + midDist.ToString("F2") + " m from the lines, cell " + midCell + " " + (midCell >= 0 ? twF0[midCell].ToString("F3") + " -> " + f1[midCell].ToString("F3") : "-") + ") mask dG " + midG.ToString("F4") + (midDist > farM ? "" : " (not asserted: within " + farM.ToString("F2") + " m)") + "; " + twCh);
            }));
            HwEnd(s);

            // ---- H-hand-samepart: two hands far apart on the same part while draws wait: two capsules, never one across ----
            s = WsCase("H-hand-samepart", 220);
            HwBegin(s, true);
            int spPart = -1; float spU1 = 0f, spU2 = 0f, spSep = 0f; List<WsAnchor> spL1 = null, spL2 = null; Vector3 spM1 = Vector3.zero, spM2 = Vector3.zero, spMid = Vector3.zero; int spMidPart = -1;
            Color[] spM0 = null; int spSnaps = 0, spBoth = 0, spMax = 0, spSeen = 0, spIss = 0; float spLongest = 0f;
            s.Add(WsDo(() =>
            {
                HwSamePart(out spPart, out spU1, out spU2, out spSep);
                Vector3 a, b; float len = WsPathEnds(out a, out b); float du = 0.04f / Mathf.Max(0.05f, len);
                spL1 = new List<WsAnchor> { HwPathAnchor(spU1), HwPathAnchor(Mathf.Min(spU1 + du, spU2)) };
                spL2 = new List<WsAnchor> { HwPathAnchor(Mathf.Max(spU2 - du, spU1)), HwPathAnchor(spU2) };
                var h1 = WsPath(spU1); var h2 = WsPath(spU2); var hm = WsPath(0.5f * (spU1 + spU2));
                spM1 = WsToMesh(h1.ci, h1.p); spM2 = WsToMesh(h2.ci, h2.p);
                if (hm.ci >= 0) { spMid = WsToMesh(hm.ci, hm.p); spMidPart = wsColPart[hm.ci]; }
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            // light foam along the whole stretch, so a capsule drawn across it would show at the midpoint (the lather gate is open there)
            s.Add(WsDrive(0.6f, 1, u => WsPath(Mathf.Lerp(spU1, spU2, u)), WsSponge, -1f, null));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                spM0 = WsMask(); spIss = HwInt("handStrokesIssued");
                hwL = HwRub(spL1, HwRight, HwRubSpeed); hwR = HwRub(spL2, HwRight, HwRubSpeed); HwPush();
            }));
            s.Add(WsWait(0.4));
            s.Add(WsDo(() => { hwMaxBlits0 = Get<int>(wsWash, "maxBlitsPerFrame"); wsWash.SetProgramVariable<int>("maxBlitsPerFrame", 0); }));   // draws pile up
            s.Add(now =>
            {
                int cnt = HwInt("_pdCount"); var tl = Get<int[]>(wsWash, "_pdTool"); var pa = Get<Vector3[]>(wsWash, "_pdA"); var pb = Get<Vector3[]>(wsWash, "_pdB");
                int n1 = 0, n2 = 0;
                for (int i = 0; i < cnt && i < tl.Length; i++)
                {
                    if (tl[i] != KH_DogWash.ToolHand) continue;
                    int ea = (pa[i] - spM1).sqrMagnitude <= (pa[i] - spM2).sqrMagnitude ? 1 : 2, eb = (pb[i] - spM1).sqrMagnitude <= (pb[i] - spM2).sqrMagnitude ? 1 : 2;
                    float l = (pb[i] - pa[i]).magnitude * wsMts; spLongest = Mathf.Max(spLongest, l);
                    if (ea != eb) throw new InvalidOperationException("a waiting hand draw runs from one hand's point to the other's (entry " + i + ", " + l.ToString("F3") + " m): the two hands were merged into one capsule");
                    if (l > hwMaxSeg) throw new InvalidOperationException("a waiting hand draw is " + l.ToString("F3") + " m long (> handMaxSegment " + hwMaxSeg + ")");
                    if (ea == 1) n1++; else n2++;
                }
                spSnaps++; spSeen += n1 + n2; spMax = Mathf.Max(spMax, n1 + n2); if (n1 > 0 && n2 > 0) spBoth++;
                return now - wsStepAt >= 1.0;
            });
            s.Add(WsDo(() => { wsWash.SetProgramVariable<int>("maxBlitsPerFrame", hwMaxBlits0); hwMaxBlits0 = -1; }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() => { hwL = hwR = null; HwPush(); }));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                var m1 = WsMask(); float dm;
                int iss = HwInt("handStrokesIssued") - spIss, left = HwInt("_pdCount");
                float g1 = WsLocal(spM1, spPart, spM0, m1, 1, out dm), g2 = WsLocal(spM2, spPart, spM0, m1, 1, out dm);
                WsExpect(iss > 0, "both hands lathered (issued +" + iss + ")");
                WsExpect(spBoth > 0 && spMax >= 2, "hand draws of both hands waited at the same time (snapshots with both " + spBoth + " of " + spSnaps + ", max waiting " + spMax + ")");
                WsExpect(left == 0, "the waiting draws were drawn after maxBlitsPerFrame came back (" + left + " left)");
                string midText;
                float farM = 0.04f + hwHandR * (1f + wsEdgeNoise) + 2f * wsTexelHiM + 0.02f;
                if (spMidPart >= 0)
                {
                    float mg = WsLocal(spMid, spMidPart, spM0, m1, 1, out dm), before = WsLocal(spMid, spMidPart, null, spM0, 1, out dm);
                    float d1 = (spMid - spM1).magnitude * wsMts, d2 = (spMid - spM2).magnitude * wsMts;
                    bool asserted = d1 > farM && d2 > farM && before >= hwHandMin + 0.1f;
                    if (asserted) WsExpect(mg < 0.01f, "the mask at the midpoint between the hands did not gain foam (dG " + mg.ToString("F4") + ", foam before " + before.ToString("F3") + ")");
                    midText = "midpoint (" + d1.ToString("F2") + " / " + d2.ToString("F2") + " m from the hands, foam before " + before.ToString("F3") + ") dG " + mg.ToString("F4") + (asserted ? "" : " (not asserted: closer than " + farM.ToString("F2") + " m or no foam to gate)");
                }
                else midText = "midpoint ray missed (mask not asserted)";
                WsNote("simulated: left and right hands rubbing +-0.02 m on part " + spPart + " " + spSep.ToString("F2") + " m apart on the right flank (path u " + spU1.ToString("F2") + " / " + spU2.ToString("F2") + ") after a light sponge along the stretch; maxBlitsPerFrame 0 for 1 s (heap, restored): " + spSnaps + " snapshots, " + spBoth + " with both hands waiting, max " + spMax + " waiting hand draws, " + spSeen + " seen, longest " + spLongest.ToString("F3") + " m, none across; ends dG " + g1.ToString("F3") + " / " + g2.ToString("F3") + "; " + midText + "; issued +" + iss);
            }));
            HwEnd(s);

            // ---- H-hand-holding: hands that are not free do nothing ----
            s = WsCase("H-hand-holding", 200);
            HwBegin(s, true);
            WsAnchor hoB = default; List<WsAnchor> hoLine = null; float[] hoF0 = null; int hoIss = 0, hoCc = 0;
            s.Add(WsDo(() =>
            {
                hoB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)");
                hoLine = HwTopLine(WsAt(hoB).p, 0.05f, 3, p => true, "the back");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => HwSquirt(hoB)));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                hoF0 = HwFoam(); hoIss = HwInt("handStrokesIssued"); hoCc = Get<int>(hwHand, "contactCalls");
                hwLFree = false; hwRFree = false; hwL = HwRub(hoLine, HwUp, HwRubSpeed); hwR = HwRub(hoLine, HwUp, HwRubSpeed * 0.7f); HwPush();
            }));
            s.Add(WsWait(1.5));
            int hoCcHeld = 0, hoIssHeld = 0, hoUp = 0;
            s.Add(WsDo(() =>
            {
                hoCcHeld = Get<int>(hwHand, "contactCalls") - hoCc; hoIssHeld = HwInt("handStrokesIssued") - hoIss; hoUp = HwCountUp(hoF0, HwFoam(), 1e-4f);
                WsExpect(hoCcHeld == 0, "no contact from hands that hold something (_HandContact calls +" + hoCcHeld + ")");
                WsExpect(hoIssHeld == 0, "no lather stroke (issued +" + hoIssHeld + ")");
                WsExpect(hoUp == 0, "no cell gained foam (" + hoUp + ")");
                hwLFree = true; hwR = null; HwPush();   // control: the left hand free on the same foam
                hoCc = Get<int>(hwHand, "contactCalls"); hoIss = HwInt("handStrokesIssued");
            }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                int cc = Get<int>(hwHand, "contactCalls") - hoCc, iss = HwInt("handStrokesIssued") - hoIss;
                WsExpect(cc > 0, "control: the same hand, free, touches (" + cc + " contacts)");
                WsNote("simulated: dose, then both hands rubbing the foam with debugLeftFree / debugRightFree false (GetPickupInHand not null) for 1.5 s: contacts +" + hoCcHeld + ", issued +" + hoIssHeld + ", cells up " + hoUp + "; control, left free 1 s: contacts +" + cc + ", issued +" + iss);
            }));
            HwEnd(s);

            // ---- H-hand-inert: a hand NetStroke on clean fur changes nothing but the order ----
            s = WsCase("H-hand-inert", 200);
            HwBegin(s, false);
            WsAnchor inB = default; Color[] inM = null; float[] inD = null, inF = null, inW = null;
            int inMs = 0, inBy = 0, inSerial = 0, inInert = 0, inRej = 0, inSeq = 0, inMe = 0; bool inSeen = false, inArmed = false; float inAge0 = 0f; double inAt = 0;
            s.Add(WsDo(() => { inB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)"); wsWash.SendCustomEvent("_DebugClear"); }));
            s.Add(WsWait(1.5));
            s.Add(WsDo(() =>
            {
                inMe = Networking.LocalPlayer.playerId;
                inM = WsMask(); inD = (float[])Get<float[]>(wsWash, "_dirt").Clone(); inF = HwFoam(); inW = (float[])Get<float[]>(wsWash, "_wet").Clone();
                inMs = HwInt("washMs"); inSeen = Get<bool>(wsWash, "washSeen"); inBy = HwInt("washBy"); inSerial = WsSerial(); inArmed = Get<bool>(wsWash, "cleanArmed");
                inInert = HwInt("handStrokesInert"); inRej = HwInt("handStrokesRejected");
                // markers the stroke must not touch (simulated, restored at the end / by HwRecover)
                hwWasher0 = HwInt("_lastWasherId"); wsWash.SetProgramVariable<int>("_lastWasherId", -1);
                hwRiver0 = Get<bool>(wsWash, "riverBlocksEdge") ? 1 : 0; wsWash.SetProgramVariable<bool>("riverBlocksEdge", true);
                inAge0 = HwStrokeAge(inMe);
                var h = WsAt(inB); var m = WsToMesh(h.ci, h.p); int part = wsColPart[h.ci], packed = HwPack(part, m);
                int amount = 125 | (Mathf.RoundToInt(hwHandR * 1000f) << 12);
                inSeq = HwInt("_seq");
                wsWash.SendCustomNetworkEvent(NetworkEventTarget.All, "NetStroke", KH_DogWash.ToolHand, part, packed, packed, amount, inSeq, HwInt("epoch"));
                wsWash.SetProgramVariable<int>("_seq", (inSeq + 1) & 0x3FFFFFFF);   // the dog's own next send must stay newer than this one
                inAt = EditorApplication.timeSinceStartup;
            }));
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                int inert = HwInt("handStrokesInert") - inInert, rej = HwInt("handStrokesRejected") - inRej, cells = WsCellDiff(inD, inF, inW);
                float md = WsMaxDiff(inM, WsMask()), age1 = HwStrokeAge(inMe), waited = (float)(EditorApplication.timeSinceStartup - inAt);
                int washer = HwInt("_lastWasherId"); bool river = Get<bool>(wsWash, "riverBlocksEdge");
                int sndSeq = HwSenderSeq(inMe), applied = HwAppliedSeq(inMe);
                WsExpect(inert == 1, "the Owner counted it inert (handStrokesInert +" + inert + "; 0 = the event did not arrive or touched foam)");
                WsExpect(rej == 0, "not rejected (handStrokesRejected +" + rej + ")");
                WsExpect(cells == 0, "cells unchanged (" + cells + ")");
                WsExpect(md < 1e-3f, "mask unchanged (max diff " + md.ToString("F4") + ")");
                WsExpect(HwInt("washMs") == inMs && Get<bool>(wsWash, "washSeen") == inSeen && HwInt("washBy") == inBy, "the wash hold untouched (washMs / washSeen / washBy)");
                WsExpect(washer == -1, "the last washer untouched (_lastWasherId " + washer + ", set to -1 before)");
                WsExpect(age1 >= inAge0 + 0.5f * waited - 0.05f, "the stroke age of me was not reset (_StrokeAgeOf " + inAge0.ToString("F2") + " -> " + age1.ToString("F2") + " s over " + waited.ToString("F2") + " s)");
                WsExpect(river, "riverBlocksEdge untouched (set true before)");
                WsExpect(WsSerial() == inSerial && Get<bool>(wsWash, "cleanArmed") == inArmed, "the clean signal untouched (serial " + inSerial + " -> " + WsSerial() + ", armed " + inArmed + " -> " + Get<bool>(wsWash, "cleanArmed") + ")");
                WsExpect(sndSeq == inSeq && applied == inSeq, "the order advanced to the stroke's seq " + inSeq + " (sender table " + sndSeq + ", appliedSeqs " + applied + ")");
                WsNote("simulated: NetStroke(kind 5, part " + wsColPart[inB.ci] + ", a zero-length capsule on the clean back, 125 ms, r " + hwHandR + ") sent through SendCustomNetworkEvent(All) = the local caller (the own-echo path; the Owner's hand / head distance check is not reached for isLocal). _lastWasherId -1 and riverBlocksEdge true written before (heap) and restored; _seq bumped past it. Result: inert +" + inert + ", rejected +" + rej + ", cells " + cells + ", mask " + md.ToString("F4") + ", hold same, washer " + washer + ", age " + inAge0.ToString("F2") + " -> " + age1.ToString("F2") + " s, river " + river + ", serial same, order seq " + inSeq + " (sender " + sndSeq + ", applied " + applied + ")");
                HwRestoreMarkers();
            }));
            HwEnd(s);

            // ---- H-hand-local-inert (round 2, Codex): through the real local path. (A) cells just below handFoamMin, the own-foam ring empty:
            //      no stroke, no washer / stroke age / hold. (B) the ring alone allows it (the dose's echo held back = the cells do not
            //      confirm foam yet): it draws / sends but does not take the washer or the stroke time ----
            s = WsCase("H-hand-local-inert", 240);
            HwBegin(s, true);
            WsAnchor liB = default; List<WsAnchor> liLine = null; int liIss = 0, liMe = 0, liSeq = 0; float liAge0 = 0f, liInj = 0f, liLsa = 0f; double liAt = 0; string liA = "";
            HwDose liD = default; float liCellAtDose = 0f;
            s.Add(WsDo(() =>
            {
                liB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)");
                liLine = HwTopLine(WsAt(liB).p, 0.05f, 3, p => true, "the back");
                wsWash.SendCustomEvent("_DebugClear");   // also empties the own-foam ring (DropInFlight)
            }));
            s.Add(WsWait(1.2));
            HwWaitNoHold(s);
            s.Add(WsDo(() =>
            {
                liMe = Networking.LocalPlayer.playerId;
                var h = WsAt(liB); var m = WsToMesh(h.ci, h.p);
                liInj = Mathf.Max(0f, hwHandMin - 0.01f);
                var f = HwFoam(); var cells = HwCellsNear(m, 0.05f + hwHandR + hwCellReach + 0.05f);
                foreach (int c in cells) f[c] = liInj;
                wsWash.SetProgramVariable<float[]>("_foam", f); wsWash.SendCustomEvent("_DebugCellsChanged");
                WsExpect(HwRingLive() == 0, "precondition: the own-foam ring is empty (" + HwRingLive() + " live entries)");
                hwWasher0 = HwInt("_lastWasherId"); wsWash.SetProgramVariable<int>("_lastWasherId", -1);
                liAge0 = HwStrokeAge(liMe); liIss = HwInt("handStrokesIssued"); liAt = EditorApplication.timeSinceStartup;
                liA = cells.Count + " cells set to " + liInj.ToString("F2");
                hwL = HwRub(liLine, HwUp, HwRubSpeed); HwPush();
            }));
            s.Add(WsWait(2.0));
            s.Add(WsDo(() => { hwL = null; HwPush(); }));
            s.Add(WsWait(0.4));
            s.Add(WsDo(() =>
            {
                int iss = HwInt("handStrokesIssued") - liIss, washer = HwInt("_lastWasherId"); bool hold = WsHold();
                float age1 = HwStrokeAge(liMe), waited = (float)(EditorApplication.timeSinceStartup - liAt);
                WsExpect(iss == 0, "(A) no stroke issued on foam below handFoamMin " + hwHandMin + " (" + liA + "; issued +" + iss + ")");
                WsExpect(washer == -1, "(A) the last washer untouched (" + washer + ", set to -1 before)");
                WsExpect(age1 >= liAge0 + 0.5f * waited - 0.05f, "(A) _StrokeAgeOf(me) not refreshed (" + liAge0.ToString("F2") + " -> " + age1.ToString("F2") + " s over " + waited.ToString("F2") + " s)");
                WsExpect(!hold, "(A) no wash hold");
                liA = "(A) " + liA + ", rub 2 s: issued +" + iss + ", washer " + washer + ", age " + liAge0.ToString("F2") + " -> " + age1.ToString("F2") + ", hold " + hold;
                HwRestoreMarkers();
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            HwWaitNoHold(s);
            s.Add(WsDo(() =>
            {
                // (B) the send slot held (heap _nextSend, simulated: the dose's echo is late), so the Owner's cells do not get the dose
                hwSendHeld = true; wsWash.SetProgramVariable<float>("_nextSend", 1e9f);
                liSeq = HwInt("_seq");
                liD = HwSquirt(liB);   // a held tool: its own stroke takes the washer / stroke time (allowed)
                liCellAtDose = HwCellMax(HwFoam(), liD.mesh, hwHandR + hwCellReach);
                hwWasher0 = HwInt("_lastWasherId"); wsWash.SetProgramVariable<int>("_lastWasherId", -1);
                liLsa = Get<float>(wsWash, "_localStrokeAt"); liIss = HwInt("handStrokesIssued");
                WsExpect(HwRingLive() > 0, "precondition (B): the dose is in the own-foam ring");
                hwL = HwRub(liLine, HwUp, HwRubSpeed); HwPush();
            }));
            s.Add(WsWait(1.5));   // < ownFoamSeconds
            s.Add(WsDo(() =>
            {
                hwL = null; HwPush();
                int iss = HwInt("handStrokesIssued") - liIss, washer = HwInt("_lastWasherId"), seq = HwInt("_seq") - liSeq;
                float cell = HwCellMax(HwFoam(), liD.mesh, hwHandR + hwCellReach), lsa = Get<float>(wsWash, "_localStrokeAt");
                WsExpect(cell < hwHandMin, "precondition (B): the cells still hold no foam (" + liCellAtDose.ToString("F3") + " -> " + cell.ToString("F3") + "; the echo is held)");
                WsExpect(iss > 0, "(B) the own-foam ring let the hand lather (issued +" + iss + ")");
                WsExpect(washer == -1, "(B) a ring-only hand stroke does not take the last washer (" + washer + ")");
                WsExpect(Mathf.Abs(lsa - liLsa) < 1e-4f, "(B) ... nor the local stroke time (_localStrokeAt " + liLsa.ToString("F3") + " -> " + lsa.ToString("F3") + ")");
                WsExpect(seq == 0, "(B) nothing left while the send slot was held (_seq +" + seq + ")");
                liA += "; (B) send slot held, dose (cells " + liCellAtDose.ToString("F3") + " -> " + cell.ToString("F3") + "), rub 1.5 s on the ring alone: issued +" + iss + ", washer " + washer + ", _localStrokeAt unchanged, queue " + HwInt("_qCount");
                HwRestoreMarkers();
                wsWash.SetProgramVariable<float>("_nextSend", 0f); hwSendHeld = false;
            }));
            s.Add(WsWait(2.0));
            s.Add(WsDo(() =>
            {
                WsNote("simulated: cells written in the Owner's _foam (A); the send slot held through the heap _nextSend so the dose's echo waits (B: the state a client has before the cells confirm its own dose); _lastWasherId -1 written and restored; KH_HandWash debug hands. " + liA + "; after release: queue " + HwInt("_qCount") + ", cell max " + HwCellMax(HwFoam(), liD.mesh, hwHandR + hwCellReach).ToString("F3"));
            }));
            HwEnd(s);

            // ---- H-hand-still-on-moving (round 2, Codex): the rub speed is the tracked hand's, not the contact point's. Both hands fixed in
            //      world space on the dose while the part collider slides +-2 cm at 2 Hz under them -> no stroke. Control: a moving hand ----
            s = WsCase("H-hand-still-on-moving", 220);
            HwBegin(s, true);
            WsAnchor smB = default; List<WsAnchor> smLine = null; HwDose smD = default; Vector3 smL = Vector3.zero, smR = Vector3.zero; int smIss = 0, smCc = 0; float smT0 = 0f, smMaxOff = 0f; string smA = "";
            s.Add(WsDo(() =>
            {
                smB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)");
                smLine = HwTopLine(WsAt(smB).p, 0.05f, 3, p => true, "the back");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => smD = HwSquirt(smB)));
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                var t = wsCols[smD.ci].transform;
                // the bone is the animator's and the root is the station's: only a collider on its own child object can be moved
                if (t == wsBones[smD.ci] || t.parent == null) throw new WsSkip("the dose's collider " + t.name + " sits on its bone (moving it would fight the animator); the speed source is not observable here without a runtime read-out");
                hwMovedCol = t; hwMovedBase = t.localPosition;
                var p = WsAt(smB).p; var fwd = WsFlat(wsRoot.forward);
                smL = p + Vector3.up * hwGap; smR = p + Vector3.up * hwGap + fwd * 0.03f;   // fixed in world space
                hwL = () => smL; hwR = () => smR; HwPush();
                smIss = HwInt("handStrokesIssued"); smCc = Get<int>(hwHand, "contactCalls"); smT0 = Time.time;
            }));
            s.Add(now =>
            {
                float t = Time.time - smT0, x = 0.02f * Mathf.Sin(2f * Mathf.PI * 2f * t);
                hwMovedCol.localPosition = hwMovedBase + hwMovedCol.parent.InverseTransformVector(WsFlat(wsRoot.forward) * x);
                smMaxOff = Mathf.Max(smMaxOff, Mathf.Abs(x));
                return t >= 2f;
            });
            s.Add(WsDo(() =>
            {
                hwMovedCol.localPosition = hwMovedBase; hwMovedCol = null;
                int cc = Get<int>(hwHand, "contactCalls") - smCc, iss = HwInt("handStrokesIssued") - smIss;
                WsExpect(cc >= 20, "the still hands touched the sliding part (" + cc + " contacts)");
                WsExpect(iss == 0, "no stroke from hands that do not move while the part slides under them (issued +" + iss + "; the contact point moved up to " + (2f * Mathf.PI * 2f * 0.02f).ToString("F2") + " m/s)");
                smA = "still hands, collider " + wsCols[smD.ci].name + " slid +-" + smMaxOff.ToString("F3") + " m at 2 Hz for 2 s: contacts +" + cc + ", issued +" + iss;
                hwL = HwRub(smLine, HwUp, HwRubSpeed); hwR = null; HwPush();
                smIss = HwInt("handStrokesIssued");
            }));
            s.Add(WsWait(1.5));
            s.Add(WsDo(() =>
            {
                int iss = HwInt("handStrokesIssued") - smIss;
                WsExpect(iss > 0, "control: a hand moving " + HwRubSpeed + " m/s on the same foam lathers (issued +" + iss + ")");
                hwL = null; HwPush();
                WsNote("simulated: the part collider's transform (its own child of the bone) moved along the dog's forward, restored after; debug hands fixed in world space. " + smA + "; control moving left hand " + HwRubSpeed + " m/s 1.5 s: issued +" + iss);
            }));
            HwEnd(s);

            // ---- H-queue-protect (round 2, Codex): a full send queue drops / never evicts a held tool's entry for a hand's. The send slot
            //      is held (heap _nextSend) so the queue fills: a shower segment first, then the two hands ----
            s = WsCase("H-queue-protect", 220);
            HwBegin(s, true);
            WsAnchor qpB = default; List<WsAnchor> qpL1 = null, qpL2 = null; int qpLeg = -1; Vector3 qpLegP = Vector3.zero; int qpQMax = 0, qpFullTicks = 0, qpLostAt = -1, qpSeq0 = 0, qpTicks = 0; float qpFullSince = -1f; string qpEnd = "";
            s.Add(WsDo(() =>
            {
                qpB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)");
                qpL1 = HwTopLine(WsAt(qpB).p, 0.05f, 3, p => true, "the back");
                qpL2 = HwTopLine(WsAt(qpB).p + WsFlat(wsRoot.forward) * 0.04f, 0.04f, 3, p => true, "the back");
                for (int i = 0; i < wsCols.Length; i++) if (WsUsable(i) && wsColPart[i] == 3 && (qpLeg < 0 || wsCols[i].bounds.center.y < wsCols[qpLeg].bounds.center.y)) qpLeg = i;
                if (qpLeg < 0) throw new InvalidOperationException("no front-leg (part 3) collider for the shower entry");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => HwSquirt(qpB)));
            s.Add(WsWait(0.8));   // the dose is sent and integrated (the cells confirm the foam)
            s.Add(WsDo(() =>
            {
                hwSendHeld = true; wsWash.SetProgramVariable<float>("_nextSend", 1e9f);
                WsExpect(HwInt("_qCount") == 0, "precondition: the send queue is empty (" + HwInt("_qCount") + ")");
                var c = wsCols[qpLeg]; var o = c.bounds.center + WsFlat(wsRoot.right) * 0.3f; qpLegP = c.ClosestPoint(o);
                WsContact(WsShower, qpLeg, qpLegP, 0.1f, 0.05f); WsEnd(WsShower);   // one shower segment, queued first (far from the hands)
                var ch = Get<int[]>(wsWash, "_qCh");
                WsExpect(HwInt("_qCount") == 1 && ch[0] == 1, "the shower segment is queued first (count " + HwInt("_qCount") + ", channel " + ch[0] + ")");
                hwL = HwRub(qpL1, HwUp, HwRubSpeed); hwR = HwRub(qpL2, HwUp, HwRubSpeed * 0.8f); HwPush();
            }));
            s.Add(now =>
            {
                int cnt = HwInt("_qCount"); var ch = Get<int[]>(wsWash, "_qCh"); int showers = 0;
                for (int i = 0; i < cnt && i < ch.Length; i++) if (ch[i] == 1) showers++;
                qpTicks++; qpQMax = Mathf.Max(qpQMax, cnt);
                if (showers == 0 && qpLostAt < 0) qpLostAt = qpTicks;
                if (cnt >= ch.Length) { qpFullTicks++; if (qpFullSince < 0f) qpFullSince = Time.time; }
                if (qpFullSince >= 0f && Time.time - qpFullSince >= 1.5f) return true;   // full for 1.5 s: several hand segments had no room
                if (now - wsStepAt > 12) throw new TimeoutException("the send queue did not fill in 12 s (max " + qpQMax + " of " + ch.Length + "; issued " + HwInt("handStrokesIssued") + ")");
                return false;
            });
            s.Add(WsDo(() =>
            {
                hwL = hwR = null; HwPush();
                var ch = Get<int[]>(wsWash, "_qCh"); int cnt = HwInt("_qCount");
                qpEnd = "queue " + cnt + ": [" + string.Join(",", ch.Take(cnt)) + "]";
                WsExpect(qpLostAt < 0, "the shower entry stayed in the full queue (lost at tick " + qpLostAt + " of " + qpTicks + "; " + qpEnd + ")");
                WsExpect(cnt > 0 && ch[0] == 1, "the shower entry is the next to send (" + qpEnd + ")");
                qpSeq0 = HwInt("_seq");
                wsWash.SetProgramVariable<float>("_nextSend", 0f); hwSendHeld = false;
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                var ch = Get<int[]>(wsWash, "_qCh"); int cnt = HwInt("_qCount"), sent = HwInt("_seq") - qpSeq0, showers = 0;
                for (int i = 0; i < cnt && i < ch.Length; i++) if (ch[i] == 1) showers++;
                WsExpect(sent >= 1 && showers == 0, "the shower entry was sent first (_seq +" + sent + ", shower entries left " + showers + ")");
                WsNote("simulated: the send slot held through the heap _nextSend (restored); one shower segment (_LocalContact r 0.05, 0.1 s on the front leg) queued first, then both debug hands rubbing the dose until the queue was full for 1.5 s (" + qpFullTicks + " full ticks, max " + qpQMax + "): " + qpEnd + " (channel 1 = shower, 4 / 5 = hands); released: _seq +" + sent + " in 0.3 s, shower entries left " + showers);
            }));
            s.Add(WsWait(1.5));   // drain
            HwEnd(s);

            // ---- H-hand-saturated: rubbing foam at its cap keeps refreshing the wash hold ----
            s = WsCase("H-hand-saturated", 220);
            HwBegin(s, true);
            WsAnchor saB = default; List<WsAnchor> saLine = null; HwDose saD = default; List<int> saCells = null;
            int saMs0 = 0, saInert = 0, saIss = 0, saMsChanges = 0, saPrevMs = 0; float saFirst = -1f, saLostAt = -1f, saMinF = 1f; double saT0 = 0; float saDur = 0f;
            s.Add(WsDo(() =>
            {
                saB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)");
                saLine = HwTopLine(WsAt(saB).p, 0.05f, 3, p => true, "the back");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => saD = HwSquirt(saB)));
            s.Add(WsDrive(1.0f, 1, u => WsAt(saB), WsSponge, -1f, null));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.5));
            HwWaitNoHold(s);
            s.Add(WsDo(() =>
            {
                // the cap: every cell within 0.05 + the hand's reach of the point set to 1 (Owner's array, simulated)
                var f = HwFoam(); saCells = HwCellsNear(saD.mesh, 0.05f + hwHandR + hwCellReach + 0.05f);
                foreach (int c in saCells) f[c] = 1f;
                wsWash.SetProgramVariable<float[]>("_foam", f); wsWash.SendCustomEvent("_DebugCellsChanged");
                saMs0 = HwInt("washMs"); saPrevMs = saMs0; saInert = HwInt("handStrokesInert"); saIss = HwInt("handStrokesIssued");
                saDur = wsHoldSeconds + 2f;
                hwL = HwRub(saLine, HwUp, HwRubSpeed); HwPush();
                saT0 = EditorApplication.timeSinceStartup;
            }));
            s.Add(now =>
            {
                float t = (float)(now - saT0);
                bool hold = WsHold();
                if (hold && saFirst < 0f) saFirst = t;
                if (!hold && saFirst >= 0f && saLostAt < 0f) saLostAt = t;
                int ms = HwInt("washMs"); if (ms != saPrevMs) { saMsChanges++; saPrevMs = ms; }
                var f = Get<float[]>(wsWash, "_foam"); foreach (int c in saCells) saMinF = Mathf.Min(saMinF, f[c]);
                return t >= saDur;
            });
            s.Add(WsDo(() => { hwL = null; HwPush(); }));
            s.Add(WsDo(() =>
            {
                int inert = HwInt("handStrokesInert") - saInert, iss = HwInt("handStrokesIssued") - saIss, span = saPrevMs - saMs0;
                WsExpect(iss > 0, "lather strokes issued on the capped foam (" + iss + ")");
                WsExpect(saFirst >= 0f && saFirst <= 1.0f, "the hold set within 1 s of rubbing (" + saFirst.ToString("F2") + " s)");
                WsExpect(saLostAt < 0f, "the hold never lapsed while rubbing (lost at " + saLostAt.ToString("F2") + " s)");
                WsExpect(inert == 0, "no hand stroke was inert on capped foam (touchedFoam, not the foam increase, decides; inert +" + inert + ")");
                WsExpect(span >= Mathf.RoundToInt((saDur - 1.5f) * 1000f), "washMs kept moving forward (" + saMsChanges + " refreshes over " + span + " ms)");
                WsNote("simulated: dose + sponge 1 s, hold expired, then " + saCells.Count + " cells around the point set to foam 1 (the cap; Owner's _foam written, _DebugCellsChanged), left hand rubbing +-0.05 m for " + saDur.ToString("F1") + " s (holdSeconds " + wsHoldSeconds + " + 2): hold from +" + saFirst.ToString("F2") + " s, never lost; washMs refreshed " + saMsChanges + " times over " + span + " ms; issued +" + iss + ", inert +" + inert + "; capped cells min " + saMinF.ToString("F3") + " (drying may lower them a little)");
            }));
            HwEnd(s);

            // ---- H-hand-order: foam -> hand -> rinse vs foam -> rinse -> hand ----
            s = WsCase("H-hand-order", 240);
            HwBegin(s, true);
            const float OrRad = 0.15f;
            WsAnchor orB = default; List<WsAnchor> orLine = null; HwDose orD = default;
            float a0 = 0f, a1 = 0f, a2 = 0f, am0 = 0f, am1 = 0f, am2 = 0f, b1 = 0f, b2 = 0f, bm1 = 0f, bm2 = 0f; int aIss = 0, bIss = 0;
            s.Add(WsDo(() =>
            {
                orB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)");
                orLine = HwTopLine(WsAt(orB).p, 0.04f, 3, p => true, "the back");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            // (A) foam -> hand -> rinse
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => orD = HwSquirt(orB)));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() => { a0 = HwCenterCell(orD); am0 = HwMaskAt(WsMask(), orD.mesh, orD.part); aIss = HwInt("handStrokesIssued"); hwL = HwRub(orLine, HwUp, HwRubSpeed); HwPush(); }));
            s.Add(WsWait(1.5));
            s.Add(WsDo(() => { hwL = null; HwPush(); }));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() => { a1 = HwCenterCell(orD); am1 = HwMaskAt(WsMask(), orD.mesh, orD.part); aIss = HwInt("handStrokesIssued") - aIss; }));
            s.Add(WsDrive(1.5f, 1, u => WsAt(orB), WsShower, OrRad, null));
            s.Add(WsDo(() => WsEnd(WsShower)));
            s.Add(WsWait(wsRunoffSeconds + 0.8));
            s.Add(WsDo(() =>
            {
                a2 = HwCenterCell(orD); am2 = HwMaskAt(WsMask(), orD.mesh, orD.part);
                WsExpect(aIss > 0 && (a1 > a0 + 0.02f || am1 > am0 + 0.02f), "(A) the hand lathered the dose before the rinse (issued " + aIss + ", centre cell " + a0.ToString("F3") + " -> " + a1.ToString("F3") + ", mask " + am0.ToString("F3") + " -> " + am1.ToString("F3") + ")");
                WsExpect(a2 < hwStart && am2 < hwStart, "(A) the rinse took it off (centre cell " + a2.ToString("F3") + ", mask " + am2.ToString("F3") + ")");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            // (B) foam -> rinse -> hand
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => orD = HwSquirt(orB)));
            s.Add(WsWait(0.8));
            s.Add(WsDrive(1.5f, 1, u => WsAt(orB), WsShower, OrRad, null));
            s.Add(WsDo(() => WsEnd(WsShower)));
            s.Add(WsWait(wsRunoffSeconds + 0.8));
            s.Add(WsDo(() => { b1 = HwCenterCell(orD); bm1 = HwMaskAt(WsMask(), orD.mesh, orD.part); bIss = HwInt("handStrokesIssued"); hwL = HwRub(orLine, HwUp, HwRubSpeed); HwPush(); }));
            s.Add(WsWait(1.5));
            s.Add(WsDo(() => { hwL = null; HwPush(); }));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                b2 = HwCenterCell(orD); bm2 = HwMaskAt(WsMask(), orD.mesh, orD.part); bIss = HwInt("handStrokesIssued") - bIss;
                WsExpect(b1 < hwStart && bm1 < hwStart, "(B) precondition: rinsed (centre cell " + b1.ToString("F3") + ", mask " + bm1.ToString("F3") + ")");
                WsExpect(b2 < hwStart && b2 <= b1 + 1e-3f, "(B) the hand after the rinse brought no foam back to the centre cell (" + b1.ToString("F3") + " -> " + b2.ToString("F3") + ")");
                WsExpect(bm2 < hwStart && bm2 <= bm1 + 0.02f, "(B) ... nor to the mask at the centre (" + bm1.ToString("F3") + " -> " + bm2.ToString("F3") + ")");
                WsNote("simulated: dose (_DebugSquirt), left hand +-0.04 m 1.5 s, shower r " + OrRad + " m 1.5 s (_LocalContact). (A) foam -> hand -> rinse: centre cell " + a0.ToString("F3") + " -> " + a1.ToString("F3") + " -> " + a2.ToString("F3") + ", mask " + am0.ToString("F3") + " -> " + am1.ToString("F3") + " -> " + am2.ToString("F3") + ", issued " + aIss + ". (B) foam -> rinse -> hand: centre cell " + b1.ToString("F3") + " -> " + b2.ToString("F3") + ", mask " + bm1.ToString("F3") + " -> " + bm2.ToString("F3") + ", issued " + bIss);
            }));
            HwEnd(s);

            // ---- H-hand-reject: Owner-side predicate unit check (_DebugHandPlausible) + an out-of-range kind ----
            s = WsCase("H-hand-reject", 200);
            HwBegin(s, false);
            WsAnchor rjB = default; bool rjNear = false, rjFar = true, rjFlat = true, rjFree = true; float rjNearM = 0f, rjFarM = 0f;
            Color[] rjM = null; float[] rjD = null, rjF = null, rjW = null; int rjCnt0 = 0, rjSnd0 = 0;
            Func<bool, float, bool> plausible = (vr, dist) =>
            {
                var h = WsAt(rjB); var w = h.p; var fwd = WsFlat(wsRoot.forward);
                return HwPlausible(vr, w + Vector3.up * dist, w + Vector3.up * dist + WsFlat(wsRoot.right) * 0.01f, w + Vector3.up * 0.6f, wsColPart[h.ci], w, w + fwd * 0.05f);
            };
            s.Add(WsDo(() =>
            {
                rjB = HwTop(WsTorsoCenter(), p => p == 0 || p == 1, "the back (hip / chest)");
                rjNearM = 0.6f * hwVerifyR; rjFarM = 2f * hwVerifyR;
                WsExpect(0.6f < hwReachM, "the head 0.6 m above the point is within handReachMeters " + hwReachM);
                rjNear = plausible(true, rjNearM);
                rjFar = plausible(true, rjFarM);
                rjFlat = plausible(false, rjNearM);
                WsExpect(rjNear, "a VR sender with a hand " + rjNearM.ToString("F2") + " m from both capsule ends (0.6 x handVerifyRadius " + hwVerifyR + ") at a held dog is accepted");
                WsExpect(!rjFar, "a hand " + rjFarM.ToString("F2") + " m away (2 x handVerifyRadius) is refused");
                WsExpect(!rjFlat, "a sender not in VR is refused");
                // an out-of-range kind (6) through NetStroke changes nothing
                rjM = WsMask(); rjD = (float[])Get<float[]>(wsWash, "_dirt").Clone(); rjF = HwFoam(); rjW = (float[])Get<float[]>(wsWash, "_wet").Clone();
                rjCnt0 = HwInt("handStrokesInert") + HwInt("handStrokesRejected") + HwInt("handStrokesIssued");
                rjSnd0 = HwSenderSeq(Networking.LocalPlayer.playerId);
                var h = WsAt(rjB); int part = wsColPart[h.ci], packed = HwPack(part, WsToMesh(h.ci, h.p));
                wsWash.SendCustomNetworkEvent(NetworkEventTarget.All, "NetStroke", 6, part, packed, packed, 125 | (70 << 12), HwInt("_seq"), HwInt("epoch"));
            }));
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                int cells = WsCellDiff(rjD, rjF, rjW), cnt = HwInt("handStrokesInert") + HwInt("handStrokesRejected") + HwInt("handStrokesIssued") - rjCnt0, snd = HwSenderSeq(Networking.LocalPlayer.playerId);
                float md = WsMaxDiff(rjM, WsMask());
                WsExpect(cells == 0 && md < 1e-3f, "kind 6: cells and mask unchanged (" + cells + " cells, mask " + md.ToString("F4") + ")");
                WsExpect(cnt == 0 && snd == rjSnd0, "kind 6: no hand counter moved and the sender's order did not advance (counters +" + cnt + ", seq " + rjSnd0 + " -> " + snd + ")");
                WcPlayerAtStation();
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0, 3, () => "ReqRelease did not release the Shiba (heldDog " + Get<int>(wcStation, "heldDog") + ")"));
            s.Add(WsDo(() =>
            {
                rjFree = plausible(true, rjNearM);
                WsExpect(!rjFree, "the same stroke at a dog no station holds is refused");
                WsNote("Owner-side predicate unit check: KH_DogWash._DebugHandPlausible(senderVr, handL, handR, head, part, a, b) with a 0.05 m capsule on the back and the head 0.6 m above (the values a remote sender's tracking would give; no real CallingPlayer): VR + hand " + rjNearM.ToString("F2") + " m -> " + rjNear + "; hand " + rjFarM.ToString("F2") + " m -> " + rjFar + "; not VR -> " + rjFlat + "; dog released (ReqRelease) -> " + rjFree + ". NetStroke kind 6 (local caller): nothing changed (like W-reject). The Shiba is left released");
            }));
            HwEnd(s);

            // ---- H-hand-idle: no frame loop / no contact when not VR, too far, or no dog held ----
            s = WsCase("H-hand-idle", 240);
            HwBegin(s, true);
            List<WsAnchor> idLine = null; int idCc = 0; var idNotes = new List<string>();
            s.Add(WsDo(() => { idLine = HwTopLine(WsTorsoCenter(), 0.05f, 3, p => true, "the back"); hwL = HwRub(idLine, HwUp, HwRubSpeed); HwPush(); idCc = Get<int>(hwHand, "contactCalls"); }));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() => { int d = Get<int>(hwHand, "contactCalls") - idCc; WsExpect(Get<bool>(hwHand, "frameLoop") && d > 0, "control: held, near, VR -> frame loop on and contacts (" + d + ")"); idNotes.Add("control near " + d + " contacts / 0.8 s"); }));
            // too far: me nearMeters + 1.5 m from the anchor
            s.Add(WsDo(() =>
            {
                Vector3 a = wcAnchor.position, side = Quaternion.Euler(0f, Get<float>(wcStation, "standYaw") + 90f, 0f) * Vector3.forward, p;
                try { p = CmGround(a + side * (hwNearM + 1.5f)); } catch (InvalidOperationException) { p = a + side * (hwNearM + 1.5f); }
                CmTeleport(p, a - p);
            }));
            HwIdleQuiet(s, "too far (" + "nearMeters + 1.5 m)", idNotes);
            s.Add(WsDo(() => WcPlayerAtStation()));
            s.Add(WsUntil(() => Get<bool>(hwHand, "frameLoop"), 3, () => "control: back near, the frame loop did not resume"));
            // not VR
            s.Add(WsDo(() => { hwVr = false; HwPush(); }));
            HwIdleQuiet(s, "not VR (debugVr false; ClientSim is Desktop)", idNotes);
            s.Add(WsDo(() => { hwVr = true; HwPush(); }));
            s.Add(WsUntil(() => Get<bool>(hwHand, "frameLoop"), 7, () => "control: VR again, the frame loop did not resume within the 5 s non-VR recheck + 2 s"));
            // no dog held
            s.Add(WsDo(() => { WcPlayerAtStation(); wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease"); }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0, 3, () => "ReqRelease did not release the Shiba"));
            HwIdleQuiet(s, "no dog held (released; the hands follow the Shiba's back)", idNotes);
            s.Add(WsDo(() => WsNote("simulated hands rubbing the Shiba's back throughout (debugHands 1): " + string.Join("; ", idNotes) + ". The Shiba is left released")));
            HwEnd(s);

            // ---- H-guide: the picture guide's words follow the language and the device ----
            s = WsCase("H-guide", 60);
            var gdNotes = new List<string>();
            s.Add(WsDo(() =>
            {
                HwBind(); HwBindGuide();
                WcPlayerAtStation();   // the board refreshes only within showMeters
                hwBoard.SetProgramVariable<bool>("debugVr", false);
                hwLang0 = Get<int>(hwLang, "_languageIndex");
                HwSetLang("ja");
            }));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() => gdNotes.Add(HwCheckGuide("ja", false))));
            s.Add(WsDo(() => HwSetLang("en")));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                gdNotes.Add(HwCheckGuide("en", false));
                // every one of the 14 guide words differs between ja and en (IMP-UI-003)
                var codes = Get<string[]>(hwLang, "languageCodes"); int ja = Array.IndexOf(codes, "ja"), en = Array.IndexOf(codes, "en");
                var same = new List<string>();
                for (int w = KH_WashStationBoard.WGuideStep1; w <= KH_WashStationBoard.WGuideHelpFlat; w++) if (HwWord(w, ja) == HwWord(w, en)) same.Add(w + ":" + HwWord(w, en));
                WsExpect(same.Count == 0, "the 14 guide words differ between ja and en (same: " + string.Join(", ", same) + ")");
                hwBoard.SetProgramVariable<bool>("debugVr", true); hwBoard.SendCustomEvent("_ApplyGuide");
                gdNotes.Add(HwCheckGuide("en", true));
                hwBoard.SetProgramVariable<bool>("debugVr", false); hwBoard.SendCustomEvent("_ApplyGuide");
                gdNotes.Add(HwCheckGuide("en", false));
                hwLang.SetProgramVariable<int>("_languageIndex", hwLang0); hwLang0 = -1; hwBoard.SetProgramVariable<float>("_nextAt", 0f);
            }));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                hwBoard.SendCustomEvent("_ApplyGuide");
                WsNote("simulated: the language index written to KD_Language._languageIndex (no PlayerData save; restored), the board's debugVr flipped and _ApplyGuide called. ClientSim is Desktop, so the flat (sponge) branch is the device's own; the VR branch is debugVr. " + string.Join(" | ", gdNotes));
            }));
            HwSeal(s);
        }

        // ============================================================
        // 組み立ての部品
        // ============================================================

        /// <summary>Bind, the Shiba held and arrived at the main station (called again if not), me 1.5 m beside the stand,
        /// and (hands) the KH_HandWash frame loop running on the simulated VR hands.</summary>
        static void HwBegin(List<Func<double, bool>> s, bool hands)
        {
            s.Add(WsDo(() => { HwBind(); HwHandsOffNow(); }));
            HwEnsureHeld(s);
            s.Add(WsDo(() => WcPlayerAtStation()));
            if (!hands) return;
            s.Add(WsDo(() => { hwActive = true; hwVr = true; hwLFree = hwRFree = true; hwL = hwR = null; HwPush(); }));
            s.Add(WsUntil(() => Get<bool>(hwHand, "frameLoop"), 7, () => "KH_HandWash's frame loop did not start within 7 s (debugVr, the Shiba held " + (Get<int>(wcStation, "heldDog") == wsDog) + " and arrived " + Get<bool>(wcStation, "arrived") + ", me "
                + Flat(Networking.LocalPlayer.GetPosition() - wcAnchor.position).ToString("F2") + " m from the anchor, nearMeters " + hwNearM + ")"));
        }

        static void HwEnd(List<Func<double, bool>> s)
        {
            s.Add(WsDo(() => { HwHandsOffNow(); HwRestoreMarkers(); }));
            HwSeal(s);
        }

        // ============================================================
        // 診断の記録 (2026-10-03、H-bottle: セルには届くがマスクが 0 のままの件)
        // ============================================================

        static readonly string[] HwTraceKeys = { "_restoreCount", "_restorePending", "_replaying", "_replayPending", "_pdCount", "_blitCount", "_dryFinished", "_shellOn", "_anyFoamOrWet", "_cellsEmpty", "_lastWetActivity", "_lastActivity", "epoch", "_busyUntil" };

        /// <summary>The trace of the running case: (full line, short line). The full lines go into the failure dump (wsFailDump =
        /// HwTraceDump), the short ones into the PASS note.</summary>
        static readonly List<KeyValuePair<string, string>> hwTrace = new List<KeyValuePair<string, string>>();
        static string HwTraceDump() { return "TRACE (full): " + string.Join(" | ", hwTrace.Select(x => x.Key)); }

        /// <summary>act() in one step, then trace lines at that frame, +1 frame, +2 frames, +0.1 s, +0.3 s, +0.8 s (game time), kept in
        /// hwTrace as they are taken (a later FAIL still dumps them).</summary>
        static void HwTraced(List<Func<double, bool>> s, string label, Action act, Func<Vector3> mesh, Func<int> part)
        {
            int f0 = 0; float t0 = 0f;
            s.Add(WsDo(() => { act(); f0 = Time.frameCount; t0 = Time.time; hwTrace.Add(HwTraceLine(label + " @dose", mesh(), part(), f0, t0)); }));
            foreach (int k in new[] { 1, 2 })
            {
                int kk = k;
                s.Add(now => { if (Time.frameCount < f0 + kk) return false; hwTrace.Add(HwTraceLine(label + " @+" + kk + "f", mesh(), part(), f0, t0)); return true; });
            }
            foreach (float x in new[] { 0.1f, 0.3f, 0.8f })
            {
                float xx = x;
                s.Add(now => { if (Time.time - t0 < xx) return false; hwTrace.Add(HwTraceLine(label + " @+" + xx + "s", mesh(), part(), f0, t0)); return true; });
            }
        }

        /// <summary>Both mask RTs (maskA / maskB) read separately: G at the hit point (mean of the 6 nearest texels of the part and its
        /// neighbours, as WsLocal) and the single nearest texel, and the max G over the valid texels; which one the heap's _cur and the
        /// shell's _Mask point at (WsMask reads _cur afresh on every call, so it follows Swap); and the heap values of HwTraceKeys.</summary>
        static KeyValuePair<string, string> HwTraceLine(string label, Vector3 mesh, int part, int f0, float t0)
        {
            var sb = new System.Text.StringBuilder(); string shortCur = "?";
            sb.Append(label).Append(" (frame +").Append(Time.frameCount - f0).Append(", t +").Append((Time.time - t0).ToString("F3")).Append(", Time.time ").Append(Time.time.ToString("F3")).Append("): ");
            try
            {
                var a = Get<RenderTexture>(wsWash, "maskA"); var b = Get<RenderTexture>(wsWash, "maskB");
                object o; var cur = wsWash.TryGetProgramVariable("_cur", out o) ? o as RenderTexture : null;
                var sm = wsShell != null ? wsShell.sharedMaterial : null; var shell = sm != null && sm.HasProperty("_Mask") ? sm.GetTexture("_Mask") as RenderTexture : null;
                Func<RenderTexture, string> which = rt => rt == null ? "null" : rt == a ? "maskA" : rt == b ? "maskB" : rt.name;
                sb.Append("_cur=").Append(which(cur)).Append(", shell _Mask=").Append(which(shell)).Append("; ");
                foreach (var rt in new[] { a, b })
                {
                    var px = WsRead(rt, TextureFormat.RGBAHalf);
                    float dm, hit = part >= 0 ? WsLocal(mesh, part, null, px, 1, out dm) : -1f, max = 0f, near = -1f, nd = float.MaxValue;
                    for (int k = 0; k < wsTexCount; k++)
                    {
                        float g = px[wsTexIdx[k]].g; if (g > max) max = g;
                        float d = (wsTexPos[k] - mesh).sqrMagnitude; if (d < nd) { nd = d; near = g; }
                    }
                    sb.Append(which(rt)).Append(" hit6 ").Append(hit.ToString("F3")).Append(" hit1 ").Append(near.ToString("F3")).Append(" (").Append((Mathf.Sqrt(nd) * wsMts).ToString("F3")).Append(" m) max ").Append(max.ToString("F3")).Append("; ");
                    if (rt == cur) shortCur = hit.ToString("F2") + "/" + max.ToString("F2");
                }
            }
            catch (Exception e) { sb.Append("mask read failed: ").Append(e.Message).Append("; "); }
            foreach (var key in HwTraceKeys)
            {
                string v;
                try { object o; v = wsWash.TryGetProgramVariable(key, out o) ? (o is float f ? f.ToString("F3") : o != null ? o.ToString() : "null") : "missing"; }
                catch (Exception) { v = "?"; }
                sb.Append(key).Append('=').Append(v).Append(' ');
            }
            string restores;
            try { restores = Get<int>(wsWash, "_restoreCount").ToString(); } catch (Exception) { restores = "?"; }
            return new KeyValuePair<string, string>(sb.ToString().TrimEnd(), label.Replace("TRACE ", "") + " " + shortCur + " r" + restores);
        }

        /// <summary>Every step of the case first writes the simulated hands (every tick, design §8.3).</summary>
        static void HwSeal(List<Func<double, bool>> s)
        {
            for (int i = 0; i < s.Count; i++) { var f = s[i]; s[i] = now => { HwPush(); return f(now); }; }
        }

        static void HwEnsureHeld(List<Func<double, bool>> s)
        {
            bool need = false;
            s.Add(WsDo(() =>
            {
                need = !(Get<int>(wcStation, "heldDog") == wsDog && Get<bool>(wcStation, "arrived") && Flat(wsRoot.position - wcAnchor.position) < 0.35f);
                if (!need) WsExpect(Get<int[]>(manager, "walkers")[wsDog] < 0, "the held Shiba is not leashed");
            }));
            var tmp = new List<Func<double, bool>>();
            WcSetup(tmp);
            WcCallAndArrive(tmp, wsDog, 60);
            foreach (var f in tmp) { var g = f; s.Add(now => !need || g(now)); }
            s.Add(WsDo(() => WsExpect(Get<int>(wcStation, "heldDog") == wsDog && Get<bool>(wcStation, "arrived"), "the Shiba is held and arrived at the main station")));
        }

        static void HwWaitNoHold(List<Func<double, bool>> s)
        {
            s.Add(WsUntil(() => !WsHold(), wsHoldSeconds + 1.5, () => "the wash hold from an earlier stroke did not expire"));
        }

        /// <summary>The loop stops (within one retarget, idleInterval) and no _HandContact happens for 1.5 s.</summary>
        static void HwIdleQuiet(List<Func<double, bool>> s, string label, List<string> notes)
        {
            int cc = 0;
            s.Add(WsUntil(() => !Get<bool>(hwHand, "frameLoop"), 1.5, () => label + ": the frame loop did not stop"));
            s.Add(WsDo(() => cc = Get<int>(hwHand, "contactCalls")));
            s.Add(WsWait(1.5));
            s.Add(WsDo(() =>
            {
                int d = Get<int>(hwHand, "contactCalls") - cc; bool loop = Get<bool>(hwHand, "frameLoop");
                WsExpect(!loop && d == 0, label + ": frameLoop false and no _HandContact for 1.5 s (frameLoop " + loop + ", contacts +" + d + ")");
                notes.Add(label + ": loop off, contacts +" + d + " / 1.5 s");
            }));
        }

        // ============================================================
        // 配線 (実行時に引く。無ければ FAIL: run Wash/11)
        // ============================================================

        static void HwBind()
        {
            var go = GameObject.Find(HwStationName);
            if (go == null || go.transform.parent != null) throw new InvalidOperationException(HwStationName + " not found at the scene root (run Wash/11)");
            hwStation = go.transform;
            hwHand = HwFindU(hwStation, HwHandName, "KH_HandWash");
            hwBottle = HwFindU(hwStation, HwBottleName, "KH_WashTool");
            if (Get<int>(hwBottle, "tool") != KH_DogWash.ToolBottle) throw new InvalidOperationException(HwBottleName + ".tool is " + Get<int>(hwBottle, "tool") + ", not " + KH_DogWash.ToolBottle + " (run Wash/11)");
            var nozzle = WsRef(hwBottle, "nozzle") as Transform;
            if (nozzle == null || !nozzle.IsChildOf(hwBottle.transform)) throw new InvalidOperationException(HwBottleName + ".nozzle is not its Nozzle child (run Wash/11)");
            // the main station's Call and the Shiba's helpers (as MakeWashCallCases; run time, so a missing piece FAILs the case)
            wcStation = null;
            foreach (var u in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>())
                if (u.programSource != null && u.programSource.name == "KH_WashStationCall" && Get<int>(u, "stationIndex") == 0) { wcStation = u; break; }
            if (wcStation == null) throw new InvalidOperationException("no KH_WashStationCall with stationIndex 0 (run Wash/11)");
            wcAnchor = WsRef(wcStation, "anchor") as Transform;
            if (wcAnchor == null) throw new InvalidOperationException("station 0 anchor is not wired (run Wash/11)");
            wcLiftRoot = WsRef(wcStation, "liftRoot") as Transform;
            if (wcLiftRoot != null) wcLiftBaseY = wcLiftRoot.localPosition.y - Get<float>(wcStation, "_liftNow");
            wcFetch = WsRef(wsWander, "fetchController") as UdonBehaviour;
            wcCatcher = WsRef(wsWander, "frisbeeCatch") as UdonBehaviour;
            wcActivities = WsRef(wsWander, "activities") as UdonBehaviour;
            // wiring the hand wash needs
            var hw = WsArr<UdonBehaviour>(hwHand, "washes"); var hc = WsArr<UdonBehaviour>(hwHand, "calls");
            if (!hw.Contains(wsWash)) throw new InvalidOperationException("KH_HandWash.washes does not hold the Shiba's KH_DogWash (" + hw.Length + " entries; run Wash/11)");
            if (!hc.Contains(wcStation)) throw new InvalidOperationException("KH_HandWash.calls does not hold the main station's Call (" + hc.Length + " entries; run Wash/11)");
            object wc; var wcs = wsWash.TryGetProgramVariable("washCalls", out wc) ? wc as Array : null;
            if (wcs == null || !wcs.Cast<object>().Contains(wcStation)) throw new InvalidOperationException("the Shiba's KH_DogWash.washCalls does not hold the main station's Call (run Wash/11)");
            if (!WsArr<UdonBehaviour>(wsWash, "tools").Contains(hwBottle)) throw new InvalidOperationException("the Shiba's KH_DogWash.tools does not hold " + HwBottleName + " (run Wash/11)");
            // tuning (scene values; code defaults do not reach the scene)
            hwHandR = Get<float>(wsWash, "handRadius"); hwHandMin = Get<float>(wsWash, "handFoamMin"); hwStart = Get<float>(wsWash, "handStartFoam");
            hwBottleR = Get<float>(wsWash, "bottleRadius"); hwBottleRate = Get<float>(wsWash, "bottleFoamRate"); hwCellReach = Get<float>(wsWash, "cellReach");
            hwMaxSeg = Get<float>(wsWash, "handMaxSegment"); hwVerifyR = Get<float>(wsWash, "handVerifyRadius"); hwReachM = Get<float>(wsWash, "handReachMeters");
            hwMinSpeed = Get<float>(wsWash, "handMinSpeed"); hwMaxSpeed = Get<float>(wsWash, "handMaxSpeed");
            hwContactR = Get<float>(hwHand, "handContactRadius"); hwNearM = Get<float>(hwHand, "nearMeters");
            hwSquirtSec = Get<float>(hwBottle, "bottleSquirtSeconds"); hwBottleReach = Get<float>(hwBottle, "bottleReach");
            hwGap = 0.3f * hwContactR;
            if (!(HwRubSpeed >= 2f * hwMinSpeed && HwRubSpeed <= 0.5f * hwMaxSpeed)) throw new InvalidOperationException("the rub speed " + HwRubSpeed + " m/s is not well inside handMinSpeed " + hwMinSpeed + " .. handMaxSpeed " + hwMaxSpeed);
            string key = "tuning";
            if (!wsReport.resolved.Any(r => r.StartsWith("100g " + key)))
                wsReport.resolved.Add("100g " + key + ": handRadius " + hwHandR + ", handFoamMin " + hwHandMin + ", handStartFoam " + hwStart + ", bottleRadius " + hwBottleR + ", bottleFoamRate " + hwBottleRate + ", cellReach " + hwCellReach + ", handMaxSegment " + hwMaxSeg
                    + ", handVerifyRadius " + hwVerifyR + ", handReachMeters " + hwReachM + ", handMinSpeed " + hwMinSpeed + ", handMaxSpeed " + hwMaxSpeed + ", handContactRadius " + hwContactR + ", nearMeters " + hwNearM + ", bottleSquirtSeconds " + hwSquirtSec + ", bottleReach " + hwBottleReach);
        }

        static void HwBindGuide()
        {
            hwBoard = HwFindU(hwStation, HwBoardName, "KH_WashStationBoard");
            hwGuide = hwStation.Find(HwGuideName);
            if (hwGuide == null) throw new InvalidOperationException(HwStationName + "/" + HwGuideName + " missing (IMP-DOG-039 picture guide: run Wash/11)");
            var labels = WsArr<TMPro.TextMeshProUGUI>(hwBoard, "guideLabels");
            if (labels.Length != 9 || labels.Any(l => l == null || !l.transform.IsChildOf(hwGuide))) throw new InvalidOperationException("the board's guideLabels are not the 9 labels under " + HwGuideName + " (" + labels.Length + "; run Wash/11)");
            var pics = WsArr<UnityEngine.UI.RawImage>(hwBoard, "guidePics");
            if (pics.Length != 4 || pics[1] == null || pics[2] == null || !pics[1].transform.IsChildOf(hwGuide) || !pics[2].transform.IsChildOf(hwGuide)) throw new InvalidOperationException("the board's guidePics are not the 4 pictures under " + HwGuideName + " (" + pics.Length + "; run Wash/11)");
            var keys = Get<string[]>(hwBoard, "wordKeys"); var words = Get<string[]>(hwBoard, "words");
            if (keys.Length != KH_WashStationBoard.WordCount || words.Length != KH_WashStationBoard.WordCount) throw new InvalidOperationException("the board has " + keys.Length + " word keys / " + words.Length + " words, not " + KH_WashStationBoard.WordCount + " (run Wash/11)");
            hwLang = WsRef(hwBoard, "language") as UdonBehaviour;
            if (hwLang == null) throw new InvalidOperationException("the board's language (KD_Language) is not wired (run Wash/11)");
            var codes = Get<string[]>(hwLang, "languageCodes");
            if (Array.IndexOf(codes, "ja") < 0 || Array.IndexOf(codes, "en") < 0) throw new InvalidOperationException("KD_Language has no ja / en");
        }

        static UdonBehaviour HwFindU(Transform root, string child, string program)
        {
            var t = root.Find(child);
            if (t == null) throw new InvalidOperationException(root.name + "/" + child + " missing (IMP-DOG-039: run Wash/11)");
            var u = t.GetComponents<UdonBehaviour>().FirstOrDefault(x => x != null && x.programSource != null && x.programSource.name == program);
            if (u == null) throw new InvalidOperationException(root.name + "/" + child + " has no " + program + " (run Wash/10 and Wash/11)");
            return u;
        }

        // ============================================================
        // 疑似の手
        // ============================================================

        static void HwPush()
        {
            if (!hwActive || hwHand == null) return;
            Vector3 park = (wcAnchor != null ? wcAnchor.position : Vector3.zero) + Vector3.up * 5f;
            hwHand.SetProgramVariable<int>("debugHands", 1);
            hwHand.SetProgramVariable<bool>("debugVr", hwVr);
            hwHand.SetProgramVariable<Vector3>("debugLeft", hwL != null ? hwL() : park);
            hwHand.SetProgramVariable<Vector3>("debugRight", hwR != null ? hwR() : park + Vector3.right);
            hwHand.SetProgramVariable<bool>("debugLeftFree", hwLFree);
            hwHand.SetProgramVariable<bool>("debugRightFree", hwRFree);
        }

        /// <summary>The scene's values back (debugHands 0, debugVr false, not free); the loop ends the open contacts itself on its next sample.</summary>
        static void HwHandsOffNow()
        {
            hwL = hwR = null; hwActive = false; hwVr = true; hwLFree = hwRFree = true;
            if (hwHand == null) return;
            hwHand.SetProgramVariable<int>("debugHands", 0);
            hwHand.SetProgramVariable<bool>("debugVr", false);
            hwHand.SetProgramVariable<bool>("debugLeftFree", false);
            hwHand.SetProgramVariable<bool>("debugRightFree", false);
        }

        static void HwRestoreMarkers()
        {
            if (wsWash == null) return;
            if (hwWasher0 != int.MinValue) { wsWash.SetProgramVariable<int>("_lastWasherId", hwWasher0); hwWasher0 = int.MinValue; }
            if (hwRiver0 >= 0) { wsWash.SetProgramVariable<bool>("riverBlocksEdge", hwRiver0 == 1); hwRiver0 = -1; }
        }

        /// <summary>From WsRecover (a failed / skipped case): the hands off, every heap value a 100g case wrote put back.</summary>
        static void HwRecover()
        {
            try { HwHandsOffNow(); } catch (Exception) { }
            try { HwRestoreMarkers(); } catch (Exception) { }
            if (hwMaxBlits0 >= 0 && wsWash != null) { try { wsWash.SetProgramVariable<int>("maxBlitsPerFrame", hwMaxBlits0); } catch (Exception) { } hwMaxBlits0 = -1; }
            if (hwLang0 >= 0 && hwLang != null) { try { hwLang.SetProgramVariable<int>("_languageIndex", hwLang0); if (hwBoard != null) hwBoard.SetProgramVariable<float>("_nextAt", 0f); } catch (Exception) { } hwLang0 = -1; }
            if (hwBoard != null) { try { hwBoard.SetProgramVariable<bool>("debugVr", false); hwBoard.SendCustomEvent("_ApplyGuide"); } catch (Exception) { } }
            if (hwSendHeld && wsWash != null) { try { wsWash.SetProgramVariable<float>("_nextSend", 0f); } catch (Exception) { } hwSendHeld = false; }
            if (hwMovedCol != null) { try { hwMovedCol.localPosition = hwMovedBase; } catch (Exception) { } hwMovedCol = null; }
        }

        /// <summary>Live entries of the own-foam ring (_ofAt within ownFoamSeconds).</summary>
        static int HwRingLive()
        {
            var at = Get<float[]>(wsWash, "_ofAt"); float secs = Get<float>(wsWash, "ownFoamSeconds"); int n = 0;
            foreach (var a in at) if (Time.time - a <= secs) n++;
            return n;
        }

        static Vector3 HwUp() { return Vector3.up; }
        static Vector3 HwRight() { return WsFlat(wsRoot.right); }

        /// <summary>A hand going back and forth along the line (the anchors' current world points) at speed, gap above the surface.</summary>
        static Func<Vector3> HwRub(List<WsAnchor> line, Func<Vector3> normal, float speed)
        {
            float t0 = -1f;
            return () =>
            {
                if (t0 < 0f) t0 = Time.time;
                var pts = line.Select(a => WsAt(a).p).ToList();
                return HwAlong(pts, speed * (Time.time - t0)) + normal() * hwGap;
            };
        }
        static Func<Vector3> HwFixed(WsAnchor a, Func<Vector3> normal) { return () => WsAt(a).p + normal() * hwGap; }
        static Vector3 HwAlong(List<Vector3> pts, float d)
        {
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += (pts[i] - pts[i - 1]).magnitude;
            if (pts.Count < 2 || total < 1e-4f) return pts[0];
            float x = Mathf.PingPong(d, total);
            for (int i = 1; i < pts.Count; i++)
            {
                float l = (pts[i] - pts[i - 1]).magnitude;
                if (x <= l || i == pts.Count - 1) return Vector3.Lerp(pts[i - 1], pts[i], l > 1e-6f ? Mathf.Clamp01(x / l) : 0f);
                x -= l;
            }
            return pts[pts.Count - 1];
        }
        static float HwLen(List<WsAnchor> line)
        {
            float l = 0f; for (int i = 1; i < line.Count; i++) l += (WsAt(line[i]).p - WsAt(line[i - 1]).p).magnitude; return l;
        }
        static List<Vector3> HwMesh(List<WsAnchor> line) { return line.Select(a => { var h = WsAt(a); return WsToMesh(h.ci, h.p); }).ToList(); }

        // ============================================================
        // 表面の点
        // ============================================================

        /// <summary>The first Shiba part collider hit by a ray straight down from 0.6 m above `above`; ci -1 when that first hit is not partOk.</summary>
        static WsHit HwDownHit(Vector3 above, Func<int, bool> partOk)
        {
            var hits = Physics.RaycastAll(above + Vector3.up * 0.6f, Vector3.down, 1.4f, wsLayer, QueryTriggerInteraction.Collide);
            var best = new WsHit { ci = -1 }; float bd = float.MaxValue;
            foreach (var h in hits)
            {
                int ci = Array.IndexOf(wsCols, h.collider);
                if (ci < 0 || !WsUsable(ci) || h.distance >= bd) continue;
                bd = h.distance; best = new WsHit { ci = ci, p = h.point };
            }
            if (best.ci >= 0 && !partOk(wsColPart[best.ci])) best.ci = -1;
            return best;
        }

        /// <summary>n points from above along the dog's forward, centre +- half; missing rays are skipped (at least min(2, n) needed).</summary>
        static List<WsAnchor> HwTopLine(Vector3 center, float half, int n, Func<int, bool> partOk, string what)
        {
            var fwd = WsFlat(wsRoot.forward); var r = new List<WsAnchor>();
            for (int k = 0; k < n; k++)
            {
                float x = n == 1 ? 0f : Mathf.Lerp(-half, half, k / (float)(n - 1));
                var h = HwDownHit(center + fwd * x, partOk);
                if (h.ci >= 0) r.Add(WsAnchorOf(h));
            }
            if (r.Count < Mathf.Min(2, n)) throw new InvalidOperationException("rays from above do not hit " + what + " of the Shiba around " + center.ToString("F2") + " (" + r.Count + " of " + n + ")");
            return r;
        }

        /// <summary>The top of a part: straight down at the centre, else at +-0.02 .. 0.08 m along the forward.</summary>
        static WsAnchor HwTop(Vector3 center, Func<int, bool> partOk, string what)
        {
            var fwd = WsFlat(wsRoot.forward);
            for (int k = 0; k <= 8; k++)
            {
                float x = (k + 1) / 2 * 0.02f * (k % 2 == 0 ? 1f : -1f);
                var h = HwDownHit(center + fwd * x, partOk);
                if (h.ci >= 0) return WsAnchorOf(h);
            }
            throw new InvalidOperationException("no ray from above hits " + what + " of the Shiba (first hits are another part)");
        }

        static Vector3 HwPartCenter(int part)
        {
            bool found; var c = WsPartCenter(part, out found);
            if (!found) throw new InvalidOperationException("no collider of part " + part);
            return c;
        }

        static WsAnchor HwPathAnchor(float u)
        {
            var h = WsPath(u);
            if (h.ci < 0) throw new InvalidOperationException("the flank ray at u=" + u.ToString("F2") + " misses the Shiba");
            return WsAnchorOf(h);
        }

        /// <summary>The part with the longest stretch along the right-flank path (WsPath): its first and last ray hits.</summary>
        static void HwSamePart(out int part, out float u1, out float u2, out float sepM)
        {
            var first = new Dictionary<int, float>(); var last = new Dictionary<int, float>(); var firstM = new Dictionary<int, Vector3>(); var lastM = new Dictionary<int, Vector3>();
            for (int k = 0; k <= 50; k++)
            {
                float u = k / 50f; var h = WsPath(u);
                if (h.ci < 0) continue;
                int p = wsColPart[h.ci]; var m = WsToMesh(h.ci, h.p);
                if (!first.ContainsKey(p)) { first[p] = u; firstM[p] = m; }
                last[p] = u; lastM[p] = m;
            }
            part = -1; u1 = u2 = 0f; sepM = 0f;
            foreach (var p in first.Keys)
            {
                float d = (lastM[p] - firstM[p]).magnitude * wsMts;
                if (d > sepM) { sepM = d; part = p; u1 = first[p]; u2 = last[p]; }
            }
            if (part < 0 || sepM < 0.08f) throw new InvalidOperationException("no part spans 0.08 m along the right flank (best part " + part + ", " + sepM.ToString("F3") + " m)");
        }

        // ============================================================
        // ボトル・呼び出し
        // ============================================================

        /// <summary>One dose: _DebugSquirt from 0.4 x bottleReach above the anchor, straight down. Checks that it landed on the Shiba this
        /// frame (bottle channel 3's history) near the aimed point.</summary>
        static HwDose HwSquirt(WsAnchor a)
        {
            var h = WsAt(a); float lift = 0.4f * hwBottleReach;
            HwInvoke(hwBottle, "_DebugSquirt", "origin", h.p + Vector3.up * lift, "direction", Vector3.down);
            var ht = Get<float[]>(wsWash, "_hTime"); var hc = Get<int[]>(wsWash, "_hCollider"); var hm = Get<Vector3[]>(wsWash, "_hMesh");
            WsExpect(ht.Length > HwChBottle && Mathf.Abs(ht[HwChBottle] - Time.time) < 1e-4f, "_DebugSquirt landed on the Shiba this frame (bottle channel 3 _hTime " + (ht.Length > HwChBottle ? ht[HwChBottle].ToString("F3") : "-") + ", now " + Time.time.ToString("F3") + "; cast from " + lift.ToString("F2") + " m above " + wsCols[h.ci].name + ")");
            int ci = hc[HwChBottle];
            WsExpect(WsUsable(ci), "the dose's collider is a Shiba part (" + ci + ")");
            var d = new HwDose { ci = ci, part = wsColPart[ci], mesh = hm[HwChBottle] };
            float off = (d.mesh - WsToMesh(h.ci, h.p)).magnitude * wsMts;
            WsExpect(off < 0.1f, "the dose landed near the aimed point (" + off.ToString("F3") + " m off)");
            return d;
        }

        static bool HwPlausible(bool vr, Vector3 l, Vector3 r, Vector3 head, int part, Vector3 a, Vector3 b)
        {
            HwInvoke(wsWash, "_DebugHandPlausible", "senderVr", vr, "handL", l, "handR", r, "head", head, "part", part, "worldA", a, "worldB", b);
            return HwRet<bool>(wsWash, "_DebugHandPlausible");
        }

        static float HwStrokeAge(int playerId)
        {
            HwInvoke(wsWash, "_StrokeAgeOf", "playerId", playerId);
            return HwRet<float>(wsWash, "_StrokeAgeOf");
        }

        static int HwSenderSeq(int playerId)
        {
            var id = Get<int[]>(wsWash, "_sndId"); var sq = Get<int[]>(wsWash, "_sndSeq");
            for (int i = 0; i < id.Length && i < sq.Length; i++) if (id[i] == playerId) return sq[i];
            return int.MinValue;
        }

        static int HwAppliedSeq(int playerId)
        {
            var id = Get<int[]>(wsWash, "appliedIds"); var sq = Get<int[]>(wsWash, "appliedSeqs");
            for (int i = 0; i < id.Length && i < sq.Length; i++) if (id[i] == playerId && playerId != 0) return sq[i];
            return int.MinValue;
        }

        /// <summary>KH_DogWash.Pack (mesh point -> 3 x 10 bit around the part's centre, +-packHalfMeters).</summary>
        static int HwPack(int part, Vector3 m)
        {
            var c = Get<Vector3[]>(wsWash, "_partCenter")[part];
            float inv = wsMts / Mathf.Max(0.01f, Get<float>(wsWash, "packHalfMeters"));
            Func<float, int> q = f => Mathf.Clamp(Mathf.RoundToInt(f * 511.5f + 511.5f), 0, 1023);
            return q((m.x - c.x) * inv) | (q((m.y - c.y) * inv) << 10) | (q((m.z - c.z) * inv) << 20);
        }

        // ---- U# の呼び出し (WsCall と同じ考え方を、任意の UdonBehaviour と bool の引数に) ----

        static VRC.Udon.Common.Interfaces.IUdonProgram HwProg(UdonBehaviour u)
        {
            var p = u.programSource != null && u.programSource.SerializedProgramAsset != null ? u.programSource.SerializedProgramAsset.RetrieveProgram() : null;
            if (p == null) throw new InvalidOperationException(u.name + ": program not retrievable");
            return p;
        }

        static string HwEntry(UdonBehaviour u, string method)
        {
            string key = u.programSource.name + "|e|" + method, e;
            if (hwSyms.TryGetValue(key, out e)) return e;
            var prog = HwProg(u);
            if (WsHas(prog.EntryPoints, method)) e = method;
            else
            {
                var m = WsMangled(prog.EntryPoints, method);
                if (m.Count != 1) throw new InvalidOperationException(u.programSource.name + " entry point " + method + ": " + m.Count + " matches (an older program? run Wash/10 and compile)");
                e = m[0];
            }
            hwSyms[key] = e; return e;
        }

        static string[] HwParam(UdonBehaviour u, string name)
        {
            string key = u.programSource.name + "|p|" + name; string[] r;
            if (hwParams.TryGetValue(key, out r)) return r;
            r = WsMangled(HwProg(u).SymbolTable, name + "__param").ToArray();
            if (r.Length == 0) throw new InvalidOperationException(u.programSource.name + " has no parameter symbol for " + name);
            hwParams[key] = r; return r;
        }

        static string HwRetSym(UdonBehaviour u, string method)
        {
            string key = u.programSource.name + "|r|" + method, e;
            if (hwSyms.TryGetValue(key, out e)) return e;
            var t = HwProg(u).SymbolTable;
            var m = WsMangled(t, method + "__ret");
            for (int k = 0; m.Count == 0 && k <= 5; k++) m = WsMangled(t, "__" + k + "_" + method + "__ret");
            if (m.Count != 1) throw new InvalidOperationException(u.programSource.name + " return symbol of " + method + ": " + m.Count + " matches");
            hwSyms[key] = m[0]; return m[0];
        }

        static void HwInvoke(UdonBehaviour u, string method, params object[] nameValue)
        {
            for (int i = 0; i + 1 < nameValue.Length; i += 2)
                foreach (var sym in HwParam(u, (string)nameValue[i]))
                {
                    var v = nameValue[i + 1];
                    if (v is int iv) u.SetProgramVariable<int>(sym, iv);
                    else if (v is float fv) u.SetProgramVariable<float>(sym, fv);
                    else if (v is bool bv) u.SetProgramVariable<bool>(sym, bv);
                    else if (v is Vector3 vv) u.SetProgramVariable<Vector3>(sym, vv);
                    else throw new InvalidOperationException("unsupported argument type " + v);
                }
            u.SendCustomEvent(HwEntry(u, method));
        }

        static T HwRet<T>(UdonBehaviour u, string method)
        {
            object o;
            if (!u.TryGetProgramVariable(HwRetSym(u, method), out o) || !(o is T)) throw new InvalidOperationException("return of " + method + " unreadable (" + o + ")");
            return (T)o;
        }

        // ============================================================
        // 観測
        // ============================================================

        static int HwInt(string key) { return Get<int>(wsWash, key); }
        static float[] HwFoam() { return (float[])Get<float[]>(wsWash, "_foam").Clone(); }
        static float HwMaskAt(Color[] mask, Vector3 m, int part) { float dm; return WsLocal(m, part, null, mask, 1, out dm); }
        static int HwNearestCell(Vector3 m) { return WsNearestCell(Get<Vector3[]>(wsWash, "cellCenter"), Get<bool[]>(wsWash, "cellValid"), m); }
        static float HwCenterCell(HwDose d) { int c = HwNearestCell(d.mesh); return c >= 0 ? Get<float[]>(wsWash, "_foam")[c] : -1f; }

        static List<int> HwCellsNear(Vector3 m, float rM)
        {
            var cc = Get<Vector3[]>(wsWash, "cellCenter"); var valid = Get<bool[]>(wsWash, "cellValid"); var r = new List<int>();
            for (int c = 0; c < cc.Length && c < valid.Length; c++) if (valid[c] && (cc[c] - m).magnitude * wsMts <= rM) r.Add(c);
            return r;
        }
        static List<int> HwCellsFar(List<Vector3> pts, float rM)
        {
            var cc = Get<Vector3[]>(wsWash, "cellCenter"); var valid = Get<bool[]>(wsWash, "cellValid"); var r = new List<int>();
            for (int c = 0; c < cc.Length && c < valid.Length; c++) if (valid[c] && WsMinDist(cc[c], pts) * wsMts > rM) r.Add(c);
            return r;
        }
        static float HwCellMax(float[] f, Vector3 m, float rM) { float x = 0f; foreach (int c in HwCellsNear(m, rM)) x = Mathf.Max(x, f[c]); return x; }
        static int HwCountUp(float[] a, float[] b, float eps) { int n = 0; for (int c = 0; c < a.Length && c < b.Length; c++) if (b[c] > a[c] + eps) n++; return n; }
        static int HwCountAbove(float[] f, float thr)
        {
            var valid = Get<bool[]>(wsWash, "cellValid"); int n = 0;
            for (int c = 0; c < f.Length && c < valid.Length; c++) if (valid[c] && f[c] >= thr) n++;
            return n;
        }

        /// <summary>Mean G of the valid texels within rM (m) of any of the mesh points.</summary>
        static float HwMaskMean(Color[] m, List<Vector3> pts, float rM, out int n)
        {
            float r2 = (rM / wsMts) * (rM / wsMts), sum = 0f; n = 0;
            for (int k = 0; k < wsTexCount; k++)
            {
                bool near = false; foreach (var p in pts) if ((wsTexPos[k] - p).sqrMagnitude <= r2) { near = true; break; }
                if (!near) continue;
                n++; sum += m[wsTexIdx[k]].g;
            }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>The largest increase of a channel over the valid texels.</summary>
        static float HwMaxUp(Color[] a, Color[] b, int ch)
        {
            float x = 0f;
            for (int k = 0; k < wsTexCount; k++) { int i = wsTexIdx[k]; x = Mathf.Max(x, WsCh(b[i], ch) - WsCh(a[i], ch)); }
            return x;
        }

        // ============================================================
        // 図解
        // ============================================================

        static void HwSetLang(string code)
        {
            var codes = Get<string[]>(hwLang, "languageCodes");
            int i = Array.IndexOf(codes, code);
            if (i < 0) throw new InvalidOperationException("KD_Language has no " + code);
            hwLang.SetProgramVariable<int>("_languageIndex", i);
            hwBoard.SetProgramVariable<float>("_nextAt", 0f);   // the board's next Update reloads the words (it is within showMeters)
        }

        /// <summary>The board word w in language index li, as KH_WashStationBoard.LoadWords would load it (KD_Language.TextAt, else the builder's ja).</summary>
        static string HwWord(int w, int li)
        {
            var keys = Get<string[]>(hwBoard, "wordKeys"); var words = Get<string[]>(hwBoard, "words");
            string key = keys[w], t = HwLangText(key, li);
            return string.IsNullOrEmpty(t) || t == key ? words[w] : t;
        }

        static T HwTry<T>(UdonBehaviour u, string key) where T : class { try { return Get<T>(u, key); } catch (Exception) { return null; } }

        static string HwLangText(string key, int li)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var codes = Get<string[]>(hwLang, "languageCodes"); var keys = HwTry<string[]>(hwLang, "keys"); var texts = HwTry<string[]>(hwLang, "texts");
            var ek = HwTry<string[]>(hwLang, "extensionKeys"); var et = HwTry<string[]>(hwLang, "extensionTexts"); string fb = HwTry<string>(hwLang, "fallbackLanguage") ?? "ja";
            int idx = keys != null ? Array.LastIndexOf(keys, key) : -1;   // the runtime's DataDictionary keeps the last duplicate
            if (idx < 0)
            {
                int k = ek != null ? Array.IndexOf(ek, key) : -1;
                if (k < 0) return key;
                int at = li * ek.Length + k;
                if (et == null || at >= et.Length) return key;
                if (!string.IsNullOrEmpty(et[at])) return et[at];
                int l = Array.IndexOf(codes, fb);
                return l >= 0 && l * ek.Length + k < et.Length ? et[l * ek.Length + k] : key;
            }
            string v = texts[li * keys.Length + idx];
            if (!string.IsNullOrEmpty(v)) return v;
            int f = Array.IndexOf(codes, fb);
            return f >= 0 ? texts[f * keys.Length + idx] : key;
        }

        /// <summary>The 9 guide labels read the words of that language and device, and pictures ② ③ show that device's cells
        /// (IMP-UI-003: guideUvVr2 / guideUvFlat2, guideUvVr3 / guideUvFlat3).</summary>
        static string HwCheckGuide(string code, bool vr)
        {
            var codes = Get<string[]>(hwLang, "languageCodes"); int li = Array.IndexOf(codes, code);
            WsExpect(Get<string[]>(hwLang, "languageCodes")[Get<int>(hwLang, "_languageIndex")] == code, "the language is " + code);
            int[] want =
            {
                KH_WashStationBoard.WGuideStep1, KH_WashStationBoard.WGuideStep1Sub,
                vr ? KH_WashStationBoard.WGuideStep2 : KH_WashStationBoard.WGuideStep2Flat, vr ? KH_WashStationBoard.WGuideStep2Sub : KH_WashStationBoard.WGuideStep2FlatSub,
                vr ? KH_WashStationBoard.WGuideStep3 : KH_WashStationBoard.WGuideStep3Flat, vr ? KH_WashStationBoard.WGuideStep3Sub : KH_WashStationBoard.WGuideStep3FlatSub,
                KH_WashStationBoard.WGuideStep4, KH_WashStationBoard.WGuideStep4Sub,
                vr ? KH_WashStationBoard.WGuideHelp : KH_WashStationBoard.WGuideHelpFlat,
            };
            var labels = WsArr<TMPro.TextMeshProUGUI>(hwBoard, "guideLabels");
            var bad = new List<string>();
            for (int i = 0; i < want.Length; i++) { string w = HwWord(want[i], li), got = labels[i].text; if (got != w) bad.Add(labels[i].name + " '" + got + "' != '" + w + "'"); }
            WsExpect(bad.Count == 0, code + (vr ? " VR" : " Desktop") + ": every guide label reads its word (" + string.Join("; ", bad) + ")");
            var pics = WsArr<UnityEngine.UI.RawImage>(hwBoard, "guidePics");
            var uvs = new List<string>();
            foreach (var pv in new[] { new KeyValuePair<int, string>(1, vr ? "guideUvVr2" : "guideUvFlat2"), new KeyValuePair<int, string>(2, vr ? "guideUvVr3" : "guideUvFlat3") })
            {
                Rect uv = pics[pv.Key].uvRect, wantUv = Get<Rect>(hwBoard, pv.Value);
                WsExpect(Vector4.Distance(new Vector4(uv.x, uv.y, uv.width, uv.height), new Vector4(wantUv.x, wantUv.y, wantUv.width, wantUv.height)) < 1e-4f,
                    code + (vr ? " VR" : " Desktop") + ": picture " + (pv.Key + 1) + " shows " + pv.Value + " " + wantUv + " (" + uv + ")");
                uvs.Add(uv.ToString());
            }
            return code + (vr ? " VR" : " Desktop") + ": ② '" + labels[2].text + "' / '" + labels[3].text + "', ③ '" + labels[4].text + "' / '" + labels[5].text + "', help '" + labels[8].text + "', uv " + string.Join(" ", uvs);
        }
    }
}
