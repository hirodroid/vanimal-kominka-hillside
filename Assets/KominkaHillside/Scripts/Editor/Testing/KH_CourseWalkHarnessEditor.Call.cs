using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
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
        public static void TravelCall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start Course/82c from Edit mode");
            CwPrepare();   // C7-wet: the site is searched here, in Edit mode (Temp/call-wet-site.json). Never throws
            BeginTravel(false, false, true);
        }

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
            CallC7Wet();   // 既存のケースの後 (順序・所要時間を変えない)。片付けの C11 は最後のまま
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
        /// beforePress: C7-wet の手順 (測定がそろった後、[行く] の直前に差し込む)。watch: 押した後の待ちの各回に呼ぶ (C7-wet の観測)。どちらも null なら従来どおり。
        static void CallGoFlow(List<Func<double, bool>> s, CallCtx c, string name, bool doublePress, int expectReason, List<Func<double, bool>> beforePress = null, Action watch = null)
        {
            s.Add(TvDo(() => { CallGapReset(); c.seq = CallInject(KH_TravelMailbox.KindCall); }));
            s.Add(TvUntil(() => CallInFrom() == tvRemote.playerId && Get<int>(TvMyBox(), "_inKind") == KH_TravelMailbox.KindCall && CallBoardShown(), 2, name + ": notice not shown"));
            s.Add(TvUntil(CallReadyToGo, 3, name + ": caller speed samples / input guard"));
            if (beforePress != null) s.AddRange(beforePress);
            s.Add(TvDo(() =>
            {
                CallMark(c); c.disc = (int[])tvDisc.Clone();
                var call = Get<GameObject[]>(tvNotice, "callButtons"); var join = Get<GameObject[]>(tvNotice, "joinButtons");
                TvExpect(call != null && call.All(g => g != null && g.activeSelf) && join != null && join.All(g => g != null && !g.activeSelf), name + ": the notice shows the call buttons only");
                CallPress("_Go");
                if (doublePress) CallPress("_Go");
            }));
            s.Add(TvUntil(() => CallReplies() - c.replies >= 1, 2, name + ": no 'preparing' reply"));
            s.Add(TvUntil(() => { watch?.Invoke(); return TvFlat(Networking.LocalPlayer.GetPosition(), c.pos) > 5f; }, 12, name + ": T did not move"));
            s.Add(TvUntil(() => { watch?.Invoke(); return TvAllPhase(7, KH_LeashManager.TravelPhaseDone); }, 4, name + ": dog tickets not done"));
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

        // ============================================================
        // C7-wet (設計書 §F.4「陸で接続した犬が、後で濡れた」・§F.6 の C7-wet)
        // ============================================================
        // 到達手順 (コードの読みと、Edit モードの探索で確かめる):
        // - 接続中の犬の足もとが水になる経路は NavMesh の移動 (LeashGo と GatherStep の角まで。_navStep の間は川の帯の判定を掛けない) だけ。
        //   直進 (StepToward / Coast)・押し合い (_WaterPushBlocked) は帯の中へ入らない。Return の追従テレポートと切符の置き場所は NavMesh の点。
        // - 泳ぎ (_waterSwimming / _swimYValid) を立てるのは水遊び・Fetch・ディスクの着水・Owner 交代の引き継ぎ (同期の waterPose 2) だけで、
        //   どれも接続中には走らない (UpdateWaterPlay は接続中なら先に戻る。StartFetch は接続中を断る)。接続中の犬の水の状態は足が濡れる
        //   (waterPose 1) までしか起こらない。
        // - よって「陸で接続した犬が濡れる」は、**犬の濡れの規則 (WaterWetAt) で濡れる点に NavMesh がある**ときだけ起こる。CwSearch は
        //   川の帯 (半幅 + 1 m) と水遊び場の多角形を 0.25 m の格子で走査し、濡れる点 (深さ > 0.04 m。規則は 0.02 m) のうち NavMesh の上
        //   (水平 0.05 m 以内) にあり、最寄りの周回路ノードから完全な経路がある点と、その 0.5〜2.5 m にある乾いた NavMesh の立ち位置を探す。
        // Play: 見つかれば T を立ち位置へ、人形 R を道の上 (その点から 40 m 以上・川から 12 m 以上・橋の外) へ置き、要求 → 測定がそろう →
        // 犬 0 (接続中、陸で接続済み) をその点に置く → 濡れた (waterPose != 0) のを見て [行く] → C1 と同じ一式。合格は、C1 の条件に加えて
        // 犬 0 が切符で動く直前まで濡れていた、水の拒否 (_waterLeashVeto) が立たなかった、着いた後に犬 0〜2 の waterPose 0・_waterSwimming と
        // _swimYValid が false・waterState 0・接続が残る・地面に立つ (0.2 m 以内)。
        // 探索は 2 つに分ける (Codex レビュー 2026-09-29):
        // - 存在の走査 (exist*): 本体の規則そのまま (深さ > 0.02 m) で濡れる NavMesh 上の点が在るか。立ち位置・安定性・経路の条件は掛けない。
        //   (a) 0.25 m 格子を NavMesh へ投影 (水平 0.05 m 以内)、(b) 水の近くの NavMesh の三角形を 0.1 m 以下の重心座標の格子で当てる
        //   (頂点・辺を含む。格子で落ちる NavMesh の縁も見る)。
        // - 試験に使う地点の選別 (pick*): 深さ > 0.04 m、0.25 m 格子、経路の確認は最大 400、乾いた立ち位置 (上の段落)。
        // 選別で見つからなければケースは未実施 (状態 NOT_RUN。PASS に数えない)。notes と Console に「候補未発見・未実施」と両方の件数を書く。
        // 存在の走査も 0 件のときだけ、走査の条件を書いたうえで「この走査では 0 件」と書く (到達不能とは言わない)。
        // 探索自体が失敗したとき・ファイルが無いときは FAIL。
        const string CwFile = "Temp/call-wet-site.json";
        const float CwGrid = .25f, CwWetMin = .04f, CwRuntimeWet = .02f, CwOnNav = .05f, CwTriStep = .1f;
        const int CwMaxPathChecks = 400, CwTriMaxDiv = 200;
        [Serializable] public class CwSite
        {
            public string utc, why = "", error = "";
            // 存在の走査 (本体の規則、条件なし)
            public string existRule;
            public int existGridScanned, existGridWet, existGridNavWet, existTriangles, existTriSamples, existTriWetSamples, existCount;
            public Vector3 existExample;
            public string existExampleFrom = "";
            // 試験に使う地点の選別
            public string pickRule;
            public int pickWet, pickNavWet, pickPathChecked, pickReachable;
            public bool found, spotUsed;
            public Vector3 wet, stand;
            public float depth;
            public int node = -1, neighbours;
        }
        static CwSite cwSite;
        static int cwLastPose = -1;
        static VRC.Udon.UdonBehaviour[] cwFetch;

        /// <summary>犬の WaterWetAt (KD_DogWanderController.HillsideWater.cs) の言い直し。min は濡れとみなす深さ (規則は 0.02 m)。</summary>
        static bool CwWetAt(KH_RiverData river, KH_WaterSpot spot, Vector3 p, float min, out float depth)
        {
            depth = 0f;
            if (spot != null && spot._Contains(p))
            {
                if (spot._Bed(p) <= KH_WaterSpot.NoBed + 1f) { depth = -1f; return river._InBand(p, KH_RiverData.BandMargin); }   // 実行時も「不明 = 濡れ」
                depth = spot._Depth(p); return depth > min;
            }
            if (!river._InBand(p, KH_RiverData.BandMargin)) return false;
            depth = river._DepthAt(p); return depth > min;
        }

        static bool CwOnNavMesh(Vector3 q, float radius, out Vector3 at)
        {
            at = q; NavMeshHit h;
            if (!NavMesh.SamplePosition(q, out h, radius, NavMesh.AllAreas)) return false;
            var off = h.position - q; off.y = 0f;
            if (off.magnitude > CwOnNav) return false;
            at = h.position; return true;
        }

        static bool CwPathComplete(Vector3 a, Vector3 b)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        static long CwKey(int ix, int iz) { return ((long)ix << 32) | (uint)iz; }

        /// <summary>Edit モード (Bind の後): 接続中の犬が歩いて入れる濡れた点と、T の乾いた立ち位置。</summary>
        static CwSite CwSearch()
        {
            string wetRule = "wet = the dogs' WaterWetAt restated (spot grid inside the spot polygon, unknown bed there = wet inside the band; else river band (half width + " + KH_RiverData.BandMargin + " m, not on a bridge deck) and river-wide depth)";
            var set = new CwSite
            {
                utc = DateTime.UtcNow.ToString("o"),
                existRule = wetRule + ", depth > " + CwRuntimeWet + " m (the runtime threshold); (a) " + CwGrid + " m grid over the band and the spot box projected on the NavMesh (within " + CwOnNav + " m horizontally); (b) every NavMesh triangle near the band or the spot box sampled on a barycentric lattice of <= " + CwTriStep + " m (vertices and edges included, at most " + CwTriMaxDiv + " divisions per edge); no stand, stability or path condition",
                pickRule = wetRule + ", depth > " + CwWetMin + " m; " + CwGrid + " m grid on the NavMesh (within " + CwOnNav + " m); ranked by wet NavMesh neighbours within 0.5 m then depth; complete path from the nearest route node (at most " + CwMaxPathChecks + " checked); a dry NavMesh stand 0.5-2.5 m away"
            };
            var rn = GameObject.Find("KH_Dogs/KH_RiverData"); var river = rn == null ? null : rn.GetComponent<KH_RiverData>();
            if (river == null || !river._FetchReady()) { set.error = "KH_Dogs/KH_RiverData missing or its river-wide grid not built (Course/90)"; return set; }
            var sn = GameObject.Find("KH_Dogs/KH_WaterSpot"); var spot = sn == null ? null : sn.GetComponent<KH_WaterSpot>();
            if (spot != null && !spot._Ready()) spot = null;
            set.spotUsed = spot != null;
            var nav = new Dictionary<long, Vector3>(); var depthOf = new Dictionary<long, float>(); var seen = new HashSet<long>();
            Action<int, int> probe = (ix, iz) =>
            {
                long key = CwKey(ix, iz);
                if (!seen.Add(key)) return;
                set.existGridScanned++;
                float x = ix * CwGrid, z = iz * CwGrid, d;
                var q = new Vector3(x, river._SurfaceY(z), z);
                if (!CwWetAt(river, spot, q, CwRuntimeWet, out d)) return;
                set.existGridWet++;
                bool pickWet = CwWetAt(river, spot, q, CwWetMin, out d);
                if (pickWet) set.pickWet++;
                Vector3 at;
                if (!CwOnNavMesh(q, 1.5f, out at)) return;
                if (!CwWetAt(river, spot, at, CwRuntimeWet, out d)) return;   // y は橋の上だけに効く (橋の上は濡れない)
                set.existGridNavWet++;
                if (set.existCount++ == 0) { set.existExample = at; set.existExampleFrom = "grid"; }
                if (!pickWet || !CwWetAt(river, spot, at, CwWetMin, out d)) return;
                set.pickNavWet++; nav[key] = at; depthOf[key] = d;
            };
            float z0 = river.zStart, z1 = river.zStart + (river.centerX.Length - 1) * river.zStep;
            for (int iz = Mathf.CeilToInt(z0 / CwGrid); iz * CwGrid <= z1; iz++)
            {
                float z = iz * CwGrid, cx = river._CenterX(z), hw = river._HalfWidth(z) + KH_RiverData.BandMargin;
                for (int ix = Mathf.FloorToInt((cx - hw) / CwGrid); ix * CwGrid <= cx + hw; ix++) probe(ix, iz);
            }
            if (spot != null)
            {
                int xa = Mathf.FloorToInt(spot.polyX.Min() / CwGrid), xb = Mathf.CeilToInt(spot.polyX.Max() / CwGrid);
                int za = Mathf.FloorToInt(spot.polyZ.Min() / CwGrid), zb = Mathf.CeilToInt(spot.polyZ.Max() / CwGrid);
                for (int iz = za; iz <= zb; iz++) for (int ix = xa; ix <= xb; ix++) probe(ix, iz);
            }
            CwTriangleScan(river, spot, set);
            // 犬が少し動いても濡れたままでいやすい点から: 2 格子以内の濡れた NavMesh の数 → 深さ → キー (決定的)
            Func<long, int> neigh = k =>
            {
                int ix = (int)(k >> 32), iz = (int)(uint)(k & 0xffffffffL), n = 0;
                for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) if ((dx != 0 || dz != 0) && nav.ContainsKey(CwKey(ix + dx, iz + dz))) n++;
                return n;
            };
            var ranked = nav.Keys.Select(k => new { k, n = neigh(k), d = depthOf[k] }).OrderByDescending(a => a.n).ThenByDescending(a => a.d).ThenBy(a => a.k).ToList();
            foreach (var cand in ranked)
            {
                if (set.pickPathChecked >= CwMaxPathChecks) break;
                set.pickPathChecked++;
                var p = nav[cand.k]; int node = Closest(p); NavMeshHit nh;
                if (!NavMesh.SamplePosition(nodes[node], out nh, 2f, NavMesh.AllAreas) || !CwPathComplete(nh.position, p)) continue;
                set.pickReachable++;
                Vector3 stand; if (!CwStand(river, spot, p, out stand)) continue;
                set.found = true; set.wet = p; set.stand = stand; set.depth = cand.d; set.node = node; set.neighbours = cand.n;
                set.why = "wet NavMesh point " + p.ToString("F2") + " depth " + cand.d.ToString("F3") + " m (" + cand.n + " wet NavMesh neighbours within 0.5 m), route node " + node + ", stand " + stand.ToString("F2");
                break;
            }
            if (!set.found)
            {
                string exist = "existence scan (runtime rule, no conditions): grid " + set.existGridScanned + " points / wet " + set.existGridWet + " / wet on the NavMesh " + set.existGridNavWet
                    + "; NavMesh triangles near water " + set.existTriangles + " / samples " + set.existTriSamples + " / wet samples " + set.existTriWetSamples;
                string pick = "selection: wet (> " + CwWetMin + " m) " + set.pickWet + " / on the NavMesh " + set.pickNavWet + " / path-checked " + set.pickPathChecked + " / reachable " + set.pickReachable
                    + (set.pickReachable > 0 ? " (no dry NavMesh stand within 2.5 m)" : "");
                set.why = set.existCount == 0
                    ? "candidate not found, not run. This scan found 0 wet NavMesh points (" + set.existRule + "). " + exist + "; " + pick
                    : "candidate not found by the selection, not run; the existence scan found " + set.existCount + " wet NavMesh point(s) (first " + set.existExample.ToString("F2") + " from the " + set.existExampleFrom + "). " + exist + "; " + pick;
            }
            return set;
        }

        /// <summary>存在の走査 (b): 水 (川の帯・水遊び場の箱) の近くの NavMesh の三角形を、辺 CwTriStep 以下の重心座標の格子で当てる。
        /// 頂点・辺の上の点を含むので、0.25 m 格子の投影で落ちる NavMesh の縁も見る。点は三角形の上 = NavMesh の上。</summary>
        static void CwTriangleScan(KH_RiverData river, KH_WaterSpot spot, CwSite set)
        {
            var tri = NavMesh.CalculateTriangulation();
            var v = tri.vertices; var idx = tri.indices;
            if (v == null || idx == null) return;
            float sx0 = 0f, sx1 = 0f, sz0 = 0f, sz1 = 0f;
            if (spot != null) { sx0 = spot.polyX.Min(); sx1 = spot.polyX.Max(); sz0 = spot.polyZ.Min(); sz1 = spot.polyZ.Max(); }
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                Vector3 a = v[idx[t]], b = v[idx[t + 1]], c = v[idx[t + 2]];
                Vector3 ab = b - a, ac = c - a, bc = c - b; ab.y = 0f; ac.y = 0f; bc.y = 0f;
                float edge = Mathf.Max(ab.magnitude, Mathf.Max(ac.magnitude, bc.magnitude));
                // 近さ: 頂点の帯からの横距離の最小が 3 × 最長辺 + 1 m 未満 (川の曲がりと幅の変化の余裕)、または水遊び場の箱と重なる
                float lat = Mathf.Min(river._Lateral(a, KH_RiverData.BandMargin), Mathf.Min(river._Lateral(b, KH_RiverData.BandMargin), river._Lateral(c, KH_RiverData.BandMargin)));
                bool near = lat < 3f * edge + 1f;
                if (!near && spot != null)
                {
                    float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    near = x1 >= sx0 && x0 <= sx1 && z1 >= sz0 && z0 <= sz1;
                }
                if (!near) continue;
                set.existTriangles++;
                int n = Mathf.Clamp(Mathf.CeilToInt(edge / CwTriStep), 1, CwTriMaxDiv);
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n - i; j++)
                    {
                        var p = a + (b - a) * ((float)i / n) + (c - a) * ((float)j / n);
                        set.existTriSamples++;
                        float d;
                        if (!CwWetAt(river, spot, p, CwRuntimeWet, out d)) continue;
                        set.existTriWetSamples++;
                        if (set.existCount++ == 0) { set.existExample = p; set.existExampleFrom = "triangles"; }
                    }
            }
        }

        static bool CwStand(KH_RiverData river, KH_WaterSpot spot, Vector3 wet, out Vector3 stand)
        {
            stand = wet;
            for (float r = .5f; r <= 2.5f + 1e-4f; r += .25f)
                for (int k = 0; k < 16; k++)
                {
                    float ang = k * Mathf.PI / 8f; Vector3 at, q = wet + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r; float d;
                    if (!CwOnNavMesh(q, 1f, out at) || CwWetAt(river, spot, at, CwRuntimeWet, out d) || !CwPathComplete(at, wet)) continue;
                    stand = at; return true;
                }
            return false;
        }

        static string CwLine(CwSite s)
        {
            return !string.IsNullOrEmpty(s.error) ? "ERROR (" + s.error + ")" : s.found ? "found: " + s.why : "NOT_RUN: " + s.why;
        }

        /// <summary>82c のメニュー (Edit モード、Begin の前): 探して CwFile に書く。例外は error に入れて書く (82c の他のケースは止めない)。</summary>
        static void CwPrepare()
        {
            CwSite set;
            try { Bind(); set = CwSearch(); }
            catch (Exception e) { set = new CwSite { utc = DateTime.UtcNow.ToString("o"), error = "search failed: " + e.GetType().Name + ": " + e.Message }; }
            finally { manager = null; pouch = null; route = null; wander = null; roots = null; nodes = null; }
            try
            {
                Directory.CreateDirectory("Temp");
                File.WriteAllText(CwFile, JsonUtility.ToJson(set, true));
            }
            catch (Exception e) { Debug.LogWarning("[KH Course Walk] C7-wet site file not written: " + e.Message); }
            Debug.Log("[KH Course Walk] C7-wet site: " + CwLine(set));
        }

        static VRC.Udon.UdonBehaviour CwFetch(int dog)
        {
            if (cwFetch == null || cwFetch.Length != 6 || cwFetch.Any(f => f == null))
                cwFetch = UnityEngine.Object.FindObjectsOfType<VRC.Udon.UdonBehaviour>(true).Where(u => u.programSource != null && u.programSource.name == "KD_DogFetchController").OrderBy(u => Get<int>(u, "dogIndex")).ToArray();
            if (cwFetch.Length != 6) throw new InvalidOperationException("six KD_DogFetchController required (" + cwFetch.Length + ")");
            return cwFetch[dog];
        }

        static int CwPose(int dog) { return Get<int>(CwFetch(dog), "waterPose"); }

        static string CwDogState(int dog)
        {
            return " [pose=" + CwPose(dog) + " swimming=" + Get<bool>(wander[dog], "_waterSwimming") + " swimYValid=" + Get<bool>(wander[dog], "_swimYValid")
                + " waterState=" + Get<int>(wander[dog], "waterState") + " veto=" + Get<bool>(wander[dog], "_waterLeashVeto") + " walker=" + Get<int[]>(manager, "walkers")[dog]
                + " at=" + roots[dog].position.ToString("F2") + "]";
        }

        /// <summary>犬の根と、その下の地面 (層 0 / 11、犬自身のコライダーを除く) の高さの差。地面が無ければ -1。</summary>
        static float CwGroundGap(int dog)
        {
            var p = roots[dog].position;
            var hits = Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 5f, FgMask, QueryTriggerInteraction.Ignore)
                .Where(h => !h.transform.IsChildOf(roots[dog])).OrderBy(h => h.distance).ToArray();
            return hits.Length == 0 ? -1f : Mathf.Abs(p.y - hits[0].point.y);
        }

        static void CallC7Wet()
        {
            var s = TvCase("C7-wet", true, 3); var c = new CallCtx();
            cwSite = null; cwLastPose = -1;
            string bad = null;
            try
            {
                if (!File.Exists(CwFile)) bad = CwFile + " missing: start 82c from its menu in Edit mode (the site is searched there)";
                else cwSite = JsonUtility.FromJson<CwSite>(File.ReadAllText(CwFile));
            }
            catch (Exception e) { bad = CwFile + " unreadable: " + e.Message; }
            if (bad == null && cwSite == null) bad = CwFile + " empty";
            if (bad == null && !string.IsNullOrEmpty(cwSite.error)) bad = "Edit-mode search: " + cwSite.error;
            if (bad != null) { string why = bad; s.Add(TvDo(() => { throw new InvalidOperationException(why); })); return; }
            if (!cwSite.found)
            {
                s.Add(TvDo(() =>
                {
                    tvCaseNotRun = true;   // TravelTick: 状態 NOT_RUN (PASS に数えない)
                    TvNote("NOT_RUN (Edit-mode search " + cwSite.utc + "): " + cwSite.why);
                    Debug.Log("[KH Course Walk] C7-wet NOT_RUN: " + cwSite.why);
                }));
                return;
            }
            int puppetNode = -1;
            s.Add(TvCooldown());
            s.Add(TvDo(() =>
            {
                tvPuppetWalk = false;
                TvExpect((TvMask() & 7) == 7, "dogs 0-2 leashed to me (mask " + TvMask() + ")");
                int plaza = Closest(TvSpot(KH_LeashManager.SpotPlaza));
                for (int k = 0; k < nodes.Length && puppetNode < 0; k++)
                {
                    int n = (plaza + 8 + k) % nodes.Length;   // C1 / C7-near の点から道なりに
                    if (TvFlat(nodes[n], cwSite.wet) >= 40f && RiverLateral(nodes[n]) >= 12f && !RwBridgeZone(nodes[n].z, 10f)) puppetNode = n;
                }
                TvExpect(puppetNode >= 0, "a route node 40 m from the wet site, 12 m from the river and off the bridges");
                var face = cwSite.wet - cwSite.stand; face.y = 0f;
                holder = cwSite.stand;
                Networking.LocalPlayer.TeleportTo(cwSite.stand, face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face) : Networking.LocalPlayer.GetRotation());
                TvPlacePuppet(nodes[puppetNode], nodes[(puppetNode + 1) % nodes.Length] - nodes[puppetNode]);
                TvNote("site (Edit-mode search " + cwSite.utc + "): " + cwSite.why + "; puppet on route node " + puppetNode);
            }));
            s.Add(TvWait(2));
            s.Add(TvUntil(() => TvAllNear(7, TvNear), 30, "dogs did not catch up at the wet site"));
            s.Add(TvWait(1));
            s.Add(TvDo(() => TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), cwSite.stand) < .6f, "T stays on the stand point (" + TvFlat(Networking.LocalPlayer.GetPosition(), cwSite.stand).ToString("F2") + " m off)")));
            // [行く] の直前: 犬 0 (陸で接続済み) を濡れた NavMesh の点へ置く = 接続中の NavMesh の歩きで足が濡れた状態 (模擬の置き方、場所は実際に歩ける点)
            var wet = new List<Func<double, bool>>();
            wet.Add(TvDo(() =>
            {
                var face = cwSite.wet - cwSite.stand; face.y = 0f;
                roots[0].SetPositionAndRotation(cwSite.wet, face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face) : roots[0].rotation);
                var sync = roots[0].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
            }));
            wet.Add(CallUntil(() => CwPose(0) != 0, 2, () => "dog 0 did not get wet at " + cwSite.wet.ToString("F2") + CwDogState(0)));
            wet.Add(TvDo(() =>
            {
                TvExpect(Get<int[]>(manager, "walkers")[0] == TvMe(), "dog 0 still leashed to me" + CwDogState(0));
                TvExpect(!Get<bool>(wander[0], "_waterLeashVeto"), "no water veto: the leash was first seen on land" + CwDogState(0));
                TvExpect(Get<int>(CwFetch(0), "dogState") == 0, "dog 0 idle (a chase would stop [行く] with TravelBall)");
                cwLastPose = CwPose(0);
                TvNote("dog 0 placed on the wet NavMesh point (simulated placement; the point is leash-walkable): pose " + cwLastPose + CwDogState(0));
            }));
            CallGoFlow(s, c, "C7-wet", false, KH_LeashManager.JoinReasonNear, wet, () => { if (tvDisc[0] == 0) cwLastPose = CwPose(0); });
            s.Add(CallUntil(() => CwPose(0) == 0, 2, () => "dog 0 still wet after the move" + CwDogState(0)));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                TvExpect(cwLastPose == 1 || cwLastPose == 2, "dog 0 was still wet at the last observation before its ticket move (pose " + cwLastPose + ")");
                var walkers = Get<int[]>(manager, "walkers");
                for (int i = 0; i < 3; i++)
                {
                    bool clean = CwPose(i) == 0 && !Get<bool>(wander[i], "_waterSwimming") && !Get<bool>(wander[i], "_swimYValid") && Get<int>(wander[i], "waterState") == 0 && !Get<bool>(wander[i], "_waterLeashVeto");
                    TvExpect(clean, "dog" + i + " left no water state after the move" + CwDogState(i));
                    TvExpect(walkers[i] == TvMe(), "dog" + i + " still leashed to me");
                    float gap = CwGroundGap(i);
                    TvExpect(gap >= 0f && gap < .2f, "dog" + i + " stands on the ground (root - ground " + gap.ToString("F3") + " m)");
                }
                TvNote("C7-wet: dog 0 wet (pose " + cwLastPose + ") until its ticket move; after the move dogs 0-2 pose 0, not swimming, no float height, no play, no veto, leashed, on the ground");
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
