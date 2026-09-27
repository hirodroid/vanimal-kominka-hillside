using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.ClientSim;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-001 段階 1 の travel モード (設計書 §C.1 の T1〜T9・T11。T10 は段階 3)。
    ///
    /// 作法は既存の周回コース歩行ハーネスと同じ (Begin / Stop (Course/41) / 全体の締切 / PlayerData の退避と復旧 (Course/46) /
    /// シーンは保存しない / 結果は Logs/kh-course-walk/&lt;runId&gt;/report.json)。travel 固有の詳細は同じ場所の travel.json。
    /// 操作は本物の手元メニューのイベント (_TravelUpper / _TravelRiver / _WalkLeft* ...) で行い、犬の Transform は
    /// 初期配置 (接続の直前) 以外で書かない。1 クライアントなので Owner 交代・遅着・非 Owner・偽装は **模擬** で、
    /// 模擬の中身は各ケースの notes に書く。合格条件は §B.4 の「Owner が同じ人」の契約 (位置の変更は全頭ちょうど 1 回)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/82 - Walk Harness Travel (T1-T9, T11)")]
        public static void TravelFull() { BeginTravel(true); }
        [MenuItem("Tools/KominkaHillside/Course/83 - Walk Harness Travel Quick (T1 one dog, T11)")]
        public static void TravelQuick() { BeginTravel(false); }

        static void BeginTravel(bool full)
        {
            Begin(full);
            report.travelOnly = true;
            report.deadline = EditorApplication.timeSinceStartup + (full ? 1800 : 600);
            report.limitations = "Single local ClientSim: the local player is requester, Manager Owner and every dog Owner (the same-Owner contract of design B.4). "
                + "Owner handover (T5/T6), late answers (T3), non-Owner/forged requests (T8) and departure (T9) are SIMULATED through debug hooks on KH_LeashManager and runtime heap writes; see each case's notes. "
                + "CallingPlayer is always local. No real network order, VRCObjectSync latency, late joiner, fade (stage 2) or join (stage 3). Holder walks by short TeleportTo steps.";
            Save();
        }

        [Serializable] public class TravelCase
        {
            public string name, status = "PENDING";
            public bool simulated;
            public List<string> notes = new List<string>();
        }
        [Serializable] class TravelReport { public string runId; public List<TravelCase> cases = new List<TravelCase>(); }

        static List<List<Func<double, bool>>> tvScripts;
        static TravelReport tvReport;
        static int tvStep;
        static double tvStepAt, tvLastObs, tvCaseAt, tvWalkLast;
        static Vector3[] tvPrev;
        static bool tvObsValid; // false until the first observation of a case sets the reference time (no gap counted)
        static int[] tvDisc, tvPrevNext, tvForward, tvReverse;
        static int tvTrackMask;
        static HashSet<int> tvSeenReqs = new HashSet<int>();
        static List<string> tvLogs = new List<string>();
        static VRCPlayerApi tvRemote;
        static int tvWalkNode;
        static Vector3 tvWalkPos;
        const float TvDisc = 1.5f, TvNear = 3.75f + 0.45f; // 手 = 体から横 0.23 m・前 0.32 m (Desktop の _HandPosition)

        class TvCtx { public double press, arrived, walkStart = -1; public int serial, logFrom, req, req1, busy; public int[] old, oldPhase, applies; public Vector3 pos; }

        // ============================================================
        // ケースの組み立て
        // ============================================================

        static void MakeTravelCases()
        {
            Application.logMessageReceived -= TravelLog; Application.logMessageReceived += TravelLog;
            tvStep = 0; tvCaseAt = -1; tvPrev = null; tvObsValid = false; tvTrackMask = 0; tvRemote = null; tvWalkLast = 0;
            tvLogs.Clear(); tvScripts = new List<List<Func<double, bool>>>(); tvReport = new TravelReport { runId = report.runId };
            var spots = Get<Vector3[]>(manager, "spotPositions");
            var warp = Get<bool[]>(manager, "spotWarpEnabled");
            if (spots == null || spots.Length != 6 || warp == null || warp.Count(w => w) != 2 || !warp[KH_LeashManager.SpotUpper] || !warp[KH_LeashManager.SpotRiver])
                throw new InvalidOperationException("Travel spots not built (run Course/80 and 81 first)");
            var configs = report.full
                ? new[] { ("T1-dogs-1-L1", new[] { 0 }, new[] { 0 }), ("T1-dogs-3-L3", new[] { 0, 1, 2 }, new[] { 0, 0, 0 }), ("T1-dogs-3-L2R1", new[] { 0, 1, 2 }, new[] { 0, 0, 1 }), ("T1-dogs-6-L3R3", new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 0, 0, 1, 1, 1 }) }
                : new[] { ("T1-dogs-1-L1", new[] { 0 }, new[] { 0 }) };
            int mask = 0;
            foreach (var (name, dogs, hands) in configs)
            {
                var s = TvCase(name, false, dogs.Length);
                mask = dogs.Aggregate(0, (a, d) => a | (1 << d));
                TvSetup(s, dogs, hands);
                TvMove(s, "_TravelUpper", KH_LeashManager.SpotUpper, mask, true);
                TvMove(s, "_TravelRiver", KH_LeashManager.SpotRiver, mask, true);
                TvMove(s, "_TravelUpper", KH_LeashManager.SpotUpper, mask, true);
                s.Add(TvDo(() => TvNote("non-warp spots: spotWarpEnabled has only upper/river; the menu has only _TravelUpper/_TravelRiver. Their common checks 1-4 are certified statically by Course/81.")));
            }
            if (report.full)
            {
                TvT2(mask); TvT3(mask); TvT4(mask); TvT5(mask); TvT6(mask); TvT7(mask); TvT8(); TvT9();
            }
            TvT11(report.full ? 7 : 1);
        }

        static List<Func<double, bool>> TvCase(string name, bool simulated, int count)
        {
            report.cases.Add(new Case { name = "travel-" + name, count = count });
            tvReport.cases.Add(new TravelCase { name = name, simulated = simulated });
            var s = new List<Func<double, bool>>(); tvScripts.Add(s); return s;
        }

        static void TvSetup(List<Func<double, bool>> s, int[] dogs, int[] hands)
        {
            s.Add(TvDo(() => { for (int i = 0; i < 6; i++) if (Get<int[]>(manager, "walkers")[i] == TvMe()) pouch.SendCustomEvent("_WalkOff" + i); }));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers").All(w => w != TvMe()), 5, "detach before setup"));
            s.Add(TvDo(() =>
            {
                holder = nodes[0]; var heading = nodes[1] - nodes[0]; heading.y = 0;
                Networking.LocalPlayer.TeleportTo(holder, Quaternion.LookRotation(heading));
                pouch.SendCustomEvent("_WalkLoopForward");
            }));
            s.Add(TvWait(1));
            for (int k = 0; k < dogs.Length; k++)
            {
                int dog = dogs[k], hand = hands[k];
                s.Add(TvDo(() => { PlaceDog(dog, Networking.LocalPlayer.GetRotation()); pouch.SendCustomEvent((hand == 0 ? "_WalkLeft" : "_WalkRight") + dog); }));
                s.Add(TvWait(.5));
            }
            s.Add(TvUntil(() => { var w = Get<int[]>(manager, "walkers"); var h = Get<int[]>(manager, "hands"); for (int k = 0; k < dogs.Length; k++) if (w[dogs[k]] != TvMe() || h[dogs[k]] != hands[k]) return false; return true; }, 5, "menu attach rejected"));
            s.Add(TvWait(3));
            s.Add(TvDo(TvReset));
        }

        /// <summary>1 回の移動と、§B.4 の契約の判定 (Owner が同じ人)。walk なら着いた後 28 秒、道を正方向へ歩く。</summary>
        static void TvMove(List<Func<double, bool>> s, string evt, int spot, int mask, bool walk)
        {
            var c = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => TvPress(c, evt)));
            s.Add(TvUntil(() => TvFlat(Networking.LocalPlayer.GetPosition(), TvSpot(spot)) < 6f, 12, "player did not arrive at spot " + spot));
            s.Add(TvDo(() => c.arrived = TvNow()));
            s.Add(TvUntil(() => TvAllNear(mask, TvNear), 2, "not every dog within 3.75 m of the hand 2 s after arrival"));
            s.Add(TvUntil(() => TvAllPhase(mask, KH_LeashManager.TravelPhaseDone), 3, "Phase 2 not reached"));
            s.Add(TvUntil(() => Get<int>(manager, "travelResultCode") == KH_LeashManager.TravelArrived, 1, "requester did not switch from 'waiting for dogs' to 'arrived'"));
            s.Add(TvDo(() =>
            {
                var req = Get<int[]>(manager, "travelReq");
                int r = req[TvFirst(mask)];
                if (r == 0 || c.old.Contains(r)) throw new InvalidOperationException("travelReq is not a new id");
                for (int i = 0; i < 6; i++)
                {
                    bool mine = (mask & (1 << i)) != 0;
                    if (mine && req[i] != r) throw new InvalidOperationException("dog" + i + " carries another reqId");
                    if (mine && tvDisc[i] != 1) throw new InvalidOperationException("dog" + i + " position changes=" + tvDisc[i] + " (contract: exactly 1)");
                    if (!mine && tvDisc[i] != 0) throw new InvalidOperationException("unleashed dog" + i + " jumped");
                    if (mine && Get<int>(wander[i], "_travelApplies") - c.applies[i] != 1) throw new InvalidOperationException("dog" + i + " route-state reset count != 1");
                }
                if (Get<int>(manager, "travelResultCode") != KH_LeashManager.TravelArrived || Get<int>(manager, "travelResultSerial") == c.serial) throw new InvalidOperationException("requester did not report arrival");
                var fresh = TvNewReqs(c.logFrom);
                if (fresh.Count != 1 || fresh[0] != r) throw new InvalidOperationException("new reqIds written=" + string.Join(",", fresh));
                TvNote(evt + " req=" + r + " arrived in " + (c.arrived - c.press).ToString("F1") + "s; phase 1->2; one position change per dog; all dogs near the hand <=2s");
                TvTrack(mask);
            }));
            if (!walk) return;
            s.Add(TvWalk(28));
            s.Add(TvDo(() =>
            {
                for (int i = 0; i < 6; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    if (TvCountLogs(c.logFrom, "[KH_Leash] dog" + i + " Return") != 0) throw new InvalidOperationException("dog" + i + " entered Return within 30 s after arrival");
                    if (tvReverse[i] != 0) throw new InvalidOperationException("dog" + i + " reversed on the loop after arrival (reverse steps " + tvReverse[i] + ")");
                    if (tvDisc[i] != 1) throw new InvalidOperationException("dog" + i + " jumped again after arrival");
                }
                TvNote("30 s after arrival: Return 0, reverse 0, forward steps " + string.Join("/", Enumerable.Range(0, 6).Where(i => (mask & (1 << i)) != 0).Select(i => tvForward[i])));
                tvTrackMask = 0;
            }));
        }

        static void TvT2(int mask)
        {
            var s = TvCase("T2-no-answer", true, TvBits(mask)); var c = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugDropTravel", 1); TvNote("simulated: Owner drops every ReqTravel (debugDropTravel=1)"); TvPress(c, "_TravelRiver"); }));
            s.Add(TvUntil(() => Get<int>(manager, "travelResultSerial") != c.serial, 11, "no result"));
            s.Add(TvDo(() =>
            {
                double took = TvNow() - c.press; c.req = Get<int>(manager, "_tvReq");
                TvExpect(Get<int>(manager, "travelResultCode") == KH_LeashManager.TravelNoAnswer, "result is 'did not reach the dogs'");
                TvExpect(took > 7.5 && took < 9.5, "gave up at 8 s (took " + took.ToString("F1") + ")");
                TvExpect(TvCountLogs(c.logFrom, "req=" + c.req + " dropped (debugDropTravel)") == 2, "exactly one resend of the same reqId");
                TvExpect(TvCountLogs(c.logFrom, "travel req=" + c.req + " resend") == 1, "resend at 4 s");
                TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), c.pos) < 1f, "player stayed");
                TvExpect(tvDisc.All(d => d == 0), "no dog position change");
                TvSet("debugDropTravel", 0);
                TvNote("req=" + c.req + " resent once, cancelled at " + took.ToString("F1") + "s; no move");
            }));
        }

        static void TvT3(int mask)
        {
            var s = TvCase("T3-late-answer", true, TvBits(mask)); var c = new TvCtx(); var c2 = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { tvSeenReqs.Clear(); TvSet("debugDelayTravel", 9f); TvNote("simulated: first request written 9 s late (debugDelayTravel=9)"); TvPress(c, "_TravelRiver"); }));
            s.Add(TvUntil(() => Get<int>(manager, "travelResultSerial") != c.serial, 11, "no result for the first request"));
            s.Add(TvDo(() => { c.req1 = Get<int>(manager, "_tvReq"); TvExpect(Get<int>(manager, "travelResultCode") == KH_LeashManager.TravelNoAnswer, "first request gave up"); }));
            s.Add(TvUntil(() => TvNow() - c.press > 9.6, 3, "delayed write"));
            s.Add(TvDo(() => TvExpect(TvCountLogs(c.logFrom, "req=" + c.req1 + " rejected: cancelled earlier") == 1, "late write of the cancelled reqId was refused")));
            s.Add(TvCooldown());
            s.Add(TvDo(() => TvPress(c2, "_TravelRiver")));
            s.Add(TvUntil(() => TvFlat(Networking.LocalPlayer.GetPosition(), TvSpot(KH_LeashManager.SpotRiver)) < 6f, 12, "second request did not move the player"));
            s.Add(TvUntil(() => TvAllPhase(mask, KH_LeashManager.TravelPhaseDone), 4, "second ticket not done"));
            s.Add(TvDo(() =>
            {
                int r2 = Get<int[]>(manager, "travelReq")[TvFirst(mask)];
                TvExpect(r2 != c.req1 && !tvSeenReqs.Contains(c.req1), "only the second reqId ever reached the dogs");
                for (int i = 0; i < 6; i++) TvExpect(tvDisc[i] == ((mask & (1 << i)) != 0 ? 1 : 0), "dog" + i + " moved exactly by the second ticket");
                TvNote("req1=" + c.req1 + " never written (cancel recorded before the late write); req2=" + r2 + " moved once");
            }));
        }

        static void TvT4(int mask)
        {
            var s = TvCase("T4-holder-stays", true, TvBits(mask)); var c = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugSkipTravelTeleport", 1); TvNote("simulated: requester skips TeleportTo after confirmation (debugSkipTravelTeleport=1)"); TvPress(c, "_TravelUpper"); }));
            s.Add(TvUntil(() => Get<int>(manager, "travelResultSerial") != c.serial, 9, "no confirmation"));
            s.Add(TvDo(() => { TvExpect(Get<int>(manager, "travelResultCode") == KH_LeashManager.TravelStayed, "confirmed without moving"); TvExpect(TvAllPhase(mask, KH_LeashManager.TravelPhaseReady), "tickets are Phase 1"); }));
            s.Add(TvWait(16));
            s.Add(TvDo(() =>
            {
                TvExpect(tvDisc.All(d => d == 0), "no dog position change for 15 s");
                TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), c.pos) < 1f, "player stayed");
                TvExpect(TvAllNear(mask, 5f), "dogs stayed with the holder");
            }));
            s.Add(TvUntil(() => TvAllPhase(mask, KH_LeashManager.TravelPhaseNone), 8, "ticket did not expire"));
            s.Add(TvDo(() => TvNote("15 s: no position change; Owner expired the tickets (Phase 0) at ~20 s")));
        }

        static void TvT5(int mask)
        {
            var s = TvCase("T5-owner-change-before-move", true, TvBits(mask)); var c = new TvCtx();
            int dog = TvFirst(mask);
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugSkipTravelTeleport", 1); TvPress(c, "_TravelUpper"); }));
            s.Add(TvUntil(() => Get<int>(manager, "travelResultSerial") != c.serial, 9, "no confirmation"));
            s.Add(TvDo(() =>
            {
                TvExpect(TvAllPhase(mask, KH_LeashManager.TravelPhaseReady), "tickets are Phase 1");
                wander[dog].SetProgramVariable("_wasDogOwner", false); wander[dog].SetProgramVariable("_travelAppliedReq", 0);
                TvNote("simulated: dog" + dog + " state discarded (_wasDogOwner=false, _travelAppliedReq=0) just before the move; the harness performs the requester's TeleportTo");
                var center = Get<Vector3[]>(manager, "travelCenter")[dog]; float yaw = Get<float[]>(manager, "travelYaw")[dog];
                Networking.LocalPlayer.TeleportTo(center, Quaternion.Euler(0, yaw, 0));
                c.arrived = TvNow();
            }));
            s.Add(TvUntil(() => TvAllPhase(mask, KH_LeashManager.TravelPhaseDone), 4, "tickets not done after arrival"));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                for (int i = 0; i < 6; i++) TvExpect(tvDisc[i] == ((mask & (1 << i)) != 0 ? 1 : 0), "dog" + i + " placed exactly once");
                TvExpect(TvAllNear(mask, TvNear), "dogs near the hand");
                TvNote("dog" + dog + " (fresh Owner state) placed once like the others");
            }));
        }

        static void TvT6(int mask)
        {
            // レビュー M3: 完了報告を捨てるだけでは同じ Owner の再送試験になる。適用の後・完了の前に、全頭のローカル状態を
            // 「新しい Owner」相当 (_travelAppliedReq=0・_wasDogOwner=false) へ切り替え、位置を変えずに完了 (Phase 2) することを見る
            var s = TvCase("T6-owner-change-before-done", true, TvBits(mask)); var c = new TvCtx();
            int n = TvBits(mask); int[] reapplies = new int[6];
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugDropTravelDone", n); TvNote("simulated: the first completion report of every dog is lost (debugDropTravelDone=" + n + ")"); TvPress(c, "_TravelRiver"); }));
            s.Add(TvUntil(() => TvFlat(Networking.LocalPlayer.GetPosition(), TvSpot(KH_LeashManager.SpotRiver)) < 6f, 12, "player did not arrive"));
            s.Add(TvUntil(() => TvCountLogs(c.logFrom, "dropped (debugDropTravelDone)") == n, 2, "first completion reports were not all sent"));
            s.Add(TvDo(() =>
            {
                TvExpect(TvAllPhase(mask, KH_LeashManager.TravelPhaseReady), "tickets still Phase 1 after the lost reports");
                for (int i = 0; i < 6; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    reapplies[i] = Get<int>(wander[i], "_travelApplies");
                    wander[i].SetProgramVariable("_travelAppliedReq", 0); wander[i].SetProgramVariable("_wasDogOwner", false);
                }
                TvNote("simulated Owner handover after apply / before done: every dog's local state reset (_travelAppliedReq=0, _wasDogOwner=false)");
                c.pos = Networking.LocalPlayer.GetPosition();
            }));
            s.Add(now =>
            {
                // 本人が L から 3 m 歩く (2.5 秒)。6 m 以内なので新しい Owner も同じ切符で判断する
                var lp = Networking.LocalPlayer; var fw = lp.GetRotation() * Vector3.forward; fw.y = 0; fw.Normalize();
                double t = Math.Min(1.0, (now - tvStepAt) / 2.5);
                lp.TeleportTo(c.pos + fw * (float)(3.0 * t), lp.GetRotation());
                return t >= 1.0;
            });
            s.Add(TvUntil(() => TvAllPhase(mask, KH_LeashManager.TravelPhaseDone), 5, "completion was not reported by the new Owner state"));
            s.Add(TvDo(() =>
            {
                for (int i = 0; i < 6; i++)
                {
                    TvExpect(tvDisc[i] == ((mask & (1 << i)) != 0 ? 1 : 0), "dog" + i + " no second position change");
                    if ((mask & (1 << i)) != 0) TvExpect(Get<int>(wander[i], "_travelApplies") - reapplies[i] == 1, "dog" + i + " re-evaluated the ticket once as the new Owner");
                }
                TvExpect(TvCountLogs(c.logFrom, "moved=False") == n, "new Owner applied without moving (" + TvCountLogs(c.logFrom, "moved=False") + "/" + n + ")");
                TvSet("debugDropTravelDone", 0);
                TvNote("new Owner state re-applied without moving and reported; Phase 2; one position change per dog");
            }));
        }

        static void TvT7(int mask)
        {
            var s = TvCase("T7-leash-changed-while-waiting", true, TvBits(mask)); var c = new TvCtx();
            int dog = 5;
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugDropTravel", 1); TvNote("simulated: Owner drops the request so the requester keeps waiting; dog" + dog + " is detached through the menu"); TvPress(c, "_TravelUpper"); }));
            s.Add(TvWait(1));
            s.Add(TvDo(() => pouch.SendCustomEvent("_WalkOff" + dog)));
            s.Add(TvUntil(() => Get<int>(manager, "travelResultSerial") != c.serial, 3, "no result"));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(manager, "travelResultCode") == KH_LeashManager.TravelChanged, "result 'leashed dogs changed'");
                TvExpect(TvCountLogs(c.logFrom, "[KH_Leash] CancelTravel from player") >= 1, "CancelTravel sent");
                TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), c.pos) < 1f, "player stayed");
                TvExpect(tvDisc.All(d => d == 0), "no dog position change");
                TvSet("debugDropTravel", 0);
                holder = Networking.LocalPlayer.GetPosition(); PlaceDog(dog, Networking.LocalPlayer.GetRotation());
                pouch.SendCustomEvent("_WalkRight" + dog);
            }));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers")[dog] == TvMe(), 5, "reattach"));
            s.Add(TvWait(2));
            s.Add(TvDo(() => { TvReset(); TvNote("cancelled; no move; dog" + dog + " reattached"); }));
        }

        static void TvT8()
        {
            var s = TvCase("T8-other-players-dogs", true, 3); var c = new TvCtx(); var c2 = new TvCtx();
            s.Add(TvDo(() => { for (int i = 3; i < 6; i++) pouch.SendCustomEvent("_WalkOff" + i); }));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers").Skip(3).All(w => w < 0), 5, "detach 3..5"));
            s.Add(TvDo(() => ClientSimMain.SpawnRemotePlayer("KH_TravelRemote")));
            s.Add(TvUntil(() => (tvRemote = TvRemotePlayer()) != null, 5, "remote player spawn"));
            s.Add(TvDo(() =>
            {
                var tp = tvRemote.GetPosition();
                for (int i = 3; i < 6; i++)
                {
                    var p = tp + new Vector3(1.2f + (i - 3) * .5f, 0, -.6f);
                    if (!UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 3, UnityEngine.AI.NavMesh.AllAreas)) throw new InvalidOperationException("No NavMesh near the remote player");
                    roots[i].position = hit.position; var sync = roots[i].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                }
                var w = Get<int[]>(manager, "walkers"); var h = Get<int[]>(manager, "hands");
                for (int i = 3; i < 6; i++) { w[i] = tvRemote.playerId; h[i] = 1; }
                manager.SetProgramVariable("walkers", w); manager.SetProgramVariable("hands", h);
                TvNote("simulated: remote puppet " + tvRemote.playerId + " written into walkers[3..5] (no Udon runs for it)");
            }));
            s.Add(TvWait(3));
            s.Add(TvCooldown());
            s.Add(TvDo(() => { c.oldPhase = (int[])Get<int[]>(manager, "travelPhase").Clone(); TvPress(c, "_TravelUpper"); }));
            s.Add(TvUntil(() => TvFlat(Networking.LocalPlayer.GetPosition(), TvSpot(KH_LeashManager.SpotUpper)) < 6f, 12, "player did not arrive"));
            s.Add(TvUntil(() => TvAllPhase(7, KH_LeashManager.TravelPhaseDone), 4, "own tickets not done"));
            s.Add(TvDo(() =>
            {
                var req = Get<int[]>(manager, "travelReq"); var ph = Get<int[]>(manager, "travelPhase");
                for (int i = 3; i < 6; i++) { TvExpect(tvDisc[i] == 0, "other's dog" + i + " did not move"); TvExpect(req[i] == c.old[i] && ph[i] == c.oldPhase[i], "no ticket for other's dog" + i); }
                for (int i = 0; i < 3; i++) TvExpect(tvDisc[i] == 1, "own dog" + i + " moved once");
                TvNote("own dogs 0..2 moved once; dogs 3..5 (remote holder) untouched");
            }));
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugForgeTravelMask", 1 << 3); TvNote("simulated forgery: requester adds dog3 (remote's) to dogMask"); TvPress(c2, "_TravelRiver"); }));
            s.Add(TvUntil(() => Get<int>(manager, "travelResultSerial") != c2.serial, 11, "no result for forged request"));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(manager, "travelResultCode") == KH_LeashManager.TravelNoAnswer, "forged request never confirmed");
                TvExpect(TvCountLogs(c2.logFrom, "rejected: dogMask") >= 1, "Owner rejected the mask");
                var req = Get<int[]>(manager, "travelReq");
                for (int i = 0; i < 6; i++) TvExpect(req[i] == c2.old[i], "no ticket written for dog" + i);
                TvExpect(tvDisc.All(d => d == 0) && TvFlat(Networking.LocalPlayer.GetPosition(), c2.pos) < 1f, "nothing moved");
                TvSet("debugForgeTravelMask", 0);
                TvNote("forged mask rejected by the Owner; no ticket, no move");
            }));
        }

        static void TvT9()
        {
            var s = TvCase("T9-cancel-and-leave", true, 3); var c = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugDelayTravel", 2f); TvNote("simulated: Owner write delayed 2 s; requester cancels at 1 s (the stage-2 cancel path, _TravelCancel)"); TvPress(c, "_TravelRiver"); }));
            s.Add(TvWait(1));
            s.Add(TvDo(() => { c.req = Get<int>(manager, "_tvReq"); manager.SendCustomEvent("_TravelCancel"); }));
            s.Add(TvUntil(() => TvNow() - c.press > 2.6, 3, "delayed write"));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(manager, "travelResultCode") == KH_LeashManager.TravelCanceledByMe, "result 'cancelled'");
                TvExpect(TvCountLogs(c.logFrom, "req=" + c.req + " rejected: cancelled earlier") == 1, "cancel wins over the late write");
                TvExpect(!Get<int[]>(manager, "travelReq").Contains(c.req), "no ticket for the cancelled reqId");
                TvExpect(tvDisc.All(d => d == 0) && TvFlat(Networking.LocalPlayer.GetPosition(), c.pos) < 1f, "nothing moved");
                // 退出: 相手の犬に準備の切符を載せてから人形を消す → Owner が解除し、切符も消す (§B.2 の 4)
                int id = tvRemote.playerId; var at = Networking.GetServerTimeInMilliseconds();
                var req = Get<int[]>(manager, "travelReq"); var holderIds = Get<int[]>(manager, "travelHolder"); var ph = Get<int[]>(manager, "travelPhase"); var ms = Get<int[]>(manager, "travelAtMs"); var center = Get<Vector3[]>(manager, "travelCenter");
                for (int i = 3; i < 6; i++) { req[i] = id * 65536 + 1; holderIds[i] = id; ph[i] = 1; ms[i] = at; center[i] = TvSpot(KH_LeashManager.SpotGate) + new Vector3(0, 0, 500); }
                foreach (var n in new[] { "travelReq", "travelHolder", "travelPhase", "travelAtMs" }) manager.SetProgramVariable(n, n == "travelReq" ? req : n == "travelHolder" ? holderIds : n == "travelPhase" ? ph : ms);
                manager.SetProgramVariable("travelCenter", center);
                TvNote("simulated departure: Phase 1 tickets written for the remote's dogs 3..5, then RemovePlayer");
                ClientSimMain.RemovePlayer(tvRemote);
            }));
            s.Add(TvUntil(() => { var w = Get<int[]>(manager, "walkers"); var r = Get<int[]>(manager, "travelReq"); var p = Get<int[]>(manager, "travelPhase"); for (int i = 3; i < 6; i++) if (w[i] >= 0 || r[i] != 0 || p[i] != 0) return false; return true; }, 4, "departed holder's dogs not released / tickets not cleared"));
            s.Add(TvDo(() => { tvRemote = null; TvNote("leave: dogs 3..5 released and tickets cleared. Join-side T9 (ReqJoin/AnsJoin) is stage 3."); }));
        }

        static void TvT11(int mask)
        {
            // 設計書 §D.5: 連打は門番で弾き、理由が分かる表示を出す。ClientSim は Owner = 本人なので切符が同じフレームで書かれ、
            // 1 回目の押下はその場で到着まで進む (arrived in 0.0s)。2 回目以降は「処理中」ではなく「クールダウン中」になるのが正しい
            // (_TravelBegin は待ちの間 = 処理中、TeleportTo の後 = クールダウン、の 2 段で門を閉じる)。実通信では処理中の側が効く。
            // したがって合格条件は「各押下で処理中かクールダウン中の表示が出て、切符が二重に出ない」
            var s = TvCase("T11-mash", false, TvBits(mask)); var c = new TvCtx();
            var shown = new List<string>();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { c.busy = Get<int>(manager, "_tvBusyRejects"); TvPress(c, "_TravelRiver"); }));
            for (int k = 1; k < 5; k++)
            {
                int kk = k;
                s.Add(TvWait(.2));
                s.Add(TvDo(() =>
                {
                    pouch.SendCustomEvent("_TravelRiver");
                    string key = Get<string[]>(pouch, "_hsNoteKeys")[0];
                    shown.Add(key);
                    TvExpect(key == "course.travel.busy" || key == "course.travel.cooling", "press " + (kk + 1) + " shows 'please wait' or the cooldown (shown " + key + ")");
                }));
            }
            s.Add(TvUntil(() => TvFlat(Networking.LocalPlayer.GetPosition(), TvSpot(KH_LeashManager.SpotRiver)) < 6f, 12, "player did not arrive"));
            s.Add(TvUntil(() => TvAllPhase(mask, KH_LeashManager.TravelPhaseDone), 4, "tickets not done"));
            s.Add(TvDo(() =>
            {
                var fresh = TvNewReqs(c.logFrom);
                TvExpect(fresh.Count == 1, "exactly one new reqId (" + string.Join(",", fresh) + ")");
                for (int i = 0; i < 6; i++) TvExpect(tvDisc[i] == ((mask & (1 << i)) != 0 ? 1 : 0), "dog" + i + " moved once");
                int busy = Get<int>(manager, "_tvBusyRejects") - c.busy;
                TvNote("5 presses in 1 s: one reqId, one position change per dog; presses 2-5 showed " + string.Join(",", shown) + " (busy rejects " + busy + "; same-frame Owner write makes the cooldown gate answer in ClientSim)");
            }));
        }

        // ============================================================
        // 実行
        // ============================================================

        static void TravelTick(double now)
        {
            try
            {
                if (tvScripts == null || caseIndex < 0 || caseIndex >= tvScripts.Count) throw new InvalidOperationException("Travel script lost (domain reload?)");
                if (tvStep == 0 && tvCaseAt != Current.startedAt) { tvCaseAt = Current.startedAt; tvStepAt = now; tvSeenReqs.Clear(); TvReset(); tvObsValid = false; tvTrackMask = 0; }
                ObserveTravel(now);
                if (now - Current.startedAt > 420) throw new TimeoutException("Travel case deadline (420 s)");
                var steps = tvScripts[caseIndex];
                while (tvStep < steps.Count)
                {
                    if (!steps[tvStep](now)) return;
                    tvStep++; tvStepAt = now;
                }
                Current.status = "PASS"; Current.endedAt = now; Current.reason = string.Join(" | ", tvReport.cases[caseIndex].notes);
                tvReport.cases[caseIndex].status = "PASS";
                WriteTravel(); Save();
                tvStep = 0;
                if (caseIndex + 1 == report.cases.Count)
                    End(report.full ? "TRAVEL_OBSERVED" : "TRAVEL_QUICK_OBSERVED", "Local ClientSim travel cases passed (same-Owner contract; handover/late/forged/leave are simulated). Not network, device or PCVR acceptance.");
                else Next();
            }
            catch (Exception e)
            {
                if (tvReport != null && caseIndex >= 0 && caseIndex < tvReport.cases.Count) { tvReport.cases[caseIndex].status = "FAIL"; tvReport.cases[caseIndex].notes.Add("FAIL at step " + tvStep + ": " + e.Message); }
                try { WriteTravel(); } catch { }
                throw;
            }
        }

        static void ObserveTravel(double now)
        {
            // Same rule as the main Observe (lastObserveAt starts at the case, maxObservationGap is recorded): the first
            // observation of a case only sets the reference time and positions, so no gap or position change is counted.
            if (!tvObsValid || tvPrev == null) { TvReset(); tvLastObs = now; tvObsValid = true; return; }
            double gap = now - tvLastObs; tvLastObs = now;
            Current.maxObservationGap = Math.Max(Current.maxObservationGap, gap);
            if (gap > .75) throw new InvalidOperationException("Observation gap " + gap.ToString("F2") + " s too large to count position changes");
            var req = Get<int[]>(manager, "travelReq");
            for (int i = 0; i < 6; i++)
            {
                var p = roots[i].position;
                if (Vector3.Distance(tvPrev[i], p) > TvDisc) tvDisc[i]++;
                tvPrev[i] = p;
                if (req[i] != 0) tvSeenReqs.Add(req[i]);
                if ((tvTrackMask & (1 << i)) == 0) continue;
                int next = Get<int>(wander[i], "_routeNext");
                if (next >= 0 && tvPrevNext[i] >= 0 && next != tvPrevNext[i])
                {
                    int delta = (next - tvPrevNext[i] + nodes.Length) % nodes.Length;
                    if (delta < nodes.Length / 2) tvForward[i] += delta; else tvReverse[i] += nodes.Length - delta;
                }
                if (next >= 0) tvPrevNext[i] = next;
            }
        }

        static void TvReset()
        {
            tvPrev = roots.Select(r => r.position).ToArray(); tvDisc = new int[6];
            if (tvPrevNext == null) { tvPrevNext = new int[6]; tvForward = new int[6]; tvReverse = new int[6]; }
        }

        static void TvTrack(int mask)
        {
            tvTrackMask = mask; tvPrevNext = Enumerable.Repeat(-1, 6).ToArray(); tvForward = new int[6]; tvReverse = new int[6];
        }

        static void TravelLog(string condition, string stack, LogType type)
        {
            if (report == null || !report.travelOnly || !EditorApplication.isPlaying) return;
            if (condition.StartsWith("[KH_Leash]")) tvLogs.Add(condition);
        }

        static void WriteTravel()
        {
            if (report == null || tvReport == null) return;
            Directory.CreateDirectory(Dir + "/" + report.runId);
            File.WriteAllText(Dir + "/" + report.runId + "/travel.json", JsonUtility.ToJson(tvReport, true));
        }

        // ---- 手順の部品 ----
        static Func<double, bool> TvDo(Action a) { return now => { a(); return true; }; }
        static Func<double, bool> TvWait(double seconds) { return now => now - tvStepAt >= seconds; }
        static Func<double, bool> TvUntil(Func<bool> condition, double limit, string what)
        { return now => { if (condition()) return true; if (now - tvStepAt > limit) throw new TimeoutException(what + " (" + limit + " s)"); return false; }; }
        static Func<double, bool> TvCooldown()
        {
            return TvUntil(() => Get<float>(manager, "_tvCooldownUntil") <= Time.time && Get<int>(manager, "_tvState") == 0, 15, "requester cooldown");
        }
        static Func<double, bool> TvWalk(double seconds)
        {
            return now =>
            {
                var lp = Networking.LocalPlayer;
                if (now - tvStepAt < 0.0001 || tvWalkLast < tvStepAt)
                {
                    tvWalkPos = lp.GetPosition(); tvWalkNode = Closest(tvWalkPos); tvWalkLast = now; return false;
                }
                float step = Mathf.Min((float)(now - tvWalkLast), .1f) * 1.2f; tvWalkLast = now;
                int next = (tvWalkNode + 1) % nodes.Length;
                var before = tvWalkPos; tvWalkPos = Vector3.MoveTowards(tvWalkPos, nodes[next], step);
                if (Vector3.Distance(tvWalkPos, nodes[next]) < .001f) tvWalkNode = next;
                var heading = tvWalkPos - before; heading.y = 0;
                lp.TeleportTo(tvWalkPos, heading.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(heading) : lp.GetRotation());
                return now - tvStepAt >= seconds;
            };
        }

        static void TvPress(TvCtx c, string evt)
        {
            c.press = TvNow(); c.serial = Get<int>(manager, "travelResultSerial"); c.logFrom = tvLogs.Count; c.pos = Networking.LocalPlayer.GetPosition();
            c.old = (int[])Get<int[]>(manager, "travelReq").Clone();
            c.applies = wander.Select(w => Get<int>(w, "_travelApplies")).ToArray();
            TvReset();
            pouch.SendCustomEvent(evt);
        }

        static void TvSet(string name, int value) { manager.SetProgramVariable(name, value); }
        static void TvSet(string name, float value) { manager.SetProgramVariable(name, value); }
        static void TvExpect(bool ok, string what) { if (!ok) throw new InvalidOperationException("Expected: " + what); }
        static void TvNote(string text) { tvReport.cases[caseIndex].notes.Add(text); }
        static double TvNow() { return EditorApplication.timeSinceStartup; }
        static int TvMe() { return Networking.LocalPlayer.playerId; }
        static int TvBits(int mask) { int n = 0; for (int i = 0; i < 6; i++) if ((mask & (1 << i)) != 0) n++; return n; }
        static int TvFirst(int mask) { for (int i = 0; i < 6; i++) if ((mask & (1 << i)) != 0) return i; return 0; }
        static Vector3 TvSpot(int spot) { return Get<Vector3[]>(manager, "spotPositions")[spot]; }
        static float TvFlat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }
        static bool TvAllNear(int mask, float limit)
        {
            var p = Networking.LocalPlayer.GetPosition();
            for (int i = 0; i < 6; i++) if ((mask & (1 << i)) != 0 && TvFlat(roots[i].position, p) > limit) return false;
            return true;
        }
        static bool TvAllPhase(int mask, int phase)
        {
            var ph = Get<int[]>(manager, "travelPhase");
            for (int i = 0; i < 6; i++) if ((mask & (1 << i)) != 0 && ph[i] != phase) return false;
            return true;
        }
        static int TvCountLogs(int from, string contains) { int n = 0; for (int i = from; i < tvLogs.Count; i++) if (tvLogs[i].Contains(contains)) n++; return n; }
        static List<int> TvNewReqs(int from)
        {
            var set = new List<int>();
            for (int i = from; i < tvLogs.Count; i++)
            {
                var m = Regex.Match(tvLogs[i], @"ReqTravel from player \d+ req=(\d+) dogs=\d+ phase=1");
                if (m.Success) { int r = int.Parse(m.Groups[1].Value); if (!set.Contains(r)) set.Add(r); }
            }
            return set;
        }
        static VRCPlayerApi TvRemotePlayer()
        {
            var all = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; VRCPlayerApi.GetPlayers(all);
            return all.FirstOrDefault(p => p != null && !p.isLocal);
        }
    }
}
