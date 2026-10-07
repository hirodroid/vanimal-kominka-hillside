using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>破壊監査 2026-10-07 Phase A (コミット 3e20683) の対象ケース — 群 call (Course/100f) と群 audit-a (Course/100h、この 5 ケースだけ)。
    /// 根拠は docs/audits/2026-10-07/triage.md §1 (RC-1・RC-2・RC-8) と §8.1 (Codex の優先表)。枠・部品は Wash.cs / WashCall.cs / WashCallAvail.cs。
    /// ランタイムの .cs とシーンは変えない。模擬 (ヒープへの書き込み) は各ケースの notes に「simulated」と書く。
    ///
    /// C-avail-traveling (RC-1 (a)): 自分の接続犬 (柴) に切符 (Phase 1 Ready / 4 Moved、ヒープ = 模擬) → ボードの _dogState (= _Availability(柴, 自分))
    ///   が RefuseTraveling (12)、ReqCall は refusedReason 12 で断られ heldDog は変わらない。切符を消すと呼べる。Phase 4 は travelCenter を
    ///   100 m 先に書いて犬の適用 (位置の変更) を起こさない (_travelApplies が増えないことを対照に見る)。
    /// C-wash-travel-race (RC-1 (b)): 切符 Phase 1 を書き、犬側が立ち上がりを処理した (_washErrandPrev) 後に、洗い場のヒープへ
    ///   heldDog / callerId / heldMs を直接書いて「切符中に受理された保持」を作る (模擬)。期待: 1 s 以内に heldDog = -1 (レベル解放)、
    ///   _stationFrames は増えない。切符を Done (2) にした後 1.5 s、保持は戻らず犬は跳ばない。対照: 切符が無ければ注入した保持は残り、
    ///   洗い場の枝が回る。幾何は呼出範囲の内側なので、30 m 超のテレポートの枝そのものは通らない (保持が戻らないことで見る)。
    /// C-avail-station-washed-other (RC-8 (a)): 柴を台へ呼び、柴の洗い保持を人形の id で書く (模擬、C-avail-washed-other と同じ)。
    ///   別の犬 (柴の次の番号) は AvWashedByOther (10)、ReqCall は待機 (_pendingDog = 別の犬、heldDog は柴のまま)。washMs を過去へ
    ///   書いて保持を切ると (模擬) 別の犬が受理されて交代する。対照: 保持中の犬の洗い手が自分なら即受理 (待たない)。
    /// C-placed-river-timeout (RC-2): 川の帯の中央 (KH_RiverData の配列から _Lateral と同じ式で求めた帯の内の点) を場所指定して呼ぶ。
    ///   犬は岸に置く (模擬の配置)。期待: PlacedSleepHeld は一度も立たず、着かない。呼ばれている間はリードを断る (対照)。calledMs から
    ///   arriveTimeout × 2 を越えた次の Watch で sleeperId = -1、直後にリードが付く。待ち時間は実時間 (60 s)。
    /// C-placed-reach (対照): 地上 1 m の点 (持ち上げで届く) を指定 → arriveTimeout × 2 以内に arrived、calledMs を 90 s 過去へ書いても
    ///   (模擬) 解けない。続けて PlacedHeld の除外: arriveTimeout をヒープで 0.3 s に縮め (模擬、時間切れ 0.6 s)、犬の真上 1 m 超を指定 →
    ///   持ち上げ中 (PlacedSleepHeld、未着) に時間切れを越えた Watch が来ても解けず、着く。
    ///
    /// 見ていないもの: 2 人 (犬と洗い場の Owner が別・切符の持ち主が別のクライアント)、ボードの「移動中」の文言 (未追加)、
    /// 30 m 超からのテレポートの実際の絵、帯の拒否 (Phase C)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        const int PaTicketReq = 987653;   // a travel request id no pouch ever makes (TreatCall.cs uses 987654)

        static void MakeWashAuditACases()
        {
            PaBindStation();
            int me = Networking.LocalPlayer.playerId;
            int other = (wsDog + 1) % wander.Length;
            float at = 30f;
            try { at = Get<float>(PaSlot(), "arriveTimeout"); } catch (Exception) { }   // the budget only; the cases read it again at run time
            wsReport.notCovered.Add("Audit 2026-10-07 Phase A (C-avail-traveling .. C-placed-reach): the ticket, the injected station hold, the other washer and the shortened arriveTimeout are heap writes on one ClientSim client; the dog / station / slot Owners on different clients, a real travel (fade, Phase 1 -> 4 -> 2 by the pouch), the board word for RefuseTraveling (none yet) and the 30 m far-call teleport itself -> 2 clients / PCVR");

            // ================= C-avail-traveling (RC-1 (a)) =================
            var s = WsCase("C-avail-traveling", 150);
            int atSerial = 0, atApplies0 = 0;
            WcSetup(s);
            WcRequireMain(s);
            WaBind(s);
            s.Add(WsDo(() => wsFailDump = PaDump));
            WcLeashShiba(s, me);
            s.Add(WsUntil(() => WaState(waBoard, wsDog) == KH_WashStationCall.AvCallable, 3,
                () => "control: my leashed Shiba was not shown callable before the ticket (state " + WaState(waBoard, wsDog) + ")"));
            foreach (int phase in new[] { KH_LeashManager.TravelPhaseReady, KH_LeashManager.TravelPhaseMoved })
            {
                int ph = phase;
                s.Add(WsDo(() =>
                {
                    atApplies0 = Get<int>(wsWander, "_travelApplies");
                    PaSetTicket(wsDog, ph);
                    waBoard.SetProgramVariable("_nextAt", 0f);
                }));
                s.Add(WsUntil(() => WaState(waBoard, wsDog) == KH_WashStationCall.RefuseTraveling, 1.5,
                    () => "phase " + ph + ": the board did not read RefuseTraveling (state " + WaState(waBoard, wsDog) + ")"));
                s.Add(WsDo(() =>
                {
                    WsExpect(WaWordShown(waBoard, wsDog) == -1, "phase " + ph + ": no word yet for 'traveling' (word " + WaWordShown(waBoard, wsDog) + ")");
                    atSerial = Get<int>(wcStation, "refuseSerial");
                    wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog);
                }));
                s.Add(WsUntil(() => Get<int>(wcStation, "refuseSerial") != atSerial, 2, () => "phase " + ph + ": the Owner did not refuse the ReqCall (heldDog " + Get<int>(wcStation, "heldDog") + ", pending " + Get<int>(wcStation, "_pendingDog") + ")"));
                s.Add(WsDo(() =>
                {
                    WsExpect(Get<int>(wcStation, "refusedReason") == KH_WashStationCall.RefuseTraveling && Get<int>(wcStation, "refusedDog") == wsDog,
                        "phase " + ph + ": refused as traveling (reason " + Get<int>(wcStation, "refusedReason") + ", dog " + Get<int>(wcStation, "refusedDog") + ")");
                    WsExpect(Get<int>(wcStation, "heldDog") < 0 && Get<int>(wcStation, "_pendingDog") < 0, "phase " + ph + ": nothing held or waiting (heldDog " + Get<int>(wcStation, "heldDog") + ", pending " + Get<int>(wcStation, "_pendingDog") + ")");
                    WsExpect(Get<int>(wsWander, "_travelApplies") == atApplies0, "control: the simulated ticket was never applied by the dog (travel applies " + atApplies0 + " -> " + Get<int>(wsWander, "_travelApplies") + ")");
                    WsNote("phase " + ph + " (simulated ticket, holder = walker = me" + (ph == KH_LeashManager.TravelPhaseMoved ? ", travelCenter 100 m away so it is never applied" : "") + "): board state 12, label '"
                        + WaShownText(waBoard, wsDog) + "', ReqCall refused 12, heldDog " + Get<int>(wcStation, "heldDog"));
                }));
            }
            s.Add(WsDo(() => { PaClearTicket(); waBoard.SetProgramVariable("_nextAt", 0f); }));
            s.Add(WsUntil(() => WaState(waBoard, wsDog) == KH_WashStationCall.AvCallable, 1.5, () => "ticket cleared: not callable again (state " + WaState(waBoard, wsDog) + ")"));
            s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog)));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") == wsDog, 3, () => "ticket cleared: ReqCall not accepted (refused " + Get<int>(wcStation, "refusedReason") + ")"));
            // Cleanup: ReqRelease is accepted only within operateRange (6 m) of the station (OperatorRelease / NearStation) - stand beside the
            // stand first (round 6 WcPlayerAt), wait until the player's position is read inside the range, then send it again every
            // 1.1 s (releaseInterval 1 s) until the hold is gone. Runs 20261007-100304 / 101050 failed here (the RC checks above had all
            // passed): the release sent in the same callback as the teleport was refused by NearStation
            s.Add(WsDo(() => { WsNote("ticket cleared: callable, ReqCall accepted"); WcPlayerAt(wcStation); paReleaseAt = -10; }));
            s.Add(WsUntil(() => Flat(Networking.LocalPlayer.GetPosition() - wcAnchor.position) <= Get<float>(wcStation, "operateRange") - 0.3f, 2,
                () => "not beside the station for the release (" + Flat(Networking.LocalPlayer.GetPosition() - wcAnchor.position).ToString("F1") + " m)"));
            s.Add(WsUntil(() =>
            {
                if (Get<int>(wcStation, "heldDog") < 0) return true;
                double now = UnityEditor.EditorApplication.timeSinceStartup;
                if (now - paReleaseAt >= 1.1) { paReleaseAt = now; wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease"); }
                return false;
            }, 5, () => "the release did not take"));
            s.Add(WsDo(() => pouch.SendCustomEvent("_WalkOff" + wsDog)));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] < 0, 3, () => "the leash did not come off"));

            // ================= C-wash-travel-race (RC-1 (b)) =================
            s = WsCase("C-wash-travel-race", 150);
            int trFrames0 = 0, trTick = 0; float trMaxStep = 0f, trMaxNet = 0f, trT0 = 0f; double trAt = 0; bool trBack = false, trBranch = false;
            Vector3 trPrev = Vector3.zero, trStart = Vector3.zero;
            WcSetup(s);
            WcRequireMain(s);
            s.Add(WsDo(() => wsFailDump = PaDump));
            WcLeashShiba(s, me);
            s.Add(WsDo(() =>
            {
                WsExpect(Flat(wsRoot.position - wcAnchor.position) < Get<float>(wcStation, "callRange"), "control: the leashed Shiba within callRange (" + Flat(wsRoot.position - wcAnchor.position).ToString("F1") + " m)");
                WsExpect(!Get<bool>(wsWander, "_washErrandPrev"), "control: no errand before the ticket");
                PaSetTicket(wsDog, KH_LeashManager.TravelPhaseReady);
            }));
            s.Add(WsUntil(() => Get<bool>(wsWander, "_washErrandPrev"), 1.0, () => "the dog side did not see the ticket's edge (_washErrandPrev false: TravelPending)"));
            s.Add(WsWait(0.3));   // a few frames after the edge: only the level rule can let the next hold go
            s.Add(WsDo(() =>
            {
                trFrames0 = Get<int>(wsWander, "_stationFrames");
                trTick = Time.frameCount; trT0 = Time.time;
                PaInjectHold(wsDog, me);
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0, 1.0,
                () => "the injected hold was not let go while the ticket is out (heldDog " + Get<int>(wcStation, "heldDog") + ", _stationIdx " + Get<int>(wsWander, "_stationIdx") + ", errand " + Get<bool>(wsWander, "_washErrandPrev") + ")"));
            s.Add(WsDo(() =>
            {
                int frames = Time.frameCount - trTick; float sec = Time.time - trT0;
                WsExpect(Get<int>(wsWander, "_stationFrames") == trFrames0, "the station branch never ran for the hold accepted during the ticket (" + trFrames0 + " -> " + Get<int>(wsWander, "_stationFrames") + ")");
                WsNote("simulated: ticket Phase 1 (holder = walker = me), then after the dog's edge heldDog / callerId / heldMs written in the station heap: let go in " + frames + " frames (" + sec.ToString("F2") + " s), station branch 0 frames");
                PaEndTicket(KH_LeashManager.TravelPhaseDone);
                trBack = false; trBranch = false; trMaxStep = 0f; trMaxNet = 0f;
                trStart = wsRoot.position; trPrev = trStart; trAt = UnityEditor.EditorApplication.timeSinceStartup;
            }));
            s.Add(now =>
            {
                if (Get<int>(wcStation, "heldDog") >= 0) trBack = true;
                // the station branch (its frame counter); _stationIdx itself clears on the dog's next tick after the release, so it is read at the end
                if (Get<int>(wsWander, "_stationFrames") != trFrames0) trBranch = true;
                var p = wsRoot.position;
                trMaxStep = Mathf.Max(trMaxStep, Flat(p - trPrev)); trPrev = p;
                trMaxNet = Mathf.Max(trMaxNet, Flat(p - trStart));
                return now - trAt >= 1.5;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(!trBack, "the hold did not come back after the ticket ended (heldDog " + Get<int>(wcStation, "heldDog") + ")");
                WsExpect(!trBranch && Get<int>(wsWander, "_stationIdx") < 0, "the dog never took the station after the ticket ended (_stationIdx " + Get<int>(wsWander, "_stationIdx") + ", frames " + trFrames0 + " -> " + Get<int>(wsWander, "_stationFrames") + ")");
                WsExpect(trMaxStep < 4f && trMaxNet < 5f, "no jump after the ticket ended (largest step " + trMaxStep.ToString("F2") + " m, largest move from the start " + trMaxNet.ToString("F2") + " m in 1.5 s)");
                WsNote("ticket -> Done (2): 1.5 s no hold, no station branch, largest step " + trMaxStep.ToString("F2") + " m, moved at most " + trMaxNet.ToString("F2") + " m (within callRange: the 30 m teleport path itself is not reached by this geometry)");
                // control: with no ticket the same injected hold stays and the station branch takes the dog
                PaClearTicket();
                trFrames0 = Get<int>(wsWander, "_stationFrames");
                PaInjectHold(wsDog, me);
            }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wcStation, "heldDog") == wsDog, "control: without a ticket the injected hold stays (heldDog " + Get<int>(wcStation, "heldDog") + ")");
                WsExpect(Get<int>(wsWander, "_stationFrames") > trFrames0, "control: the station branch runs for it (" + trFrames0 + " -> " + Get<int>(wsWander, "_stationFrames") + ")");
                WsNote("control (no ticket): the injected hold stayed 1 s, station branch " + (Get<int>(wsWander, "_stationFrames") - trFrames0) + " frames");
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0, 3, () => "the release did not take"));
            s.Add(WsDo(() => pouch.SendCustomEvent("_WalkOff" + wsDog)));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] < 0, 3, () => "the leash did not come off"));

            // ================= C-avail-station-washed-other (RC-8 (a)) =================
            s = WsCase("C-avail-station-washed-other", 150);
            int swSerial = 0; double swAt = 0; bool swPendingSeen = false;
            WcSetup(s);
            WcRequireMain(s);
            WaBind(s);
            s.Add(WsDo(() => wsFailDump = PaDump));
            WaSpawnPuppet(s);
            s.Add(WsDo(() => { if (Get<int[]>(manager, "walkers")[other] >= 0) pouch.SendCustomEvent("_WalkOff" + other); }));
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() => { WcBringNear(other); waBoard.SetProgramVariable("_nextAt", 0f); }));
            s.Add(WsUntil(() => WaState(waBoard, other) == KH_WashStationCall.AvCallable, 3,
                () => "control: dog " + other + " by the gate not callable before the hold (state " + WaState(waBoard, other) + ")"));
            s.Add(WsDo(() =>
            {
                if (wsWash == null) throw new InvalidOperationException("the Shiba has no KH_DogWash");
                if (PaWash(other) == null) throw new InvalidOperationException("the station's washes[" + other + "] is not wired");
                PaSaveWashes(other);
                wsWash.SetProgramVariable("washBy", waPuppet.playerId);
                wsWash.SetProgramVariable("_released", false);
                wsWash.SetProgramVariable("washSeen", true);
                wsWash.SetProgramVariable("washMs", Networking.GetServerTimeInMilliseconds());
                WsExpect(WsHold(), "control: the written hold is on (KH_DogWash._WashHold)");
                waBoard.SetProgramVariable("_nextAt", 0f);
            }));
            s.Add(WsUntil(() => WaState(waBoard, other) == KH_WashStationCall.AvWashedByOther, 1.5,
                () => "dog " + other + " was not shown waiting for the Shiba's washer (state " + WaState(waBoard, other) + ")"));
            s.Add(WsDo(() =>
            {
                swSerial = Get<int>(wcStation, "refuseSerial");
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", other);
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wcStation, "_pendingDog") == other && Get<int>(wcStation, "heldDog") == wsDog,
                    "waits while someone else washes the dog on the stand (pending " + Get<int>(wcStation, "_pendingDog") + ", heldDog " + Get<int>(wcStation, "heldDog") + ")");
                WsNote("simulated: the Shiba's wash hold written as remote player " + waPuppet.playerId + "; dog " + other + ": board state 10 ('" + WaShownText(waBoard, other) + "'), ReqCall -> pending, the Shiba still held");
                // the hold ends now (simulated: washMs older than holdSeconds)
                int holdMs = Mathf.RoundToInt(Get<float>(wsWash, "holdSeconds") * 1000f);
                wsWash.SetProgramVariable("washMs", Networking.GetServerTimeInMilliseconds() - holdMs - 1000);
                swAt = UnityEditor.EditorApplication.timeSinceStartup;
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") == other, 1.5, () => "dog " + other + " not accepted after the hold ended (heldDog " + Get<int>(wcStation, "heldDog") + ", pending " + Get<int>(wcStation, "_pendingDog") + ", refused " + Get<int>(wcStation, "refusedReason") + ")"));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wcStation, "refuseSerial") == swSerial, "accepted without a refusal in between (reason " + Get<int>(wcStation, "refusedReason") + ")");
                WsNote("hold ended (washMs written into the past): dog " + other + " accepted " + (UnityEditor.EditorApplication.timeSinceStartup - swAt).ToString("F2") + " s later, the Shiba let go");
                // control: the dog on the stand washed by ME -> calling another dog is not held back
                var ow = PaWash(other);
                ow.SetProgramVariable("washBy", me);
                ow.SetProgramVariable("_released", false);
                ow.SetProgramVariable("washSeen", true);
                ow.SetProgramVariable("washMs", Networking.GetServerTimeInMilliseconds());
                swPendingSeen = false;
                swAt = UnityEditor.EditorApplication.timeSinceStartup;
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog);
            }));
            s.Add(WsUntil(() =>
            {
                if (Get<int>(wcStation, "_pendingDog") == wsDog) swPendingSeen = true;
                return Get<int>(wcStation, "heldDog") == wsDog;
            }, 1.5, () => "control: the Shiba not accepted while dog " + other + " is washed by me (heldDog " + Get<int>(wcStation, "heldDog") + ", pending " + Get<int>(wcStation, "_pendingDog") + ", refused " + Get<int>(wcStation, "refusedReason") + ")"));
            s.Add(WsDo(() =>
            {
                WsExpect(!swPendingSeen, "control: accepted at once (never waited)");
                WsNote("control (simulated: dog " + other + " on the stand washed by me): ReqCall(Shiba) accepted at once in " + (UnityEditor.EditorApplication.timeSinceStartup - swAt).ToString("F2") + " s, no wait");
                PaRestoreWashes();
                wsWash.SetProgramVariable("washBy", -1);
                WaRemovePuppet();
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0, 3, () => "the release did not take"));

            // ================= C-placed-river-timeout (RC-2) =================
            s = WsCase("C-placed-river-timeout", 2f * at + 80f);
            UdonBehaviour rvSlot = null;
            Vector3 rvSpot = Vector3.zero, rvBank = Vector3.zero; float rvCx = 0f, rvHw = 0f, rvAt = 30f, rvLift = 0.4f, rvMinDist = 0f, rvMinLat = 0f;
            int rvCalled = 0, rvAge = -1, rvWater = 0; bool rvHeld = false, rvArr = false; string rvWhere = "";
            WcSetup(s);
            s.Add(WsDo(() =>
            {
                wsFailDump = PaDump;
                rvSlot = PaSlot();
                rvAt = Get<float>(rvSlot, "arriveTimeout");
                rvLift = Get<float>(wsWander, "placedSleepLiftRadius");
                rvWhere = PaRiverSpot(out rvSpot, out rvBank, out rvCx, out rvHw);
                WaPlaceShiba(rvBank);
                PaPlayerBeside(rvBank, rvCx);
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                PaCallPlaced(rvSlot, rvSpot, 0f, me);
                rvCalled = Get<int>(rvSlot, "calledMs");
                rvMinDist = Flat(wsRoot.position - rvSpot); rvMinLat = Mathf.Abs(wsRoot.position.x - rvCx) - rvHw;
                rvHeld = false; rvArr = false; rvWater = 0; rvAge = -1;
                WsNote("river spot " + rvSpot.ToString("F1") + " (" + rvWhere + "); the Shiba placed on the bank " + rvBank.ToString("F1") + " (simulated placement, home pinned); arriveTimeout " + rvAt + " s");
            }));
            s.Add(WsWait(3.0));
            s.Add(WsDo(() => { PaPlayerBeside(wsRoot.position, rvCx); pouch.SendCustomEvent("_WalkLeft" + wsDog); }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() => WsExpect(Get<int[]>(manager, "walkers")[wsDog] < 0, "control: the called Shiba takes no leash (walker " + Get<int[]>(manager, "walkers")[wsDog] + ")")));
            s.Add(now =>
            {
                if (PaPlacedHeld(wsWander)) rvHeld = true;
                if (Get<bool>(rvSlot, "arrived")) rvArr = true;
                if (Get<int>(wsWander, "waterState") != 0) rvWater++;
                rvMinDist = Mathf.Min(rvMinDist, Flat(wsRoot.position - rvSpot));
                rvMinLat = Mathf.Min(rvMinLat, Mathf.Abs(wsRoot.position.x - rvCx) - rvHw);
                int age = Networking.GetServerTimeInMilliseconds() - rvCalled;
                if (Get<int>(rvSlot, "sleeperId") < 0) { rvAge = age; return true; }
                if (age > (int)(rvAt * 2000f) + 4000) throw new TimeoutException("the placed call into the river was still live " + age + " ms after calledMs (arriveTimeout x 2 = " + (int)(rvAt * 2000f) + " ms)");
                return false;
            });
            s.Add(WsDo(() =>
            {
                int limit = (int)(rvAt * 2000f);
                WsExpect(!rvArr, "never arrived on a spot in the river band");
                WsExpect(!rvHeld, "PlacedSleepHeld never set (the dog stopped on the bank, " + rvMinDist.ToString("F2") + " m from the spot at the closest)");
                WsExpect(rvMinDist > rvLift, "the dog stayed out of the lift radius (" + rvMinDist.ToString("F2") + " / " + rvLift + " m)");
                WsExpect(rvAge > limit && rvAge <= limit + 2000, "released by the timeout: after arriveTimeout x 2 = " + limit + " ms, at the next Watch (released at " + rvAge + " ms)");
                WsNote("released " + rvAge + " ms after calledMs (limit " + limit + "); closest " + rvMinDist.ToString("F2") + " m to the spot, closest to the band edge " + rvMinLat.ToString("F2") + " m (|x - centre| - halfWidth), water play frames " + rvWater + "; leash refused while called");
                PaPlayerBeside(wsRoot.position, rvCx);
                pouch.SendCustomEvent("_WalkLeft" + wsDog);
            }));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] == me, 3,
                () => "the leash did not attach right after the release (walker " + Get<int[]>(manager, "walkers")[wsDog] + ", called " + Get<int>(rvSlot, "sleeperId") + ", water " + Get<int>(wsWander, "waterState") + ")"));
            s.Add(WsDo(() => { WsNote("leash attached right after the release"); pouch.SendCustomEvent("_WalkOff" + wsDog); }));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] < 0, 3, () => "the leash did not come off"));
            s.Add(WsDo(() => WcCleanup()));   // the pinned home back

            // ================= C-placed-reach (control) =================
            s = WsCase("C-placed-reach", 2f * at + 70f);
            UdonBehaviour rcSlot = null;
            Vector3 rcSpot = Vector3.zero; float rcAt = 30f, rcT0 = 0f, rcNextWatch = 0f; int rcCalled = 0, rcArrAge = -1, rcWatches = 0, rcLate = 0; bool rcHeld = false, rcLost = false;
            var rcLog = new List<string>();
            WcSetup(s);
            s.Add(WsDo(() =>
            {
                wsFailDump = PaDump;
                rcSlot = PaSlot();
                rcAt = Get<float>(rcSlot, "arriveTimeout");
                Vector3 dir = WsFlat(wsRoot.position - wcAnchor.position);
                Vector3 g = CmGround(wsRoot.position + dir * 3f);
                rcSpot = g + Vector3.up * 1.0f;
                Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
                Vector3 p = WcGroundNear(g + side * 2f);
                CmTeleport(p, g - p);
                PaCallPlaced(rcSlot, rcSpot, Quaternion.LookRotation(dir).eulerAngles.y, me);
                rcCalled = Get<int>(rcSlot, "calledMs");
                rcHeld = false; rcLost = false;
            }));
            s.Add(now =>
            {
                if (PaPlacedHeld(wsWander)) rcHeld = true;
                if (Get<int>(rcSlot, "sleeperId") != me) rcLost = true;
                int age = Networking.GetServerTimeInMilliseconds() - rcCalled;
                if (rcLost) throw new InvalidOperationException("the placed call was released before arriving (" + age + " ms)");
                if (Get<bool>(rcSlot, "arrived")) { rcArrAge = age; return true; }
                if (age > (int)(rcAt * 2000f)) throw new TimeoutException("not arrived at a reachable spot 1 m above the ground within arriveTimeout x 2 (" + Vector3.Distance(wsRoot.position, rcSpot).ToString("F2") + " m from it)");
                return false;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(rcHeld, "PlacedSleepHeld was set on the way up (the lift)");
                WsExpect(Vector3.Distance(wsRoot.position, rcSpot) <= 0.1f, "on the spot (" + Vector3.Distance(wsRoot.position, rcSpot).ToString("F3") + " m)");
                WsNote("spot 1 m above the ground, 3 m away: arrived " + rcArrAge + " ms after calledMs");
                rcSlot.SetProgramVariable("calledMs", Networking.GetServerTimeInMilliseconds() - 90000);
                rcNextWatch = Get<float>(rcSlot, "_nextWatch"); rcWatches = 0;
            }));
            s.Add(now =>
            {
                float nw = Get<float>(rcSlot, "_nextWatch");
                if (nw != rcNextWatch) { rcNextWatch = nw; rcWatches++; }
                if (Get<int>(rcSlot, "sleeperId") != me) throw new InvalidOperationException("an arrived placed call was released after calledMs was written 90 s into the past");
                return rcWatches >= 2 && now - wsStepAt >= 1.2;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(Get<bool>(rcSlot, "arrived"), "still arrived");
                WsNote("simulated: calledMs written 90 s into the past after the arrival: " + rcWatches + " Watch runs, still called (arrived)");
                rcSlot.SendCustomEvent("_Release");
            }));
            s.Add(WsUntil(() => Get<int>(rcSlot, "sleeperId") < 0, 2, () => "_Release did not release the call"));
            // PlacedHeld exclusion: the timeout passes while the dog is being lifted (simulated: arriveTimeout 0.3 s in the heap)
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                float liftSpeed = Mathf.Max(0.05f, Get<float>(wsWander, "placedSleepLiftSpeed"));
                float h = Mathf.Clamp(liftSpeed * 2.5f, 1.0f, 1.6f);   // >= 2 s of lift: several Watch runs past the 0.6 s limit
                Vector3 g = wsRoot.position;
                WaPlaceShiba(g);
                rcSpot = g + Vector3.up * h;
                PaSetArriveTimeout(rcSlot, 0.3f);
                PaCallPlaced(rcSlot, rcSpot, wsRoot.eulerAngles.y, me);
                rcCalled = Get<int>(rcSlot, "calledMs");
                rcNextWatch = Get<float>(rcSlot, "_nextWatch"); rcWatches = 0; rcLate = 0; rcT0 = Time.time; rcLog.Clear();
                WsNote("simulated: arriveTimeout 0.3 s (limit 0.6 s); spot " + h.ToString("F2") + " m straight above the Shiba (lift " + liftSpeed.ToString("F2") + " m/s)");
            }));
            s.Add(now =>
            {
                int age = Networking.GetServerTimeInMilliseconds() - rcCalled;
                bool held = PaPlacedHeld(wsWander), arr = Get<bool>(rcSlot, "arrived");
                float nw = Get<float>(rcSlot, "_nextWatch");
                if (nw != rcNextWatch)
                {
                    rcNextWatch = nw; rcWatches++;
                    if (rcLog.Count < 12) rcLog.Add(age + "ms held " + (held ? 1 : 0) + " arr " + (arr ? 1 : 0));
                    if (age > 600 && !arr && held) rcLate++;
                }
                if (Get<int>(rcSlot, "sleeperId") != me) throw new InvalidOperationException("released during the lift " + age + " ms after calledMs (Watch runs: " + string.Join(", ", rcLog) + ")");
                if (arr) return true;
                if (Time.time - rcT0 > 8f) throw new TimeoutException("not arrived 8 s into the lift (" + Vector3.Distance(wsRoot.position, rcSpot).ToString("F2") + " m; Watch runs: " + string.Join(", ", rcLog) + ")");
                return false;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(rcLate >= 1, "at least one Watch ran past the 0.6 s limit while the dog was lifted and not yet arrived (" + string.Join(", ", rcLog) + ")");
                WsNote("lift: " + rcLate + " Watch runs past the limit while PlacedSleepHeld and not arrived, never released; arrived (" + string.Join(", ", rcLog) + ")");
                PaRestoreArriveTimeout();
                rcSlot.SendCustomEvent("_Release");
            }));
            s.Add(WsUntil(() => Get<int>(rcSlot, "sleeperId") < 0, 2, () => "_Release did not release the call"));
            s.Add(WsDo(() => WcCleanup()));
        }

        // ================= helpers =================

        /// <summary>The main station and the Shiba's helpers, as at the top of MakeWashCallCases (the audit-a group runs without it).</summary>
        static void PaBindStation()
        {
            wcStation = null; wcAnchor = null; wcLiftRoot = null;
            foreach (var u in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>())
                if (u.programSource != null && u.programSource.name == "KH_WashStationCall" && Get<int>(u, "stationIndex") == 0) { wcStation = u; break; }
            if (wcStation != null)
            {
                wcAnchor = WsRef(wcStation, "anchor") as Transform;
                wcLiftRoot = WsRef(wcStation, "liftRoot") as Transform;
                if (wcLiftRoot != null) wcLiftBaseY = wcLiftRoot.localPosition.y - Get<float>(wcStation, "_liftNow");
            }
            wcFetch = WsRef(wsWander, "fetchController") as UdonBehaviour;
            wcCatcher = WsRef(wsWander, "frisbeeCatch") as UdonBehaviour;
            wcActivities = WsRef(wsWander, "activities") as UdonBehaviour;
        }

        // ---- travel ticket (simulated: the leash manager's synced arrays, copies written back as TreatCall.cs TcSetTicket) ----
        static bool paTicketSet;
        static Vector3[] paCenter0;

        static void PaSetTicket(int d, int phase)
        {
            var req = (int[])Get<int[]>(manager, "travelReq").Clone(); var hold = (int[])Get<int[]>(manager, "travelHolder").Clone();
            var ph = (int[])Get<int[]>(manager, "travelPhase").Clone(); var at = (int[])Get<int[]>(manager, "travelAtMs").Clone();
            var center = (Vector3[])Get<Vector3[]>(manager, "travelCenter").Clone();
            if (paCenter0 == null) paCenter0 = (Vector3[])center.Clone();
            req[d] = PaTicketReq; hold[d] = Networking.LocalPlayer.playerId; ph[d] = phase; at[d] = Networking.GetServerTimeInMilliseconds();
            // Phase 4 is applied by the dog only when its holder stands within 6 m of travelCenter: 100 m away = never applied
            center[d] = Networking.LocalPlayer.GetPosition() + new Vector3(100f, 0f, 0f);
            manager.SetProgramVariable("travelReq", req); manager.SetProgramVariable("travelHolder", hold);
            manager.SetProgramVariable("travelPhase", ph); manager.SetProgramVariable("travelAtMs", at);
            manager.SetProgramVariable("travelCenter", center);
            paTicketSet = true;
        }

        /// <summary>The simulated ticket ends with `phase` (Done = 2), kept as that ticket (holder, request id).</summary>
        static void PaEndTicket(int phase)
        {
            var req = Get<int[]>(manager, "travelReq");
            var ph = (int[])Get<int[]>(manager, "travelPhase").Clone();
            for (int i = 0; i < req.Length && i < ph.Length; i++) if (req[i] == PaTicketReq) ph[i] = phase;
            manager.SetProgramVariable("travelPhase", ph);
        }

        static void PaClearTicket()
        {
            if (manager == null) return;
            var req = (int[])Get<int[]>(manager, "travelReq").Clone(); var hold = (int[])Get<int[]>(manager, "travelHolder").Clone();
            var ph = (int[])Get<int[]>(manager, "travelPhase").Clone();
            bool changed = false;
            for (int i = 0; i < req.Length; i++)
                if (req[i] == PaTicketReq) { req[i] = 0; hold[i] = -1; ph[i] = KH_LeashManager.TravelPhaseNone; changed = true; }
            if (changed) { manager.SetProgramVariable("travelReq", req); manager.SetProgramVariable("travelHolder", hold); manager.SetProgramVariable("travelPhase", ph); }
            if (paCenter0 != null) { manager.SetProgramVariable("travelCenter", paCenter0); paCenter0 = null; }
            paTicketSet = false;
        }

        /// <summary>Simulated: the station's synced hold written as Accept writes it (heldDog / callerId / arrived / heldMs), Owner = me.</summary>
        static void PaInjectHold(int dog, int caller)
        {
            wcStation.SetProgramVariable("heldDog", dog);
            wcStation.SetProgramVariable("callerId", caller);
            wcStation.SetProgramVariable("arrived", false);
            wcStation.SetProgramVariable("heldMs", Networking.GetServerTimeInMilliseconds());
        }

        // ---- the other dog's wash (KH_WashStationCall.washes[dog]) ----
        static UdonBehaviour paWash;
        static int paWashBy0, paWashMs0; static bool paWashSeen0, paReleased0, paWashSaved;

        static UdonBehaviour PaWash(int dog)
        {
            var a = WsRef(wcStation, "washes") as Array;
            return a != null && dog >= 0 && dog < a.Length ? a.GetValue(dog) as UdonBehaviour : null;
        }

        static void PaSaveWashes(int dog)
        {
            paWash = PaWash(dog);
            if (paWash == null) return;
            paWashBy0 = Get<int>(paWash, "washBy"); paWashMs0 = Get<int>(paWash, "washMs");
            paWashSeen0 = Get<bool>(paWash, "washSeen"); paReleased0 = Get<bool>(paWash, "_released");
            paWashSaved = true;
        }

        static void PaRestoreWashes()
        {
            if (!paWashSaved || paWash == null) { paWashSaved = false; return; }
            paWash.SetProgramVariable("washBy", paWashBy0); paWash.SetProgramVariable("washMs", paWashMs0);
            paWash.SetProgramVariable("washSeen", paWashSeen0); paWash.SetProgramVariable("_released", paReleased0);
            paWashSaved = false; paWash = null;
        }

        // ---- the Shiba's KD_SleepSlot (the placed call) ----
        static UdonBehaviour paSlot;
        static float paArrive0 = -1f;
        static bool paSlotCalled;   // a placed call made here (PaCleanup releases only that one)
        static double paReleaseAt;  // C-avail-traveling cleanup: when ReqRelease was last sent (re-sent every 1.1 s until the hold is gone)

        static UdonBehaviour PaSlot()
        {
            foreach (var u in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>())
                if (u.programSource != null && u.programSource.name == "KD_SleepSlot" && Get<int>(u, "dogIndex") == wsDog) { paSlot = u; return u; }
            throw new InvalidOperationException("KD_SleepSlot of dog " + wsDog + " missing");
        }

        /// <summary>The product path of a placed call: _BeginSelection (I am the slot Owner: the reservation is taken at once), the
        /// requested spot / yaw / base pose 0, then _TryCallPlaced (Commit).</summary>
        static void PaCallPlaced(UdonBehaviour slot, Vector3 spot, float yaw, int me)
        {
            slot.SetProgramVariable("requestedPose", 0);
            paSlotCalled = true;
            slot.SendCustomEvent("_BeginSelection");
            if (Get<int>(slot, "reservedId") != me) throw new InvalidOperationException("the placed call's reservation was not taken (reservedId " + Get<int>(slot, "reservedId") + ", sleeper " + Get<int>(slot, "sleeperId") + ", walker " + Get<int[]>(manager, "walkers")[wsDog] + ")");
            slot.SetProgramVariable("requestedSpot", spot);
            slot.SetProgramVariable("requestedYaw", yaw);
            slot.SendCustomEvent("_TryCallPlaced");
            WsExpect(Get<int>(slot, "sleeperId") == me && Get<bool>(slot, "hasSpot") && Vector3.Distance(Get<Vector3>(slot, "spot"), spot) < 0.01f,
                "the placed call committed (sleeper " + Get<int>(slot, "sleeperId") + ", hasSpot " + Get<bool>(slot, "hasSpot") + ")");
        }

        static bool PaPlacedHeld(UdonBehaviour w) { return Get<bool>(w, "_placedSleepActive") && Get<bool>(w, "_sleepFrozen"); }

        static void PaSetArriveTimeout(UdonBehaviour slot, float v)
        {
            if (paArrive0 < 0f) paArrive0 = Get<float>(slot, "arriveTimeout");
            slot.SetProgramVariable("arriveTimeout", v);
        }

        static void PaRestoreArriveTimeout()
        {
            if (paArrive0 >= 0f && paSlot != null) paSlot.SetProgramVariable("arriveTimeout", paArrive0);
            paArrive0 = -1f;
        }

        /// <summary>A point in the middle of the river band and a NavMesh bank point beside it, from KH_RiverData's arrays (the same
        /// numbers _Lateral / _OnBridgeDeck use: the centre is inside the band with any margin, away from the bridges by bridgeHalfZ
        /// + 5 m). Preferred: 20 m or more from every water spot and wash station (no water play / keep-out in the way), then the
        /// slice nearest the gate. The bank point: centre +- (halfWidth + 2.5), sampled within 1 m and at least halfWidth + 1.2 out.</summary>
        static string PaRiverSpot(out Vector3 spot, out Vector3 bank, out float centerX, out float halfWidth)
        {
            var river = WsRef(wsWander, "riverData") as UdonBehaviour;
            if (river == null) throw new InvalidOperationException("the Shiba's riverData is not wired");
            float z0 = Get<float>(river, "zStart"), dz = Get<float>(river, "zStep");
            var cx = Get<float[]>(river, "centerX"); var sy = Get<float[]>(river, "surfaceY"); var hw = Get<float[]>(river, "halfWidth");
            float[] bz = null; try { bz = Get<float[]>(river, "bridgeZ"); } catch (InvalidOperationException) { }
            float bh = Get<float>(river, "bridgeHalfZ");
            if (cx == null || cx.Length < 2) throw new InvalidOperationException("KH_RiverData is not built (centerX)");
            var avoid = new List<Vector3>();
            foreach (var u in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>())
            {
                if (u.programSource == null) continue;
                if (u.programSource.name == "KH_WaterSpot") avoid.Add(u.transform.position);
                else if (u.programSource.name == "KH_WashStationCall") { var an = WsRef(u, "anchor") as Transform; if (an != null) avoid.Add(an.position); }
            }
            int best = -1; Vector3 bestBank = Vector3.zero; float bestScore = float.MaxValue, bestClear = 0f;
            for (int i = 2; i < cx.Length - 2; i++)
            {
                float z = z0 + i * dz;
                bool nearBridge = false;
                if (bz != null) foreach (float b in bz) if (Mathf.Abs(z - b) <= bh + 5f) nearBridge = true;
                if (nearBridge || hw[i] <= 0.05f) continue;
                Vector3 c = new Vector3(cx[i], sy[i], z);
                foreach (float sd in new[] { -1f, 1f })
                {
                    UnityEngine.AI.NavMeshHit h;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(new Vector3(cx[i] + sd * (hw[i] + 2.5f), sy[i], z), out h, 1.0f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    if (Mathf.Abs(h.position.x - cx[i]) - hw[i] < 1.2f || Mathf.Abs(h.position.y - sy[i]) > 4f || Mathf.Abs(h.position.z - z) > 1f) continue;
                    float clear = float.MaxValue;
                    foreach (var a in avoid) clear = Mathf.Min(clear, Flat(a - c));
                    float score = (clear >= 20f ? 0f : 100000f - clear * 1000f) + Flat(c - nodes[0]);
                    if (score < bestScore) { bestScore = score; best = i; bestBank = h.position; bestClear = clear; }
                }
            }
            if (best < 0) throw new InvalidOperationException("no river slice with a NavMesh bank point beside it");
            spot = new Vector3(cx[best], sy[best], z0 + best * dz);
            bank = bestBank; centerX = cx[best]; halfWidth = hw[best];
            return "slice " + best + " z " + spot.z.ToString("F1") + ", halfWidth " + hw[best].ToString("F2") + ", in the band by _Lateral = -" + (hw[best] + KH_RiverData.BandMargin).ToString("F2")
                + " m, " + (bestClear == float.MaxValue ? "no water spot / station" : bestClear.ToString("F0") + " m from the nearest water spot / station");
        }

        /// <summary>Me 1.5 m from p on the land side (away from the river centre line), facing p: within the leash's attachReach.</summary>
        static void PaPlayerBeside(Vector3 p, float riverCenterX)
        {
            Vector3 outw = new Vector3(Mathf.Sign(p.x - riverCenterX), 0f, 0f);
            Vector3 q = WcGroundNear(p + outw * 1.5f);
            CmTeleport(q, p - q);
        }

        /// <summary>WcCleanup (every WcSetup): what a failed audit case left - the simulated ticket, the other dog's wash fields,
        /// the shortened arriveTimeout and my live placed call.</summary>
        static void PaCleanup()
        {
            if (paTicketSet || paCenter0 != null) { try { PaClearTicket(); } catch (Exception) { } }
            try { PaRestoreWashes(); } catch (Exception) { }
            try { PaRestoreArriveTimeout(); } catch (Exception) { }
            if (paSlotCalled && paSlot != null && Utilities.IsValid(Networking.LocalPlayer))
            {
                paSlotCalled = false;
                try
                {
                    int me = Networking.LocalPlayer.playerId;
                    if (Get<int>(paSlot, "sleeperId") == me) paSlot.SendCustomEvent("_Release");
                    else if (Get<int>(paSlot, "reservedId") == me) paSlot.SendCustomEvent("_EndSelection");
                }
                catch (Exception) { }
            }
        }

        static string PaDump()
        {
            Func<Func<string>, string> t = f => { try { return f(); } catch (Exception) { return "?"; } };
            string st = wcStation == null ? "no station" : t(() => "held " + Get<int>(wcStation, "heldDog") + " caller " + Get<int>(wcStation, "callerId") + " pending " + Get<int>(wcStation, "_pendingDog")
                + " refused " + Get<int>(wcStation, "refusedDog") + "/" + Get<int>(wcStation, "refusedReason") + " #" + Get<int>(wcStation, "refuseSerial") + " arrived " + Get<bool>(wcStation, "arrived"));
            string tk = t(() => "ticket req " + Get<int[]>(manager, "travelReq")[wsDog] + " holder " + Get<int[]>(manager, "travelHolder")[wsDog] + " phase " + Get<int[]>(manager, "travelPhase")[wsDog]
                + " age " + (Networking.GetServerTimeInMilliseconds() - Get<int[]>(manager, "travelAtMs")[wsDog]) + " ms walker " + Get<int[]>(manager, "walkers")[wsDog]);
            string dg = t(() => "stIdx " + Get<int>(wsWander, "_stationIdx") + " stFrames " + Get<int>(wsWander, "_stationFrames") + " errand " + Get<bool>(wsWander, "_washErrandPrev")
                + " placed " + Get<bool>(wsWander, "_placedSleepActive") + " frozen " + Get<bool>(wsWander, "_sleepFrozen") + " water " + Get<int>(wsWander, "waterState"));
            string sl = paSlot == null ? "no slot" : t(() => "slot sleeper " + Get<int>(paSlot, "sleeperId") + " reserved " + Get<int>(paSlot, "reservedId") + " hasSpot " + Get<bool>(paSlot, "hasSpot")
                + " arrived " + Get<bool>(paSlot, "arrived") + " age " + (Networking.GetServerTimeInMilliseconds() - Get<int>(paSlot, "calledMs")) + " ms timeout " + Get<float>(paSlot, "arriveTimeout")
                + " spot d " + Vector3.Distance(Get<Vector3>(paSlot, "spot"), wsRoot.position).ToString("F2"));
            return st + " | " + tk + " | " + dg + " | " + sl + " | " + t(WcDump);
        }
    }
}
