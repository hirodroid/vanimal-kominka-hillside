using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.ClientSim;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-002 呼ぶ (設計書 docs/plans/2026-09-27-map-and-travel.md §F.6 の期待表 C1〜C8) — Course/82c。
    ///
    /// 台は travel モード (KH_CourseWalkHarnessEditor.Travel.cs) と同じ: 作法・観測 (犬と本人の位置の不連続・暗転)・Stop・PlayerData の退避と復旧・
    /// シーンは保存しない・結果は Logs/kh-course-walk/&lt;runId&gt;/report.json と travel.json。
    /// **ローカル = T (呼ばれて動く側)、相手 = ClientSim の人形 (Udon は動かない)** を主にする。人形からの要求・取消は受付箱の試験用の入口
    /// (debugIncomingFrom/Kind/Seq + _DebugIncomingNow / _DebugIncomingCancelNow) で、ネットワークの入口と同じ共通の Receive へ入れる。
    /// T の返事は debugJoinLoopback=1 で相手へ送らず debugReplyCount / debugLastReplySeq / debugLastReplyCode に書かれるので、それを読む。
    /// 押すのは通知板の実ハンドラ (_Go / _NotNow / _Yes / _No / _Pause / _Block) と手元メニューの実ボタン (_JoinPick0 → _TravelCall / _TravelGo)。
    /// ローカル = R のケースは「表示と R が動かないこと」だけを見る (相手の答えは _DebugAnswerNow で模す)。
    /// 模擬: 通知を閉じた後の 20 秒の間隔 (_closedAt) とクールダウン (_tvCooldownUntil) は、ケースの間でヒープを書いて短くする (各ケースの notes)。
    /// 限界: CallingPlayer は常にローカル。実通信の順序・遅延・2 端末での交差・T が Manager Owner でない経路は通らない (PCVR §F.7)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/82c - Walk Harness Travel Call (C1-C8)")]
        public static void TravelCall() { BeginTravel(false, false, true); }

        static VRC.Udon.UdonBehaviour tvNotice;
        static VRCPlayerApi tvRemote2;
        static int tvCallSeq;
        static bool tvCallFailed;   // 82c: 1 つでも FAIL なら最後の状態を TRAVEL_CALL_FAIL にする

        class CallCtx { public int seq, seq2, replies, serial, logFrom; public Vector3 pos; public int[] old, disc, applies; public string text; public int puppet; }

        // ============================================================
        // 組み立て (MakeTravelCases から)
        // ============================================================

        static void MakeCallCases()
        {
            tvNotice = UnityEngine.Object.FindObjectsOfType<VRC.Udon.UdonBehaviour>(true).FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_TravelNotice");
            if (tvNotice == null) throw new InvalidOperationException("KH_TravelNotice not in the scene (run Course/85)");
            tvCallSeq = 100; tvRemote2 = null; tvCallFailed = false;
            CallSpawn();
            // ---- ローカル = R (表示と、R が動かないこと) ----
            CallR1(); CallR2(); CallR3(); CallR4();
            // ---- ローカル = T ----
            CallC1(); CallC7Cooling(); CallC2(); CallC3(); CallC4Pending(); CallC4Measure(); CallC4Ticket(); CallC4Fade(); CallC4Late(); CallC4Double();
            CallC5(); CallC6(); CallC7Landing(); CallC8Replace(); CallC8Cross(); CallC8Confirmed();
            CallC9SelfStop(); CallC10ArriveTimeout();
            CallCleanup();
        }

        static void CallSpawn()
        {
            var s = TvCase("C0-setup-puppet", true, 3);
            s.Add(TvDo(() => ClientSimMain.SpawnRemotePlayer("KH_TravelCaller")));
            s.Add(TvUntil(() => (tvRemote = TvRemotePlayer()) != null, 5, "remote player spawn"));
            s.Add(TvUntil(() => TvMyBox() != null, 5, "local KH_TravelMailbox (VRCPlayerObject) not found"));
            s.Add(TvDo(() =>
            {
                var box = TvMyBox();
                box.SetProgramVariable("debugJoinLoopback", 1);
                TvExpect(Get<int>(box, "debugIncomingFrom") == 0 && Get<int>(box, "debugCrossInvert") == 0, "debug hooks start at 0");
                TvExpect(TvMe() < tvRemote.playerId, "local playerId < puppet (the crossing 'win' side is local without debugCrossInvert)");
                TvPlacePuppet(Networking.LocalPlayer.GetPosition() + Networking.LocalPlayer.GetRotation() * Vector3.forward * 8f, Vector3.forward);
                TvNote("simulated: puppet " + tvRemote.playerId + " (no Udon). Requests from it enter the common Receive through _DebugIncomingNow; T's replies go to debugLastReply* (debugJoinLoopback=1); R's answers are injected with _DebugAnswerNow");
            }));
        }

        // ---- 部品 ----

        static int CallReplies() { return Get<int>(TvMyBox(), "debugReplyCount"); }
        static int CallLastSeq() { return Get<int>(TvMyBox(), "debugLastReplySeq"); }
        static int CallLastCode() { return Get<int>(TvMyBox(), "debugLastReplyCode"); }
        static int CallInFrom() { return Get<int>(TvMyBox(), "_inFrom"); }
        static bool CallBoardShown() { var b = Get<GameObject>(tvNotice, "board"); return b != null && b.activeSelf; }

        /// <summary>通知を閉じた後の 20 秒の間隔を消す (模擬。ケースの間を詰める)。</summary>
        static void CallGapReset() { TvMyBox().SetProgramVariable("_closedAt", -1000f); }

        /// <summary>人形 (既定は tvRemote) からの要求を共通の Receive へ入れる。seq を返す。</summary>
        static int CallInject(int kind, int seq = -1, VRCPlayerApi from = null)
        {
            var box = TvMyBox();
            if (seq < 0) seq = ++tvCallSeq;
            box.SetProgramVariable("debugIncomingFrom", (from ?? tvRemote).playerId);
            box.SetProgramVariable("debugIncomingKind", kind);
            box.SetProgramVariable("debugIncomingSeq", seq);
            box.SendCustomEvent("_DebugIncomingNow");
            return seq;
        }

        static void CallCancel(int seq, VRCPlayerApi from = null)
        {
            var box = TvMyBox();
            box.SetProgramVariable("debugIncomingFrom", (from ?? tvRemote).playerId);
            box.SetProgramVariable("debugIncomingSeq", seq);
            box.SendCustomEvent("_DebugIncomingCancelNow");
        }

        static void CallPress(string evt) { tvNotice.SendCustomEvent(evt); }
        /// <summary>世代が変わってから 0.5 秒 (ゲーム時間) たった。壁時計の待ちでは Time.time とずれるので状態で待つ。</summary>
        static bool CallGuardPassed() { return Time.time - Get<float>(TvMyBox(), "_inGenAt") >= 0.55f; }
        /// <summary>R の速さの測定が 2 回そろい、入力の 0.5 秒も過ぎた ([行く] がその場で始まる)。</summary>
        static bool CallReadyToGo() { return Get<int>(TvMyBox(), "_cSamples") >= 2 && CallGuardPassed(); }

        static void CallMark(CallCtx c)
        {
            c.replies = CallReplies(); c.serial = Get<int>(manager, "travelResultSerial"); c.logFrom = tvLogs.Count; c.pos = Networking.LocalPlayer.GetPosition();
            c.old = (int[])Get<int[]>(manager, "travelReq").Clone();
            c.applies = wander.Select(w => Get<int>(w, "_travelApplies")).ToArray();
            TvReset();
        }

        static bool CallNothingMoved(CallCtx c) { return TvFlat(Networking.LocalPlayer.GetPosition(), c.pos) < 1f && tvDisc.All(d => d == 0) && tvJumps == 0; }

        /// <summary>ローカル = R: 地図の [人] で行 0 (人形) を選び、フッターのボタンを押す。</summary>
        static void CallMenuPress(CallCtx c, string evt)
        {
            CallMark(c);
            pouch.SendCustomEvent("_TravelOpenMap"); pouch.SendCustomEvent("_TravelModePeople"); pouch.SendCustomEvent("_JoinPick0");
            c.text = TvLabel("travelCallLabel");
            pouch.SendCustomEvent(evt);
        }

        static string TvLabel(string field)
        {
            object o; if (!pouch.TryGetProgramVariable(field, out o) || !(o is Component comp) || comp == null) return "";
            var p = comp.GetType().GetProperty("text"); return p == null ? "" : (string)p.GetValue(comp);
        }

        static void CallExpectReply(CallCtx c, int count, int seq, int code, string what)
        {
            TvExpect(CallReplies() - c.replies == count, what + ": " + count + " replies (got " + (CallReplies() - c.replies) + ")");
            if (count > 0) TvExpect(CallLastSeq() == seq && CallLastCode() == code, what + ": last reply (" + seq + "," + code + ") (got (" + CallLastSeq() + "," + CallLastCode() + "))");
        }

        // ============================================================
        // ローカル = R
        // ============================================================

        /// <summary>C1-r: 呼ぶ → 準備中 → 到着。R は動かない・切符を書かない。</summary>
        static void CallR1()
        {
            var s = TvCase("C1-r-call-accepted", true, 3); var c = new CallCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => CallMenuPress(c, "_TravelCall")));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") == tvRemote.playerId && Get<int>(TvMyBox(), "_outKind") == KH_TravelMailbox.KindCall, 2, "call request did not start"));
            s.Add(TvDo(() =>
            {
                pouch.SendCustomEvent("_TravelModePeople");
                TvExpect(TvLabel("travelCallLabel") != c.text && TvLabel("travelCallLabel").Length > 0, "[来てもらう] shows [cancel] while waiting (was '" + c.text + "', now '" + TvLabel("travelCallLabel") + "')");
                TvExpect(TvLabel("travelAskLabel") != TvLabel("travelCallLabel"), "[合流をたのむ] is not the cancel button");
                TvAnswer(KH_TravelMailbox.CodePreparing);
            }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.CallComing && Get<int>(TvMyBox(), "_outPhase") == 1, 1, "preparing (code 4) did not move R to 'arriving'"));
            s.Add(TvDo(() => TvAnswer(KH_TravelMailbox.CodeYes)));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.CallArrived && Get<int>(TvMyBox(), "_outTo") < 0, 1, "arrival (code 1) not shown"));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                TvExpect(CallNothingMoved(c), "R did not move (no jump, no dog moved)");
                TvExpect(Get<int[]>(manager, "travelReq").SequenceEqual(c.old), "R wrote no ticket");
                TvNote("R: call -> code 4 'coming' -> code 1 'arrived'; R and its dogs did not move, no ticket");
            }));
        }

        /// <summary>C2-r: 断り → 「来られない」+ 同じ相手へ 30 秒 (合流も押せない)。</summary>
        static void CallR2()
        {
            var s = TvCase("C2-r-call-declined", true, 3); var c = new CallCtx();
            s.Add(TvDo(() => CallMenuPress(c, "_TravelCall")));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") == tvRemote.playerId, 2, "call request did not start"));
            s.Add(TvDo(() => TvAnswer(KH_TravelMailbox.CodeNo)));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.CallNotComing, 1, "decline not shown as 'cannot come'"));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int[]>(TvMyBox(), "_deniedIds").Contains(tvRemote.playerId), "the 30 s gap is set for the puppet");
                pouch.SendCustomEvent("_TravelGo");   // 同じ相手へ合流 (実ボタン)
                TvExpect(Get<int>(TvMyBox(), "_outTo") < 0, "no new request within 30 s (kind does not matter)");
                string key = Get<string[]>(pouch, "_hsNoteKeys")[0];
                TvExpect(key == "course.travel.join.again", "the menu says 'ask again later' (" + key + ")");
                TvMyBox().SetProgramVariable("_deniedIds", Enumerable.Repeat(-1, 16).ToArray());
                TvNote("R: declined -> 'cannot come', the 30 s gap also blocks a join request; gap cleared for the next case (simulated)");
            }));
        }

        /// <summary>C3-r: 返事が来ない → 22 秒で「結果を確認できませんでした」(30 秒に入れない)。</summary>
        static void CallR3()
        {
            var s = TvCase("C3-r-call-unconfirmed", true, 3); var c = new CallCtx();
            s.Add(TvDo(() => CallMenuPress(c, "_TravelCall")));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") == tvRemote.playerId, 2, "call request did not start"));
            s.Add(TvDo(() => c.pos = Networking.LocalPlayer.GetPosition()));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.CallUnknown, 25, "no 'unconfirmed' after 22 s"));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(TvMyBox(), "_outTo") < 0, "request cleared");
                TvExpect(!Get<int[]>(TvMyBox(), "_deniedIds").Contains(tvRemote.playerId), "no 30 s gap after an unconfirmed call");
                TvNote("R: no answer -> 'could not confirm' at 22 s, not counted as a refusal");
            }));
        }

        /// <summary>C4-r: 返事待ちで [取り消す] (実ボタン) → 後から届いた準備中は捨てる。</summary>
        static void CallR4()
        {
            var s = TvCase("C4-r-cancel-then-late-answer", true, 3); var c = new CallCtx();
            s.Add(TvDo(() => CallMenuPress(c, "_TravelCall")));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") == tvRemote.playerId, 2, "call request did not start"));
            s.Add(TvDo(() => pouch.SendCustomEvent("_TravelGo")));   // 送ったのは呼ぶ: [合流をたのむ] は何もしない
            s.Add(TvDo(() => TvExpect(Get<int>(TvMyBox(), "_outTo") == tvRemote.playerId, "[合流をたのむ] did not cancel the call")));
            s.Add(TvDo(() => pouch.SendCustomEvent("_TravelCall")));   // [取り消す]
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.JoinCanceled && Get<int>(TvMyBox(), "_outTo") < 0, 2, "cancel not reported"));
            s.Add(TvDo(() => TvAnswer(KH_TravelMailbox.CodePreparing)));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.JoinCanceled, "late 'preparing' after cancel was ignored");
                TvExpect(CallNothingMoved(c), "R did not move");
                TvNote("R: cancel acts on the sent call ([合流をたのむ] ignored); a late code 4 is dropped");
            }));
        }

        // ============================================================
        // ローカル = T
        // ============================================================

        /// <summary>要求 → 通知 → 1.1 s → [行く] → 準備中 → 本人の移動 → 到着 の一式 (C1・C4-double・C7 で使う)。</summary>
        static void CallGoFlow(List<Func<double, bool>> s, CallCtx c, string name, bool doublePress, int expectReason)
        {
            s.Add(TvDo(() => { CallGapReset(); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(() => CallInFrom() == tvRemote.playerId && Get<int>(TvMyBox(), "_inKind") == KH_TravelMailbox.KindCall && CallBoardShown(), 2, name + ": notice not shown"));
            s.Add(TvUntil(CallReadyToGo, 3, name + ": caller speed samples / input guard"));
            s.Add(TvDo(() =>
            {
                CallMark(c); c.disc = (int[])tvDisc.Clone();
                var call = Get<GameObject[]>(tvNotice, "callButtons"); var join = Get<GameObject[]>(tvNotice, "joinButtons");
                TvExpect(call != null && call.All(g => g != null && g.activeSelf) && join != null && join.All(g => g != null && !g.activeSelf), name + ": the notice shows the call buttons only");
                CallPress("_Go");
                if (doublePress) CallPress("_Go");
            }));
            s.Add(TvUntil(() => CallReplies() - c.replies >= 1, 2, name + ": no 'preparing' reply"));
            s.Add(TvUntil(() => TvFlat(Networking.LocalPlayer.GetPosition(), c.pos) > 5f, 12, name + ": T did not move"));
            s.Add(TvUntil(() => TvAllPhase(7, KH_LeashManager.TravelPhaseDone), 4, name + ": dog tickets not done"));
            s.Add(TvUntil(() => CallReplies() - c.replies >= 2, 2, name + ": no arrival reply"));
            s.Add(TvWait(0.5));
            s.Add(TvDo(() =>
            {
                CallExpectReply(c, 2, c.seq, KH_TravelMailbox.CodeYes, name);
                int started = TvFirstLog(c.logFrom, "[KH_Join] go: started"), tele = TvFirstLog(c.logFrom, "teleported"), moved = TvFirstLog(c.logFrom, "[KH_Join] go: moved");
                TvExpect(started >= 0 && tele > started && moved > tele, name + ": order = preparing (go: started) -> teleport -> arrival (log lines " + started + "/" + tele + "/" + moved + ")");
                TvExpect(tvJumps == 1, name + ": exactly one player jump (" + tvJumps + ")");
                for (int i = 0; i < 3; i++) TvExpect(tvDisc[i] == 1 && TvAppliesSince(new TvCtx { applies = c.applies }, i) == 1, name + ": dog" + i + " moved once and applied the ticket once");
                var fresh = TvNewReqs(c.logFrom);
                TvExpect(fresh.Count == 1, name + ": exactly one new reqId (" + string.Join(",", fresh) + ")");
                var land = Networking.LocalPlayer.GetPosition(); var target = tvRemote.GetPosition();
                var water = new KH_CourseTravelEditor.Water { xs = Get<float[]>(manager, "travelRiverXs"), z0 = Get<float>(manager, "travelRiverZ0"), step = Get<float>(manager, "travelRiverStep"), bridgeZs = Get<float[]>(manager, "travelBridgeZs"), bridgeXs = Get<float[]>(manager, "travelBridgeXs") };
                TvExpect(KH_CourseTravelEditor.WaterOk(land, water), name + ": landing 10 m from the river and off the bridges");
                TvExpect(TvFlat(land, target) >= 1f && TvFlat(land, target) <= 40f, name + ": landing 1-40 m from the caller (" + TvFlat(land, target).ToString("F1") + " m)");
                int reason = Get<int>(TvMyBox(), "joinReason");
                if (expectReason >= 0) TvExpect(reason == expectReason, name + ": reason " + reason + " (expected " + expectReason + ")");
                var toR = target - land; toR.y = 0; var fwd = Networking.LocalPlayer.GetRotation() * Vector3.forward; fwd.y = 0;
                if (expectReason != KH_LeashManager.JoinReasonBehind) TvExpect(toR.sqrMagnitude < 0.01f || Vector3.Angle(fwd, toR) < 45f, name + ": T faces the caller (" + Vector3.Angle(fwd, toR).ToString("F0") + " deg)");
                TvExpect(Get<int>(TvMyBox(), "_goFrom") < 0 && CallInFrom() < 0 && !CallBoardShown(), name + ": call closed, notice hidden");
                TvNote(name + ": replies (" + c.seq + ",4)->(" + c.seq + ",1), reason " + reason + ", landed " + TvFlat(land, target).ToString("F1") + " m from the caller, one jump, dogs once" + (doublePress ? " (two presses in one frame)" : ""));
            }));
        }

        static void CallC1()
        {
            var s = TvCase("C1-accept", true, 3); var c = new CallCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { tvPuppetWalk = false; TvPuppetAhead(25f); }));
            s.Add(TvWait(2));
            s.Add(TvUntil(() => TvAllNear(7, TvNear), 30, "dogs did not catch up after placing T"));
            s.Add(TvWait(1));
            CallGoFlow(s, c, "C1", false, KH_LeashManager.JoinReasonNear);
        }

        /// <summary>C7-cooling: クールダウン中に [行く] → 返事なし・理由「あと N 秒」、保留のまま → [今は行かない] で 0。</summary>
        static void CallC7Cooling()
        {
            var s = TvCase("C7-start-failed-cooling", true, 3); var c = new CallCtx();
            s.Add(TvDo(() => { CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(() => CallInFrom() == tvRemote.playerId && CallBoardShown(), 2, "notice not shown"));
            s.Add(TvUntil(CallReadyToGo, 3, "caller speed samples / input guard"));
            s.Add(TvDo(() =>
            {
                manager.SetProgramVariable("_tvCooldownUntil", Time.time + 10f);   // 模擬: 成功の後 10 秒のクールダウン
                CallPress("_Go");
            }));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                CallExpectReply(c, 0, 0, 0, "cooling");
                TvExpect(Get<int>(TvMyBox(), "_goFail") == KH_LeashManager.TravelCooling, "reason = cooling (" + Get<int>(TvMyBox(), "_goFail") + ")");
                TvExpect(CallInFrom() == tvRemote.playerId && CallBoardShown(), "the call stays pending");
                TvExpect(CallNothingMoved(c), "nothing moved");
                CallPress("_NotNow");
            }));
            s.Add(TvUntil(() => CallReplies() - c.replies == 1, 2, "no reply after [今は行かない]"));
            s.Add(TvDo(() =>
            {
                CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodeNo, "not now after the failure");
                manager.SetProgramVariable("_tvCooldownUntil", 0f);
                TvNote("simulated cooldown: [行く] could not start -> no reply, reason 'cooling', still pending; [今は行かない] -> (" + c.seq + ",0)");
            }));
        }

        static void CallC2()
        {
            var s = TvCase("C2-decline", true, 3); var c = new CallCtx();
            s.Add(TvDo(() => { CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(() => CallBoardShown(), 2, "notice not shown"));
            s.Add(TvUntil(CallGuardPassed, 2, "input guard"));
            s.Add(TvDo(() => CallPress("_NotNow")));
            s.Add(TvUntil(() => CallReplies() - c.replies == 1, 2, "no reply"));
            s.Add(TvDo(() =>
            {
                CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodeNo, "decline");
                TvExpect(!CallBoardShown() && CallInFrom() < 0 && CallNothingMoved(c), "notice closed, nothing moved");
                TvNote("T: [今は行かない] -> (" + c.seq + ",0), no move");
            }));
        }

        static void CallC3()
        {
            var s = TvCase("C3-timeout", true, 3); var c = new CallCtx();
            s.Add(TvDo(() => { CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(() => CallBoardShown(), 2, "notice not shown"));
            s.Add(TvUntil(() => CallReplies() - c.replies == 1, 23, "no reply after 20 s"));
            s.Add(TvDo(() =>
            {
                CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodeNo, "timeout");
                TvExpect(!CallBoardShown() && CallNothingMoved(c), "notice closed, nothing moved");
                TvNote("T: 20 s without a press -> (" + c.seq + ",0)");
            }));
        }

        /// <summary>C4-pending: 別の送り主の取消・古い seq の取消は無視、同じ seq の取消で閉じる (返事なし)。</summary>
        static void CallC4Pending()
        {
            var s = TvCase("C4-cancel-pending", true, 3); var c = new CallCtx();
            s.Add(TvDo(() => ClientSimMain.SpawnRemotePlayer("KH_TravelCaller2")));
            s.Add(TvUntil(() => (tvRemote2 = TvOther()) != null, 5, "second puppet spawn"));
            s.Add(TvDo(() => { CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(() => CallBoardShown(), 2, "notice not shown"));
            s.Add(TvDo(() =>
            {
                CallCancel(c.seq, tvRemote2);
                TvExpect(CallInFrom() == tvRemote.playerId, "a cancel from another sender is ignored");
                CallCancel(c.seq - 1);
                TvExpect(CallInFrom() == tvRemote.playerId, "a cancel with an old seq is ignored");
                CallCancel(c.seq);
                TvExpect(CallInFrom() < 0, "the matching cancel closes the request");
            }));
            s.Add(TvWait(0.5));
            s.Add(TvDo(() =>
            {
                TvExpect(!CallBoardShown(), "notice hidden");
                CallExpectReply(c, 0, 0, 0, "cancel");
                ClientSimMain.RemovePlayer(tvRemote2); tvRemote2 = null;
                TvNote("T: other sender / old seq cancels ignored; matching cancel closes silently");
            }));
        }

        /// <summary>C4-measure: [行く] の後、R の速さの測定がそろう前に取消 → 返事なし・移動なし。</summary>
        static void CallC4Measure()
        {
            var s = TvCase("C4-cancel-measuring", true, 3); var c = new CallCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_cSamples") == 1 && CallGuardPassed(), 2, "one caller speed sample and the input guard"));
            s.Add(TvDo(() =>
            {
                CallPress("_Go");
                TvExpect(Get<bool>(TvMyBox(), "_goPressed"), "measuring R's speed after [行く] (samples " + Get<int>(TvMyBox(), "_cSamples") + ")");
                CallCancel(c.seq);
            }));
            s.Add(TvWait(2));
            s.Add(TvDo(() =>
            {
                TvExpect(!Get<bool>(TvMyBox(), "_goPressed") && CallInFrom() < 0 && Get<int>(TvMyBox(), "_goFrom") < 0, "cancelled while measuring");
                TvExpect(Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.CallCanceledBy, "'caller cancelled' shown for a cancel while measuring");
                CallExpectReply(c, 0, 0, 0, "cancel while measuring");
                TvExpect(CallNothingMoved(c) && Get<int[]>(manager, "travelReq").SequenceEqual(c.old), "nothing moved, no ticket");
                TvNote("T: [行く] with one speed sample (measuring), cancel in the same step -> no reply, no ticket, no move");
            }));
        }

        /// <summary>C4-ticket: 切符待ち (Owner が ReqTravel を捨てる) の間に取消 → 取りやめ、返事は準備中の 1 回だけ。</summary>
        static void CallC4Ticket()
        {
            var s = TvCase("C4-cancel-ticket-wait", true, 3); var c = new CallCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugDropTravel", 1); CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(CallReadyToGo, 3, "caller speed samples / input guard"));
            s.Add(TvDo(() => CallPress("_Go")));
            s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 1 && CallReplies() - c.replies == 1, 2, "ticket wait did not start"));
            s.Add(TvDo(() => { CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodePreparing, "preparing"); CallCancel(c.seq); }));
            s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 0 && Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.CallCanceledBy, 2, "move not cancelled by the caller's cancel"));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodePreparing, "no reply after the caller's cancel");
                TvExpect(CallNothingMoved(c), "nothing moved");
                TvSet("debugDropTravel", 0);
                TvNote("T: cancel during the ticket wait -> move cancelled, 'caller cancelled', only (" + c.seq + ",4) was sent");
            }));
        }

        /// <summary>C4-fade: 暗転中 (2 秒に伸ばす) に取消 → 明るく戻り、その場に残る。切符は Phase 3。</summary>
        static void CallC4Fade()
        {
            var s = TvCase("C4-cancel-fade", true, 3); var c = new CallCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { tvFade.SetProgramVariable("fadeOutSeconds", 2f); CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(CallReadyToGo, 3, "caller speed samples / input guard"));
            s.Add(TvDo(() => CallPress("_Go")));
            s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 3, 3, "fade did not start"));
            s.Add(TvDo(() => CallCancel(c.seq)));
            s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 0 && Get<float>(tvFade, "_alpha") <= 0.001f, 4, "screen not back after the cancel"));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodePreparing, "fade cancel");
                TvExpect(CallNothingMoved(c) && TvAllPhase(7, KH_LeashManager.TravelPhaseCanceled), "nothing moved, tickets Phase 3");
                tvFade.SetProgramVariable("fadeOutSeconds", 0.2f);
                TvNote("T: cancel during a 2 s fade -> back to light, no move, Phase 3");
            }));
        }

        /// <summary>C4-late: 取消の後に届いた同じ seq の要求 (呼ぶ・合流) は復活しない。</summary>
        static void CallC4Late()
        {
            var s = TvCase("C4-late-request-after-cancel", true, 0); var c = new CallCtx();
            s.Add(TvDo(() =>
            {
                CallGapReset(); CallMark(c);
                int n = ++tvCallSeq;
                CallCancel(n);
                CallInject(KH_TravelMailbox.KindCall, n);
                TvExpect(CallInFrom() < 0 && !CallBoardShown(), "a call with a cancelled seq is dropped");
                CallInject(KH_TravelMailbox.KindJoin, n);
                TvExpect(CallInFrom() < 0, "a join with the same seq is dropped");
                CallInject(KH_TravelMailbox.KindCall, n - 1);
                TvExpect(CallInFrom() < 0, "an older seq is dropped");
                CallExpectReply(c, 0, 0, 0, "late requests");
                TvNote("T: cancel(" + n + ") then request(" + n + ") call/join and request(" + (n - 1) + "): all dropped, no notice, no reply");
            }));
        }

        static void CallC4Double()
        {
            var s = TvCase("C4-double-press", true, 3); var c = new CallCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { tvPuppetWalk = false; TvPuppetAhead(25f); }));
            s.Add(TvWait(2));
            s.Add(TvUntil(() => TvAllNear(7, TvNear), 30, "dogs did not catch up after placing T"));
            s.Add(TvWait(1));
            CallGoFlow(s, c, "C4-double", true, KH_LeashManager.JoinReasonNear);
        }

        static void CallC5()
        {
            var s = TvCase("C5-paused-and-off", true, 0); var c = new CallCtx();
            s.Add(TvDo(() => { CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(CallGuardPassed, 2, "input guard"));
            s.Add(TvDo(() => CallPress("_Pause")));
            s.Add(TvUntil(() => CallReplies() - c.replies == 1, 2, "no reply to [しばらく受けない]"));
            s.Add(TvDo(() =>
            {
                CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodePaused, "pause");
                CallGapReset();
                int a = CallInject(KH_TravelMailbox.KindCall);
                CallExpectReply(c, 2, a, KH_TravelMailbox.CodePaused, "call while paused");
                int b = CallInject(KH_TravelMailbox.KindJoin);
                CallExpectReply(c, 3, b, KH_TravelMailbox.CodePaused, "join while paused");
                TvExpect(!CallBoardShown(), "no notice while paused");
                TvMyBox().SetProgramVariable("_pauseUntil", 0f);
                TvMyBox().SendCustomEvent("_ToggleAccepting");
                TvExpect(!Get<bool>(TvMyBox(), "accepting"), "accepting OFF");
                int d = CallInject(KH_TravelMailbox.KindCall);
                CallExpectReply(c, 4, d, KH_TravelMailbox.CodePaused, "call while OFF");
                TvMyBox().SendCustomEvent("_ToggleAccepting");
                TvExpect(Get<bool>(TvMyBox(), "accepting"), "accepting back ON");
                TvNote("T: paused -> call and join auto (s,3), no notice; accepting OFF -> call auto (s,3); pause cleared (simulated), accepting restored");
            }));
        }

        static void CallC6()
        {
            var s = TvCase("C6-blocked", true, 0); var c = new CallCtx();
            s.Add(TvDo(() => { CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(CallGuardPassed, 2, "input guard"));
            s.Add(TvDo(() => CallPress("_Block")));
            s.Add(TvUntil(() => CallReplies() - c.replies == 1, 2, "no reply to [この人からは…]"));
            s.Add(TvDo(() =>
            {
                CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodeNo, "block");
                CallGapReset();
                int a = CallInject(KH_TravelMailbox.KindCall);
                CallExpectReply(c, 2, a, KH_TravelMailbox.CodeNo, "call from a blocked sender");
                int b = CallInject(KH_TravelMailbox.KindJoin);
                CallExpectReply(c, 3, b, KH_TravelMailbox.CodeNo, "join from a blocked sender");
                TvExpect(!CallBoardShown(), "no notice for a blocked sender");
                TvMyBox().SetProgramVariable("_blocked", Enumerable.Repeat(-1, 16).ToArray());
                TvNote("T: blocked sender -> call and join silently (s,0); block cleared for the next case (simulated)");
            }));
        }

        /// <summary>C7-landing: 人形 R を 通常 / 橋 / 川 / 歩行 / コース外 に置き、T が [行く]。コース外は返事なし・理由の行・保留のまま。</summary>
        static void CallC7Landing()
        {
            var conds = new (string name, int expect)[] { ("near", KH_LeashManager.JoinReasonNear), ("bridge", KH_LeashManager.JoinReasonBridge), ("water", KH_LeashManager.JoinReasonWater), ("walking", KH_LeashManager.JoinReasonBehind), ("offcourse", -1) };
            foreach (var (name, expect) in conds)
            {
                var s = TvCase("C7-landing-" + name, true, 3); var c = new CallCtx();
                Vector3 at = Vector3.zero;
                s.Add(TvCooldown());
                s.Add(TvDo(() =>
                {
                    tvPuppetWalk = false;
                    var bz = Get<float[]>(manager, "travelBridgeZs"); var bx = Get<float[]>(manager, "travelBridgeXs");
                    var rx = Get<float[]>(manager, "travelRiverXs"); float z0 = Get<float>(manager, "travelRiverZ0"), step = Get<float>(manager, "travelRiverStep");
                    int plaza = Closest(TvSpot(KH_LeashManager.SpotPlaza));
                    if (name == "near") { int n = (plaza + 8) % nodes.Length; at = nodes[n]; }
                    else if (name == "bridge") { at = nodes[Closest(new Vector3(bx[1], 0, bz[1]))]; }
                    else if (name == "water") { float z = 285f; int k = Mathf.Clamp(Mathf.RoundToInt((z - z0) / step), 0, rx.Length - 1); int n = Closest(new Vector3(rx[k], 0, z)); at = new Vector3(rx[k], nodes[n].y - 1f, z); }
                    else if (name == "walking") { tvPuppetNode = (plaza + 20) % nodes.Length; at = nodes[tvPuppetNode]; }
                    else at = new Vector3(-55f, 0f, 715f);
                }));
                s.Add(TvDo(() =>
                {
                    if (name == "near") { int n = Closest(at); TvPlacePuppet(at, nodes[(n + 1) % nodes.Length] - at); }
                    else if (name == "walking") { tvPuppetPos = at; tvPuppetLast = TvNow(); tvPuppetWalk = true; TvPlacePuppet(at, nodes[(tvPuppetNode + 1) % nodes.Length] - at); }
                    else TvPlacePuppet(at, Vector3.forward);
                }));
                s.Add(TvWait(1.5));
                if (expect >= 0)
                {
                    CallGoFlow(s, c, "C7-" + name, false, expect);
                    s.Add(TvDo(() => tvPuppetWalk = false));
                    continue;
                }
                s.Add(TvDo(() => { CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
                s.Add(TvUntil(CallReadyToGo, 3, "caller speed samples / input guard"));
                s.Add(TvDo(() => CallPress("_Go")));
                s.Add(TvWait(1));
                s.Add(TvDo(() =>
                {
                    CallExpectReply(c, 0, 0, 0, "offcourse");
                    TvExpect(Get<int>(TvMyBox(), "_goFail") == KH_TravelMailbox.GoFailNoSafe, "reason = no safe spot (" + Get<int>(TvMyBox(), "_goFail") + ")");
                    var reason = Get<Component>(tvNotice, "reasonLabel"); var textProp = reason != null ? reason.GetType().GetProperty("text") : null;
                    TvExpect(textProp != null && ((string)textProp.GetValue(reason)).Length > 0, "the notice shows the reason line");
                    TvExpect(CallInFrom() == tvRemote.playerId && CallBoardShown() && CallNothingMoved(c), "still pending, nothing moved");
                    CallPress("_NotNow");
                }));
                s.Add(TvUntil(() => CallReplies() - c.replies == 1, 2, "no reply after [今は行かない]"));
                s.Add(TvDo(() => { CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodeNo, "offcourse not now"); TvNote("offcourse: no safe landing -> no reply, reason shown, still pending; [今は行かない] -> (" + c.seq + ",0)"); }));
            }
        }

        /// <summary>C8-replace: 合流の保留 → 同じ送り主の呼ぶ (新 seq) で差し替え。0.5 秒以内の押下と、古い種類のハンドラは捨てる。</summary>
        static void CallC8Replace()
        {
            var s = TvCase("C8-replace-kind", true, 0); var c = new CallCtx();
            s.Add(TvDo(() => { CallGapReset(); CallMark(c); c.seq = CallInject(KH_TravelMailbox.KindJoin); }));
            s.Add(TvUntil(() => CallBoardShown() && Get<int>(TvMyBox(), "_inKind") == KH_TravelMailbox.KindJoin, 2, "join notice not shown"));
            s.Add(TvWait(0.6));
            s.Add(TvDo(() =>
            {
                var join = Get<GameObject[]>(tvNotice, "joinButtons");
                TvExpect(join.All(g => g.activeSelf), "join buttons shown for a join request");
                int gen = Get<int>(TvMyBox(), "_inGen");
                c.seq2 = CallInject(KH_TravelMailbox.KindCall);
                TvExpect(Get<int>(TvMyBox(), "_inKind") == KH_TravelMailbox.KindCall && Get<int>(TvMyBox(), "_inSeq") == c.seq2 && Get<int>(TvMyBox(), "_inGen") == gen + 1, "replaced: kind call, new seq, new generation");
                var call = Get<GameObject[]>(tvNotice, "callButtons");
                TvExpect(call.All(g => g.activeSelf) && join.All(g => !g.activeSelf), "the notice switched to the call buttons");
                CallPress("_Go");   // 差し替えの直後 (0.5 秒以内)
                CallExpectReply(c, 0, 0, 0, "a press right after the change");
                TvExpect(!Get<bool>(TvMyBox(), "_goPressed"), "[行く] within 0.5 s was ignored");
            }));
            s.Add(TvUntil(CallGuardPassed, 2, "input guard"));
            s.Add(TvDo(() =>
            {
                CallPress("_Yes");   // 古い種類 (合流) のハンドラ
                CallExpectReply(c, 0, 0, 0, "the old kind's handler");
                TvExpect(CallInFrom() == tvRemote.playerId, "still pending");
                CallPress("_NotNow");
            }));
            s.Add(TvUntil(() => CallReplies() - c.replies == 1, 2, "no reply after [今は行かない]"));
            s.Add(TvDo(() =>
            {
                CallExpectReply(c, 1, c.seq2, KH_TravelMailbox.CodeNo, "not now on the replaced call");
                CallGapReset();
                CallInject(KH_TravelMailbox.KindJoin, c.seq2);
                TvExpect(CallInFrom() < 0 && CallReplies() - c.replies == 1, "the same seq with another kind is dropped");
                TvNote("T: join(" + c.seq + ") replaced by call(" + c.seq2 + "): buttons switched, [行く]/[いいよ] within 0.5 s ignored, reply (" + c.seq2 + ",0); join(" + c.seq2 + ") dropped");
            }));
        }

        /// <summary>C8-cross: 自分の申し込み (合流 / 呼ぶ) が返事待ちの間に、同じ人形から逆向きの要求 (合流 / 呼ぶ)。勝ち・負け (debugCrossInvert) の 8 通り。</summary>
        static void CallC8Cross()
        {
            var s = TvCase("C8-crossing", true, 0);
            foreach (int mine in new[] { KH_TravelMailbox.KindJoin, KH_TravelMailbox.KindCall })
                foreach (int theirs in new[] { KH_TravelMailbox.KindJoin, KH_TravelMailbox.KindCall })
                    foreach (bool win in new[] { true, false })
                    {
                        var c = new CallCtx(); string tag = (mine == 0 ? "join" : "call") + "/" + (theirs == 0 ? "join" : "call") + (win ? " win" : " lose");
                        s.Add(TvDo(() =>
                        {
                            TvMyBox().SetProgramVariable("debugCrossInvert", win ? 0 : 1);
                            CallGapReset();
                            CallMenuPress(c, mine == 0 ? "_TravelGo" : "_TravelCall");
                        }));
                        s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") == tvRemote.playerId && Get<int>(TvMyBox(), "_outKind") == mine, 2, tag + ": my request did not start"));
                        s.Add(TvDo(() =>
                        {
                            c.seq = CallInject(theirs);
                            if (win)
                            {
                                CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodeBusy, tag);
                                TvExpect(Get<int>(TvMyBox(), "_outTo") == tvRemote.playerId && CallInFrom() < 0, tag + ": my request stays, no notice");
                                TvMyBox().SendCustomEvent("_CancelJoinRequest");
                            }
                            else
                            {
                                CallExpectReply(c, 0, 0, 0, tag);
                                TvExpect(Get<int>(TvMyBox(), "_outTo") < 0 && Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.CrossYield, tag + ": my request expired ('their request first')");
                                TvExpect(CallInFrom() == tvRemote.playerId && Get<int>(TvMyBox(), "_inKind") == theirs, tag + ": their request is pending");
                                TvAnswer(KH_TravelMailbox.CodeYes);   // 失効した申し込みへの遅い答え
                                TvExpect(Get<int>(TvMyBox(), "_acceptFor") < 0 && Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.CrossYield, tag + ": a late answer to the expired request is dropped");
                                TvExpect(!Get<int[]>(TvMyBox(), "_deniedIds").Contains(tvRemote.playerId), tag + ": the expired request is not a refusal (no 30 s)");
                            }
                        }));
                        if (!win)
                        {
                            s.Add(TvUntil(CallGuardPassed, 2, tag + ": input guard"));
                            s.Add(TvDo(() => CallPress(theirs == 0 ? "_No" : "_NotNow")));
                            s.Add(TvUntil(() => CallReplies() - c.replies == 1, 2, tag + ": no reply when closing"));
                        }
                        s.Add(TvDo(() => TvNote(tag + ": " + (win ? "their request auto (s,2), mine stays" : "mine expired, theirs shown, late answer dropped"))));
                    }
            s.Add(TvDo(() => TvMyBox().SetProgramVariable("debugCrossInvert", 0)));
        }

        /// <summary>C8-confirmed: 自分の呼ぶが到着待ち (答え 4 の後) のとき、同じ人形から合流 → 確定した方が優先で code 2。</summary>
        static void CallC8Confirmed()
        {
            var s = TvCase("C8-confirmed-wins", true, 0); var c = new CallCtx();
            s.Add(TvDo(() => { CallGapReset(); CallMenuPress(c, "_TravelCall"); }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") == tvRemote.playerId, 2, "call request did not start"));
            s.Add(TvDo(() =>
            {
                TvAnswer(KH_TravelMailbox.CodePreparing);
                TvExpect(Get<int>(TvMyBox(), "_outPhase") == 1, "arriving");
                TvMyBox().SetProgramVariable("debugCrossInvert", 1);   // 未確定なら負ける側でも、確定した方が優先
                c.seq = CallInject(KH_TravelMailbox.KindJoin);
                CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodeBusy, "confirmed crossing");
                TvExpect(Get<int>(TvMyBox(), "_outTo") == tvRemote.playerId && CallInFrom() < 0, "my confirmed call stays, no notice");
                TvMyBox().SetProgramVariable("debugCrossInvert", 0);
                TvMyBox().SendCustomEvent("_CancelJoinRequest");
                TvNote("R with a confirmed call (code 4 received): the puppet's join gets (s,2) even when the id order would lose");
            }));
        }

        /// <summary>C9: 開始した後、本人の移動の前に止まる終端 → code 4 と code 5 が各 1 回、移動なし。
        /// (a) T 自身の [取り消す] (手元メニューの実ボタン、切符待ち)。(b) 切符が書かれず 8 秒で取りやめ (debugDropTravel)。</summary>
        static void CallC9SelfStop()
        {
            var s = TvCase("C9-stopped-before-move", true, 3);
            // 人形は C7 の「コース外」に置いたまま (着地点が無く [行く] が始まらない: 82c 20260928-222031)。道の上へ戻す
            s.Add(TvCooldown());
            s.Add(TvDo(() => { tvPuppetWalk = false; TvPuppetAhead(25f); }));
            s.Add(TvWait(2));
            s.Add(TvUntil(() => TvAllNear(7, TvNear), 30, "dogs did not catch up after placing T"));
            s.Add(TvDo(() => TvExpect(TvBits(TvMask()) >= 1, "at least one leashed dog (a ticket wait needs one)")));
            foreach (bool self in new[] { true, false })
            {
                var c = new CallCtx(); string tag = self ? "(a) own cancel" : "(b) ticket failed";
                s.Add(TvCooldown());
                s.Add(TvDo(() => { TvSet("debugDropTravel", 1); CallGapReset(); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
                s.Add(TvUntil(CallReadyToGo, 3, tag + ": caller speed samples / input guard"));
                s.Add(TvDo(() => { CallMark(c); CallPress("_Go"); }));
                s.Add(CallUntil(() => Get<int>(manager, "_tvState") == 1 && CallReplies() - c.replies == 1, 2, () => tag + ": ticket wait did not start" + CallState(c)));
                s.Add(TvDo(() => { CallExpectReply(c, 1, c.seq, KH_TravelMailbox.CodePreparing, tag + " preparing"); if (self) pouch.SendCustomEvent("_TravelGo"); }));
                s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 0 && CallReplies() - c.replies == 2, self ? 2 : 12, tag + ": no code 5 after the stop"));
                s.Add(TvWait(1));
                s.Add(TvDo(() =>
                {
                    CallExpectReply(c, 2, c.seq, KH_TravelMailbox.CodeCallFailed, tag);
                    int code = Get<int>(manager, "travelResultCode");
                    TvExpect(code == (self ? KH_LeashManager.TravelCanceledByMe : KH_LeashManager.TravelNoAnswer), tag + ": travel result " + code);
                    TvExpect(CallNothingMoved(c) && Get<int>(TvMyBox(), "_goFrom") < 0, tag + ": nothing moved, call closed");
                    TvSet("debugDropTravel", 0);
                    TvNote(tag + ": (" + c.seq + ",4) then (" + c.seq + ",5) once each, travel=" + code + ", no move");
                }));
            }
        }

        /// <summary>C10 (R): code 4 を受けた後、到着の通知が来ない → 20 秒で「確認できませんでした」、30 秒の制限なし、遅い到着は無視。</summary>
        static void CallC10ArriveTimeout()
        {
            var s = TvCase("C10-r-arrival-unconfirmed", true, 3); var c = new CallCtx();
            s.Add(TvDo(() => { CallGapReset(); CallMenuPress(c, "_TravelCall"); }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") == tvRemote.playerId && Get<int>(TvMyBox(), "_outKind") == KH_TravelMailbox.KindCall, 2, "call request did not start"));
            s.Add(TvDo(() => { TvAnswer(KH_TravelMailbox.CodePreparing); TvExpect(Get<int>(TvMyBox(), "_outPhase") == 1, "arriving after code 4"); }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.CallUnknown, 23, "no 'could not confirm' 20 s after code 4"));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(TvMyBox(), "_outTo") < 0, "request cleared");
                TvExpect(!Get<int[]>(TvMyBox(), "_deniedIds").Contains(tvRemote.playerId), "no 30 s gap after an unconfirmed arrival");
                TvAnswer(KH_TravelMailbox.CodeYes);   // 遅れて届いた到着
                TvExpect(Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.CallUnknown, "a late arrival is ignored");
                TvExpect(CallNothingMoved(c), "R did not move");
                TvNote("R: code 4 then nothing -> 'could not confirm' at 20 s, no 30 s gap, late code 1 dropped");
            }));
        }

        static void CallCleanup()
        {
            var s = TvCase("C11-cleanup", true, 0);
            s.Add(TvDo(() =>
            {
                var box = TvMyBox();
                foreach (var hook in new[] { "debugJoinLoopback", "debugAnswerCode", "debugIncomingFrom", "debugIncomingKind", "debugIncomingSeq", "debugCrossInvert" }) box.SetProgramVariable(hook, 0);
                TvSet("debugDropTravel", 0);
                tvPuppetWalk = false;
                if (tvRemote != null) ClientSimMain.RemovePlayer(tvRemote);
                tvRemote = null;
                TvNote("debug hooks back to 0, puppet removed");
            }));
        }

        /// <summary>自分の接続犬 (walkers == 自分)。</summary>
        static int TvMask() { var w = Get<int[]>(manager, "walkers"); int m = 0; for (int i = 0; i < w.Length; i++) if (w[i] == TvMe()) m |= 1 << i; return m; }

        /// <summary>TvUntil と同じ。時間切れの文言をそのときの状態で作る (原因を 1 回で判別する)。</summary>
        static Func<double, bool> CallUntil(Func<bool> condition, double limit, Func<string> what)
        { return now => { if (condition()) return true; if (now - tvStepAt > limit) throw new TimeoutException(what() + " (" + limit + " s)"); return false; }; }

        static string CallState(CallCtx c)
        {
            var box = TvMyBox();
            return " [tvState=" + Get<int>(manager, "_tvState") + " replies+" + (CallReplies() - c.replies) + " last=(" + CallLastSeq() + "," + CallLastCode() + ") goFail=" + Get<int>(box, "_goFail")
                + " goPressed=" + Get<bool>(box, "_goPressed") + " goFrom=" + Get<int>(box, "_goFrom") + " inFrom=" + CallInFrom() + " mask=" + TvMask()
                + " cooldown=" + Get<float>(manager, "_tvCooldownUntil").ToString("F1") + " t=" + Time.time.ToString("F1") + " puppet=" + (tvRemote != null ? tvRemote.GetPosition().ToString() : "none") + "]";
        }

        static VRCPlayerApi TvOther()
        {
            var all = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; VRCPlayerApi.GetPlayers(all);
            return all.FirstOrDefault(p => p != null && !p.isLocal && (tvRemote == null || p.playerId != tvRemote.playerId));
        }
    }
}
