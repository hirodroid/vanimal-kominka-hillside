using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-029 汚れの発生と清潔の余韻 — Course/100 (Wash.cs) に足すケース。
    ///
    /// 枠・手順の部品・観測は Wash.cs のもの (WsCase / WsDo / WsUntil / WsSweep / WsMask / WsSetDirt ...)。ケースの一覧への接続は
    /// Wash.cs の MakeWashCases の末尾 (MakeWashSyncCases() の後、WsWrite() の前) で MakeWashDirtCases() を呼ぶ。ここでは一覧へ自分でつながない。
    ///
    /// 観測: セル (実行時のヒープの _dirt / _wet と cellPart / cellValid) の部位ごとの平均、Owner の数えた量 (_dirtWalkTotal /
    /// _dirtZoneTotal / _dirtWaterTotal)、止めた理由 (_dirtBlock: 0 なし / 1 無効 / 2 洗われ中 / 3 猶予 / 4 洗い場 / 5 水の中)、送った汚れの
    /// 数 (_dirtEvents)、描いた回数 (_dirtDraws)、マスク (位置マップの部位ごとの R の平均)。
    /// 試作値を一時的に変える (歩く距離 300 → 30 m、猶予、川の秒数、洗い場の半径)。各ケースの最初と最後に WdRestore で戻す。
    /// 犬を置く: WsDetachShiba の後に root を置き直し (歩行ハーネスの置き方と同じ注入)、近くに立って近接の停止で止める。離れたら置き直す。
    /// 水の中: KD_DogFetchController.waterPose = 1 + _debugPoseHold (Course/78 W4-leash-stale と同じ作法。陸の上で水の中の扱いにする)。
    ///
    /// ClientSim は 1 人: 汚れの NetStroke (道具 3) は自分へ返る分を捨てる経路しか通らない。他の人のマスクに描かれること・途中参加は対象外。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        static readonly Dictionary<string, float> wdSaved = new Dictionary<string, float>();
        static Transform wdMovedZone;
        static Vector3 wdMovedFrom;
        static UdonBehaviour wdFetch;
        static bool wdPoseHeld;

        static void MakeWashDirtCases()
        {
            wdSaved.Clear(); wdMovedZone = null; wdPoseHeld = false;
            wdFetch = WsRef(wsWander, "fetchController") as UdonBehaviour;
            int me = Networking.LocalPlayer.playerId;
            var n = wsReport.notCovered;
            n.Add("IMP-DOG-029: another client drawing the Owner's dirt NetStroke (tool 3) into its mask, a late joiner's repaint of dirt, the dirt after an Owner change (the grace carries by the synced cleanMs) -> PCVR with 2 people");
            n.Add("IMP-DOG-029: the look of the dirt (the clump pattern, whether the whole leg is covered by the part-sized brush), the Fetch / disc distance (only the leash is driven here), swimming (pose 2), and the feel of 300 m / 10 s -> Play / PCVR");

            // ---- W-dirt-zone (泥の地点の外では増えず、中に 10 s で足 0.4〜0.6、胴 0.1〜0.3。マスクは足と胴だけ) ----
            var s = WsCase("W-dirt-zone", 70);
            Transform zn = null; float znR = 0f; Vector3 znOut = Vector3.zero; float zLeg0 = 0f, zBody0 = 0f, zT0 = 0f, zT1 = 0f; int zOutBlock = -1, zRe = 0, zEv0 = 0;
            Color[] zm0 = null;
            s.Add(WsDo(() =>
            {
                WdRestore();
                zn = WdZone(0, out znR);
                WdSet("dirtGraceSeconds", 0f);   // an earlier clean edge (W-clean) must not hold the dirt back here
                WdSet("zoneDirtSeconds", 10f);   // the test rate (the release table says 25 s; KH_DogWashSetupEditor.DirtTuning)
                wsWash.SendCustomEvent("_DebugClear");
                znOut = WdPlaceNear(zn.position, znR + 3f);
                WsPlayerNear(1.3f);
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { zLeg0 = WdFoot("_dirt"); zBody0 = WdPartAvg("_dirt", 0, 1); zT0 = Get<float>(wsWash, "_dirtZoneTotal"); }));
            s.Add(WsWait(5.0));
            s.Add(WsDo(() =>
            {
                zOutBlock = Get<int>(wsWash, "_dirtBlock");
                WsExpect(Get<float>(wsWash, "_dirtZoneTotal") == zT0, "outside the zone no zone time was counted (" + zT0 + " -> " + Get<float>(wsWash, "_dirtZoneTotal") + ")");
                WsExpect(WdFoot("_dirt") == zLeg0 && WdPartAvg("_dirt", 0, 1) == zBody0, "outside the zone the cells did not change (legs " + zLeg0.ToString("F3") + " -> " + WdFoot("_dirt").ToString("F3") + ")");
                WsExpect(zOutBlock == 0, "the outside check was not blocked by something else (_dirtBlock " + zOutBlock + "; 4 = the station is within reach of this spot)");
                WdPlaceAt(zn.position); WsPlayerNear(1.3f);
                zm0 = WsMask(); zT0 = Get<float>(wsWash, "_dirtZoneTotal"); zEv0 = Get<int>(wsWash, "_dirtEvents");
            }));
            s.Add(WsUntil(() =>
            {
                if (Flat(wsRoot.position - zn.position) > znR * 0.6f) { WdPlaceAt(zn.position); WsPlayerNear(1.3f); zRe++; }
                return Get<float>(wsWash, "_dirtZoneTotal") - zT0 >= 10f;
            }, 25, () => "10 s of zone time not counted in 25 s (" + (Get<float>(wsWash, "_dirtZoneTotal") - zT0).ToString("F2") + " s, block " + Get<int>(wsWash, "_dirtBlock") + ")"));
            s.Add(WsDo(() => { WdPlaceAt(znOut); WsPlayerNear(1.3f); }));   // out again: counting stops, the next apply takes the rest
            s.Add(WsWait(2.6));
            s.Add(WsDo(() =>
            {
                zT1 = Get<float>(wsWash, "_dirtZoneTotal") - zT0;
                float leg = WdFoot("_dirt"), body = WdPartAvg("_dirt", 0, 1), head = WdPartAvg("_dirt", 7, 7);
                WsExpect(leg >= 0.4f && leg <= 0.6f, "feet (leg cells within dirtHeightFull of the ground) 0.4..0.6 after " + zT1.ToString("F2") + " s in the zone (" + leg.ToString("F3") + ")");
                // hip + chest: zoneBodyDirt 0.2 x the height weight (the torso is high: most of it near dirtTopWeight)
                WsExpect(body > 0f && body <= 0.2f, "hip + chest a little dirty, at most 0.2 (" + body.ToString("F3") + ")");
                WsExpect(head == 0f, "the head did not get dirty (" + head.ToString("F3") + ")");
                WsExpect(leg > body, "legs dirtier than the body (the coefficient order)");
                int ev = Get<int>(wsWash, "_dirtEvents") - zEv0;
                WsExpect(ev >= 3 && ev <= 8, "dirt NetStrokes sent at the apply rate (" + ev + " in ~" + (zT1 + 2.6f).ToString("F0") + " s; 0.5 Hz, only when changed)");
                var m = WsMask();
                float mLeg = WdMaskFoot(m) - WdMaskFoot(zm0), mBody = WdMaskAvg(m, 0, 1) - WdMaskAvg(zm0, 0, 1), mHead = WdMaskAvg(m, 7, 7) - WdMaskAvg(zm0, 7, 7), mNeck = WdMaskAvg(m, 2, 2) - WdMaskAvg(zm0, 2, 2);
                WsExpect(mLeg >= 0.3f && mLeg <= 0.7f, "mask R on the feet grew like the cells (+" + mLeg.ToString("F3") + ")");
                WsExpect(mBody > 0f && mBody <= 0.25f, "mask R on hip + chest grew a little (+" + mBody.ToString("F3") + ")");
                WsExpect(Mathf.Abs(mHead) < 0.02f && Mathf.Abs(mNeck) < 0.02f, "mask R on the head / neck unchanged (" + mHead.ToString("F3") + " / " + mNeck.ToString("F3") + ": the part gate)");
                WsExpect(wsShell.enabled, "shell renderer enabled");
                WsNote("outside the zone (" + (znR + 3f).ToString("F1") + " m from its centre) 5 s: nothing counted, cells unchanged, block " + zOutBlock + "; inside " + zT1.ToString("F2") + " s (re-placed " + zRe + " times): legs " + leg.ToString("F3") + ", hip+chest " + body.ToString("F3") + ", head " + head.ToString("F3")
                    + "; mask R legs +" + mLeg.ToString("F3") + ", body +" + mBody.ToString("F3") + ", neck " + mNeck.ToString("F3") + ", head " + mHead.ToString("F3") + "; " + ev + " dirt events; zone " + zn.name + " r " + znR.ToString("F2") + " m");
                WdRestore();
            }));

            // ---- W-dirt-height (決定 2026-09-30: 足元から汚れる。泥の地点 10 s: 足先 > 0.4、尻尾と頭 < 0.05、腰の低いセル > 高いセル、マスクも低い所ほど濃い) ----
            s = WsCase("W-dirt-height", 60);
            Transform hz = null; float hzR = 0f, htT0 = 0f; Color[] hm0 = null;
            s.Add(WsDo(() =>
            {
                WdRestore();
                hz = WdZone(0, out hzR);
                WdSet("dirtGraceSeconds", 0f);
                WdSet("zoneDirtSeconds", 10f);   // the test rate (the release table says 25 s; KH_DogWashSetupEditor.DirtTuning)
                wsWash.SendCustomEvent("_DebugClear");
                WdPlaceAt(hz.position); WsPlayerNear(1.3f);
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { htT0 = Get<float>(wsWash, "_dirtZoneTotal"); hm0 = WsMask(); }));
            s.Add(WsUntil(() =>
            {
                if (Flat(wsRoot.position - hz.position) > hzR * 0.6f) { WdPlaceAt(hz.position); WsPlayerNear(1.3f); }
                return Get<float>(wsWash, "_dirtZoneTotal") - htT0 >= 10f;
            }, 25, () => "10 s of zone time not counted"));
            s.Add(WsDo(() => WdPlaceNear(hz.position, hzR + 3f)));
            s.Add(WsWait(2.6));
            s.Add(WsDo(() =>
            {
                var d = Get<float[]>(wsWash, "_dirt"); var part = Get<int[]>(wsWash, "cellPart"); var valid = Get<bool[]>(wsWash, "cellValid"); var h = Get<float[]>(wsWash, "_cellHeight");
                float foot = WdFoot("_dirt"), tail = WdPartAvg("_dirt", 8, 8), head = WdPartAvg("_dirt", 7, 7);
                // hip (0): cells in the lower and the upper third of its height range
                float hmin = float.MaxValue, hmax = float.MinValue;
                for (int c = 0; c < d.Length; c++) if (valid[c] && part[c] == 0) { hmin = Mathf.Min(hmin, h[c]); hmax = Mathf.Max(hmax, h[c]); }
                float lo = 0f, hi = 0f; int nlo = 0, nhi = 0, third = 0;
                for (int c = 0; c < d.Length; c++)
                {
                    if (!valid[c] || part[c] != 0) continue;
                    if (h[c] <= hmin + (hmax - hmin) / 3f) { lo += d[c]; nlo++; }
                    else if (h[c] >= hmax - (hmax - hmin) / 3f) { hi += d[c]; nhi++; }
                    else third++;
                }
                lo = nlo > 0 ? lo / nlo : 0f; hi = nhi > 0 ? hi / nhi : 0f;
                WsExpect(foot > 0.4f, "feet > 0.4 (" + foot.ToString("F3") + ")");
                WsExpect(tail < 0.05f && head < 0.05f, "tail and head < 0.05 (tail " + tail.ToString("F3") + ", head " + head.ToString("F3") + ")");
                WsExpect(nlo > 0 && nhi > 0 && lo > hi, "the hip's low cells dirtier than its high cells (" + lo.ToString("F3") + " over " + nlo + " vs " + hi.ToString("F3") + " over " + nhi + "; hip " + hmin.ToString("F2") + ".." + hmax.ToString("F2") + " m)");
                // the mask: the feet texels gained more than the upper leg texels (the Brush's height weight)
                var m = WsMask(); float gz = Get<float>(wsWash, "_groundZ");
                float fl = 0f, up = 0f; int nf = 0, nu = 0; float full = Get<float>(wsWash, "dirtHeightFull"), mid = Get<float>(wsWash, "dirtHeightMid");
                for (int k = 0; k < wsTexCount; k++)
                {
                    int q = wsTexPart[k]; if (q < 3 || q > 6) continue;
                    float th = (wsTexPos[k].z - gz) * wsMts, dr = m[wsTexIdx[k]].r - hm0[wsTexIdx[k]].r;
                    if (th <= full) { fl += dr; nf++; } else if (th >= mid) { up += dr; nu++; }
                }
                fl = nf > 0 ? fl / nf : 0f; up = nu > 0 ? up / nu : 0f;
                WsExpect(nf > 0 && fl > 0.3f, "mask R on the foot texels +0.3 or more (+" + fl.ToString("F3") + " over " + nf + ")");
                WsExpect(nu == 0 || up < fl * 0.6f, "mask R on the leg texels above dirtHeightMid grew clearly less than the feet (+" + up.ToString("F3") + " over " + nu + " vs +" + fl.ToString("F3") + ": the Brush's _DirtHeightFade)");
                WsNote("10 s in the zone: feet " + foot.ToString("F3") + ", tail " + tail.ToString("F3") + ", head " + head.ToString("F3") + "; hip low third " + lo.ToString("F3") + " (" + nlo + ") vs high third " + hi.ToString("F3") + " (" + nhi + "), hip " + hmin.ToString("F2") + ".." + hmax.ToString("F2") + " m; mask legs feet +" + fl.ToString("F3") + " (" + nf + " texels) vs above " + mid + " m +" + up.ToString("F3") + " (" + nu + ")");
                WdRestore();
            }));

            // ---- W-dirt-restore (review 3, 1-1: Restore は汚れを二重にも消しもしない。セルに無い記録済みの汚れだけを描き直す) ----
            s = WsCase("W-dirt-restore", 60);
            Transform rz = null; float rzR = 0f, rsT0 = 0f, rsMask1 = 0f, rsCell1 = 0f, rsMask2 = 0f; int rsRc0 = 0;
            s.Add(WsDo(() =>
            {
                WdRestore();
                rz = WdZone(0, out rzR);
                WdSet("dirtGraceSeconds", 0f);
                WdSet("zoneDirtSeconds", 10f);   // the test rate (the release table says 25 s; KH_DogWashSetupEditor.DirtTuning)
                wsWash.SendCustomEvent("_DebugClear");
                WdPlaceAt(rz.position); WsPlayerNear(1.3f);
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => rsT0 = Get<float>(wsWash, "_dirtZoneTotal")));
            s.Add(WsUntil(() =>
            {
                if (Flat(wsRoot.position - rz.position) > rzR * 0.6f) { WdPlaceAt(rz.position); WsPlayerNear(1.3f); }
                return Get<float>(wsWash, "_dirtZoneTotal") - rsT0 >= 4f;
            }, 12, () => "4 s of zone time not counted"));
            s.Add(WsDo(() => WdPlaceNear(rz.position, rzR + 3f)));
            s.Add(WsWait(2.6));
            s.Add(WsDo(() =>
            {
                rsMask1 = WdMaskFoot(WsMask()); rsCell1 = WdFoot("_dirt");
                WsExpect(rsCell1 > 0.1f, "the zone dirtied the legs (" + rsCell1.ToString("F3") + ")");
                WsExpect(Get<int>(wsWash, "_dirtMaskSeq") == Get<int>(wsWash, "dirtSeq"), "the Owner's mask holds every event of its cells (maskSeq " + Get<int>(wsWash, "_dirtMaskSeq") + ", dirtSeq " + Get<int>(wsWash, "dirtSeq") + ")");
                rsRc0 = Get<int>(wsWash, "_restoreCount");
                wsWash.SetProgramVariable<bool>("_restorePending", true);
            }));
            s.Add(WsUntil(() => Get<int>(wsWash, "_restoreCount") > rsRc0, 3, () => "the forced Restore did not run"));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                rsMask2 = WdMaskFoot(WsMask());
                WsExpect(Mathf.Abs(rsMask2 - rsMask1) < 0.08f, "a Restore keeps the legs' dirt (mask " + rsMask1.ToString("F3") + " -> " + rsMask2.ToString("F3") + ", cells " + rsCell1.ToString("F3") + "): not doubled, not lost");
                // A logged event the cells do not hold (seq = dirtSeq + 1, legs +0.1): the next Restore must draw it once
                int ds = Get<int>(wsWash, "dirtSeq"), ep = Get<int>(wsWash, "epoch"), f = 0, t = 0;
                for (int k = 0; k < 4; k++) { f |= (64 + 50) << (7 * k); t |= 64 << (7 * k); }
                wsWash.SetProgramVariable<int[]>("_dlSeq", new[] { (ds + 1) & 0x3FFFFFFF, 0, 0, 0, 0, 0, 0, 0 });
                wsWash.SetProgramVariable<int[]>("_dlEpoch", new[] { ep, 0, 0, 0, 0, 0, 0, 0 });
                wsWash.SetProgramVariable<int[]>("_dlFrom", new[] { f, 0, 0, 0, 0, 0, 0, 0 });
                wsWash.SetProgramVariable<int[]>("_dlTo", new[] { t, 0, 0, 0, 0, 0, 0, 0 });
                wsWash.SetProgramVariable<int[]>("_dlAmount", new int[8]);
                wsWash.SetProgramVariable<int>("_dlCount", 1);
                rsRc0 = Get<int>(wsWash, "_restoreCount");
                wsWash.SetProgramVariable<bool>("_restorePending", true);
            }));
            s.Add(WsUntil(() => Get<int>(wsWash, "_restoreCount") > rsRc0, 3, () => "the second forced Restore did not run"));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                float m3 = WdMaskFoot(WsMask());
                wsWash.SetProgramVariable<int>("_dlCount", 0);
                WsExpect(m3 - rsMask2 >= 0.06f && m3 - rsMask2 <= 0.14f, "the logged event newer than dirtSeq was drawn once after the Restore (legs mask " + rsMask2.ToString("F3") + " -> " + m3.ToString("F3") + ", expected about +0.1)");
                WsNote("4 s in the zone: legs cells " + rsCell1.ToString("F3") + ", mask " + rsMask1.ToString("F3") + " -> forced Restore " + rsMask2.ToString("F3") + "; injected log entry seq dirtSeq+1 (+0.1 legs) -> Restore " + m3.ToString("F3") + ". Not covered: a real receiver's log (ClientSim skips the own echo)");
                WdRestore();
            }));

            // ---- W-dirt-boundary (review 3, 2-1: 泥で数えた分は、次の適用の前に洗い始めると洗った後に足されない) ----
            s = WsCase("W-dirt-boundary", 50);
            Transform bz = null; float bzR = 0f, bdLeg0 = 0f; int bdTick0 = 0; WsAnchor bdA = default;
            s.Add(WsDo(() =>
            {
                WdRestore();
                bz = WdZone(0, out bzR);
                WdSet("dirtGraceSeconds", 0f);
                WdSet("zoneDirtSeconds", 10f);   // the test rate (the release table says 25 s; KH_DogWashSetupEditor.DirtTuning)
                wsWash.SendCustomEvent("_DebugClear");
                WdPlaceAt(bz.position); WsPlayerNear(1.3f);
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => bdTick0 = Get<int>(wsWash, "_dirtTicks")));
            s.Add(WsUntil(() => Get<int>(wsWash, "_dirtTicks") > bdTick0, 4, () => "no apply tick"));
            s.Add(WsWait(1.0));   // 1 s of zone time counted, the next apply ~1 s away
            s.Add(WsDo(() => { bdLeg0 = WdFoot("_dirt"); bdA = WsAnchorOf(WsPathHit(0.5f)); bdTick0 = Get<int>(wsWash, "_dirtTicks"); }));
            s.Add(WsDrive(0.3f, 1, u => WsAt(bdA), WsSponge, -1f, null));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsUntil(() => Get<int>(wsWash, "_dirtTicks") > bdTick0, 4, () => "no apply tick after the stroke"));
            s.Add(WsDo(() =>
            {
                float leg = WdFoot("_dirt");
                WsExpect(WsHold(), "still washed at the apply");
                WsExpect(leg <= bdLeg0 + 1e-4f, "the zone time counted before the wash was not applied after it (" + bdLeg0.ToString("F3") + " -> " + leg.ToString("F3") + ")");
                WsNote("1 s of zone time, then a 0.3 s sponge before the next apply: legs " + bdLeg0.ToString("F3") + " -> " + leg.ToString("F3") + " at the apply (dropped at the wash start)");
                WdRestore();
            }));
            s.Add(WsUntil(() => !WsHold(), wsHoldSeconds + 2, () => "the hold of the boundary stroke did not end (holdSeconds " + wsHoldSeconds + ")"));

            // ---- W-dirt-walk (リードで歩いた距離で足だけ汚れる。距離の係数を 1/10 にして 30 m = 300 m 相当) ----
            s = WsCase("W-dirt-walk", 100);
            float wkW0 = 0f, wkLeg0 = 0f, wkBody0 = 0f, wkCounted = 0f, wkT0 = -1f; Vector3 wkFrom = Vector3.zero;
            const float WkMeters = 30f;
            s.Add(WsDo(() =>
            {
                WdRestore();
                WdSet("dirtGraceSeconds", 0f);
                WdSet("zoneDirtSeconds", 10f);   // the test rate (the release table says 25 s; KH_DogWashSetupEditor.DirtTuning)
                WdSet("walkDirtMeters", WkMeters);
                WsPlaceShiba(); WsPlayerNear(1.3f);
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            // W-dirt-boundary ends inside the hold of its sponge stroke; ReqAttach refuses a washed dog, so wait it out
            // (run 20260930-050508: "the Shiba did not get leashed (walker -1)")
            s.Add(WsUntil(() => !WsHold(), wsHoldSeconds + 2, () => "the hold of an earlier case did not end"));
            s.Add(WsDo(() => pouch.SendCustomEvent("_WalkLeft" + wsDog)));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] == me, 5, () => "the Shiba did not get leashed (walker " + Get<int[]>(manager, "walkers")[wsDog] + ")"));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                wkW0 = Get<float>(wsWash, "_dirtWalkTotal"); wkLeg0 = WdFoot("_dirt"); wkBody0 = WdPartAvg("_dirt", 0, 1);
                wkFrom = Networking.LocalPlayer.GetPosition(); wkT0 = -1f;
            }));
            // 持ち主が周回のノードに沿って 2 m/s で歩く (毎フレーム小さく TeleportTo)。Owner の数えた距離が 30 m に届くまで
            s.Add(now =>
            {
                if (wkT0 < 0f) wkT0 = Time.time;
                float t = Time.time - wkT0;
                if (t > 70f) throw new TimeoutException("the counted walk did not reach " + WkMeters + " m in 70 s (" + (Get<float>(wsWash, "_dirtWalkTotal") - wkW0).ToString("F1") + " m, block " + Get<int>(wsWash, "_dirtBlock") + ", walker " + Get<int[]>(manager, "walkers")[wsDog] + ")");
                if (Get<int[]>(manager, "walkers")[wsDog] != me) throw new InvalidOperationException("the leash came off during the walk");
                Vector3 dir;
                Vector3 p = WdAlong(2f * t, out dir);
                try { p = CmGround(p); } catch (InvalidOperationException) { }
                CmTeleport(p, dir);
                return Get<float>(wsWash, "_dirtWalkTotal") - wkW0 >= WkMeters;
            });
            s.Add(WsDo(() => { WsDetachShiba(); wkCounted = Get<float>(wsWash, "_dirtWalkTotal") - wkW0; }));
            s.Add(WsWait(2.6));
            s.Add(WsDo(() =>
            {
                float extra = Get<float>(wsWash, "_dirtWalkTotal") - wkW0 - wkCounted;
                float leg = WdFoot("_dirt"), body = WdPartAvg("_dirt", 0, 1);
                WsExpect(Get<int[]>(manager, "walkers")[wsDog] < 0, "detached after the walk");
                WsExpect(leg - wkLeg0 >= 0.4f && leg - wkLeg0 <= 0.6f, "legs +0.4..0.6 after " + (wkCounted + extra).ToString("F1") + " m counted (= " + ((wkCounted + extra) * 300f / WkMeters).ToString("F0") + " m at the test rate x10) (" + wkLeg0.ToString("F3") + " -> " + leg.ToString("F3") + ")");
                WsExpect(Mathf.Abs(body - wkBody0) < 1e-4f, "walking dirties the legs only (hip + chest " + wkBody0.ToString("F3") + " -> " + body.ToString("F3") + ")");
                WsNote("leashed walk along the loop nodes at 2 m/s (holder): counted " + wkCounted.ToString("F1") + " m (+" + extra.ToString("F2") + " m after the stop) with walkDirtMeters " + WkMeters + " (the test rate; release 900 m): legs " + wkLeg0.ToString("F3") + " -> " + leg.ToString("F3") + ", hip+chest " + body.ToString("F3"));
                WdRestore();
            }));

            // ---- W-dirt-grace (清潔の合図の後は泥の地点でも増えない。猶予を短くすると増える) ----
            s = WsCase("W-dirt-grace", 220);
            int grS0 = 0; List<WsAnchor> grPts = null; Transform gz = null; float gzR = 0f, grLeg0 = 0f, grZ0 = 0f; bool grSaw3 = false, grSaw0 = false; double grPlacedAt = 0; int grRe = 0;
            s.Add(WsDo(() =>
            {
                WdRestore();
                gz = WdZone(0, out gzR);
                WdSet("zoneDirtSeconds", 10f);   // the test rate (release 25 s)
                WsPlaceShiba(); WsPlayerNear(1.5f); WsSetDirt(0.6f);
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { grS0 = WsSerial(); grPts = WsCover(0.08f); }));
            s.Add(WsSweep(() => grPts, WsShower, 0.11f, 1.0f, 120f, () => WsSerial() == grS0 + 1,
                () => "not clean in 120 s: avg dirt " + WsAvgDirt().ToString("F3") + ", serial " + grS0 + " -> " + WsSerial()));
            s.Add(WsDo(() =>
            {
                WdPlaceAt(gz.position); WsPlayerNear(1.3f);
                grLeg0 = WdFoot("_dirt"); grZ0 = Get<float>(wsWash, "_dirtZoneTotal"); grPlacedAt = EditorApplication.timeSinceStartup;
            }));
            s.Add(now =>
            {
                if (Flat(wsRoot.position - gz.position) > gzR * 0.6f) { WdPlaceAt(gz.position); WsPlayerNear(1.3f); grRe++; }
                int b = Get<int>(wsWash, "_dirtBlock");
                if (b == 3) grSaw3 = true;
                if (b == 0) grSaw0 = true;
                return now - grPlacedAt >= 10.0;
            });
            s.Add(WsWait(2.6));
            s.Add(WsDo(() =>
            {
                float leg = WdFoot("_dirt");
                WsExpect(grSaw3 && !grSaw0, "blocked by the grace in the zone (saw grace " + grSaw3 + ", saw unblocked " + grSaw0 + ")");
                WsExpect(Get<float>(wsWash, "_dirtZoneTotal") == grZ0, "no zone time counted in the grace");
                WsExpect(leg <= grLeg0 + 1e-4f, "legs did not get dirtier in the grace (" + grLeg0.ToString("F3") + " -> " + leg.ToString("F3") + ")");
                WdSet("dirtGraceSeconds", 1f);   // the clean edge is > 12 s ago: the grace is over now
                grZ0 = Get<float>(wsWash, "_dirtZoneTotal"); grLeg0 = leg;
            }));
            s.Add(WsUntil(() =>
            {
                if (Flat(wsRoot.position - gz.position) > gzR * 0.6f) { WdPlaceAt(gz.position); WsPlayerNear(1.3f); grRe++; }
                return Get<float>(wsWash, "_dirtZoneTotal") - grZ0 >= 4f;
            }, 12, () => "the zone time did not count after the grace was shortened (block " + Get<int>(wsWash, "_dirtBlock") + ")"));
            s.Add(WsWait(2.6));
            s.Add(WsDo(() =>
            {
                float leg = WdFoot("_dirt");
                WsExpect(leg >= grLeg0 + 0.1f, "after the grace the zone dirties the legs again (" + grLeg0.ToString("F3") + " -> " + leg.ToString("F3") + ")");
                WsNote("clean edge (serial " + grS0 + " -> " + WsSerial() + ") then 10 s in the zone: grace block, legs unchanged; grace shortened to 1 s: " + (Get<float>(wsWash, "_dirtZoneTotal") - grZ0).ToString("F1") + " s counted, legs " + grLeg0.ToString("F3") + " -> " + leg.ToString("F3") + " (re-placed " + grRe + ")");
                WdRestore();
            }));

            // ---- W-dirt-station (洗い場から 6 m 以内では泥の地点でも増えない。半径を 0 にすると増える = 対照) ----
            s = WsCase("W-dirt-station", 70);
            Transform sz = null; float szR = 0f, stLeg0 = 0f, stZ0 = 0f; bool stSaw4 = false, stSawOther = false; double stPlacedAt = 0; Vector3 stAt = Vector3.zero; float stDist = 0f;
            s.Add(WsDo(() =>
            {
                WdRestore();
                var st = WsRef(wsWash, "washStation") as Transform;
                if (st == null) throw new WsSkip("wash.washStation is not wired (run Wash/20)");
                sz = WdZone(0, out szR);
                WdSet("dirtGraceSeconds", 0f);
                WdSet("zoneDirtSeconds", 10f);   // the test rate (the release table says 25 s; KH_DogWashSetupEditor.DirtTuning)
                wsWash.SendCustomEvent("_DebugClear");
                // the zone moved beside the station for this case (Play only; the scene is not saved). Put back in WdRestore
                Vector3 side = WsFlat(wsRoot.position - st.position); if (side == Vector3.forward) side = Vector3.right;
                stAt = CmGround(st.position + side * 3f);
                wdMovedZone = sz; wdMovedFrom = sz.position; sz.position = stAt;
                stDist = Flat(stAt - st.position);
                WdPlaceAt(stAt); WsPlayerNear(1.3f);
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { stLeg0 = WdFoot("_dirt"); stZ0 = Get<float>(wsWash, "_dirtZoneTotal"); stPlacedAt = EditorApplication.timeSinceStartup; }));
            s.Add(now =>
            {
                if (Flat(wsRoot.position - stAt) > szR * 0.6f) { WdPlaceAt(stAt); WsPlayerNear(1.3f); }
                int b = Get<int>(wsWash, "_dirtBlock");
                if (b == 4) stSaw4 = true; else stSawOther = true;
                return now - stPlacedAt >= 10.0;
            });
            s.Add(WsWait(2.6));
            s.Add(WsDo(() =>
            {
                float leg = WdFoot("_dirt");
                WsExpect(stSaw4 && !stSawOther, "blocked by the station the whole time (saw station " + stSaw4 + ", saw other " + stSawOther + ")");
                WsExpect(leg <= stLeg0 + 1e-4f && Get<float>(wsWash, "_dirtZoneTotal") == stZ0, "no dirt within the station radius (" + stLeg0.ToString("F3") + " -> " + leg.ToString("F3") + ")");
                WdSet("dirtStationRadius", 0f);
                stZ0 = Get<float>(wsWash, "_dirtZoneTotal"); stLeg0 = leg;
            }));
            s.Add(WsUntil(() =>
            {
                if (Flat(wsRoot.position - stAt) > szR * 0.6f) { WdPlaceAt(stAt); WsPlayerNear(1.3f); }
                return Get<float>(wsWash, "_dirtZoneTotal") - stZ0 >= 4f;
            }, 12, () => "control: the zone did not count with the station radius 0 (block " + Get<int>(wsWash, "_dirtBlock") + ")"));
            s.Add(WsWait(2.6));
            s.Add(WsDo(() =>
            {
                float leg = WdFoot("_dirt");
                WsExpect(leg >= stLeg0 + 0.1f, "control: with the station radius 0 the same spot dirties the legs (" + stLeg0.ToString("F3") + " -> " + leg.ToString("F3") + ")");
                WsNote("zone moved " + stDist.ToString("F1") + " m from the station: 10 s blocked (station), legs unchanged; radius 0 -> legs " + stLeg0.ToString("F3") + " -> " + leg.ToString("F3"));
                WdRestore();
            }));

            // ---- W-dirt-river (水の中で足の汚れが減り 0.15 で止まる。濡れが付く。川だけでは清潔の合図が出ない) ----
            s = WsCase("W-dirt-river", 60);
            float rvFloor = 0.15f, rvW0 = 0f, rvMid = -1f, rvWet0 = 0f, rvMask0 = 0f; int rvS0 = 0; bool rvArmed = false;
            s.Add(WsDo(() =>
            {
                WdRestore();
                if (wdFetch == null) throw new WsSkip("the Shiba's fetchController is not wired (no WaterPose to set)");
                WsPlaceShiba(); WsPlayerNear(1.3f); WsSetDirt(0.4f);
                rvFloor = Get<float>(wsWash, "riverDirtFloor");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() =>
            {
                // legs 0.4, everything else 0 in the cells (the mask keeps the 0.4 seed: only the legs' mask is read), still armed:
                // the average then falls below cleanDirtThreshold once the legs are at the floor, so only the river block keeps the edge back
                var d = (float[])Get<float[]>(wsWash, "_dirt").Clone(); var part = Get<int[]>(wsWash, "cellPart"); var valid = Get<bool[]>(wsWash, "cellValid");
                for (int c = 0; c < d.Length; c++) if (c < valid.Length && valid[c] && (part[c] < 3 || part[c] > 6)) d[c] = 0f;
                wsWash.SetProgramVariable<float[]>("_dirt", d);
                rvS0 = WsSerial(); rvArmed = Get<bool>(wsWash, "cleanArmed");
                WdSet("riverDropSeconds", 3f);   // -0.3 per 3 s: the floor within the case
                rvW0 = Get<float>(wsWash, "_dirtWaterTotal"); rvWet0 = WdPartAvg("_wet", 3, 6); rvMask0 = WdMaskAvg(WsMask(), 3, 6);
                wdFetch.SetProgramVariable("waterPose", 1); wdFetch.SetProgramVariable("_debugPoseHold", true); wdPoseHeld = true;
            }));
            s.Add(WsUntil(() =>
            {
                float wt = Get<float>(wsWash, "_dirtWaterTotal") - rvW0;
                if (rvMid < 0f && wt >= 2.5f && Get<int>(wsWash, "_dirtTicks") > 0) { float l = WdFoot("_dirt"); if (l < 0.4f - 1e-3f) rvMid = l; }
                return wt >= 10f;
            }, 20, () => "10 s of water time not counted (" + (Get<float>(wsWash, "_dirtWaterTotal") - rvW0).ToString("F1") + " s, block " + Get<int>(wsWash, "_dirtBlock") + ", waterPose " + Get<int>(wdFetch, "waterPose") + ")"));
            s.Add(WsDo(() => WdReleasePose()));
            s.Add(WsWait(2.6));
            s.Add(WsDo(() =>
            {
                var d = Get<float[]>(wsWash, "_dirt"); var part = Get<int[]>(wsWash, "cellPart"); var valid = Get<bool[]>(wsWash, "cellValid");
                float min = 1f; for (int c = 0; c < d.Length; c++) if (c < valid.Length && valid[c] && part[c] >= 3 && part[c] <= 6) min = Mathf.Min(min, d[c]);
                float leg = WdFoot("_dirt"), wet = WdPartAvg("_wet", 3, 6), mask = WdMaskAvg(WsMask(), 3, 6);
                float thr = Get<float>(wsWash, "cleanDirtThreshold");
                WsExpect(rvMid > 0f, "the legs' dirt went down while in the water (a mid sample below 0.4)");
                WsExpect(min >= rvFloor - 1e-4f && leg <= rvFloor + 0.01f, "the legs stopped at the floor " + rvFloor + " (avg " + leg.ToString("F3") + ", min cell " + min.ToString("F3") + ")");
                WsExpect(wet > rvWet0 + 0.3f, "the legs got wet (" + rvWet0.ToString("F3") + " -> " + wet.ToString("F3") + ")");
                WsExpect(mask < rvMask0 - 0.1f, "mask R on the legs went down with the cells (" + rvMask0.ToString("F3") + " -> " + mask.ToString("F3") + ")");
                WsExpect(rvArmed, "armed before the river (the edge was possible)");
                WsExpect(WsSerial() == rvS0, "the river alone did not give the clean edge (serial " + rvS0 + " -> " + WsSerial() + ", avg dirt " + WsAvgDirt().ToString("F3") + " vs threshold " + thr + ")");
                WsNote("legs 0.4 (others 0 in the cells), 10 s at WaterPose 1 with riverDropSeconds 3: mid " + rvMid.ToString("F3") + ", end avg " + leg.ToString("F3") + " / min cell " + min.ToString("F3") + " (floor " + rvFloor + "); wet " + rvWet0.ToString("F3") + " -> " + wet.ToString("F3") + "; mask legs R " + rvMask0.ToString("F3") + " -> " + mask.ToString("F3")
                    + "; serial unchanged " + rvS0 + " with avg dirt " + WsAvgDirt().ToString("F3") + (WsAvgDirt() < thr ? " (< " + thr + ": the edge was held back by the river block alone)" : " (>= " + thr + ": the average alone kept the edge back; the river block was not needed here)"));
                WdRestore();
                wsWash.SendCustomEvent("_DebugClear");
            }));
        }

        // ============================================================
        // 部品
        // ============================================================

        /// <summary>試作値を一時的に書く (元の値を覚える)。</summary>
        static void WdSet(string key, float value)
        {
            if (!wdSaved.ContainsKey(key)) wdSaved[key] = Get<float>(wsWash, key);
            wsWash.SetProgramVariable<float>(key, value);
        }

        /// <summary>書いた試作値・動かした地点・水の中の扱いを戻す (ケースの最初と最後。失敗したケースの後にも次の最初で)。</summary>
        static void WdRestore()
        {
            foreach (var kv in wdSaved) wsWash.SetProgramVariable<float>(kv.Key, kv.Value);
            wdSaved.Clear();
            if (wdMovedZone != null) { wdMovedZone.position = wdMovedFrom; wdMovedZone = null; }
            WdReleasePose();
        }

        static void WdReleasePose()
        {
            if (!wdPoseHeld || wdFetch == null) return;
            wdFetch.SetProgramVariable("_debugPoseHold", false);
            wdFetch.SetProgramVariable("waterPose", 0);
            wdPoseHeld = false;
        }

        /// <summary>配線された泥の地点 i と半径 (球 = lossyScale.x / 2)。無ければ SKIP。</summary>
        static Transform WdZone(int i, out float radius)
        {
            var zones = WsArr<Transform>(wsWash, "dirtZones");
            if (zones.Length <= i || zones[i] == null) throw new WsSkip("wash.dirtZones[" + i + "] is not wired (run Wash/20)");
            radius = Mathf.Abs(zones[i].lossyScale.x) * 0.5f;
            return zones[i];
        }

        /// <summary>柴を p へ置く (リードを外してから。歩行ハーネスの置き方と同じ注入)。</summary>
        static void WdPlaceAt(Vector3 p)
        {
            WsDetachShiba();
            var g = CmGround(p, 2f);
            wsRoot.SetPositionAndRotation(g, wsRoot.rotation);
            var sync = wsRoot.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
            wsPosHist.Clear();
        }

        /// <summary>center から dist m 離れた NavMesh の上 (8 方向を順に) へ柴を置き、その位置を返す。</summary>
        static Vector3 WdPlaceNear(Vector3 center, float dist)
        {
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * dist;
                UnityEngine.AI.NavMeshHit hit;
                if (!UnityEngine.AI.NavMesh.SamplePosition(p, out hit, 1f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                if (Flat(hit.position - center) < dist - 0.8f) continue;
                WdPlaceAt(hit.position);
                return hit.position;
            }
            throw new InvalidOperationException("no NavMesh point " + dist.ToString("F1") + " m around " + center);
        }

        /// <summary>周回のノードの折れ線に沿って s m 進んだ点 (ノード 0 から。終わりまで来たら最後の点)。</summary>
        static Vector3 WdAlong(float s, out Vector3 dir)
        {
            dir = Vector3.forward;
            for (int i = 0; i + 1 < nodes.Length; i++)
            {
                Vector3 seg = nodes[i + 1] - nodes[i]; float len = Flat(seg);
                if (len < 1e-4f) continue;
                dir = seg;
                if (s <= len) return nodes[i] + seg * (s / len);
                s -= len;
            }
            return nodes[nodes.Length - 1];
        }

        /// <summary>有効なセルのうち部位 lo..hi のヒープの配列 (key = _dirt / _wet) の平均。</summary>
        static float WdPartAvg(string key, int lo, int hi)
        {
            var v = Get<float[]>(wsWash, key); var part = Get<int[]>(wsWash, "cellPart"); var valid = Get<bool[]>(wsWash, "cellValid");
            float sum = 0f; int n = 0;
            for (int c = 0; c < v.Length && c < part.Length && c < valid.Length; c++)
            {
                if (!valid[c] || part[c] < lo || part[c] > hi) continue;
                sum += v[c]; n++;
            }
            if (n == 0) throw new InvalidOperationException("no valid cells of parts " + lo + ".." + hi);
            return sum / n;
        }

        /// <summary>足先: 足 (3..6) の有効なセルのうち地面から dirtHeightFull 以内 (実行時の _cellHeight)。無ければ足のセルの低い方 1/4。</summary>
        static float WdFoot(string key)
        {
            var v = Get<float[]>(wsWash, key); var part = Get<int[]>(wsWash, "cellPart"); var valid = Get<bool[]>(wsWash, "cellValid");
            var h = Get<float[]>(wsWash, "_cellHeight"); float full = Get<float>(wsWash, "dirtHeightFull");
            var legs = new List<int>();
            for (int c = 0; c < v.Length && c < part.Length && c < valid.Length && c < h.Length; c++) if (valid[c] && part[c] >= 3 && part[c] <= 6) legs.Add(c);
            if (legs.Count == 0) throw new InvalidOperationException("no valid leg cells");
            var feet = legs.FindAll(c => h[c] <= full);
            if (feet.Count == 0) { legs.Sort((a, b) => h[a].CompareTo(h[b])); feet = legs.GetRange(0, Mathf.Max(1, legs.Count / 4)); }
            float sum = 0f; foreach (int c in feet) sum += v[c];
            return sum / feet.Count;
        }

        /// <summary>足 (3..6) のテクセルのうち地面から dirtHeightFull 以内 (位置マップの z、実行時の _groundZ) のマスクの R の平均。</summary>
        static float WdMaskFoot(Color[] m)
        {
            float gz = Get<float>(wsWash, "_groundZ"), full = Get<float>(wsWash, "dirtHeightFull"), sum = 0f; int n = 0;
            for (int k = 0; k < wsTexCount; k++)
            {
                int q = wsTexPart[k];
                if (q < 3 || q > 6 || (wsTexPos[k].z - gz) * wsMts > full) continue;
                sum += m[wsTexIdx[k]].r; n++;
            }
            if (n == 0) throw new InvalidOperationException("no foot texels within " + full + " m of the ground");
            return sum / n;
        }

        /// <summary>位置マップで部位 lo..hi のテクセルのマスクの R の平均。</summary>
        static float WdMaskAvg(Color[] m, int lo, int hi)
        {
            float sum = 0f; int n = 0;
            for (int k = 0; k < wsTexCount; k++)
            {
                int q = wsTexPart[k];
                if (q < lo || q > hi) continue;
                sum += m[wsTexIdx[k]].r; n++;
            }
            if (n == 0) throw new InvalidOperationException("no texels of parts " + lo + ".." + hi);
            return sum / n;
        }
    }
}
