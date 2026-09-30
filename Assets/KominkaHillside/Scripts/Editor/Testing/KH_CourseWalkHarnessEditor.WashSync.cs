using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-026 洗浄の状態と同期のケース (Course/100 に追加。MakeWashCases の最後、WsWrite() の前から MakeWashSyncCases() を呼ぶ)。
    ///
    /// ClientSim は 1 クライアント (自分 = すべての Owner) なので、途中参加・リセットの受信・Owner の交代・2 人の送り主は **模擬**:
    /// 同期変数 (cells / epoch) とローカルの状態 (_gotFirst) をヒープへ直接書き、U# のイベント (_onPreSerialization / _onDeserialization /
    /// _onOwnershipTransferred) を SendCustomEvent で呼ぶ。2 人の送り主は _DebugAcceptSender へ作った playerId を渡す。
    /// 実際の到着順・Owner 移譲の承認・他のクライアントの描画はここでは通らない (82b の debugHeldOverride と同じ扱い。実通信の合格にしない)。
    /// 乾きは foamDrySeconds / wetDrySeconds を実行中に縮めて見る (ケースの最後と次のケースの最初に戻す)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        const int WssCenter = 511 | (511 << 10) | (511 << 20), WssAmount = 300 | (90 << 12);
        static float wssFoamDry = -1f, wssWetDry = -1f, wssArmFrac = -1f;

        static void MakeWashSyncCases()
        {
            wsReport.notCovered.Add("IMP-DOG-026 sync cases (W-latejoin / W-reset / W-handover / W-budget) are single-client mocks: synced variables and local flags written into the heap and the U# events called directly; real arrival order, ownership approval, other clients' masks -> PCVR with 2 people");

            // ---- W-decay (乾き: 洗っている最中は泡が減らず濡れは減る。止めた後は泡も減る。汚れは減らない。長く放置しても負・NaN にならない) ----
            var s = WsCase("W-decay", 70);
            WsAnchor dcA = default; var dcT = new WsTrace();
            float[] dcD0 = null, dcF0 = null, dcW0 = null, dcF1 = null; Color[] dcM0 = null, dcM1 = null;
            s.Add(WsDo(() =>
            {
                WssRestoreTuning();
                WsPlaceShiba(); WsPlayerNear(1.3f); WsSetDirt(0.6f);
                wssFoamDry = Get<float>(wsWash, "foamDrySeconds"); wssWetDry = Get<float>(wsWash, "wetDrySeconds");
                // 濡れは 30 s: スポンジの濡れは +0.2/s しか入らないので、8 s (1 刻み -0.25) だと写しの前の刻み 1 回で 0 になる (刻みの位相で PASS / FAIL が揺れた)
                wsWash.SetProgramVariable<float>("foamDrySeconds", 8f); wsWash.SetProgramVariable<float>("wetDrySeconds", 30f);
                dcA = WsAnchorOf(WsPathHit(0.5f));
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDrive(1f, 1, u => WsAt(dcA), WsSponge, -1f, dcT));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.7));   // the echoes of the last segments are integrated by now (8 Hz send)
            s.Add(WsDo(() =>
            {
                dcD0 = WssCells("_dirt"); dcF0 = WssCells("_foam"); dcW0 = WssCells("_wet"); dcM0 = WsMask();
                WsExpect(WsHold(), "_WashHold() true right after the strokes");
                WsExpect(dcF0.Sum() > 0.01f && dcW0.Sum() > 0.01f, "the sponge put foam and wet into the cells (foam " + dcF0.Sum().ToString("F3") + ", wet " + dcW0.Sum().ToString("F3") + ")");
            }));
            // 最後のストロークから holdSeconds - 0.1 s まで (写しから約 2.2 s = 乾きの刻み 2 s が少なくとも 1 回入る)
            s.Add(WsUntil(() => Time.time - Get<float>(wsWash, "_lastStrokeTime") >= wsHoldSeconds - 0.1f, 10, () => "hold window did not pass"));
            s.Add(WsDo(() =>
            {
                var f1 = WssCells("_foam"); var w1 = WssCells("_wet"); var d1 = WssCells("_dirt"); dcM1 = WsMask(); dcF1 = f1;
                float dm, g = WsLocal(dcT.mesh[0], dcT.part[0], dcM0, dcM1, 1, out dm), b = WsLocal(dcT.mesh[0], dcT.part[0], dcM0, dcM1, 2, out dm);
                WsExpect(Mathf.Abs(f1.Sum() - dcF0.Sum()) < 1e-4f, "cells: foam unchanged while washing (" + dcF0.Sum().ToString("F4") + " -> " + f1.Sum().ToString("F4") + ")");
                WsExpect(w1.Sum() < dcW0.Sum() - 1e-4f, "cells: wet dried while washing (a drying tick ran: " + dcW0.Sum().ToString("F4") + " -> " + w1.Sum().ToString("F4") + ")");
                WsExpect(WssSame(dcD0, d1), "cells: dirt unchanged by drying");
                WsExpect(g > -0.01f, "mask: foam at the stroke not reduced while washing (dG " + g.ToString("F3") + ")");
                WsExpect(b < -0.01f, "mask: wet at the stroke dried while washing (dB " + b.ToString("F3") + ")");
                WsNote("while washing (within holdSeconds of the last stroke, foam dry 8 s, wet dry 30 s): cell foam " + dcF0.Sum().ToString("F3") + " -> " + f1.Sum().ToString("F3") + ", cell wet " + dcW0.Sum().ToString("F3") + " -> " + w1.Sum().ToString("F3") + ", mask dG " + g.ToString("F3") + " dB " + b.ToString("F3") + ", dirt unchanged");
            }));
            s.Add(WsUntil(() => Time.time - Get<float>(wsWash, "_lastStrokeTime") > wsHoldSeconds && !WsHold(), 10, () => "the hold did not end"));
            s.Add(WsWait(4.5));
            s.Add(WsDo(() =>
            {
                var f2 = WssCells("_foam"); var d2 = WssCells("_dirt"); var m2 = WsMask();
                float dm, g = WsLocal(dcT.mesh[0], dcT.part[0], dcM1, m2, 1, out dm);
                WsExpect(f2.Sum() < dcF1.Sum() - 0.01f, "cells: foam dried after the hold (" + dcF1.Sum().ToString("F3") + " -> " + f2.Sum().ToString("F3") + ")");
                WsExpect(g < -0.05f, "mask: foam at the stroke dried after the hold (dG " + g.ToString("F3") + ")");
                WsExpect(WssSame(dcD0, d2), "cells: dirt unchanged by drying");
                WsNote("after the hold: cell foam " + dcF1.Sum().ToString("F3") + " -> " + f2.Sum().ToString("F3") + ", mask dG " + g.ToString("F3") + ", dirt unchanged");
                // 長い放置の模擬: 乾きを 1 s にして 7 s (刻み 3 回 x 2 = 泡・濡れ 6 単位 = 乾き 120 s で 6 分以上ぶん)
                wsWash.SetProgramVariable<float>("foamDrySeconds", 1f); wsWash.SetProgramVariable<float>("wetDrySeconds", 1f);
            }));
            s.Add(WsWait(7.0));
            s.Add(WsDo(() =>
            {
                var d = WssCells("_dirt"); var f = WssCells("_foam"); var w = WssCells("_wet"); var m = WsMask();
                int bad = 0; for (int c = 0; c < f.Length; c++) if (!(f[c] == 0f) || !(w[c] == 0f) || float.IsNaN(d[c]) || d[c] < 0f) bad++;
                WsExpect(bad == 0, "cells: foam and wet exactly 0, dirt finite and >= 0 after a long dry (" + bad + " cells off)");
                WsExpect(WssSame(dcD0, d), "cells: dirt still unchanged");
                int mBad = 0; float gMax = 0f, bMax = 0f;
                for (int k = 0; k < wsTexCount; k++)
                {
                    var p = m[wsTexIdx[k]];
                    if (float.IsNaN(p.r) || float.IsNaN(p.g) || float.IsNaN(p.b) || p.r < 0f || p.g < 0f || p.b < 0f || p.r > 1f) mBad++;
                    gMax = Mathf.Max(gMax, p.g); bMax = Mathf.Max(bMax, p.b);
                }
                WsExpect(mBad == 0, "mask: no NaN / out of 0..1 texel (" + mBad + ")");
                WsExpect(gMax <= 1e-3f && bMax <= 1e-3f, "mask: foam and wet gone (max G " + gMax.ToString("F4") + ", B " + bMax.ToString("F4") + ")");
                WsNote("long dry (dry 1 s, 7 s): cells foam / wet 0, dirt unchanged, mask max G " + gMax.ToString("F4") + " B " + bMax.ToString("F4") + ", no NaN / negative");
                WssRestoreTuning();
            }));

            // ---- W-armed-local (足だけ泡 → 構える → 足だけ流す → 合図 +1。少しの泡と流しでは再び出ない) ----
            s = WsCase("W-armed-local", 160);
            int alS0 = 0; List<WsAnchor> alPts = null; float alShare = 0f, alDirtyFoam = 0.3f;
            s.Add(WsDo(() =>
            {
                WssRestoreTuning();
                WsPlaceShiba(); WsPlayerNear(1.5f); wsWash.SendCustomEvent("_DebugClear");
                alDirtyFoam = Get<float>(wsWash, "dirtyFoamThreshold");
                var valid = Get<bool[]>(wsWash, "cellValid"); var cp = Get<int[]>(wsWash, "cellPart");
                int n = 0, legs = 0; for (int c = 0; c < valid.Length && c < cp.Length; c++) if (valid[c]) { n++; if (cp[c] >= 3 && cp[c] <= 6) legs++; }
                alShare = n > 0 ? legs / (float)n : 0f;
                float frac = Get<float>(wsWash, "armFoamCellFraction");
                if (alShare < frac * 1.5f)
                {
                    wssArmFrac = frac;
                    wsWash.SetProgramVariable<float>("armFoamCellFraction", alShare * 0.5f);
                    WsNote("legs are " + alShare.ToString("P0") + " of the valid cells (< 1.5 x armFoamCellFraction " + frac + "): the fraction is set to " + (alShare * 0.5f).ToString("F3") + " for this case");
                }
                alPts = WsCover(0.05f).Where(a => wsColPart[a.ci] >= 3 && wsColPart[a.ci] <= 6).ToList();
                if (alPts.Count == 0) throw new InvalidOperationException("no leg surface points");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { alS0 = WsSerial(); WsExpect(!Get<bool>(wsWash, "cleanArmed"), "not armed after _DebugClear"); }));
            s.Add(WsSweep(() => alPts, WsSponge, -1f, 0.6f, 60f, () => Get<bool>(wsWash, "cleanArmed"),
                () => "foaming the legs did not arm in 60 s: avg foam " + WsAvgFoam().ToString("F3") + ", " + WssFoamedCells(alDirtyFoam) + " cells >= " + alDirtyFoam));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(WsSerial() == alS0, "foam alone does not count (" + alS0 + " -> " + WsSerial() + ")");
                float af = WsAvgFoam(); int set = WssArmCount(); int setOff = WssArmOutside(3, 6);
                if (alShare < 0.3f) WsExpect(af < alDirtyFoam, "armed by the legs alone while the whole-body average foam stays below " + alDirtyFoam + " (" + af.ToString("F3") + ")");
                WsExpect(set > 0, "the arm set holds the foamed cells (" + set + ")");
                WsNote("legs (" + alShare.ToString("P0") + " of the cells, " + alPts.Count + " points) foamed: armed with avg foam " + af.ToString("F3") + ", arm set " + set + " cells (" + setOff + " outside parts 3-6: neighbour reach)");
            }));
            s.Add(WsSweep(() => alPts, WsShower, 0.2f, 0.5f, 60f, () => WsSerial() == alS0 + 1,
                () => "rinsing the legs gave no edge in 60 s: avg foam " + WsAvgFoam().ToString("F3") + ", avg dirt " + WsAvgDirt().ToString("F3") + ", arm set " + WssArmCount() + ", serial " + alS0 + " -> " + WsSerial()));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(WsSerial() == alS0 + 1, "_CleanSerial() +1 exactly after rinsing the legs (" + alS0 + " -> " + WsSerial() + ")");
                WsExpect(!Get<bool>(wsWash, "cleanArmed") && WssArmCount() == 0, "disarmed and the arm set cleared");
            }));
            // ヒステリシス: 1 点だけ少し泡を付けて流す (構える数に届かない) → 合図は増えない
            s.Add(WsDrive(0.4f, 1, u => WsAt(alPts[0]), WsSponge, -1f, null));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsDrive(1f, 1, u => WsAt(alPts[0]), WsShower, 0.2f, null));
            s.Add(WsDo(() => WsEnd(WsShower)));
            s.Add(WsWait(1.5));
            s.Add(WsDo(() =>
            {
                WsExpect(WsSerial() == alS0 + 1, "a small re-foam and rinse near the threshold gives no second edge (" + (alS0 + 1) + " -> " + WsSerial() + ")");
                WsNote("rinsed the legs -> serial " + alS0 + " -> " + (alS0 + 1) + "; a 0.4 s dab + 1 s rinse at one point afterwards: serial stays " + WsSerial() + ", armed " + Get<bool>(wsWash, "cleanArmed"));
                WssRestoreTuning();
            }));

            // ---- W-latejoin (模擬: _gotFirst=false の間に自分で洗い、ストローク前の cells で最初の受信 → 塗り直しの後も自分の帯が残る。通常の受信では塗り直さない) ----
            s = WsCase("W-latejoin", 40);
            WsAnchor ljA = default; var ljT = new WsTrace(); byte[] ljCells = null; int[] ljApIds = null, ljApSeqs = null; int ljRc0 = 0, ljRp0 = 0; Color[] ljM0 = null; float ljG1 = 0f, ljG2 = 0f;
            s.Add(WsDo(() =>
            {
                WssRestoreTuning();
                WsPlaceShiba(); WsPlayerNear(1.3f); WsSetDirt(0.6f);
                ljA = WsAnchorOf(WsPathHit(0.5f));
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() =>
            {
                WssEvent("_onPreSerialization");
                ljCells = (byte[])Get<byte[]>(wsWash, "cells").Clone();
                ljApIds = (int[])Get<int[]>(wsWash, "appliedIds").Clone(); ljApSeqs = (int[])Get<int[]>(wsWash, "appliedSeqs").Clone();
                ljRc0 = Get<int>(wsWash, "_restoreCount"); ljRp0 = Get<int>(wsWash, "_replayCount");
                ljM0 = WsMask();
                wsWash.SetProgramVariable<bool>("_gotFirst", false);
            }));
            s.Add(WsDrive(1f, 1, u => WsAt(ljA), WsSponge, -1f, ljT));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.4));
            s.Add(WsDo(() =>
            {
                var m1 = WsMask(); float dm; ljG1 = WsLocal(ljT.mesh[0], ljT.part[0], ljM0, m1, 1, out dm);
                WsExpect(ljG1 > 0.1f, "the joiner's own band before the first receive (dG " + ljG1.ToString("F3") + ")");
                WsExpect(Get<int>(wsWash, "_lgCount") > 0, "the segments sent before the first receive were logged");
                // 受信 = ストローク前の版 (イベントは同期変数より速いので、最初の受信が自分のストロークを含まないことは実際に起こる)
                wsWash.SetProgramVariable<byte[]>("cells", (byte[])ljCells.Clone());
                WssSetApplied(ljApIds, ljApSeqs);   // the received version does not hold the strokes above
                WssEvent("_onDeserialization");
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                var m2 = WsMask(); float dm; ljG2 = WsLocal(ljT.mesh[0], ljT.part[0], ljM0, m2, 1, out dm);
                int rc = Get<int>(wsWash, "_restoreCount"), rp = Get<int>(wsWash, "_replayCount");
                WsExpect(rc == ljRc0 + 1, "the first receive repainted once (Restore " + ljRc0 + " -> " + rc + ")");
                WsExpect(rp > ljRp0, "the logged draws were drawn again (" + ljRp0 + " -> " + rp + ")");
                WsExpect(Get<bool>(wsWash, "_gotFirst") && Get<int>(wsWash, "_lgCount") == 0 && !Get<bool>(wsWash, "_replaying"), "_gotFirst set, the replay finished and the log emptied");
                WsExpect(ljG2 > 0.5f * ljG1, "the own band survived the repaint (dG " + ljG1.ToString("F3") + " -> " + ljG2.ToString("F3") + "; the received cells had no foam there)");
                int lid = Networking.LocalPlayer.playerId;
                WsExpect(WsRet<int>("_WasherPlayerId") == lid, "_WasherPlayerId() = the local player");
                float age = WssCallRet<float>("_StrokeAgeOf", "playerId", lid);
                WsExpect(age >= 0f && age < 3f, "_StrokeAgeOf(local) is the time since the last stroke (" + age.ToString("F2") + " s)");
                WsExpect(WssCallRet<float>("_StrokeAgeOf", "playerId", 123456) > 1e8f, "_StrokeAgeOf(unknown) is large");
                // 通常の受信 (同じ版、_gotFirst = true): 塗り直さない
                WssEvent("_onPreSerialization");
                WssEvent("_onDeserialization");
            }));
            s.Add(WsWait(0.4));
            s.Add(WsDo(() =>
            {
                var m3 = WsMask(); float dm, g3 = WsLocal(ljT.mesh[0], ljT.part[0], ljM0, m3, 1, out dm);
                int rc = Get<int>(wsWash, "_restoreCount");
                WsExpect(rc == ljRc0 + 1, "a normal receive does not repaint (Restore " + (ljRc0 + 1) + " -> " + rc + ")");
                WsExpect(g3 > ljG2 - 0.05f, "a normal receive keeps the band (dG " + ljG2.ToString("F3") + " -> " + g3.ToString("F3") + ")");
                WsNote("mock joiner (_gotFirst=false, sponge 1 s, first receive = the cells and appliedSeqs from before the strokes): band dG " + ljG1.ToString("F3") + " -> after the repaint " + ljG2.ToString("F3") + " (Restore +1, " + (Get<int>(wsWash, "_replayCount") - ljRp0) + " segments replayed) -> after a normal receive " + g3.ToString("F3") + " (no Restore). Mock artifact: this client is also the Owner, so its cells went back to the pre-stroke copy");
                // 2 回目: 最初の受信がストロークを含む (appliedSeqs が送った連番まで) → 重ねない
                wsWash.SetProgramVariable<bool>("_gotFirst", false);
            }));
            s.Add(WsDrive(0.6f, 1, u => WsAt(ljA), WsSponge, -1f, null));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.7));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wsWash, "_qCount") == 0, "every segment was sent (queue empty)");
                ljRp0 = Get<int>(wsWash, "_replayCount"); ljRc0 = Get<int>(wsWash, "_restoreCount");
                WssEvent("_onPreSerialization");   // the received version holds every stroke (appliedSeqs = the last seq)
                WssEvent("_onDeserialization");
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                int rp = Get<int>(wsWash, "_replayCount"), rc = Get<int>(wsWash, "_restoreCount");
                WsExpect(rc == ljRc0 + 1, "the first receive repainted (Restore " + ljRc0 + " -> " + rc + ")");
                WsExpect(rp == ljRp0, "segments the received cells already hold are not drawn again (" + (rp - ljRp0) + " replayed)");
                WsNote("second mock joiner whose first receive already holds its strokes: Restore +1, 0 segments replayed (no double foam)");
            }));

            // ---- W-reint (模擬: 古い版を受けた後に Owner になる → 記録から、受けたセルに無いストロークを積算し直す) ----
            s = WsCase("W-reint", 30);
            WsAnchor riA = default; byte[] riCells = null; int[] riIds = null, riSeqs = null; int riN0 = 0; float riFoamA = 0f;
            s.Add(WsDo(() => { WssRestoreTuning(); WsPlaceShiba(); WsPlayerNear(1.3f); WsSetDirt(0.6f); riA = WsAnchorOf(WsPathHit(0.5f)); }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() =>
            {
                WssEvent("_onPreSerialization");
                riCells = (byte[])Get<byte[]>(wsWash, "cells").Clone();
                riIds = (int[])Get<int[]>(wsWash, "appliedIds").Clone(); riSeqs = (int[])Get<int[]>(wsWash, "appliedSeqs").Clone();
                riN0 = Get<int>(wsWash, "_reintCount");
            }));
            s.Add(WsDrive(1f, 1, u => WsAt(riA), WsSponge, -1f, null));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.7));
            s.Add(WsDo(() =>
            {
                riFoamA = WssCells("_foam").Sum();
                WsExpect(riFoamA > 0.05f, "the strokes reached the cells (foam " + riFoamA.ToString("F3") + ")");
                wsWash.SetProgramVariable<byte[]>("cells", (byte[])riCells.Clone());
                WssSetApplied(riIds, riSeqs);
                WssEvent("_onDeserialization");   // an older version arrives (normal receive: no repaint)
                float fb = WssCells("_foam").Sum();
                WsExpect(fb < 0.2f * riFoamA, "the older receive replaced the cells (foam " + riFoamA.ToString("F3") + " -> " + fb.ToString("F3") + ")");
                WssOwnershipEvent();
                float fc = WssCells("_foam").Sum(); int nr = Get<int>(wsWash, "_reintCount") - riN0;
                WsExpect(nr > 0, "recorded strokes were re-integrated (" + nr + ")");
                WsExpect(fc >= 0.8f * riFoamA, "the new Owner's cells hold the strokes again (foam " + fb.ToString("F3") + " -> " + fc.ToString("F3") + ", before " + riFoamA.ToString("F3") + ")");
                WsNote("sponge 1 s, then an older receive (cells + appliedSeqs from before) and a handover to self: foam " + riFoamA.ToString("F3") + " -> " + fb.ToString("F3") + " -> re-integrated " + nr + " strokes -> " + fc.ToString("F3"));
            }));

            // ---- W-quiet (模擬: 全体で構え、8 割を流して 2 割 0.4 が残ったまま合図 → 残りの泡では再び構えない。残りが消えてから新しく付ければ構える) ----
            s = WsCase("W-quiet", 20);
            int quS0 = 0; bool[] quValid = null; List<int> quCells = null;
            s.Add(WsDo(() =>
            {
                WssRestoreTuning(); wsWash.SendCustomEvent("_DebugClear");
                quValid = Get<bool[]>(wsWash, "cellValid");
                var idx = Enumerable.Range(0, 256).Where(c => c < quValid.Length && quValid[c]).ToList();
                quCells = idx.Where((c, i) => i % 5 == 0).ToList();   // 20 %
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() =>
            {
                quS0 = WsSerial();
                var f = new float[256]; foreach (int c in quCells) f[c] = 0.4f;
                var bits = new int[8]; for (int c = 0; c < 256; c++) if (c < quValid.Length && quValid[c]) bits[c >> 5] |= 1 << (c & 31);
                wsWash.SetProgramVariable<float[]>("_foam", f);
                wsWash.SetProgramVariable<int[]>("armBits", bits);
                wsWash.SetProgramVariable<bool>("cleanArmed", true);
            }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() => WsExpect(WsSerial() == quS0 + 1 && !Get<bool>(wsWash, "cleanArmed"), "the edge fired with 20 % of the cells still at 0.4 (serial " + quS0 + " -> " + WsSerial() + ")")));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(!Get<bool>(wsWash, "cleanArmed"), "the left-over foam does not arm again");
                var f = new float[256]; wsWash.SetProgramVariable<float[]>("_foam", f);   // rinsed
            }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(WsSerial() == quS0 + 1 && !Get<bool>(wsWash, "cleanArmed"), "rinsing the rest gives no second edge (serial " + WsSerial() + ")");
                var f = new float[256]; foreach (int c in quCells) f[c] = 0.4f; wsWash.SetProgramVariable<float[]>("_foam", f);   // a new foaming
            }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<bool>(wsWash, "cleanArmed"), "fresh foam on the same 20 % arms again");
                WsNote("arm set = all valid cells, 20 % at foam 0.4: edge +1; the rest did not re-arm; after it was rinsed a fresh 0.4 on the same cells armed again");
                wsWash.SendCustomEvent("_DebugClear");
            }));

            // ---- W-release (_ReleaseHold: 保持が直ちに切れ、その後の新しいストロークで戻る) ----
            s = WsCase("W-release-sync", 30);
            WsAnchor reA = default;
            s.Add(WsDo(() => { WssRestoreTuning(); WsPlaceShiba(); WsPlayerNear(1.3f); WsSetDirt(0.6f); reA = WsAnchorOf(WsPathHit(0.5f)); }));
            s.Add(WsWait(1.2));
            s.Add(WsDrive(0.6f, 1, u => WsAt(reA), WsSponge, -1f, null));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(WsHold(), "held after the strokes");
                wsWash.SendCustomEvent("_ReleaseHold");
                WsExpect(!WsHold(), "_ReleaseHold ends the hold at once");
            }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() => WsExpect(!WsHold(), "still released 1 s later (the echoes of the earlier strokes did not hold again)")));
            s.Add(WsDrive(0.3f, 1, u => WsAt(reA), WsSponge, -1f, null));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.4));
            s.Add(WsDo(() =>
            {
                WsExpect(WsHold(), "a stroke after the release holds again");
                WsNote("hold true -> _ReleaseHold -> false at once and 1 s later -> a new 0.3 s stroke -> true");
            }));

            // ---- W-reset (模擬: epoch が進んだ受信で塗り直し、待ちと送信待ちを捨てる。古い epoch のストロークは連番と予算を使わない) ----
            s = WsCase("W-reset", 30);
            int rsEp = 0, rsRc0 = 0, rsS = 0, rsSlotSeq = 0; float[] rsD = null, rsF = null, rsW = null;
            s.Add(WsDo(() =>
            {
                WssRestoreTuning();
                WsPlayerNear(1.3f); WsSetDirt(0.6f);
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() =>
            {
                WssEvent("_onPreSerialization");
                rsEp = Get<int>(wsWash, "epoch"); rsRc0 = Get<int>(wsWash, "_restoreCount");
                int next = (rsEp + 1) & 0x3FFFFFFF; if (next == 0) next = 1;
                wsWash.SetProgramVariable<int>("epoch", next);
                WssEvent("_onDeserialization");
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                int ep = Get<int>(wsWash, "epoch"), rc = Get<int>(wsWash, "_restoreCount");
                WsExpect(rc == rsRc0 + 1, "the epoch change repainted once (Restore " + rsRc0 + " -> " + rc + ")");
                WsExpect(Get<int>(wsWash, "_appliedEpoch") == ep, "_appliedEpoch = the received epoch");
                WsExpect(Get<int>(wsWash, "_pdCount") == 0 && Get<int>(wsWash, "_qCount") == 0, "waiting draws and sends dropped");
                // 古い epoch (rsEp) のストローク: 予算の前で捨てる → 同じ連番の新しい epoch のストロークが通る
                rsS = Get<int>(wsWash, "_seq");
                rsSlotSeq = WssLocalSlotSeq();
                rsD = WssCells("_dirt"); rsF = WssCells("_foam"); rsW = WssCells("_wet");
                wsWash.SendCustomNetworkEvent(NetworkEventTarget.All, "NetStroke", WsSponge, 1, WssCenter, WssCenter, WssAmount, rsS, rsEp);
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(WsCellDiff(rsD, rsF, rsW) == 0, "a stroke of the old epoch did not reach the cells");
                WsExpect(WssLocalSlotSeq() == rsSlotSeq, "a stroke of the old epoch did not use the sender's sequence (" + rsSlotSeq + " -> " + WssLocalSlotSeq() + ")");
                wsWash.SendCustomNetworkEvent(NetworkEventTarget.All, "NetStroke", WsSponge, 1, WssCenter, WssCenter, WssAmount, rsS, Get<int>(wsWash, "epoch"));
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                int cd = WsCellDiff(rsD, rsF, rsW);
                WsExpect(cd > 0, "the same sequence number with the current epoch was accepted (" + cd + " cells changed)");
                WsNote("mock reset receive (epoch " + rsEp + " -> +1): Restore +1, queues empty; an old-epoch NetStroke with seq " + rsS + " was dropped before the sender check (slot seq kept " + rsSlotSeq + "), the same seq with the current epoch then changed " + cd + " cells");
            }));

            // ---- W-handover (模擬: 自分へ Owner が移った。新 Owner は塗り直さず、セル・合図・構えを引き継ぐ。状態を持たずに Owner になったら新しい epoch) ----
            s = WsCase("W-handover", 20);
            int hoRc0 = 0, hoSerial = 0, hoEp = 0; bool hoArmed = false; float[] hoD = null, hoF = null, hoW = null; int[] hoBits = null;
            s.Add(WsDo(() =>
            {
                WssRestoreTuning();
                WsPlayerNear(1.3f); WsSetDirt(0.6f);
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() =>
            {
                hoRc0 = Get<int>(wsWash, "_restoreCount"); hoSerial = WsSerial(); hoEp = Get<int>(wsWash, "epoch"); hoArmed = Get<bool>(wsWash, "cleanArmed");
                hoD = WssCells("_dirt"); hoF = WssCells("_foam"); hoW = WssCells("_wet"); hoBits = (int[])Get<int[]>(wsWash, "armBits").Clone();
                wsWash.SetProgramVariable<bool>("_syncDue", false);
                WssOwnershipEvent();
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wsWash, "_restoreCount") == hoRc0, "the new Owner does not repaint");
                WsExpect(WsCellDiff(hoD, hoF, hoW) == 0, "cells kept");
                WsExpect(WsSerial() == hoSerial && Get<bool>(wsWash, "cleanArmed") == hoArmed && Get<int>(wsWash, "epoch") == hoEp, "serial, armed and epoch kept");
                WsExpect(Get<int[]>(wsWash, "armBits").SequenceEqual(hoBits), "arm set kept");
                WsExpect(Get<bool>(wsWash, "_syncDue"), "the new Owner schedules a serialization");
                // 状態を受け取る前に Owner になった (途中参加の直後に Owner が抜けた)
                wsWash.SetProgramVariable<bool>("_gotFirst", false);
                wsWash.SetProgramVariable<int>("epoch", 0);
                WssOwnershipEvent();
                int ep = Get<int>(wsWash, "epoch");
                WsExpect(ep >= 0x10000000 && Get<int>(wsWash, "_appliedEpoch") == ep && Get<bool>(wsWash, "_gotFirst"), "an Owner without a state takes a fresh epoch (" + ep + ") and keeps its seed");
                WsExpect(Get<int>(wsWash, "_restoreCount") == hoRc0, "and does not repaint");
                WsNote("mock handover to self: no Restore, cells / serial " + hoSerial + " / armed " + hoArmed + " / arm set / epoch " + hoEp + " kept, sync scheduled; stateless handover -> fresh epoch " + ep);
                WsSetDirt(0.6f);   // leave a normal state behind
            }));

            // ---- W-budget (模擬: 2 人の送り主が 8 Hz で 0.25 s ずつ = どちらも全部通る。欲張りな 3 人目は予算で止まるが、2 人を飢えさせない) ----
            s = WsCase("W-budget", 20);
            const int BA = 90001, BB = 90002, BC = 90003, BN = 24;
            int bk = 0, accA = 0, accB = 0, accC = 0; float bNext = -1f, bT0 = 0f;
            s.Add(WsDo(() => WssRestoreTuning()));
            s.Add(now =>
            {
                float t = Time.time;
                if (bNext < 0f) { bNext = t; bT0 = t; }
                while (bk < BN && t >= bNext)
                {
                    if (WssAccept(BA, 1000 + bk, 0.25f)) accA++;
                    if (WssAccept(BB, 5000 + bk, 0.25f)) accB++;
                    if (WssAccept(BC, 9000 + bk, 1.0f)) accC++;
                    bk++; bNext += 0.125f;
                }
                return bk >= BN;
            });
            s.Add(WsDo(() =>
            {
                float el = Time.time - bT0;
                bool replay = WssAccept(BA, 1000 + BN - 1, 0.01f), older = WssAccept(BB, 5000, 0.01f);
                WsExpect(accA == BN && accB == BN, "both washers at 8 Hz x 0.25 s got every segment through (A " + accA + "/" + BN + ", B " + accB + "/" + BN + ")");
                WsExpect(accC < BN && accC <= Mathf.CeilToInt(3f + 2.2f * el + 1f), "the greedy sender (1 s per call) was limited by its own budget (" + accC + "/" + BN + " in " + el.ToString("F2") + " s)");
                WsExpect(!replay && !older, "a replayed / older sequence number is refused (replay " + replay + ", older " + older + ")");
                float ageA = WssCallRet<float>("_StrokeAgeOf", "playerId", BA);
                WsExpect(ageA >= 0f && ageA < 5f, "_StrokeAgeOf(sender A) follows the accepted strokes (" + ageA.ToString("F2") + " s)");
                WsNote("mock senders over " + el.ToString("F2") + " s: A " + accA + "/" + BN + ", B " + accB + "/" + BN + " (0.25 s each at 8 Hz = 2 s/s, budget 2.2 s/s per sender), greedy C " + accC + "/" + BN + " (1 s each); replay / older seq refused; _StrokeAgeOf(A) " + ageA.ToString("F2") + " s");
            }));
        }

        // ---- 部品 ----

        static void WssRestoreTuning()
        {
            if (wssFoamDry > 0f) { wsWash.SetProgramVariable<float>("foamDrySeconds", wssFoamDry); wssFoamDry = -1f; }
            if (wssWetDry > 0f) { wsWash.SetProgramVariable<float>("wetDrySeconds", wssWetDry); wssWetDry = -1f; }
            if (wssArmFrac >= 0f) { wsWash.SetProgramVariable<float>("armFoamCellFraction", wssArmFrac); wssArmFrac = -1f; }
        }

        static void WssSetApplied(int[] ids, int[] seqs)
        {
            wsWash.SetProgramVariable<int[]>("appliedIds", (int[])ids.Clone());
            wsWash.SetProgramVariable<int[]>("appliedSeqs", (int[])seqs.Clone());
        }

        static float[] WssCells(string key) { return (float[])Get<float[]>(wsWash, key).Clone(); }
        static bool WssSame(float[] a, float[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>U# のイベント (_onDeserialization など) を直接呼ぶ。入口が無ければ例外。</summary>
        static void WssEvent(string name)
        {
            if (!WsHas(wsEntryTable, name)) throw new InvalidOperationException("KH_DogWash has no entry point " + name);
            wsWash.SendCustomEvent(name);
        }
        static void WssOwnershipEvent()
        {
            const string param = "onOwnershipTransferredPlayer";
            if (!WsHas(wsSymTable, param)) throw new InvalidOperationException("KH_DogWash has no symbol " + param);
            wsWash.SetProgramVariable<VRCPlayerApi>(param, Networking.LocalPlayer);
            WssEvent("_onOwnershipTransferred");
        }

        static T WssCallRet<T>(string method, params object[] nameValue)
        {
            WsCall(method, nameValue);
            object o;
            if (!wsWash.TryGetProgramVariable(WsRetSym(method), out o) || !(o is T)) throw new InvalidOperationException("return of " + method + " unreadable (" + o + ")");
            return (T)o;
        }
        static bool WssAccept(int playerId, int seq, float sec) { return WssCallRet<bool>("_DebugAcceptSender", "playerId", playerId, "seq", seq, "sec", sec); }

        /// <summary>送り主の表のうち、自分の枠の最後の連番 (枠が無ければ int.MinValue)。</summary>
        static int WssLocalSlotSeq()
        {
            int lid = Networking.LocalPlayer.playerId;
            var ids = Get<int[]>(wsWash, "_sndId"); var seqs = Get<int[]>(wsWash, "_sndSeq");
            for (int i = 0; i < ids.Length; i++) if (ids[i] == lid) return seqs[i];
            return int.MinValue;
        }

        static int WssFoamedCells(float thr)
        {
            var f = Get<float[]>(wsWash, "_foam"); var valid = Get<bool[]>(wsWash, "cellValid"); int n = 0;
            for (int c = 0; c < f.Length && c < valid.Length; c++) if (valid[c] && f[c] >= thr) n++;
            return n;
        }
        static int WssArmCount()
        {
            var bits = Get<int[]>(wsWash, "armBits"); int n = 0;
            for (int c = 0; c < 256; c++) if ((bits[c >> 5] & (1 << (c & 31))) != 0) n++;
            return n;
        }
        /// <summary>構えの集合のうち、部位 lo..hi の外のセルの数。</summary>
        static int WssArmOutside(int lo, int hi)
        {
            var bits = Get<int[]>(wsWash, "armBits"); var cp = Get<int[]>(wsWash, "cellPart"); int n = 0;
            for (int c = 0; c < 256 && c < cp.Length; c++) if ((bits[c >> 5] & (1 << (c & 31))) != 0 && (cp[c] < lo || cp[c] > hi)) n++;
            return n;
        }
    }
}
