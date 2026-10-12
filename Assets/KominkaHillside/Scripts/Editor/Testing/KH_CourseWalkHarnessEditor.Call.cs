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
            CwPrepare();   // C7-wet (IMP-DOG-053; was C7-wet-sim): the site is searched here, in Edit mode (Temp/call-wet-site.json). Never throws
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
            CallC7Wet();   // C7-wet (IMP-DOG-053: リードで歩いて入水 → [行く] の実際の経路。旧 C7-wet-sim)。既存のケースの後。片付けの C11 は最後のまま
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
        // C7-wet (IMP-DOG-053, 2026-10-12: C7-wet-sim の置き換え。設計書 §F.4・§F.6)
        // ============================================================
        // 2026-10-11 の決定 (DES-DOG-010) で上書き: リードの犬は川へ入れる。実際の経路をそのまま通す: T は岸 (下の地点探索の stand) で
        // 犬 0〜2 を揃えたあと、川の中 (hold: 最初に濡れる浅い点から中心へ最大 1.5 m、深さ 0.12〜0.35 m、川全体の水の中、水遊び場の外) へ
        // 入り、中心を向いて立つ。犬はリードの川の追従 (KD_DogWanderController.HillsideLeashRiver) で岸の入口から水へ入る。犬 0 が
        // 本体の規則で濡れて (waterPose != 0) 追従の状態 2 になるのを待ってから [行く] を押す (置き方・waterPose の保持の模擬は無い)。
        // 合格: C1 の一式 + 犬 0 が切符で動く直前まで濡れていた、着いた後に犬 0〜2 の waterPose 0・_waterSwimming / _swimYValid が false・
        // waterState 0・リードの川の状態 0 (_lrState)・接続が残る・地面に立つ (0.2 m 以内)。切符は位置を変えた適用で浮きを消し姿勢を
        // 更新する (HillsideTravel、IMP-DOG-053 #6)。
        // 以下は C7-wet-sim (2026-10-11 版) の記録 (地点探索の規則はそのまま使う):
        // 模擬 (配置と保持) による防御の試験。実際の同期の競合の経路は未検査 (2026-10-11 の独立レビュー、IMP-WLD-002)。
        // PASS が示すのは「人工的に置いて保った waterPose が切符の同行を妨げず、保持を外すと本体の再計算で乾く」ことまで。
        // 実際に起こり得る経路 (Manager が古い乾いた姿勢で接続を承認 → 犬の Owner が濡れを見て _waterLeashVeto を立て解除を要求 →
        // その間に [行く]) は期待値が未決で、このケースは扱わない (拒否なしを要求している)。
        // 濡れの規則 (コードの読み):
        // - 犬の濡れ (waterPose 1 / 2) は根の位置だけで決まる (WaterPoseRefresh → WaterWetAt: 水遊び場の多角形の中はその深さ、外は川の帯
        //   (半幅 + BandMargin 1 m) の中で川全体の深さ > 0.02 m)。上がった後に残る濡れのフラグ・タイマーは無い (水遊びの WaterShake は
        //   リードの無い犬だけ。KH_DogWash の _wet は洗いの毛の濡れで別のもの)。
        // - 接続中の犬が動くのは NavMesh の上だけ (LeashGo / TeleportTo / 切符の置き場所)。泳ぎ (_waterSwimming) を立てる水遊び・Fetch・
        //   ディスクは接続中には走らない。
        // - 川の帯 = NavMesh の川床の除外 (KH_RiverData.BandMargin のコメント、RiverbedNotWalkable、Course/20 の「Riverbed walkable」検査)。
        //   よって「濡れる点で NavMesh の上」はほぼ無い。2026-09-29 版 (19db81d の記録、a06178c) はこの点を探して 0 件
        //   (格子 87,023 点 + 三角形 1,953 枚・約 1,611 万点) で未実施だった。ただし LeashGo は角の 0.35 m 手前で次の角へ切り替え、
        //   移動中は _navStep で川の進入防止を迂回する (HillsideLeash.cs) ので、通常の経路で足が濡れないとは言えない (未確認)。
        // 2026-10-11 の通し方: 川の実際の出入り口 (川の Fetch が使う ok な岸のスライス KH_RiverData.sliceBank。Course/90 で焼き 91 で検査、
        // NavMesh の上・帯の外・最寄りの周回路ノードから経路あり) を T の立ち位置にし、そこから川の中心へ 0.1 m ずつ進んで最初に濡れる
        // (深さ > 0.08 m、規則は 0.02 m) 浅い点 (≤ 0.3 m、水遊び場の外) に犬 0 (陸で接続済み) を置く = 川から上がる手前の犬の足もと。
        // 濡れ (waterPose != 0) は**本体の規則が書いたもの**を待って確かめる (ハーネスは waterPose を書かない)。そのあと _debugPoseHold
        // (Course/78 W4-leash-stale・WashDirt と同じ作法) でその値を保つ: リードは帯の中の犬を 1 秒足らずで NavMesh へ歩かせて乾かすので、
        // 保たないと [行く] の暗転の間に濡れが消える。保持は犬 0 が切符で動いたのを観測した時点で外し、その後の waterPose 0 は本体の
        // 再計算が書く (外したことも合格条件)。模擬は 2 つ: 置き方 (接続中の犬はそこへ歩いて入れない) と保持。
        // 合格は従来どおり: C1 の一式 + 犬 0 が切符で動く直前まで濡れていた、水の拒否 (_waterLeashVeto) が立たなかった、着いた後に
        // 犬 0〜2 の waterPose 0・_waterSwimming / _swimYValid が false・waterState 0・接続が残る・地面に立つ (0.2 m 以内)。
        // 地点が無ければ FAIL (SETUP_FAIL: 試験の入力を作れなかった)。川のデータの不具合とは限らない: 岸から 3.5 m 以内・深さ 0.08〜0.3 m・
        // 水遊び場の外・乾いた立ち位置から経路あり は、このケースの追加条件で sliceOk には含まれない。探索自体の失敗・ファイルが無いときも
        // SETUP_FAIL (FAIL)。
        const string CwFile = "Temp/call-wet-site.json";
        // CwWetMin は規則 (0.02 m) の 4 倍: 置いた直後の最初の再計算の前にリードが岸へ数 cm 歩かせても濡れたままでいる余裕
        const float CwWetMin = .08f, CwRuntimeWet = .02f, CwWadeMax = .3f, CwStep = .1f, CwStandMax = 3.5f, CwBankOnNav = .3f;
        const int CwMaxPathChecks = 400;
        [Serializable] public class CwSite
        {
            public string utc, why = "", error = "", rule;
            public int slicesOk, slicesShallow, pathChecked, reachable;
            public bool found, spotUsed;
            public Vector3 wet, stand;
            public float depth;
            public Vector3 hold;      // IMP-DOG-053 C7-wet: where T stands in the river (from wet toward the centre, see CwHold)
            public float holdDepth;
            public int code = -1, node = -1;
        }
        static CwSite cwSite;
        static int cwLastPose = -1;
        static Vector3 cwLastAt;
        static bool cwPoseHeld, cwReleasedAtMove;
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

        static bool CwOnNavMesh(Vector3 q, float radius, float maxOff, out Vector3 at)
        {
            at = q; NavMeshHit h;
            if (!NavMesh.SamplePosition(q, out h, radius, NavMesh.AllAreas)) return false;
            var off = h.position - q; off.y = 0f;
            if (off.magnitude > maxOff) return false;
            at = h.position; return true;
        }

        static bool CwPathComplete(Vector3 a, Vector3 b)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>岸の点 bank から川の中心へ (side は岸の側、中心は -side) CwStep ずつ進み、最初に濡れる (深さ > CwWetMin) 点。
        /// その点が深い (> CwWadeMax)・川床が不明・水遊び場の中なら、そのスライスは使わない (浅い水際ではない)。y は川床 (地形) の高さ。</summary>
        static bool CwShallow(KH_RiverData river, KH_WaterSpot spot, Vector3 bank, int side, out Vector3 wet, out float depth)
        {
            wet = bank; depth = 0f;
            for (int k = 1; k * CwStep <= CwStandMax + 1e-4f; k++)
            {
                var p = new Vector3(bank.x - side * k * CwStep, 0f, bank.z);
                if (spot != null && spot._Contains(p)) return false;
                float bed = river._BedAt(p);
                if (bed <= KH_RiverData.NoBed + 1f) continue;
                p.y = bed;
                float d;
                if (!CwWetAt(river, spot, p, CwWetMin, out d)) continue;
                if (d > CwWadeMax) return false;
                wet = p; depth = d; return true;
            }
            return false;
        }

        /// <summary>IMP-DOG-053 C7-wet: where T stands in the river. From the first wet point toward the centre (side = the bank's side), every
        /// CwStep up to 1.5 m: the last point in the river-wide water (KH_RiverData._FetchWater, outside the spot) 0.12-0.35 m deep (T's hand
        /// is then in the band and the dogs' targets in the water). None = the wet point itself. y = the riverbed.</summary>
        static Vector3 CwHold(KH_RiverData river, KH_WaterSpot spot, Vector3 wet, int side, out float depth)
        {
            Vector3 best = wet; depth = river._DepthAt(wet);
            for (int k = 1; k * CwStep <= 1.5f + 1e-4f; k++)
            {
                var p = new Vector3(wet.x - side * k * CwStep, 0f, wet.z);
                if (!river._FetchWater(p) || (spot != null && spot._Contains(p))) continue;
                float d = river._DepthAt(p);
                if (d < .12f || d > .35f) continue;
                p.y = river._BedAt(p); best = p; depth = d;
            }
            return best;
        }

        /// <summary>Edit モード (Bind の後): 川の ok な岸のスライスの岸 (T の立ち位置) と、その先の最初の浅い濡れた点 (犬 0 を置く)。
        /// 浅い順 → 岸からの近さ → コード (決定的)。立ち位置は NavMesh の上 (0.3 m 以内) で、最寄りの周回路ノードから完全な経路がある。</summary>
        static CwSite CwSearch()
        {
            var set = new CwSite
            {
                utc = DateTime.UtcNow.ToString("o"),
                rule = "stand = an ok river bank slice (KH_RiverData.sliceBank, Course/90-91: NavMesh, outside the band, out of the bridge zones) on the NavMesh within "
                    + CwBankOnNav + " m, dry by the dogs' WaterWetAt, complete path from the nearest route node; dog 0 = the first point from the bank toward the centre (every "
                    + CwStep + " m, at most " + CwStandMax + " m) that is wet by the dogs' WaterWetAt restated with depth > " + CwWetMin + " m (runtime " + CwRuntimeWet
                    + " m), not deeper than " + CwWadeMax + " m, outside the water-play spot, at the riverbed height; ranked shallowest, nearest the bank, then by code (at most "
                    + CwMaxPathChecks + " path checks)"
            };
            var rn = GameObject.Find("KH_Dogs/KH_RiverData"); var river = rn == null ? null : rn.GetComponent<KH_RiverData>();
            if (river == null || !river._FetchReady()) { set.error = "KH_Dogs/KH_RiverData missing or its river-wide grid / bank slices not built (Course/90)"; return set; }
            var sn = GameObject.Find("KH_Dogs/KH_WaterSpot"); var spot = sn == null ? null : sn.GetComponent<KH_WaterSpot>();
            if (spot != null && !spot._Ready()) spot = null;
            set.spotUsed = spot != null;
            var cands = new List<KeyValuePair<int, Vector3>>(); var depthOf = new Dictionary<int, float>();
            for (int code = 0; code < river.sliceOk.Length; code++)
            {
                if (!river.sliceOk[code]) continue;
                set.slicesOk++;
                Vector3 bank = river.sliceBank[code], w; float d;
                if (river._InBridgeZone(bank.z, 0f) || CwWetAt(river, spot, bank, CwRuntimeWet, out d)) continue;
                if (!CwShallow(river, spot, bank, river._SliceSide(code), out w, out d)) continue;
                set.slicesShallow++;
                cands.Add(new KeyValuePair<int, Vector3>(code, w)); depthOf[code] = d;
            }
            foreach (var cand in cands.OrderBy(a => depthOf[a.Key]).ThenBy(a => TvFlat(river.sliceBank[a.Key], a.Value)).ThenBy(a => a.Key))
            {
                if (set.pathChecked >= CwMaxPathChecks) break;
                set.pathChecked++;
                Vector3 stand; float d;
                if (!CwOnNavMesh(river.sliceBank[cand.Key], 1f, CwBankOnNav, out stand) || CwWetAt(river, spot, stand, CwRuntimeWet, out d)) continue;
                int node = Closest(stand); NavMeshHit nh;
                if (!NavMesh.SamplePosition(nodes[node], out nh, 2f, NavMesh.AllAreas) || !CwPathComplete(nh.position, stand)) continue;
                set.reachable++;
                set.found = true; set.code = cand.Key; set.wet = cand.Value; set.stand = stand; set.depth = depthOf[cand.Key]; set.node = node;
                set.hold = CwHold(river, spot, cand.Value, river._SliceSide(cand.Key), out set.holdDepth);   // IMP-DOG-053 C7-wet
                set.why = "bank slice " + cand.Key + " (side " + river._SliceSide(cand.Key) + "): stand " + stand.ToString("F2") + " on the NavMesh, route node " + node
                    + "; dog 0 at " + cand.Value.ToString("F2") + " depth " + set.depth.ToString("F3") + " m, " + TvFlat(stand, cand.Value).ToString("F2") + " m from the stand";
                break;
            }
            if (!set.found)
                set.why = "no site: ok bank slices " + set.slicesOk + " / with a shallow wet point " + set.slicesShallow + " / path-checked " + set.pathChecked + " / reachable " + set.reachable + " (" + set.rule + ")";
            return set;
        }

        static string CwLine(CwSite s)
        {
            return !string.IsNullOrEmpty(s.error) ? "ERROR (" + s.error + ")" : s.found ? "found: " + s.why : "SETUP_FAIL: no shallow bank site (test input could not be built; not by itself a river data defect): " + s.why;
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
            catch (Exception e) { Debug.LogWarning("[KH Course Walk] C7-wet-sim site file not written: " + e.Message); }
            Debug.Log("[KH Course Walk] C7-wet-sim site: " + CwLine(set));
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
                + " waterState=" + Get<int>(wander[dog], "waterState") + " leashRiver=" + Get<int>(wander[dog], "_lrState") + " walker=" + Get<int[]>(manager, "walkers")[dog]   // IMP-DOG-053: the RC-14 veto field is retired
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

        /// <summary>犬 0 の _debugPoseHold を外す (保持中のときだけ。切符の移動の観測・流れの後・片付けから)。</summary>
        static void CwReleasePose(bool atMove)
        {
            if (!cwPoseHeld) return;
            CwFetch(0).SetProgramVariable("_debugPoseHold", false);
            cwPoseHeld = false; cwReleasedAtMove = atMove;
        }

        /// <summary>Play 中: p が犬の濡れの規則 (深さ > 0.02 m) で濡れるか。notes 用 (合否には使わない)。</summary>
        static string CwWetNow(Vector3 p)
        {
            var rn = GameObject.Find("KH_Dogs/KH_RiverData"); var river = rn == null ? null : rn.GetComponent<KH_RiverData>();
            if (river == null || !river._FetchReady()) return "unknown";
            var sn = GameObject.Find("KH_Dogs/KH_WaterSpot"); var spot = sn == null ? null : sn.GetComponent<KH_WaterSpot>();
            if (spot != null && !spot._Ready()) spot = null;
            float d;
            return CwWetAt(river, spot, p, CwRuntimeWet, out d) ? "in the water (depth " + d.ToString("F3") + " m)" : "on dry ground";
        }

        static void CallC7Wet()
        {
            var s = TvCase("C7-wet", false, 3); var c = new CallCtx();   // IMP-DOG-053: the real path (was C7-wet-sim, simulated)
            cwSite = null; cwLastPose = -1; cwLastAt = Vector3.zero; cwPoseHeld = false; cwReleasedAtMove = false;
            string bad = null;
            try
            {
                if (!File.Exists(CwFile)) bad = "SETUP_FAIL: " + CwFile + " missing: start 82c from its menu in Edit mode (the site is searched there)";
                else cwSite = JsonUtility.FromJson<CwSite>(File.ReadAllText(CwFile));
            }
            catch (Exception e) { bad = "SETUP_FAIL: " + CwFile + " unreadable: " + e.Message; }
            if (bad == null && cwSite == null) bad = "SETUP_FAIL: " + CwFile + " empty";
            if (bad == null && !string.IsNullOrEmpty(cwSite.error)) bad = "SETUP_FAIL: Edit-mode search: " + cwSite.error;
            // 2026-10-11: 地点が無いのは未実施ではなく FAIL。ただし「試験の入力を作れなかった」(SETUP_FAIL) で、川のデータの不具合とは区別する
            // (岸から 3.5 m・深さ 0.08〜0.3 m・水遊び場の外 はこのケースの追加条件で sliceOk に含まれない)。このケースは NOT_RUN を使わない
            if (bad == null && !cwSite.found) bad = "SETUP_FAIL: no shallow bank site (test input could not be built; the extra conditions - within " + CwStandMax
                + " m of the bank, depth " + CwWetMin + "-" + CwWadeMax + " m, outside the water-play spot, a dry stand with a path - are this case's, not sliceOk's, so this is not by itself a river data defect). Edit-mode search " + cwSite.utc + ": " + cwSite.why;
            if (bad != null) { string why = bad; s.Add(TvDo(() => { throw new InvalidOperationException(why); })); return; }
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
            s.Add(TvUntil(() => TvAllNear(7, TvNear), 30, "dogs did not catch up at the bank"));
            s.Add(TvWait(1));
            s.Add(TvDo(() => TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), cwSite.stand) < .6f, "T stays on the stand point (" + TvFlat(Networking.LocalPlayer.GetPosition(), cwSite.stand).ToString("F2") + " m off)")));
            // IMP-DOG-053: T steps into the river (hold, facing the centre) and the leashed dogs follow by the leash's river follow. Nothing
            // is placed or held: dog 0's wet pose is the runtime's own (the old simulated placement and _debugPoseHold are gone)
            s.Add(TvDo(() =>
            {
                var centre = KH_CourseGroundEditor.RiverAt(cwSite.hold.z);
                var face = new Vector3(centre.x - cwSite.hold.x, 0f, 0f);
                Networking.LocalPlayer.TeleportTo(cwSite.hold, face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face) : Networking.LocalPlayer.GetRotation());
                TvNote("T in the river at " + cwSite.hold.ToString("F2") + " (depth " + cwSite.holdDepth.ToString("F3") + " m), facing the centre");
            }));
            s.Add(CallUntil(() => CwPose(0) != 0 && Get<int>(wander[0], "_lrState") == 2, 25,
                () => "dog 0 did not follow into the water on the leash (river follow state 2 and a wet pose written by the runtime)" + CwDogState(0)));
            s.Add(TvWait(1));
            var wet = new List<Func<double, bool>>();
            wet.Add(TvDo(() =>
            {
                TvExpect(Get<int[]>(manager, "walkers")[0] == TvMe(), "dog 0 still leashed to me" + CwDogState(0));
                TvExpect(CwPose(0) != 0 && Get<int>(wander[0], "_lrState") == 2, "dog 0 still in the water on the leash just before [行く]" + CwDogState(0));
                TvExpect(Get<int>(CwFetch(0), "dogState") == 0, "dog 0 idle (a chase would stop [行く] with TravelBall)");
                cwLastPose = CwPose(0); cwLastAt = roots[0].position;
                TvNote("dog 0 followed T into the river on the leash (real path, no placement / hold): pose " + cwLastPose + CwDogState(0));
            }));
            CallGoFlow(s, c, "C7-wet", false, KH_LeashManager.JoinReasonNear, wet, () =>
            {
                if (tvDisc[0] == 0) { cwLastPose = CwPose(0); cwLastAt = roots[0].position; }
            });
            s.Add(CallUntil(() => CwPose(0) == 0, 2, () => "dog 0 still wet after the move" + CwDogState(0)));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                TvExpect(cwLastPose == 1 || cwLastPose == 2, "dog 0 was still wet at the last observation before its ticket move (pose " + cwLastPose + ")");
                var walkers = Get<int[]>(manager, "walkers");
                for (int i = 0; i < 3; i++)
                {
                    bool clean = CwPose(i) == 0 && !Get<bool>(wander[i], "_waterSwimming") && !Get<bool>(wander[i], "_swimYValid") && Get<int>(wander[i], "waterState") == 0 && Get<int>(wander[i], "_lrState") == 0;
                    TvExpect(clean, "dog" + i + " left no water state after the move" + CwDogState(i));
                    TvExpect(walkers[i] == TvMe(), "dog" + i + " still leashed to me");
                    float gap = CwGroundGap(i);
                    TvExpect(gap >= 0f && gap < .2f, "dog" + i + " stands on the ground (root - ground " + gap.ToString("F3") + " m)");
                }
                TvNote("C7-wet: the real path (IMP-DOG-053). dog 0 pose " + cwLastPose + " until its ticket move (last seen at " + cwLastAt.ToString("F2") + ", " + CwWetNow(cwLastAt)
                    + "); after the move dogs 0-2 pose 0, not swimming, no float height, no play, river follow 0, leashed, on the ground");
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
                bool held = cwPoseHeld;
                CwReleasePose(false);   // C7-wet-sim が途中で止まったとき、犬 0 の waterPose の保持を残さない
                tvPuppetWalk = false;
                if (tvRemote != null) ClientSimMain.RemovePlayer(tvRemote);
                tvRemote = null;
                TvNote("debug hooks back to 0" + (held ? ", dog 0 pose hold released" : "") + ", puppet removed");
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
