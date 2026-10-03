using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-037 洗い場で呼べる犬の表示と距離 — Course/100f の群 call の末尾 (WashCall.cs の MakeWashCallCases から呼ぶ)。
    /// 設計は docs/plans/2026-10-01-wash-call-and-height.md §7。主洗い場 (stationIndex 0) のボード (KH_WashStationBoard) が犬ごとに出す
    /// 状態 (ヒープの _dogState = KH_WashStationCall._Availability の値、2 行目の文字、ボタンの色) と、Owner の受理・拒否 (refusedReason)
    /// が同じ規則であることを見る。ボードは自分が showMeters (12 m) 以内に居る時だけ更新するので、自分は主洗い場の横に立つ。
    ///
    /// C-avail-near: 門の柴は「呼べます」(緑)。ボードの _Dog で呼ぶ → 「向かっています」→ 着いて「ここにいます」(青)。
    /// C-avail-mine: 自分がリードで連れている柴は「呼べます」(自分の犬は呼べる)。
    /// C-avail-range (家の固定だけ模擬): 範囲の内側 (callRange − 0.5〜1.5 m) は「呼べます」で受理、外側 (+0.5〜3 m) は「遠すぎます」、
    ///   ボードの _Dog は押したその場で理由を出し (ネットワークを待たない)、Owner は RefuseTooFar で断り、犬は動かない。45 m 先 (遠い
    ///   犬を近くへ出す 30 m の規則より遠い) でも直接の ReqCall は断られ、近くへ現れない (すり抜けない)。
    /// C-avail-busy (集合はヒープへの書き込み = 模擬): Fetch 中は「ボールで遊んでいます」、集合中は「集合しています」。集合中に押すと
    ///   待って 5 秒で RefuseBusy、ボードの知らせは「集合しています」(具体的な理由)。
    /// C-avail-other-station: 主洗い場に留めた柴を、川の洗い場のボードは「ほかの洗い場にいます」と出し、川の洗い場の Owner も
    ///   RefuseOtherStation で断る。
    /// C-avail-leash-other: ClientSim の遠隔プレイヤー (人形、Udon なし) の id を walkers[柴] に書く (リード管理の保守は実在する
    ///   プレイヤーを外さない) と「ほかの人と散歩中」、直接の ReqCall は RefuseLeashedByOther。SKIP しない。
    /// C-avail-washed-other (入力は模擬): 柴の洗い保持 (同期値 washMs / washSeen / washBy) を人形の id のストロークとしてヒープへ書く
    ///   (ClientSim は他人の NetStroke を送れない) と「ほかの人が洗っています」、呼ぶと待ち、保持 (3 s) が切れたら来る。
    /// C-avail-range の拒否の後は、2 s のあいだ毎 tick「留められない・犬の側が洗い場を取らない・跳ばない」を見る (自由な犬が
    ///   歩き回るのは可。run 20261002-223745 は 26.5 → 22.5 m の徘徊を「入って来た」と誤判定していた)。
    ///
    /// 見ていないもの: 2 人の実際の画面 (表示を見た後に他人が先に呼んだ・リードを付けた時の拒否の伝わり方)、言語の切り替え後の文字、
    /// 文字の読みやすさと枠 (Play / 実機)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        static UdonBehaviour waBoard, waRiver, waRiverBoard;

        static void MakeWashCallAvailCases()
        {
            int me = Networking.LocalPlayer.playerId;

            // ---- C-avail-near ----
            var s = WsCase("C-avail-near", 110);
            WcSetup(s);
            WaBind(s);
            s.Add(WsUntil(() => WaState(waBoard, wsDog) == KH_WashStationCall.AvCallable, 3, () => "the board did not show the Shiba at the gate as callable (state " + WaState(waBoard, wsDog) + ")"));
            s.Add(WsDo(() =>
            {
                WaExpectShown(waBoard, wsDog, KH_WashStationBoard.WStCallable, KH_WashStationBoard.LookCallable, "the Shiba at the gate");
                var rule = WaRef<TMPro.TextMeshProUGUI>(waBoard, "ruleText");
                string range = Mathf.RoundToInt(Get<float>(wcStation, "callRange")).ToString();
                WsExpect(rule != null && rule.text.Contains(range), "the rule line names the range " + range + " m ('" + (rule != null ? rule.text : "none") + "')");
                WsNote("gate (" + Flat(wsRoot.position - wcAnchor.position).ToString("F1") + " m): '" + WaShownText(waBoard, wsDog) + "'; rule: '" + rule.text + "'");
                waBoard.SendCustomEvent("_Dog" + wsDog);
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") == wsDog, 6, () => "board _Dog" + wsDog + " did not hold the Shiba (refused " + Get<int>(wcStation, "refusedReason") + ")"));
            bool anComing = false;
            s.Add(WsUntil(() =>
            {
                if (WaState(waBoard, wsDog) == KH_WashStationCall.AvHeldHere && WaWordShown(waBoard, wsDog) == KH_WashStationBoard.WStComing) anComing = true;
                return Get<bool>(wcStation, "arrived") && Flat(wsRoot.position - wcAnchor.position) < 0.35f;
            }, 60, () => "the Shiba did not arrive"));
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                WsExpect(anComing, "the board showed 'on the way' while the Shiba walked to the stand");
                WaExpectShown(waBoard, wsDog, KH_WashStationBoard.WStHere, KH_WashStationBoard.LookHere, "the Shiba on the stand");
                WsNote("pressed: '" + WaWord(waBoard, KH_WashStationBoard.WStComing) + "' on the way, then '" + WaShownText(waBoard, wsDog) + "'; status '" + Get<string>(waBoard, "_shown") + "'");
                waBoard.SendCustomEvent("_End");
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0, 3, () => "board _End did not release"));

            // ---- C-avail-mine (my own leashed dog can be called) ----
            s = WsCase("C-avail-mine", 80);
            WcSetup(s);
            WaBind(s);
            s.Add(WsDo(() =>
            {
                Vector3 d = wsRoot.position, p = WcGroundNear(d + WsFlat(wsRoot.right) * 1.3f);
                CmTeleport(p, d - p);
                pouch.SendCustomEvent("_WalkLeft" + wsDog);
            }));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] == me, 6, () => "control: the leash did not attach"));
            s.Add(WsDo(() => WcPlayerAtStation()));
            s.Add(WsUntil(() => Flat(wsRoot.position - wcAnchor.position) < Get<float>(wcStation, "callRange") - 2f && WaState(waBoard, wsDog) == KH_WashStationCall.AvCallable, 20,
                () => "the leashed Shiba near me was not shown callable (state " + WaState(waBoard, wsDog) + ", " + Flat(wsRoot.position - wcAnchor.position).ToString("F1") + " m)"));
            s.Add(WsDo(() =>
            {
                WaExpectShown(waBoard, wsDog, KH_WashStationBoard.WStCallable, KH_WashStationBoard.LookCallable, "my leashed Shiba");
                WsNote("leashed to me, " + Flat(wsRoot.position - wcAnchor.position).ToString("F1") + " m: '" + WaShownText(waBoard, wsDog) + "'");
                pouch.SendCustomEvent("_WalkOff" + wsDog);
            }));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] < 0, 3, () => "the leash did not come off"));

            // ---- C-avail-range (inside / outside the boundary, the press, the Owner's refusal, no teleport from 45 m) ----
            s = WsCase("C-avail-range", 150);
            float rgRange = 0f; int rgSerial = 0; Vector3 rgOut = Vector3.zero;
            WcSetup(s);
            WaBind(s);
            s.Add(WsDo(() =>
            {
                rgRange = Get<float>(wcStation, "callRange");
                if (rgRange <= 0f) throw new InvalidOperationException("callRange is " + rgRange + " (no limit): run Wash/11");
                WcSaveWander();
                WaPlaceShiba(WaRingPoint(wcAnchor.position, rgRange - 2f, rgRange - 0.5f));
            }));
            s.Add(WsUntil(() => WaState(waBoard, wsDog) == KH_WashStationCall.AvCallable, 2, () => "inside the range: not shown callable (state " + WaState(waBoard, wsDog) + ", " + Flat(wsRoot.position - wcAnchor.position).ToString("F2") + " m)"));
            s.Add(WsDo(() =>
            {
                WsNote("inside: " + Flat(wsRoot.position - wcAnchor.position).ToString("F2") + " / " + rgRange + " m -> '" + WaShownText(waBoard, wsDog) + "'");
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog);
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") == wsDog, 3, () => "inside the range: the call was not accepted (refused " + Get<int>(wcStation, "refusedReason") + ")"));
            s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease")));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0 && Get<int>(wsWander, "_stationIdx") < 0, 3, () => "the release did not take"));
            s.Add(WsDo(() =>
            {
                rgOut = WaRingPoint(wcAnchor.position, rgRange + 1.5f, rgRange + 3f);   // the free dog wanders within 3 m of its (pinned) home
                WaPlaceShiba(rgOut);
            }));
            s.Add(WsUntil(() => WaState(waBoard, wsDog) == KH_WashStationCall.RefuseTooFar, 2, () => "outside the range: not shown too far (state " + WaState(waBoard, wsDog) + ", " + Flat(wsRoot.position - wcAnchor.position).ToString("F2") + " m)"));
            s.Add(WsDo(() =>
            {
                WaExpectShown(waBoard, wsDog, KH_WashStationBoard.WStTooFar, KH_WashStationBoard.LookUnavailable, "the Shiba just outside the range");
                rgSerial = Get<int>(wcStation, "refuseSerial");
                waBoard.SendCustomEvent("_Dog" + wsDog);
                // the reason is on this screen at once (the board's own check, before any network round trip)
                string local = Get<string>(waBoard, "_refuseText");
                WsExpect(Get<float>(waBoard, "_refuseUntil") > Time.time && local.Contains(WaWord(waBoard, KH_WashStationBoard.WStTooFar)), "the press showed the reason at once ('" + local + "')");
                WsExpect(Get<int>(waBoard, "_myPick") < 0, "a refused press is not shown as 'calling'");
                WsNote("outside: " + Flat(wsRoot.position - wcAnchor.position).ToString("F2") + " / " + rgRange + " m -> '" + WaShownText(waBoard, wsDog) + "'; pressed -> '" + local + "' at once");
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "refuseSerial") != rgSerial, 2, () => "the Owner did not refuse the press from outside the range"));
            // Run 20261002-223745: the refused Shiba was 22.5 m away 2 s later (from 26.5 m) although it was never held - the free dog
            // simply moved (wander / a stop-by toward me). What the rule forbids is a hold, the station branch or a teleport, so those
            // are sampled every tick for 2 s instead of the end distance.
            WaWatchNotCalled(s, 2.0);
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wcStation, "refusedReason") == KH_WashStationCall.RefuseTooFar && Get<int>(wcStation, "refusedDog") == wsDog, "refused as too far (" + Get<int>(wcStation, "refusedReason") + ")");
                WaExpectNotCalled("26 m press");
                // 45 m: beyond the dog side's 30 m "appear near the station" rule - the request itself is refused, so no teleport
                WaPlaceShiba(WaRingPoint(wcAnchor.position, 40f, 50f));
                rgSerial = Get<int>(wcStation, "refuseSerial");
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog);
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "refuseSerial") != rgSerial, 2, () => "the Owner did not refuse the direct ReqCall from 45 m"));
            WaWatchNotCalled(s, 2.0);
            s.Add(WsDo(() =>
            {
                float d = Flat(wsRoot.position - wcAnchor.position);
                WsExpect(Get<int>(wcStation, "refusedReason") == KH_WashStationCall.RefuseTooFar, "45 m: refused as too far (" + Get<int>(wcStation, "refusedReason") + ")");
                WaExpectNotCalled("45 m ReqCall");
                WsExpect(d > 35f, "45 m: no teleport near the station (" + d.ToString("F1") + " m)");
                WsNote("45 m (" + d.ToString("F1") + " m 2 s later): direct ReqCall refused as too far, no teleport (the 30 m far-call rule is never reached)");
                WcCleanup();   // the Shiba's home back (simulated pin)
                WsPlaceShiba();
            }));

            // ---- C-avail-busy (Fetch: shown; gathering: shown, pressed -> waits -> RefuseBusy with the specific reason) ----
            s = WsCase("C-avail-busy", 130);
            int buSerial = 0;
            WcSetup(s);
            WaBind(s);
            s.Add(WsDo(() =>
            {
                if (wcFetch == null || WsRef(wcFetch, "ballController") as UnityEngine.Object == null) throw new WsSkip("the Shiba's fetch / ball is not wired");
                wcFetch.SendCustomEvent("_OnBallThrown");
            }));
            s.Add(WsUntil(() => Get<int>(wcFetch, "dogState") != 0, 2, () => "control: _OnBallThrown did not start a Fetch"));
            bool buFetchSeen = false; int buOther = -99;
            // the first refresh right away (the dog runs off toward the ball: still in range for a moment)
            s.Add(WsDo(() => waBoard.SetProgramVariable("_nextAt", 0f)));
            s.Add(WsUntil(() =>
            {
                int st = WaState(waBoard, wsDog);
                if (st == KH_WashStationCall.AvFetch) buFetchSeen = true; else if (st != KH_WashStationCall.AvCallable) buOther = st;
                return buFetchSeen || Get<int>(wcFetch, "dogState") == 0;
            }, 2, () => "the board did not show the Fetch (state " + WaState(waBoard, wsDog) + ")"));
            s.Add(WsDo(() =>
            {
                if (!buFetchSeen && buOther == KH_WashStationCall.RefuseTooFar) throw new WsSkip("the Fetch took the Shiba out of range before the board refreshed (shown too far)");
                if (!buFetchSeen) throw new WsSkip("the Fetch ended before the board refreshed");
                WaExpectShown(waBoard, wsDog, KH_WashStationBoard.WStFetch, KH_WashStationBoard.LookUnavailable, "the Shiba in a Fetch");
                WsNote("Fetch: '" + WaShownText(waBoard, wsDog) + "'");
            }));
            WcFree(s);
            s.Add(WsDo(() =>
            {
                if (wcActivities == null) throw new WsSkip("the Shiba's activities is not wired");
                WsPlaceShiba();   // the Fetch may have left it far away (too far would be shown first)
                wcActivities.SetProgramVariable("participants", 1 << wsDog);
                wcActivities.SetProgramVariable("formation", 0);
                wcActivities.SetProgramVariable("anchor", CmGround(wsRoot.position + WsFlat(wsRoot.forward) * 5f));
                wcActivities.SetProgramVariable("yaw", 0f);
                wcActivities.SetProgramVariable("host", Networking.LocalPlayer.playerId);
                wcActivities.SetProgramVariable("startedMs", Networking.GetServerTimeInMilliseconds());
                wcActivities.SetProgramVariable("mode", 1);
                wcActivities.SetProgramVariable("revision", Get<int>(wcActivities, "revision") + 1);
                wcGatherSet = true;
            }));
            s.Add(WsUntil(() => WaState(waBoard, wsDog) == KH_WashStationCall.AvGathering, 2, () => "the board did not show the gathering (state " + WaState(waBoard, wsDog) + ")"));
            s.Add(WsDo(() =>
            {
                WaExpectShown(waBoard, wsDog, KH_WashStationBoard.WStGathering, KH_WashStationBoard.LookUnavailable, "the Shiba in a gathering");
                buSerial = Get<int>(wcStation, "refuseSerial");
                waBoard.SendCustomEvent("_Dog" + wsDog);
                WsExpect(Get<int>(waBoard, "_myPick") == wsDog, "a busy dog's press waits (shown as calling in the status line)");
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() => WsExpect(Get<int>(wcStation, "_pendingDog") == wsDog && Get<int>(wcStation, "heldDog") != wsDog, "the Owner waits (pending " + Get<int>(wcStation, "_pendingDog") + ")")));
            s.Add(WsUntil(() => Get<int>(wcStation, "refuseSerial") != buSerial, 7, () => "the gathering's wait was not refused within callWaitSeconds"));
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                string shown = Get<string>(waBoard, "_shown");
                WsExpect(Get<int>(wcStation, "refusedReason") == KH_WashStationCall.RefuseBusy, "refused as busy after the wait (" + Get<int>(wcStation, "refusedReason") + ")");
                WsExpect(shown.Contains(WaWord(waBoard, KH_WashStationBoard.WStGathering)), "the status line names the gathering ('" + shown + "')");
                WsNote("gathering: '" + WaShownText(waBoard, wsDog) + "'; pressed -> waited, refused as busy; status '" + shown + "'");
                WcCleanup();
            }));

            // ---- C-avail-other-station ----
            s = WsCase("C-avail-other-station", 120);
            int osSerial = 0;
            WcSetup(s);
            WaBind(s);
            s.Add(WsDo(() => { if (waRiver == null || waRiverBoard == null) throw new WsSkip("no River station / board in the scene"); }));
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() => WcPlayerAt(waRiver)));
            s.Add(WsUntil(() => WaState(waRiverBoard, wsDog) == KH_WashStationCall.RefuseOtherStation, 3, () => "the River board did not show the Shiba as at another station (state " + WaState(waRiverBoard, wsDog) + ")"));
            s.Add(WsDo(() =>
            {
                WaExpectShown(waRiverBoard, wsDog, KH_WashStationBoard.WStOtherStation, KH_WashStationBoard.LookUnavailable, "the Shiba held at the main station, seen from the River board");
                osSerial = Get<int>(waRiver, "refuseSerial");
                waRiver.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog);
            }));
            s.Add(WsUntil(() => Get<int>(waRiver, "refuseSerial") != osSerial, 2, () => "the River Owner did not refuse"));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(waRiver, "refusedReason") == KH_WashStationCall.RefuseOtherStation, "refused as held by another station (" + Get<int>(waRiver, "refusedReason") + ")");
                WsExpect(Get<int>(wcStation, "heldDog") == wsDog && Get<int>(waRiver, "heldDog") != wsDog, "the main station still holds the Shiba");
                WsNote("River board: '" + WaShownText(waRiverBoard, wsDog) + "'; River ReqCall refused as another station's");
                WcPlayerAtStation();
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
            }));

            // ---- C-avail-leash-other (a ClientSim remote player holds the Shiba's leash: walkers[Shiba] = its id) ----
            // Run 20261002-223745: the stand-in id -77 is not a leash at all (_IsLeashed is walkers >= 0), and an invalid positive id
            // is released by the leash manager's upkeep (a player that does not exist). A real remote player's id stays.
            s = WsCase("C-avail-leash-other", 70);
            int loSerial = 0;
            WcSetup(s);
            WaBind(s);
            WaSpawnPuppet(s);
            s.Add(WsDo(() =>
            {
                Vector3 pp = WcGroundNear(wsRoot.position + WsFlat(wsRoot.right) * 1.5f);
                WaPlacePuppet(pp, wsRoot.position - pp);
                loSerial = Get<int>(wcStation, "refuseSerial");
                WaSetWalker(wsDog, waPuppet.playerId);
                waBoard.SetProgramVariable("_nextAt", 0f);
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog);
            }));
            s.Add(WsUntil(() => WaState(waBoard, wsDog) == KH_WashStationCall.RefuseLeashedByOther, 2,
                () => "the board did not show the Shiba as walking with someone (state " + WaState(waBoard, wsDog) + ", walker " + Get<int[]>(manager, "walkers")[wsDog] + ")"));
            s.Add(WsUntil(() => Get<int>(wcStation, "refuseSerial") != loSerial, 2, () => "the Owner did not answer the ReqCall"));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int[]>(manager, "walkers")[wsDog] == waPuppet.playerId, "control: the leash is still the remote player's (" + Get<int[]>(manager, "walkers")[wsDog] + ")");
                WaExpectShown(waBoard, wsDog, KH_WashStationBoard.WStLeashedOther, KH_WashStationBoard.LookUnavailable, "the Shiba on someone else's leash");
                WsExpect(Get<int>(wcStation, "refusedReason") == KH_WashStationCall.RefuseLeashedByOther && Get<int>(wcStation, "refusedDog") == wsDog, "refused as leashed by someone else (" + Get<int>(wcStation, "refusedReason") + ")");
                WsExpect(Get<int>(wcStation, "heldDog") != wsDog, "not held");
                WsNote("leash of remote player " + waPuppet.playerId + " (ClientSim puppet, no Udon): board '" + WaShownText(waBoard, wsDog) + "', ReqCall refused as leashed by someone else");
                WaSetWalker(wsDog, -1);
                WaRemovePuppet();
            }));
            s.Add(WsWait(1.0));

            // ---- C-avail-washed-other (the Shiba's wash hold written as a remote player's stroke) ----
            // Run 20261002-223745 SKIPped: the stand-in washer -77 is not a player id (_Availability needs washer >= 0). ClientSim cannot
            // send a NetStroke from a remote player (the puppet has no Udon), so the hold the board reads (KH_DogWash washMs / washSeen,
            // washBy, the synced holder since the review of 8faf572) is written in the heap with the puppet's real id - simulated input, real rule and real board.
            s = WsCase("C-avail-washed-other", 80);
            double woAt = 0;
            WcSetup(s);
            WaBind(s);
            WaSpawnPuppet(s);
            s.Add(WsDo(() =>
            {
                if (wsWash == null) throw new InvalidOperationException("the Shiba has no KH_DogWash");
                wsWash.SetProgramVariable("washBy", waPuppet.playerId);
                wsWash.SetProgramVariable("_released", false);
                wsWash.SetProgramVariable("washSeen", true);
                wsWash.SetProgramVariable("washMs", Networking.GetServerTimeInMilliseconds());
                WsExpect(WsHold(), "control: the written hold is on (KH_DogWash._WashHold)");
                waBoard.SetProgramVariable("_nextAt", 0f);
            }));
            s.Add(WsUntil(() => WaState(waBoard, wsDog) == KH_WashStationCall.AvWashedByOther, 1.5,
                () => "the board did not show the Shiba as washed by someone else (state " + WaState(waBoard, wsDog) + ", washer " + Get<int>(wsWash, "washBy") + ")"));
            s.Add(WsDo(() =>
            {
                WaExpectShown(waBoard, wsDog, KH_WashStationBoard.WStWashedOther, KH_WashStationBoard.LookUnavailable, "the Shiba washed by someone else");
                woAt = UnityEditor.EditorApplication.timeSinceStartup;
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog);
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() => WsExpect(Get<int>(wcStation, "_pendingDog") == wsDog && Get<int>(wcStation, "heldDog") != wsDog, "waits while someone else washes it (pending " + Get<int>(wcStation, "_pendingDog") + ")")));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") == wsDog, 6, () => "not held after the written hold ran out (holdSeconds 3 s; refused " + Get<int>(wcStation, "refusedReason") + ")"));
            s.Add(WsDo(() =>
            {
                WsNote("washed by remote player " + waPuppet.playerId + " (hold written in the heap): '" + WaWord(waBoard, KH_WashStationBoard.WStWashedOther) + "' -> ReqCall waited, held "
                    + (UnityEditor.EditorApplication.timeSinceStartup - woAt).ToString("F1") + " s later (the hold's end)");
                wsWash.SetProgramVariable("washBy", -1);
                WaRemovePuppet();
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0, 3, () => "the release did not take"));
        }

        // ================= helpers =================

        // Sampled by WaWatchNotCalled: was the Shiba ever held here / in the station branch, the largest move in one tick, the
        // closest it came to the anchor
        static bool waEverHeld, waEverStation;
        static float waMaxStep, waClosest;
        static Vector3 waPrev;

        /// <summary>For `seconds`, every tick: the Shiba is never held by station 0, the dog side never takes a station
        /// (_stationIdx), and it never jumps (a teleport toward the station would be a step of many metres; a stretched tick of
        /// 0.33 s at run speed is about 2 m).</summary>
        static void WaWatchNotCalled(List<Func<double, bool>> s, double seconds)
        {
            double from = 0;
            s.Add(WsDo(() => { from = UnityEditor.EditorApplication.timeSinceStartup; waEverHeld = false; waEverStation = false; waMaxStep = 0f; waPrev = wsRoot.position; waClosest = Flat(wsRoot.position - wcAnchor.position); }));
            s.Add(now =>
            {
                if (Get<int>(wcStation, "heldDog") == wsDog) waEverHeld = true;
                if (Get<int>(wsWander, "_stationIdx") >= 0) waEverStation = true;
                var p = wsRoot.position;
                waMaxStep = Mathf.Max(waMaxStep, Flat(p - waPrev)); waPrev = p;
                waClosest = Mathf.Min(waClosest, Flat(p - wcAnchor.position));
                return now - from >= seconds;
            });
        }

        static void WaExpectNotCalled(string what)
        {
            WsExpect(!waEverHeld && !waEverStation, what + ": never held, the dog side never took the station (held " + waEverHeld + ", station branch " + waEverStation + ")");
            WsExpect(waMaxStep < 4f, what + ": no teleport (largest move in one tick " + waMaxStep.ToString("F2") + " m)");
            WsNote(what + ": 2 s after the refusal never held / no station branch, largest step " + waMaxStep.ToString("F2") + " m, closest " + waClosest.ToString("F1") + " m from the anchor (a free dog may wander)");
        }

        /// <summary>A ClientSim remote player (no Udon) standing in for "someone else" (as the Call / Travel harnesses do). Spawned
        /// once per case by WaSpawnPuppet, removed by WaRemovePuppet.</summary>
        static VRCPlayerApi waPuppet;
        const string WaPuppetName = "KH_WashNeighbor";

        static void WaSpawnPuppet(List<Func<double, bool>> s)
        {
            s.Add(WsDo(() => { WaRemovePuppet(); ClientSimMain.SpawnRemotePlayer(WaPuppetName); }));
            s.Add(WsUntil(() =>
            {
                var all = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; VRCPlayerApi.GetPlayers(all);
                VRCPlayerApi any = null;
                foreach (var p in all) { if (p == null || p.isLocal) continue; if (p.displayName == WaPuppetName) { waPuppet = p; return true; } any = p; }
                waPuppet = any;   // ClientSim may name it differently: any remote player (the Call / Travel harnesses take the first)
                return any != null;
            }, 5, () => "the stand-in remote player did not spawn"));
        }

        static void WaPlacePuppet(Vector3 p, Vector3 facing)
        {
            if (!Utilities.IsValid(waPuppet)) return;
            var cs = waPuppet.GetClientSimPlayer();
            cs.transform.position = p; facing.y = 0f;
            if (facing.sqrMagnitude > 1e-6f) cs.transform.rotation = Quaternion.LookRotation(facing);
        }

        /// <summary>WcCleanup (every WcSetup): undo what a failed C-avail-leash-other / -washed-other left - the Shiba's leash held by
        /// the remote player, its wash hold attributed to it, and the remote player itself.</summary>
        static void WaCleanupStandIns()
        {
            if (!Utilities.IsValid(waPuppet)) { waPuppet = null; return; }
            int id = waPuppet.playerId;
            if (manager != null && wsDog >= 0 && Get<int[]>(manager, "walkers")[wsDog] == id) WaSetWalker(wsDog, -1);
            if (wsWash != null && Get<int>(wsWash, "washBy") == id) wsWash.SetProgramVariable("washBy", -1);
            WaRemovePuppet();
        }

        static void WaRemovePuppet()
        {
            if (Utilities.IsValid(waPuppet)) ClientSimMain.RemovePlayer(waPuppet);
            waPuppet = null;
        }

        /// <summary>The main station's board (its call = station 0) and the River station and its board.</summary>
        static void WaBind(List<Func<double, bool>> s)
        {
            s.Add(WsDo(() =>
            {
                waBoard = null; waRiver = null; waRiverBoard = null;
                int riverIndex = -1;
                foreach (var u in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>())
                {
                    if (u.programSource == null || u.programSource.name != "KH_WashStationCall") continue;
                    for (var t = u.transform; t != null; t = t.parent) if (t.name == "River") { waRiver = u; riverIndex = Get<int>(u, "stationIndex"); break; }
                }
                foreach (var u in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>())
                {
                    if (u.programSource == null || u.programSource.name != "KH_WashStationBoard") continue;
                    var c = WsRef(u, "call") as UdonBehaviour;
                    if (c == wcStation) waBoard = u;
                    else if (c != null && c == waRiver) waRiverBoard = u;
                }
                if (waBoard == null) throw new InvalidOperationException("the main station's board (KH_WashStationBoard with call = station 0) not found");
                WsArr<int>(waBoard, "_dogState");   // the IMP-DOG-037 board program (throws on an older one)
            }));
        }

        static int WaState(UdonBehaviour board, int dog)
        {
            try { var a = Get<int[]>(board, "_dogState"); return a != null && dog >= 0 && dog < a.Length ? a[dog] : -99; }
            catch (InvalidOperationException) { return -99; }
        }

        static int WaWordShown(UdonBehaviour board, int dog)
        {
            try { var a = Get<int[]>(board, "_dogWord"); return a != null && dog >= 0 && dog < a.Length ? a[dog] : -99; }
            catch (InvalidOperationException) { return -99; }
        }

        static string WaWord(UdonBehaviour board, int w)
        {
            try { var a = Get<string[]>(board, "_w"); return a != null && w >= 0 && w < a.Length ? a[w] : "?"; }
            catch (InvalidOperationException) { return "?"; }
        }

        static string WaShownText(UdonBehaviour board, int dog)
        {
            var l = WsArr<TMPro.TextMeshProUGUI>(board, "dogStateLabels");
            return dog < l.Length && l[dog] != null ? l[dog].text : "(no label)";
        }

        static T WaRef<T>(UdonBehaviour u, string key) where T : UnityEngine.Object { return WsRef(u, key) as T; }

        /// <summary>The second line reads the expected word (in the board's current language) and the button has that look's colour.</summary>
        static void WaExpectShown(UdonBehaviour board, int dog, int word, int look, string what)
        {
            WsExpect(WaWordShown(board, dog) == word, what + ": word index " + word + " (" + WaWordShown(board, dog) + ")");
            string want = WaWord(board, word), got = WaShownText(board, dog);
            WsExpect(got == want, what + ": the second line reads '" + want + "' ('" + got + "')");
            var imgs = WsArr<UnityEngine.UI.Image>(board, "dogButtons");
            Color c = look == KH_WashStationBoard.LookCallable ? Get<Color>(board, "colorCallable") : look == KH_WashStationBoard.LookHere ? Get<Color>(board, "colorHere") : Get<Color>(board, "colorUnavailable");
            WsExpect(dog < imgs.Length && imgs[dog] != null && Vector4.Distance(imgs[dog].color, c) < 0.01f, what + ": the button colour is look " + look + " (" + (dog < imgs.Length && imgs[dog] != null ? imgs[dog].color.ToString() : "none") + ")");
        }

        /// <summary>A NavMesh point whose flat distance from `center` is in lo..hi: toward each course node in turn, then 16 compass
        /// directions (the NavMesh snap is kept within 1 m, and the result is measured again).</summary>
        static Vector3 WaRingPoint(Vector3 center, float lo, float hi)
        {
            var dirs = new List<Vector3>();
            foreach (var n in nodes) { var d = n - center; d.y = 0f; if (d.sqrMagnitude > 1f) dirs.Add(d.normalized); }
            for (int k = 0; k < 16; k++) dirs.Add(Quaternion.Euler(0f, 22.5f * k, 0f) * Vector3.forward);
            float mid = (lo + hi) * 0.5f;
            foreach (var d in dirs)
            {
                UnityEngine.AI.NavMeshHit h;
                if (!UnityEngine.AI.NavMesh.SamplePosition(center + d * mid, out h, 1.0f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                float dist = Flat(h.position - center);
                if (dist >= lo && dist <= hi) return h.position;
            }
            throw new InvalidOperationException("no NavMesh point " + lo + ".." + hi + " m from " + center.ToString("F1"));
        }

        /// <summary>The free Shiba at p, its roaming home pinned there (simulated: WcSetHome; WcCleanup puts it back), so it does not
        /// walk back toward the gate while the board and the Owner judge the distance.</summary>
        static void WaPlaceShiba(Vector3 p)
        {
            WcSaveWander();
            WcSetHome(p);
            wsRoot.SetPositionAndRotation(p, wsRoot.rotation);
            var sync = wsRoot.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
            wsPosHist.Clear();
            WcInvalidateWander();
            if (waBoard != null) waBoard.SetProgramVariable("_nextAt", 0f);
        }

        /// <summary>Simulated: the leash manager's synced walkers[dog] (a copy written back, as the K3 collar case does).</summary>
        static void WaSetWalker(int dog, int pid)
        {
            var a = (int[])Get<int[]>(manager, "walkers").Clone();
            a[dog] = pid;
            manager.SetProgramVariable("walkers", a);
        }

        /// <summary>IMP-DOG-037: another dog by the gate (2 m ahead of where WsPlaceShiba puts the Shiba), within every main-station
        /// range, outside its keep-out. For C-swap / C-pending-cancel, whose second dog may be anywhere when the case starts.</summary>
        static void WcBringNear(int dog)
        {
            if (roots == null || dog < 0 || dog >= roots.Length || roots[dog] == null) return;
            Vector3 dir = nodes[1] - nodes[0]; dir.y = 0f;
            var rot = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward);
            var p = WcGroundNear(nodes[0] + rot * new Vector3(2.5f, 0f, 2f));
            roots[dog].SetPositionAndRotation(p, rot);
            var sync = roots[dog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
        }
    }
}
