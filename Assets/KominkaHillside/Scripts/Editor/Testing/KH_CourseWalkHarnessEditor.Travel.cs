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
        [MenuItem("Tools/KominkaHillside/Course/82b - Walk Harness Travel RC cases (T6b, T12, T9 new)")]
        public static void TravelRc() { BeginTravel(false, true); }

        static void BeginTravel(bool full, bool rc = false, bool call = false)
        {
            Begin(full);
            report.travelOnly = true;
            report.travelRc = rc;
            report.travelCall = call;   // IMP-WLD-002 Course/82c (KH_CourseWalkHarnessEditor.Call.cs)
            report.deadline = EditorApplication.timeSinceStartup + (full ? 2700 : rc ? 1200 : call ? 1800 : 600);   // 破壊監査 RC-01/02/06/07 のケースで 6 本増えた
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
        [Serializable] class TravelReport
        {
            public string runId; public List<TravelCase> cases = new List<TravelCase>();
            public int casesPass, casesNotRun, casesFail;   // WriteTravel が数える (実施して PASS / 未実施 NOT_RUN / FAIL)
        }
        static bool tvCaseNotRun;   // 82c C7-wet: このケースは実施しなかった (TravelTick が NOT_RUN にする)

        /// <summary>82c の件数の要約 (End の reason の先頭)。例: "cases 30: pass 29, not run 1 (C7-wet), fail 0"。</summary>
        static string TvCountLine()
        {
            if (tvReport == null) return "cases ?";
            var nr = tvReport.cases.Where(x => x.status == "NOT_RUN").Select(x => x.name).ToArray();
            return "cases " + tvReport.cases.Count + ": pass " + tvReport.cases.Count(x => x.status == "PASS") + ", not run " + nr.Length + (nr.Length > 0 ? " (" + string.Join(",", nr) + ")" : "")
                + ", fail " + tvReport.cases.Count(x => x.status == "FAIL");
        }

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
        static List<string> tvJumpNotes = new List<string>();
        // 観測間隔は 3 つで測る: 壁時計 (EditorApplication)、ゲーム時間 (Time.time = 犬が歩幅に掛ける時間)、フレーム数
        static int tvLastFrameObs;
        static float tvLastGameTime, tvLastRealtime;   // 2 回目以降の跳びの証拠 (位置・時刻・その時の犬の状態)
        static VRCPlayerApi tvRemote;
        static List<VRCPlayerApi> tvHist;   // T9-join-history-kept の在室中 16 人
        static List<int> tvHistValid; static int tvHistX;
        // 段階 2: 暗転の観測 (KH_TravelFade の _alpha と、プレイヤーの 5 m 以上の跳び)
        static VRC.Udon.UdonBehaviour tvFade;
        static float tvPrevAlpha, tvMaxAlpha, tvJumpAlpha = -1f, tvJumpPrevAlpha = -1f;
        static double tvJumpAt = -1, tvLastDarkAt = -1, tvJumpDarkGap = -1, tvFadeBackAt = -1;
        // 段階 3: 合流の相手 (人形) を歩かせる
        static bool tvPuppetWalk;
        static int tvPuppetNode;
        static Vector3 tvPuppetPos;
        static double tvPuppetLast;
        static VRC.Udon.UdonBehaviour tvBox;
        static int tvJumps;
        static Vector3 tvPrevPlayer;
        static bool tvPrevPlayerValid;
        static int tvWalkNode;
        static Vector3 tvWalkPos;
        const float TvDisc = 1.5f, TvNear = 3.75f + 0.45f; // 手 = 体から横 0.23 m・前 0.32 m (Desktop の _HandPosition)

        class TvCtx { public double press, arrived, walkStart = -1; public int serial, logFrom, req, req1, busy, spot; public int[] old, oldPhase, applies, disc; public Vector3 pos, center; public string text; }

        // ============================================================
        // ケースの組み立て
        // ============================================================

        static void MakeTravelCases()
        {
            Application.logMessageReceived -= TravelLog; Application.logMessageReceived += TravelLog;
            tvStep = 0; tvCaseAt = -1; tvPrev = null; tvObsValid = false; tvBox = null; tvPuppetWalk = false; tvJumpNotes.Clear(); tvTrackMask = 0; tvRemote = null; tvWalkLast = 0;
            tvFade = UnityEngine.Object.FindObjectsOfType<VRC.Udon.UdonBehaviour>().FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_TravelFade");
            if (tvFade == null) throw new InvalidOperationException("KH_TravelFade not in the scene (run Course/84)");
            tvLogs.Clear(); tvScripts = new List<List<Func<double, bool>>>(); tvReport = new TravelReport { runId = report.runId };
            var spots = Get<Vector3[]>(manager, "spotPositions");
            var warp = Get<bool[]>(manager, "spotWarpEnabled");
            if (spots == null || spots.Length != 6 || warp == null || warp.Count(w => w) != 2 || !warp[KH_LeashManager.SpotUpper] || !warp[KH_LeashManager.SpotRiver])
                throw new InvalidOperationException("Travel spots not built (run Course/80 and 81 first)");
            var configs = report.travelRc || report.travelCall
                ? new[] { (report.travelCall ? "C-setup-dogs-3-L3" : "RC-setup-dogs-3-L3", new[] { 0, 1, 2 }, new[] { 0, 0, 0 }) }
                : report.full
                ? new[] { ("T1-dogs-1-L1", new[] { 0 }, new[] { 0 }), ("T1-dogs-3-L3", new[] { 0, 1, 2 }, new[] { 0, 0, 0 }), ("T1-dogs-3-L2R1", new[] { 0, 1, 2 }, new[] { 0, 0, 1 }), ("T1-dogs-6-L3R3", new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 0, 0, 1, 1, 1 }) }
                : new[] { ("T1-dogs-1-L1", new[] { 0 }, new[] { 0 }) };
            int mask = 0;
            foreach (var (name, dogs, hands) in configs)
            {
                var s = TvCase(name, false, dogs.Length);
                mask = dogs.Aggregate(0, (a, d) => a | (1 << d));
                TvSetup(s, dogs, hands);
                TvMove(s, "_TravelUpper", KH_LeashManager.SpotUpper, mask, true);
                if (report.travelRc) { s.Add(TvDo(() => TvNote("Course/82b setup: dogs 0-2 on the left hand, one move to the upper plaza (same attach state as the full run before the RC cases)"))); continue; }
                if (report.travelCall) { s.Add(TvDo(() => TvNote("Course/82c setup: dogs 0-2 on the left hand, one move to the upper plaza"))); continue; }
                TvMove(s, "_TravelRiver", KH_LeashManager.SpotRiver, mask, true);
                TvMove(s, "_TravelUpper", KH_LeashManager.SpotUpper, mask, true);
                s.Add(TvDo(() => TvNote("non-warp spots: spotWarpEnabled has only upper/river; the menu has only _TravelUpper/_TravelRiver. Their common checks 1-4 are certified statically by Course/81.")));
            }
            if (report.travelRc)
            {
                TvRcCases();
                TvSwimResidue();   // 82b だけの末尾 (82 の件数は変えない)
                return;
            }
            if (report.travelCall)
            {
                MakeCallCases();
                return;
            }
            if (report.full)
            {
                TvT2(mask); TvT3(mask); TvT4(mask); TvT5(mask); TvT6(mask); TvT7(mask); TvT8(); TvT9();
                TvT10(); TvT9Join();
                // 破壊監査 2026-09-28 (T8/T9 の後は犬 0..2 だけが自分に繋がっている)
                TvRcCases();
            }
            TvT11(report.full ? 7 : 1);
        }

        /// <summary>破壊監査 2026-09-28 の新規ケース (82 の後半と 82b で共通。前提: 犬 0..2 だけが自分に繋がっている)。</summary>
        static void TvRcCases()
        {
            TvT6b(7); TvT12WalkIn(7); TvT12MovedLate(7); TvT12MovedFirstLost(7); TvT12Join(7); TvT9JoinFastCancel(); TvT9JoinHistory();
        }

        /// <summary>浮きの高さの取り残し (_swimYValid) が消えるか (82b の末尾、2 件とも模擬)。犬 0 を陸でリードに繋いで落ち着かせ、
        /// 補間の途中でリードが制御を取った状態をヒープの書き込みで作る (_swimYValid = true、_waterSwimY = 根の高さ + 0.3)。
        /// - T-swimresidue-ticket: 書き込みと同じ手順 (同じエディタの Tick) で T1 と同じワープを押す。切符の適用 (KD_DogWanderController.HillsideTravel の
        ///   状態の初期化の WaterClearFloat) で消えることを見るつもりだが、押してから Phase 4 まで (暗転) 何フレームも犬の Update が走り、
        ///   毎フレーム リードの処理 (UpdateHillsideLeash の非 Owner 排除の直後の WaterClearFloat) が切符の適用より先に走る。エディタからの書き込みを
        ///   その間に差し込めないので、**どちらの経路で消えたかは区別しない** (notes にも書く)。切符が適用されたこと (適用 1 回・位置の変更 1 回) は見る。
        /// - T-swimresidue-leash: 書き込みの後ワープせず 3 フレーム → リードの経路 (2) だけを見る。(2) が無いコードでは FAIL が正しい。
        /// 合格 (両方): _swimYValid false、_waterSwimming false、_WaterFloating() false (= 両者の OR、Udon の戻り値は読まない)、地面との差 ≤ 0.2 m、waterPose 0。
        /// leash を後に置く (82b は FAIL でその場で止まるため、先の ticket の結果を残す)。</summary>
        static void TvSwimResidue()
        {
            foreach (bool ticket in new[] { true, false })
            {
                string tag = ticket ? "T-swimresidue-ticket" : "T-swimresidue-leash";
                var s = TvCase(tag, true, 1); var c = new TvCtx(); int frame = 0, mask = 0;
                s.Add(TvCooldown());
                s.Add(TvDo(() =>
                {
                    holder = nodes[0]; var heading = nodes[1] - nodes[0]; heading.y = 0;
                    Networking.LocalPlayer.TeleportTo(holder, Quaternion.LookRotation(heading));
                    if (Get<int[]>(manager, "walkers")[0] != TvMe()) { PlaceDog(0, Networking.LocalPlayer.GetRotation()); pouch.SendCustomEvent("_WalkLeft0"); }
                }));
                s.Add(TvUntil(() => Get<int[]>(manager, "walkers")[0] == TvMe(), 5, tag + ": dog 0 not leashed to me"));
                s.Add(TvWait(2));
                s.Add(TvUntil(() => TvAllNear(TvMask(), TvNear), 30, tag + ": dogs did not catch up on land"));
                s.Add(TvWait(2));
                s.Add(TvDo(() =>
                {
                    float gap = CwGroundGap(0);
                    TvExpect(CwPose(0) == 0 && !Get<bool>(wander[0], "_swimYValid") && !Get<bool>(wander[0], "_waterSwimming") && gap >= 0f && gap <= .2f, tag + ": dog 0 settled on dry land before the write (gap " + gap.ToString("F3") + ")" + CwDogState(0));
                    mask = TvMask();
                    wander[0].SetProgramVariable("_swimYValid", true);
                    wander[0].SetProgramVariable("_waterSwimY", roots[0].position.y + .3f);
                    frame = Time.frameCount;
                    if (ticket) TvPress(c, TvFarEvt(c));   // 同じ Tick のうちにワープを押す
                    TvNote("simulated: heap write _swimYValid=true, _waterSwimY=root.y+0.3 on leashed dog 0 at frame " + frame + (ticket ? ", warp pressed in the same editor tick (mask " + mask + ")" : ", no warp"));
                }));
                if (ticket)
                {
                    s.Add(TvUntil(() => TvFlat(Networking.LocalPlayer.GetPosition(), TvSpot(c.spot)) < 6f, 12, tag + ": player did not arrive at spot " + c.spot));
                    s.Add(TvUntil(() => TvAllPhase(mask, KH_LeashManager.TravelPhaseDone), 5, tag + ": Phase 2 not reached"));
                    s.Add(TvWait(1));
                    s.Add(TvDo(() =>
                    {
                        TvExpect(TvAppliesSince(c, 0) == 1 && tvDisc[0] == 1, tag + ": dog 0 applied the ticket once and moved once (applies " + TvAppliesSince(c, 0) + ", moves " + tvDisc[0] + ")");
                        TvSwimResidueCheck(tag);
                        TvNote("which path cleared it is NOT distinguished: between the press and Phase 4 the dog's Update runs many frames, and in each the leash takeover (UpdateHillsideLeash) runs before the ticket apply; the ticket was applied once (frames " + frame + " -> " + Time.frameCount + ")");
                    }));
                }
                else
                {
                    s.Add(TvUntil(() => Time.frameCount >= frame + 3, 2, tag + ": 3 frames did not pass"));
                    s.Add(TvDo(() =>
                    {
                        TvSwimResidueCheck(tag);
                        TvNote("leash path only: no warp, cleared within " + (Time.frameCount - frame) + " frames of the write");
                    }));
                }
            }
        }

        static void TvSwimResidueCheck(string tag)
        {
            bool swimY = Get<bool>(wander[0], "_swimYValid"), swim = Get<bool>(wander[0], "_waterSwimming");
            float gap = CwGroundGap(0); int pose = CwPose(0);
            string st = CwDogState(0);
            TvExpect(!swimY, tag + ": _swimYValid false" + st);
            TvExpect(!swim, tag + ": _waterSwimming false" + st);
            TvExpect(!(swim || swimY), tag + ": _WaterFloating() false (_waterSwimming || _swimYValid)" + st);
            TvExpect(gap >= 0f && gap <= .2f, tag + ": root within 0.2 m of the ground (" + gap.ToString("F3") + ")" + st);
            TvExpect(pose == 0, tag + ": waterPose 0" + st);
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
            // 段階 2 (§D.1): 暗転 → 黒 → 移動 → 明転。W2 レビュー M5: 期限は跳びを観測した時刻から数える。
            // 観測は毎フレームとは限らないので「跳びの直前 0.3 秒以内に alpha >= 0.99 の観測がある」で判定する (描画そのものは見ていない)
            s.Add(TvUntil(() => tvFadeBackAt >= 0, 3, "screen did not fade back in after the move"));
            s.Add(TvDo(() =>
            {
                TvExpect(tvJumps == 1, "exactly one player jump (" + tvJumps + ")");
                TvExpect(tvMaxAlpha >= 0.99f, "fade reached black (max alpha observed " + tvMaxAlpha.ToString("F3") + ")");
                TvExpect(tvJumpDarkGap >= 0 && tvJumpDarkGap <= 0.3, "alpha >= 0.99 observed within 0.3 s before the move (gap " + tvJumpDarkGap.ToString("F2") + " s)");
                TvExpect(tvFadeBackAt - tvJumpAt <= 1.5, "back to alpha 0 within 1.5 s of the move (" + (tvFadeBackAt - tvJumpAt).ToString("F2") + " s)");
                TvNote("fade: alpha >= 0.99 observed " + tvJumpDarkGap.ToString("F2") + " s before the move, alpha 0 again " + (tvFadeBackAt - tvJumpAt).ToString("F2") + " s after");
            }));
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
                    if (tvDisc[i] != 1) throw new InvalidOperationException("dog" + i + " jumped again after arrival: " + string.Join(" | ", tvJumpNotes));
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
            s.Add(TvUntil(() => TvAllPhase(mask, KH_LeashManager.TravelPhaseNone), 14, "ticket did not expire (Owner expires at ~20 s after the write; 12 s wait + 14 s)"));
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
            // RC-01: 犬は本人の移動済み (Phase 4) の後だけ適用する。代わりに TeleportTo したハーネスが、要求する人の移動済みも代わりに送る
            s.Add(TvWait(.3));
            s.Add(TvDo(() => { TvExpect(TvAllPhase(mask, KH_LeashManager.TravelPhaseReady), "no dog applied before the moved notice"); manager.SendCustomEvent("_DebugTravelMovedNow"); }));
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
                TvExpect(TvAllPhase(mask, KH_LeashManager.TravelPhaseMoved), "tickets still Phase 4 (moved, not done) after the lost reports");
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

        // ============================================================
        // 段階 3: 合流 (T10 着地点、T9 の合流側)。相手は ClientSim の人形 (Udon は動かない) なので、
        // 自分の受付箱の debugJoinLoopback で相手の箱を使わずに待ちへ入り、_DebugAnswerNow で相手の答えを模す
        // ============================================================

        static VRC.Udon.UdonBehaviour TvMyBox()
        {
            if (tvBox != null) return tvBox;
            var objs = Networking.GetPlayerObjects(Networking.LocalPlayer);
            if (objs != null)
                foreach (var o in objs)
                {
                    if (o == null) continue;
                    var u = o.GetComponents<VRC.Udon.UdonBehaviour>().FirstOrDefault(b => b.programSource != null && b.programSource.name == "KH_TravelMailbox");
                    if (u != null) { tvBox = u; return u; }
                }
            return null;
        }

        static void TvPlacePuppet(Vector3 p, Vector3 facing)
        {
            var cs = tvRemote.GetClientSimPlayer();
            cs.transform.position = p; facing.y = 0;
            if (facing.sqrMagnitude > 1e-6f) cs.transform.rotation = Quaternion.LookRotation(facing);
        }

        static void TvJoinPress(TvCtx c)
        {
            var box = TvMyBox();
            c.press = TvNow(); c.serial = Get<int>(box, "joinResultSerial"); c.logFrom = tvLogs.Count; c.pos = Networking.LocalPlayer.GetPosition();
            c.old = (int[])Get<int[]>(manager, "travelReq").Clone();
            c.applies = wander.Select(w => Get<int>(w, "_travelApplies")).ToArray();
            TvReset();
            pouch.SendCustomEvent("_TravelOpenMap"); pouch.SendCustomEvent("_TravelModePeople"); pouch.SendCustomEvent("_JoinPick0");
            c.text = TvGoLabel();   // [合流をたのむ] の表示 (RC-06 の比較用)
            pouch.SendCustomEvent("_TravelGo");
        }

        /// <summary>実行ボタンの表示 (TMP の text)。[人] のときは [合流をたのむ] (IMP-WLD-002 で [ここへワープ] は [地点] のときだけになった)。</summary>
        static string TvGoLabel()
        {
            object mode; string field = "travelGoLabel";
            if (pouch.TryGetProgramVariable("_travelMode", out mode) && mode is int m && m == 1 && pouch.TryGetProgramVariable("travelAskLabel", out object ask) && ask is Component ac && ac != null) field = "travelAskLabel";
            object o; if (!pouch.TryGetProgramVariable(field, out o) || !(o is Component comp) || comp == null) return "";
            var p = comp.GetType().GetProperty("text"); return p == null ? "" : (string)p.GetValue(comp);
        }

        /// <summary>今の位置から 20 m 以上離れた方のワープ地点 (イベント名)。c.spot に地点を入れる。</summary>
        static string TvFarEvt(TvCtx c)
        {
            var p = Networking.LocalPlayer.GetPosition();
            c.spot = TvFlat(p, TvSpot(KH_LeashManager.SpotUpper)) >= 20f ? KH_LeashManager.SpotUpper : KH_LeashManager.SpotRiver;
            return c.spot == KH_LeashManager.SpotUpper ? "_TravelUpper" : "_TravelRiver";
        }

        static int TvFirstLog(int from, string contains) { for (int i = from; i < tvLogs.Count; i++) if (tvLogs[i].Contains(contains)) return i; return -1; }
        static int TvAppliesSince(TvCtx c, int dog) { return Get<int>(wander[dog], "_travelApplies") - c.applies[dog]; }
        static bool TvNoApplies(TvCtx c, int mask) { for (int i = 0; i < 6; i++) if ((mask & (1 << i)) != 0 && TvAppliesSince(c, i) != 0) return false; return true; }

        /// <summary>合流の相手 (人形) を、本人の正面 ahead m に同じ向きで置く。着地点は相手の後ろ 3.5 m = 本人から ahead - 3.5 m (RC-01 の 3〜5 m)。</summary>
        static void TvPuppetAhead(float ahead)
        {
            int n = (Closest(TvSpot(KH_LeashManager.SpotPlaza)) + 8) % nodes.Length;
            var fwd = nodes[(n + 1) % nodes.Length] - nodes[n]; fwd.y = 0; fwd.Normalize();
            holder = nodes[n];
            Networking.LocalPlayer.TeleportTo(nodes[n], Quaternion.LookRotation(fwd));
            TvPlacePuppet(nodes[n] + fwd * ahead, fwd);
        }

        static void TvAnswer(int code)
        {
            var box = TvMyBox();
            box.SetProgramVariable("debugAnswerCode", code); box.SendCustomEvent("_DebugAnswerNow");
        }

        static void TvT10()
        {
            var s = TvCase("T10-join-landing", true, 3);
            s.Add(TvDo(() => ClientSimMain.SpawnRemotePlayer("KH_TravelJoinTarget")));
            s.Add(TvUntil(() => (tvRemote = TvRemotePlayer()) != null, 5, "remote player spawn"));
            s.Add(TvUntil(() => TvMyBox() != null, 5, "local KH_TravelMailbox (VRCPlayerObject) not found"));
            s.Add(TvDo(() => { TvMyBox().SetProgramVariable("debugJoinLoopback", 1); TvNote("simulated: the target is a ClientSim puppet (no Udon); requests loop back (debugJoinLoopback=1) and the answer is injected with _DebugAnswerNow"); }));
            // 条件ごとに相手を置く: 通常 / 橋の上 / 川の中 / 歩行中 (2 m/s) / コースの外 (安全な点が無い)
            var conds = new (string name, int expect)[] { ("near", KH_LeashManager.JoinReasonNear), ("bridge", KH_LeashManager.JoinReasonBridge), ("water", KH_LeashManager.JoinReasonWater), ("walking", KH_LeashManager.JoinReasonBehind), ("offcourse", -1) };
            foreach (var (name, expect) in conds)
            {
                var c = new TvCtx();
                s.Add(TvCooldown());
                s.Add(TvDo(() =>
                {
                    tvPuppetWalk = false;
                    var bz = Get<float[]>(manager, "travelBridgeZs"); var bx = Get<float[]>(manager, "travelBridgeXs");
                    var rx = Get<float[]>(manager, "travelRiverXs"); float z0 = Get<float>(manager, "travelRiverZ0"), step = Get<float>(manager, "travelRiverStep");
                    int plaza = Closest(TvSpot(KH_LeashManager.SpotPlaza));
                    if (name == "near") { int n = (plaza + 8) % nodes.Length; TvPlacePuppet(nodes[n], nodes[(n + 1) % nodes.Length] - nodes[n]); }
                    else if (name == "bridge") { int n = Closest(new Vector3(bx[1], 0, bz[1])); TvPlacePuppet(nodes[n], Vector3.forward); }
                    else if (name == "water") { float z = 285f; /* 川辺の地点の近く。前の「橋」の条件の着地点 (上流の橋の手前) から離す — 505 だと本人がもう着地点のそばにいて「5 m 以上動いた」が成り立たなかった */ int k = Mathf.Clamp(Mathf.RoundToInt((z - z0) / step), 0, rx.Length - 1); int n = Closest(new Vector3(rx[k], 0, z)); TvPlacePuppet(new Vector3(rx[k], nodes[n].y - 1f, z), Vector3.forward); }
                    else if (name == "walking") { tvPuppetNode = (plaza + 20) % nodes.Length; tvPuppetPos = nodes[tvPuppetNode]; tvPuppetLast = TvNow(); tvPuppetWalk = true; TvPlacePuppet(tvPuppetPos, nodes[(tvPuppetNode + 1) % nodes.Length] - tvPuppetPos); }
                    else TvPlacePuppet(new Vector3(-55f, 0f, 715f), Vector3.forward);
                }));
                s.Add(TvWait(1.5));   // 相手の速さ (0.5 秒ごと) が決まるまで
                s.Add(TvDo(() => TvJoinPress(c)));
                s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") >= 0, 2, "join request did not start (" + name + ")"));
                s.Add(TvWait(1.1));   // 受付箱が相手の速さを 0.5 秒ごとに測る (待っている間だけ)
                s.Add(TvDo(() => { c.pos = Networking.LocalPlayer.GetPosition(); TvAnswer(KH_TravelMailbox.CodeYes); }));
                if (expect < 0)
                {
                    s.Add(TvUntil(() => Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.JoinNoSafe, 2, "expected 'no safe spot' (" + name + ")"));
                    s.Add(TvWait(1));
                    s.Add(TvDo(() =>
                    {
                        TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), c.pos) < 1f && tvDisc.All(d => d == 0), "nothing moved (" + name + ")");
                        TvNote(name + ": no safe landing within 30 m -> no move, 'no safe spot' shown");
                    }));
                    continue;
                }
                s.Add(TvUntil(() => TvFlat(Networking.LocalPlayer.GetPosition(), c.pos) > 5f, 12, "player did not move (" + name + ")"));
                s.Add(TvUntil(() => TvAllPhase(7, KH_LeashManager.TravelPhaseDone), 4, "dog tickets not done (" + name + ")"));
                s.Add(TvDo(() =>
                {
                    int reason = Get<int>(TvMyBox(), "joinReason");
                    var land = Networking.LocalPlayer.GetPosition(); var target = tvRemote.GetPosition();
                    var water = new KH_CourseTravelEditor.Water { xs = Get<float[]>(manager, "travelRiverXs"), z0 = Get<float>(manager, "travelRiverZ0"), step = Get<float>(manager, "travelRiverStep"), bridgeZs = Get<float[]>(manager, "travelBridgeZs"), bridgeXs = Get<float[]>(manager, "travelBridgeXs") };
                    TvExpect(reason == expect, name + ": reason " + reason + " (expected " + expect + ")");
                    TvExpect(KH_CourseTravelEditor.WaterOk(land, water), name + ": landing is 10 m from the river and off the bridges");
                    TvExpect(TvFlat(land, target) <= 40f, name + ": landing within 40 m of the target (" + TvFlat(land, target).ToString("F1") + " m)");
                    TvExpect(TvFlat(land, target) >= 1f, name + ": landing not on top of the target");
                    for (int i = 0; i < 3; i++) TvExpect(tvDisc[i] == 1, name + ": dog" + i + " moved once with the holder");
                    TvNote(name + ": reason " + reason + ", landed " + TvFlat(land, target).ToString("F1") + " m from the target, dogs followed once");
                    tvPuppetWalk = false;
                }));
            }
        }

        static void TvT9Join()
        {
            var s = TvCase("T9-join-leave-and-cancel", true, 3); var a = new TvCtx(); var b = new TvCtx();
            // (a) 申し込み中に相手が退室 → 「相手が退室しました」、移動しない
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvPlacePuppet(nodes[Closest(TvSpot(KH_LeashManager.SpotPlaza))], Vector3.forward); TvJoinPress(a); }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") >= 0, 2, "join request did not start"));
            s.Add(TvDo(() => { a.pos = Networking.LocalPlayer.GetPosition(); ClientSimMain.RemovePlayer(tvRemote); tvRemote = null; }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.JoinLeft, 3, "target left but no 'left' result"));
            s.Add(TvDo(() =>
            {
                TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), a.pos) < 1f && tvDisc.All(d => d == 0), "nothing moved after the target left");
                TvNote("leave during the request: 'target left', no move");
            }));
            // (b) 取り消した後に承認が届く → 取消が勝つ (保留中の seq が無いので捨てる)
            s.Add(TvDo(() => ClientSimMain.SpawnRemotePlayer("KH_TravelJoinTarget2")));
            s.Add(TvUntil(() => (tvRemote = TvRemotePlayer()) != null, 5, "remote player spawn"));
            s.Add(TvDo(() => { TvPlacePuppet(nodes[Closest(TvSpot(KH_LeashManager.SpotPlaza))], Vector3.forward); TvJoinPress(b); }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") >= 0, 2, "second join request did not start"));
            s.Add(TvDo(() => { b.pos = Networking.LocalPlayer.GetPosition(); pouch.SendCustomEvent("_TravelGo"); }));   // 返事待ちの [取り消す]
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.JoinCanceled, 2, "cancel not reported"));
            s.Add(TvDo(() => TvAnswer(KH_TravelMailbox.CodeYes)));   // 行き違いで届いた承認
            s.Add(TvWait(2));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.JoinCanceled, "late accept after cancel was ignored");
                TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), b.pos) < 1f && tvDisc.All(d => d == 0), "nothing moved after cancel + late accept");
                TvMyBox().SetProgramVariable("debugJoinLoopback", 0);
                ClientSimMain.RemovePlayer(tvRemote); tvRemote = null;
                TvNote("cancel then late accept: cancel wins, no move");
            }));
            // (c) 承認の後、切符待ちの間に相手が退室 → 移動を取りやめる (§D.4)。切符待ちを保つため Owner は要求を捨てる (debugDropTravel)
            var c3 = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => ClientSimMain.SpawnRemotePlayer("KH_TravelJoinTarget3")));
            s.Add(TvUntil(() => (tvRemote = TvRemotePlayer()) != null, 5, "remote player spawn"));
            s.Add(TvDo(() => { TvMyBox().SetProgramVariable("debugJoinLoopback", 1); TvSet("debugDropTravel", 1); TvPlacePuppet(nodes[Closest(TvSpot(KH_LeashManager.SpotGate)) + 5], Vector3.forward); TvJoinPress(c3); }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") >= 0, 2, "third join request did not start"));
            s.Add(TvWait(1.1));
            s.Add(TvDo(() => { c3.pos = Networking.LocalPlayer.GetPosition(); TvAnswer(KH_TravelMailbox.CodeYes); }));
            s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 1, 2, "accepted join did not start the ticket wait"));
            s.Add(TvDo(() => { ClientSimMain.RemovePlayer(tvRemote); tvRemote = null; }));
            s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 0 && Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.JoinLeft, 3, "move not cancelled after the target left"));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), c3.pos) < 1f && tvDisc.All(d => d == 0), "nothing moved after the target left during the ticket wait");
                TvExpect(Get<float>(tvFade, "_alpha") <= 0.001f, "screen is not left dark");
                TvNote("accepted, target left during the ticket wait: move cancelled, no move (§D.4)");
            }));
            // (d) 承認の後、切符待ちの間に本人が [取り消す] (§D.6)
            var c4 = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => ClientSimMain.SpawnRemotePlayer("KH_TravelJoinTarget4")));
            s.Add(TvUntil(() => (tvRemote = TvRemotePlayer()) != null, 5, "remote player spawn"));
            s.Add(TvDo(() => { TvPlacePuppet(nodes[Closest(TvSpot(KH_LeashManager.SpotGate)) + 5], Vector3.forward); TvJoinPress(c4); }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") >= 0, 2, "fourth join request did not start"));
            s.Add(TvWait(1.1));
            s.Add(TvDo(() => { c4.pos = Networking.LocalPlayer.GetPosition(); TvAnswer(KH_TravelMailbox.CodeYes); }));
            s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 1, 2, "accepted join did not start the ticket wait"));
            s.Add(TvDo(() => pouch.SendCustomEvent("_TravelGo")));   // 切符待ちの [取り消す]
            s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 0 && Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.JoinCanceled, 2, "cancel during the ticket wait not reported"));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), c4.pos) < 1f && tvDisc.All(d => d == 0), "nothing moved after cancelling during the ticket wait");
                TvSet("debugDropTravel", 0);
                TvMyBox().SetProgramVariable("debugJoinLoopback", 0);
                ClientSimMain.RemovePlayer(tvRemote); tvRemote = null;
                TvNote("accepted, requester cancelled during the ticket wait: no move. Real two-client ReqJoin/AnsJoin order is a PCVR check (§C.2 #3-#5)");
            }));
        }

        // ============================================================
        // 破壊監査 2026-09-28 (docs/_sessions/destructive-audit-2026-09-28/triage.md の RC-01・02・06・07)
        // ============================================================

        /// <summary>RC-02: 完了報告を犬ごとに 3 回落とし、その間に本人が着地点から 6 m より離れる。位置を置き直さずに Phase 2 へ収束する。</summary>
        static void TvT6b(int mask)
        {
            var s = TvCase("T6b-done-lost-holder-walks-away", true, TvBits(mask)); var c = new TvCtx();
            int n = TvBits(mask);
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugDropTravelDone", 3 * n); TvNote("simulated: the first 3 completion reports of every dog are lost (debugDropTravelDone=" + (3 * n) + "); the holder walks away along the trail at 4 m/s right after arriving"); TvPress(c, TvFarEvt(c)); c.req = Get<int>(manager, "_tvReq"); }));
            s.Add(TvUntil(() => TvFlat(Networking.LocalPlayer.GetPosition(), TvSpot(c.spot)) < 6f, 12, "player did not arrive"));
            s.Add(TvDo(() => c.center = Get<Vector3[]>(manager, "travelCenter")[TvFirst(mask)]));
            s.Add(TvWalk(2.5, 4f));
            s.Add(TvUntil(() => TvCountLogs(c.logFrom, "dropped (debugDropTravelDone)") >= 3 * n, 5, "three completion reports per dog were not all lost"));
            s.Add(TvDo(() => TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), c.center) > 6f, "the holder is more than 6 m from the landing while the reports are retried (" + TvFlat(Networking.LocalPlayer.GetPosition(), c.center).ToString("F1") + " m)")));
            s.Add(TvUntil(() => TvReqPhase(mask, KH_LeashManager.TravelPhaseDone, c.req), 4, "completion was not retried after the holder walked away (RC-02)"));
            s.Add(TvDo(() =>
            {
                for (int i = 0; i < 6; i++)
                {
                    bool mine = (mask & (1 << i)) != 0;
                    if (mine) TvExpect(TvAppliesSince(c, i) == 1, "dog" + i + " applied the ticket once (no re-apply after walking away)");
                    TvExpect(TvCountLogs(c.logFrom, "dog" + i + " travel apply") == (mine ? 1 : 0), "dog" + i + " apply logs");
                }
                TvSet("debugDropTravelDone", 0);
                TvNote("3 reports per dog lost, holder " + TvFlat(Networking.LocalPlayer.GetPosition(), c.center).ToString("F1") + " m from the landing: retries continued, Phase 2 without a second apply");
            }));
        }

        /// <summary>RC-01: 確認待ちの間に本人が歩いて着地点の 4.5 m まで近づく (Owner の書き込みを 3 秒遅らせる)。犬は本人の移動済みの後だけ適用する。</summary>
        static void TvT12WalkIn(int mask)
        {
            var s = TvCase("T12-walk-in-while-confirming", true, TvBits(mask)); var c = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() =>
            {
                var spot = TvSpot(KH_LeashManager.SpotUpper); int pick = -1;
                for (int i = 0; i < nodes.Length && pick < 0; i++) { float d = TvFlat(nodes[i], spot); if (d >= 17f && d <= 24f) pick = i; }
                if (pick < 0) throw new InvalidOperationException("no trail node 17-24 m from the upper plaza");
                var face = spot - nodes[pick]; face.y = 0;
                holder = nodes[pick]; Networking.LocalPlayer.TeleportTo(nodes[pick], Quaternion.LookRotation(face));
            }));
            s.Add(TvWait(3));   // 犬が追いつくまで
            s.Add(TvDo(() => { TvSet("debugDelayTravel", 3f); TvNote("simulated: the ticket is written 3 s late (debugDelayTravel=3); meanwhile the holder walks to 4.5 m from the landing"); TvPress(c, "_TravelUpper"); c.req = Get<int>(manager, "_tvReq"); c.center = Get<Vector3>(manager, "_tvCenter"); c.spot = KH_LeashManager.SpotUpper; }));
            s.Add(now =>
            {
                var lp = Networking.LocalPlayer; var d = c.center - c.pos; d.y = 0; float len = d.magnitude;
                double t = Math.Min(1.0, (now - tvStepAt) / 1.8);
                var at = c.pos + d / Math.Max(len, 0.001f) * (float)(Math.Max(0f, len - 4.5f) * t);
                lp.TeleportTo(at, lp.GetRotation());
                return t >= 1.0;
            });
            s.Add(TvDo(() => TvExpect(TvNoApplies(c, mask), "no dog applied while the holder walked in (ticket not written yet)")));
            // 歩いている間の Return は切符が書かれる前なので正当 (合否に含めない)。ここからは今回の要求 ID だけを見る
            s.Add(TvUntil(() => TvCountLogs(c.logFrom, "delayed write now (debugDelayTravel)") >= 1, 6, "the delayed ticket was not written"));
            s.Add(TvUntil(() => TvReqWritten(mask, c.req), 2, "the new reqId is not on every ticket"));
            s.Add(TvUntil(() => TvCountLogs(c.logFrom, "travel req=" + c.req + " teleported to spot") >= 1, 8, "the requester did not teleport after the confirmation"));
            s.Add(TvUntil(() => TvReqPhase(mask, KH_LeashManager.TravelPhaseDone, c.req), 8, "tickets of this request not done after the move"));
            s.Add(TvDo(() =>
            {
                int moved = TvFirstLog(c.logFrom, "travel moved from player"), apply = TvFirstLog(c.logFrom, " travel apply req=");
                TvExpect(moved >= 0 && apply > moved, "the first dog apply came after the requester's moved notice (moved log " + moved + ", apply log " + apply + ")");
                for (int i = 0; i < 6; i++) if ((mask & (1 << i)) != 0) TvExpect(TvAppliesSince(c, i) == 1, "dog" + i + " applied once");
                TvNote("walked to 4.5 m during the wait; dogs applied once, only after the moved notice");
            }));
        }

        /// <summary>RC-01: 移動済みの通知が 2 秒遅れ、届いたときに 2 回処理される。遅れの間は適用 0 回、届いたら 1 回だけ。</summary>
        static void TvT12MovedLate(int mask)
        {
            var s = TvCase("T12-moved-notice-late-and-twice", true, TvBits(mask)); var c = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugDelayTravelMoved", 2f); TvNote("simulated: the Owner holds the moved notice 2 s and then handles it twice (debugDelayTravelMoved=2)"); TvPress(c, TvFarEvt(c)); c.req = Get<int>(manager, "_tvReq"); }));
            s.Add(TvUntil(() => TvFlat(Networking.LocalPlayer.GetPosition(), TvSpot(c.spot)) < 6f, 12, "player did not arrive"));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                TvExpect(TvReqPhase(mask, KH_LeashManager.TravelPhaseReady, c.req), "tickets of this request stay Phase 1 while the moved notice is held");
                TvExpect(TvNoApplies(c, mask) && tvDisc.All(d => d == 0), "no dog applied or moved before the moved notice");
            }));
            s.Add(TvUntil(() => TvReqPhase(mask, KH_LeashManager.TravelPhaseDone, c.req), 5, "tickets not done after the moved notice was released"));
            s.Add(TvDo(() =>
            {
                for (int i = 0; i < 6; i++)
                {
                    bool mine = (mask & (1 << i)) != 0;
                    if (mine) TvExpect(TvAppliesSince(c, i) == 1, "dog" + i + " applied once despite the duplicate notice");
                    TvExpect(tvDisc[i] == (mine ? 1 : 0), "dog" + i + " position changes " + tvDisc[i]);
                }
                TvExpect(TvCountLogs(c.logFrom, "released twice (debugDelayTravelMoved)") == 1, "the held notice was released once");
                TvExpect(TvCountLogs(c.logFrom, " unchanged") >= 1, "the duplicate moved notice changed nothing");
                TvNote("moved notice 2 s late + duplicated: 0 applies while held, then exactly one per dog; duplicate ignored");
            }));
        }

        /// <summary>RC-01 (レビュー 2026-09-28 高): 初回の移動済みを Owner が捨て、その直後に本人が着地点の 2〜6 m へ歩く。
        /// 着いたのを一度確かめた後の再送は距離によらないので、次の再送で Phase 4 → 適用 → Done まで進む (直す前は再送が止まり Phase 1 のまま)。</summary>
        static void TvT12MovedFirstLost(int mask)
        {
            var s = TvCase("T12-moved-first-lost-then-walk", true, TvBits(mask)); var c = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvSet("debugDropTravelMoved", 1); TvNote("simulated: the Owner loses the first moved notice (debugDropTravelMoved=1, e.g. Owner change); right after, the holder steps to 4 m from the landing"); TvPress(c, TvFarEvt(c)); c.req = Get<int>(manager, "_tvReq"); }));
            s.Add(TvUntil(() => TvCountLogs(c.logFrom, "dropped (debugDropTravelMoved)") >= 1, 12, "the first moved notice was not sent"));
            s.Add(TvDo(() =>
            {
                c.center = Get<Vector3>(manager, "_tvCenter");
                int pick = -1;
                for (int i = 0; i < nodes.Length && pick < 0; i++) { float d = TvFlat(nodes[i], c.center); if (d >= 3.5f && d <= 5f) pick = i; }
                var lp = Networking.LocalPlayer;
                var to = pick >= 0 ? nodes[pick] : c.center + lp.GetRotation() * Vector3.forward * 4f;
                lp.TeleportTo(to, lp.GetRotation());
            }));
            s.Add(TvWait(.3));
            s.Add(TvDo(() =>
            {
                float d = TvFlat(Networking.LocalPlayer.GetPosition(), c.center);
                TvExpect(d > 2f && d < 6f, "the holder is 2-6 m from the landing after the lost notice (" + d.ToString("F1") + " m)");
                TvExpect(TvNoApplies(c, mask), "no dog applied before a moved notice reached the Owner");
            }));
            s.Add(TvUntil(() => TvReqPhase(mask, KH_LeashManager.TravelPhaseDone, c.req), 6, "moved notice was not resent after the holder left the 2 m circle (review high)"));
            s.Add(TvDo(() =>
            {
                TvExpect(TvCountLogs(c.logFrom, "dropped (debugDropTravelMoved)") == 1, "exactly one moved notice was lost");
                TvExpect(TvCountLogs(c.logFrom, " phase=4 dogs=") >= 1, "a resent moved notice set Phase 4");
                for (int i = 0; i < 6; i++)
                {
                    bool mine = (mask & (1 << i)) != 0;
                    if (mine) TvExpect(TvAppliesSince(c, i) == 1, "dog" + i + " applied once");
                    TvExpect(tvDisc[i] == (mine ? 1 : 0), "dog" + i + " position changes " + tvDisc[i]);
                }
                TvSet("debugDropTravelMoved", 0);
                TvNote("first moved notice lost, holder " + TvFlat(Networking.LocalPlayer.GetPosition(), c.center).ToString("F1") + " m from the landing: resent, Phase 4, one apply per dog, Phase 2");
            }));
        }

        /// <summary>RC-01 の合流側: 着地点が本人から 3〜5 m。(a) TeleportTo を省く → 犬の適用 0 回、切符は期限切れ。
        /// (b) 暗転中に実ボタンで取り消す → 本人も犬も動かず、切符は取消。</summary>
        static void TvT12Join(int mask)
        {
            var s = TvCase("T12-join-near-landing", true, TvBits(mask)); var a = new TvCtx(); var b = new TvCtx();
            s.Add(TvDo(() => ClientSimMain.SpawnRemotePlayer("KH_TravelJoinNear")));
            s.Add(TvUntil(() => (tvRemote = TvRemotePlayer()) != null, 5, "remote player spawn"));
            s.Add(TvUntil(() => TvMyBox() != null, 5, "local KH_TravelMailbox not found"));
            // (a) 3〜5 m + TeleportTo を省く
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvMyBox().SetProgramVariable("debugJoinLoopback", 1); TvPuppetAhead(7.5f); }));
            s.Add(TvWait(2));
            s.Add(TvDo(() => { TvSet("debugSkipTravelTeleport", 1); TvNote("simulated: target 7.5 m ahead (landing 4 m from the requester); the requester skips TeleportTo after confirmation"); TvJoinPress(a); }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") >= 0, 2, "join request did not start"));
            s.Add(TvWait(1.1));
            s.Add(TvDo(() => { a.pos = Networking.LocalPlayer.GetPosition(); a.disc = (int[])tvDisc.Clone(); a.req1 = Get<int>(manager, "travelResultSerial"); TvAnswer(KH_TravelMailbox.CodeYes); }));
            s.Add(TvUntil(() => Get<int>(manager, "travelResultSerial") != a.req1 && Get<int>(manager, "travelResultCode") == KH_LeashManager.TravelStayed && Get<int>(manager, "_tvState") == 0, 10, "join was not confirmed"));
            s.Add(TvDo(() =>
            {
                float land = TvFlat(Get<Vector3>(manager, "_tvCenter"), a.pos);
                TvExpect(land >= 2.4f && land <= 5.6f, "landing is 3-5 m from the requester (" + land.ToString("F1") + " m)");
                TvNote("(a) landing " + land.ToString("F1") + " m from the requester, TeleportTo skipped");
            }));
            // 切符の寿命 (15 s) の内側で検査する。期限切れの後は通常の Return (追従テレポートを含む) が正当なので合否に含めない (82 初回の FAIL、2026-09-28)
            s.Add(TvWait(12));
            s.Add(TvDo(() =>
            {
                TvExpect(TvNoApplies(a, mask), "no dog applied the ticket while the requester had not moved (RC-01)");
                TvExpect(TvReqPhase(mask, KH_LeashManager.TravelPhaseReady, Get<int[]>(manager, "travelReq")[TvFirst(mask)]), "tickets are still Phase 1 (inside the 15 s life) when checked");
                TvExpect(TvDiscSince(a.disc) && TvFlat(Networking.LocalPlayer.GetPosition(), a.pos) < 1f, "nothing moved while the ticket was alive");
            }));
            s.Add(TvUntil(() => TvAllPhase(mask, KH_LeashManager.TravelPhaseNone), 14, "ticket did not expire (Owner expires at ~20 s after the write; 12 s wait + 14 s)"));
            // (b) 3〜5 m + 暗転中の取消 (暗転を 2 秒に伸ばして、その間に実ボタン)
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvPuppetAhead(7.5f); }));
            s.Add(TvWait(2));
            s.Add(TvDo(() => { tvFade.SetProgramVariable("fadeOutSeconds", 2f); TvNote("(b) fade-out slowed to 2 s; the requester presses [cancel] while it darkens"); TvJoinPress(b); }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") >= 0, 2, "second join request did not start"));
            s.Add(TvWait(1.1));
            s.Add(TvDo(() => { b.pos = Networking.LocalPlayer.GetPosition(); b.disc = (int[])tvDisc.Clone(); TvAnswer(KH_TravelMailbox.CodeYes); }));
            s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 3, 3, "fade did not start"));
            s.Add(TvDo(() => pouch.SendCustomEvent("_TravelGo")));
            s.Add(TvUntil(() => Get<int>(manager, "_tvState") == 0 && Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.JoinCanceled, 2, "cancel during the fade not reported"));
            s.Add(TvUntil(() => Get<float>(tvFade, "_alpha") <= 0.001f, 3, "screen left dark after the cancel"));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                TvExpect(TvFlat(Networking.LocalPlayer.GetPosition(), b.pos) < 1f && TvDiscSince(b.disc), "neither the requester nor a dog moved");
                TvExpect(TvNoApplies(b, mask), "no dog applied the ticket (RC-01)");
                TvExpect(TvAllPhase(mask, KH_LeashManager.TravelPhaseCanceled), "tickets are Phase 3 (cancelled)");
                tvFade.SetProgramVariable("fadeOutSeconds", 0.2f); TvSet("debugSkipTravelTeleport", 0);
                TvNote("(b) cancelled during the fade: no move, no apply, Phase 3");
            }));
        }

        /// <summary>RC-06: 返事待ちから 0.2 秒で承認 (速さの測定待ち) → 0.3 秒で実ボタンの [取り消す]。表示も取消で、本人も犬も動かない。</summary>
        static void TvT9JoinFastCancel()
        {
            var s = TvCase("T9-join-fast-accept-cancel", true, 3); var c = new TvCtx();
            s.Add(TvCooldown());
            s.Add(TvDo(() => { TvMyBox().SetProgramVariable("debugJoinLoopback", 1); TvPuppetAhead(20f); }));
            s.Add(TvWait(1.5));
            s.Add(TvDo(() => TvJoinPress(c)));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "_outTo") >= 0, 2, "join request did not start"));
            s.Add(TvWait(.2));
            s.Add(TvDo(() => { c.pos = Networking.LocalPlayer.GetPosition(); TvAnswer(KH_TravelMailbox.CodeYes); }));
            s.Add(TvWait(.1));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(TvMyBox(), "_acceptFor") >= 0, "accepted and still measuring the target's speed");
                pouch.SendCustomEvent("_TravelModePeople");   // 表示を描き直す
                string label = TvGoLabel();
                TvExpect(label.Length > 0 && label != c.text, "the button shows [cancel] while measuring (shown '" + label + "', ask was '" + c.text + "')");
                pouch.SendCustomEvent("_TravelGo");
            }));
            s.Add(TvUntil(() => Get<int>(TvMyBox(), "joinResultCode") == KH_TravelMailbox.JoinCanceled && Get<int>(TvMyBox(), "_acceptFor") < 0, 2, "cancel while measuring not reported (RC-06)"));
            s.Add(TvWait(2));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(manager, "_tvState") == 0 && TvFlat(Networking.LocalPlayer.GetPosition(), c.pos) < 1f && tvDisc.All(d => d == 0), "neither the requester nor a dog moved");
                TvExpect(Get<int[]>(manager, "travelReq").SequenceEqual(c.old), "no ticket was written");
                TvNote("accept at 0.2 s, real [cancel] at 0.3 s while measuring: cancelled, no ticket, no move");
            }));
        }

        /// <summary>RC-07: (1) 在室中の A を「この人からは受けない」にしたまま、退室済みの 16 人を同じく拒否 → A の拒否が残り、枠は再利用される。
        /// (2) A を含む 9 人に断られる → 最初の A への 30 秒が残る (実ボタンでの再申請は「もう少し待って」)。</summary>
        static void TvT9JoinHistory()
        {
            var s = TvCase("T9-join-history-kept", true, 0); var c = new TvCtx();
            s.Add(TvDo(() =>
            {
                var box = TvMyBox(); int a = tvRemote.playerId;
                box.SetProgramVariable("_inFrom", a); box.SendCustomEvent("_BlockIncoming");
                for (int k = 1; k <= 16; k++) { box.SetProgramVariable("_inFrom", 900000 + k); box.SendCustomEvent("_BlockIncoming"); }
                var blocked = Get<int[]>(box, "_blocked");
                TvExpect(blocked.Contains(a), "A (still in the instance) is still blocked after 16 more blocks");
                TvExpect(blocked.Length == 16, "departed players' slots were reused (length " + blocked.Length + ")");
                TvNote("simulated: 16 blocked ids are players that already left (not in the instance); A = puppet " + a + " kept, array length " + blocked.Length);
            }));
            s.Add(TvDo(() =>
            {
                var box = TvMyBox(); int a = tvRemote.playerId;
                box.SetProgramVariable("debugJoinLoopback", 1); box.SetProgramVariable("debugAnswerCode", KH_TravelMailbox.CodeNo);
                box.SetProgramVariable("_outTo", a); box.SendCustomEvent("_DebugAnswerNow");
                for (int k = 1; k <= 8; k++) { box.SetProgramVariable("_outTo", 910000 + k); box.SendCustomEvent("_DebugAnswerNow"); }
                TvExpect(Get<int[]>(box, "_deniedIds").Contains(a), "the first refusal (A) is still remembered after 8 more");
                TvJoinPress(c);   // A へもう一度 (実ボタン)
                TvExpect(Get<int>(box, "_outTo") < 0, "no new request to A within 30 s");
                string key = Get<string[]>(pouch, "_hsNoteKeys")[0];
                TvExpect(key == "course.travel.join.again", "the menu says 'ask again later' (shown " + key + ")");
                TvNote("9 refusals (A first, 8 departed ids): A's 30 s gap kept; the real button shows " + key);
            }));
            // (3) レビュー 2026-09-28 中: 在室中の有効 ID で 16 枠を満たしてからの 17 件目 (伸長して既存を保持)、30 秒経過した断りの枠の再利用、
            // 退室 (RemovePlayer) の後に両方の履歴から枠が空くこと。循環配列 16 枠のままでは (a)(c) が落ちる
            s.Add(TvDo(() => { for (int k = 1; k <= 16; k++) ClientSimMain.SpawnRemotePlayer("KH_TravelHist" + k); }));
            s.Add(TvUntil(() => VRCPlayerApi.GetPlayerCount() >= 18, 10, "16 extra remote players did not spawn"));
            s.Add(TvWait(1));   // 同じフレームで湧かせた 16 人の初期化を待つ (82b 初回の NullReference の疑い 1)
            s.Add(TvDo(() =>
            {
                TvExpect(tvRemote != null && Utilities.IsValid(tvRemote), "the join puppet A is still in the instance");
                TvExpect(TvMyBox() != null, "the local KH_TravelMailbox is found with 18 players");
                int me = Networking.LocalPlayer.playerId, a = tvRemote.playerId;
                var all = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; VRCPlayerApi.GetPlayers(all);
                tvHist = all.Where(p => p != null && Utilities.IsValid(p) && p.playerId != me && p.playerId != a).OrderBy(p => p.playerId).Take(16).ToList();
                TvExpect(tvHist.Count == 16, "16 extra players in the instance (" + tvHist.Count + ")");
                tvHistValid = new List<int> { a }; for (int k = 0; k < 15; k++) tvHistValid.Add(tvHist[k].playerId);
                tvHistX = tvHist[15].playerId;
                TvNote("history players: A=" + a + ", extras " + string.Join(",", tvHist.Select(p => p.playerId)));
            }));
            s.Add(TvDo(() =>
            {
                var box = TvMyBox(); var valid = tvHistValid; int x = tvHistX;
                // (a) 拒否: 在室中 16 人で満杯 → 17 人目で伸長
                box.SetProgramVariable("_blocked", Enumerable.Repeat(-1, 16).ToArray());
                foreach (int id in valid) { box.SetProgramVariable("_inFrom", id); box.SendCustomEvent("_BlockIncoming"); }
                TvExpect(Get<int[]>(box, "_blocked").Length == 16, "16 in-room blocks fit in 16 slots");
                box.SetProgramVariable("_inFrom", x); box.SendCustomEvent("_BlockIncoming");
                var bl = Get<int[]>(box, "_blocked");
                TvExpect(bl.Length == 32, "the 17th in-room block grew the array (length " + bl.Length + ")");
                TvExpect(valid.All(id => bl.Contains(id)) && bl.Contains(x), "all 17 in-room blocks are kept");
            }));
            s.Add(TvDo(() =>
            {
                var box = TvMyBox(); var valid = tvHistValid; int x = tvHistX;
                // (b) 断り: 在室中 16 人で満杯 → 30 秒経過にしてから 17 人目 → 期限切れの枠を再利用 (伸長しない)
                box.SetProgramVariable("debugJoinLoopback", 1); box.SetProgramVariable("debugAnswerCode", KH_TravelMailbox.CodeNo);
                box.SetProgramVariable("_deniedIds", Enumerable.Repeat(-1, 16).ToArray()); box.SetProgramVariable("_deniedAts", new float[16]);
                foreach (int id in valid) { box.SetProgramVariable("_outTo", id); box.SendCustomEvent("_DebugAnswerNow"); }
                TvExpect(Get<int[]>(box, "_deniedIds").Length == 16 && valid.All(id => Get<int[]>(box, "_deniedIds").Contains(id)), "16 in-room refusals fit in 16 slots");
                var ats = (float[])Get<float[]>(box, "_deniedAts").Clone(); for (int i = 0; i < ats.Length; i++) ats[i] -= 31f;
                box.SetProgramVariable("_deniedAts", ats);
                box.SetProgramVariable("_outTo", x); box.SendCustomEvent("_DebugAnswerNow");
                var dn = Get<int[]>(box, "_deniedIds");
                TvExpect(dn.Length == 16 && dn.Contains(x), "after 30 s the 17th refusal reused an expired slot (length " + dn.Length + ")");
            }));
            s.Add(TvDo(() =>
            {
                var box = TvMyBox(); var valid = tvHistValid; int x = tvHistX;
                // (c) 断り: 在室中 16 人の新しい断りで満杯 → 17 人目で伸長して既存を保持
                box.SetProgramVariable("_deniedIds", Enumerable.Repeat(-1, 16).ToArray()); box.SetProgramVariable("_deniedAts", new float[16]);
                foreach (int id in valid) { box.SetProgramVariable("_outTo", id); box.SendCustomEvent("_DebugAnswerNow"); }
                box.SetProgramVariable("_outTo", x); box.SendCustomEvent("_DebugAnswerNow");
                var dn = Get<int[]>(box, "_deniedIds");
                TvExpect(dn.Length == 32 && valid.All(id => dn.Contains(id)) && dn.Contains(x), "the 17th fresh refusal grew the array and kept all (length " + dn.Length + ")");
                TvNote("in-room history: 17 blocks -> 32 slots, all kept; 30 s-old refusals reused (16 slots); 17 fresh refusals -> 32 slots, all kept");
                c.req = tvHist[0].playerId;
                ClientSimMain.RemovePlayer(tvHist[0]);
            }));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                var box = TvMyBox(); int gone = c.req;
                TvExpect(!Get<int[]>(box, "_blocked").Contains(gone) && !Get<int[]>(box, "_deniedIds").Contains(gone), "the departed player's slots were freed in both histories (OnPlayerLeft)");
                TvExpect(Get<int[]>(box, "_blocked").Contains(tvRemote.playerId), "others' blocks are unchanged after the leave");
                TvNote("RemovePlayer(" + gone + "): freed in _blocked and _deniedIds");
                for (int k = 1; k < tvHist.Count; k++) ClientSimMain.RemovePlayer(tvHist[k]);
                tvHist = null;
            }));
            s.Add(TvDo(() =>
            {
                TvMyBox().SetProgramVariable("debugJoinLoopback", 0); TvMyBox().SetProgramVariable("debugAnswerCode", 0);
                ClientSimMain.RemovePlayer(tvRemote); tvRemote = null;
            }));
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
                if (tvStep == 0 && tvCaseAt != Current.startedAt) { tvCaseAt = Current.startedAt; tvStepAt = now; tvSeenReqs.Clear(); TvReset(); tvObsValid = false; tvTrackMask = 0; tvCaseNotRun = false; }
                ObserveTravel(now);
                if (now - Current.startedAt > 420) throw new TimeoutException("Travel case deadline (420 s)");
                var steps = tvScripts[caseIndex];
                while (tvStep < steps.Count)
                {
                    if (!steps[tvStep](now)) return;
                    tvStep++; tvStepAt = now;
                }
                // 82c C7-wet: 地点が無く実施しなかったケースは NOT_RUN (PASS に数えない)。82 / 82b は立てないので従来どおり PASS
                string done = tvCaseNotRun ? "NOT_RUN" : "PASS";
                Current.status = done; Current.endedAt = now; Current.reason = string.Join(" | ", tvReport.cases[caseIndex].notes);
                tvReport.cases[caseIndex].status = done;
                WriteTravel(); Save();
                tvStep = 0;
                if (caseIndex + 1 == report.cases.Count)
                    End(report.travelCall ? (tvCallFailed ? "TRAVEL_CALL_FAIL" : "TRAVEL_CALL_PASS") : report.travelRc ? "TRAVEL_RC_OBSERVED" : report.full ? "TRAVEL_OBSERVED" : "TRAVEL_QUICK_OBSERVED",
                        (report.travelCall ? TvCountLine() + ". " : "") + "Local ClientSim travel cases passed (same-Owner contract; handover/late/forged/leave are simulated). Not network, device or PCVR acceptance.");
                else Next();
            }
            catch (Exception e)
            {
                if (tvReport != null && caseIndex >= 0 && caseIndex < tvReport.cases.Count) { tvReport.cases[caseIndex].status = "FAIL"; tvReport.cases[caseIndex].notes.Add("FAIL at step " + tvStep + ": " + e.Message); tvReport.cases[caseIndex].notes.Add("at: " + TvWhere(e)); }
                try { WriteTravel(); } catch { }
                // 82c (IMP-WLD-002): ケースの失敗は FAIL に記録して次のケースへ進む (片付けの C11 まで流す)。台本が失われたときは止める
                if (report != null && report.travelCall && tvScripts != null && caseIndex >= 0 && caseIndex < tvScripts.Count && Current != null)
                {
                    tvCallFailed = true;
                    Current.status = "FAIL"; Current.endedAt = now; Current.reason = e.GetType().Name + ": " + e.Message;
                    try
                    {
                        TvSet("debugDropTravel", 0); tvPuppetWalk = false;
                        if (tvFade != null) tvFade.SetProgramVariable("fadeOutSeconds", 0.2f);
                        var box = TvMyBox(); if (box != null) box.SetProgramVariable("debugCrossInvert", 0);
                    }
                    catch { }
                    tvStep = 0; Save();
                    if (caseIndex + 1 == report.cases.Count) End("TRAVEL_CALL_FAIL", TvCountLine() + ". One or more 82c cases failed; see travel.json");
                    else Next();
                    return;
                }
                throw;
            }
        }

        static void ObserveTravel(double now)
        {
            // Same rule as the main Observe (lastObserveAt starts at the case, maxObservationGap is recorded): the first
            // observation of a case only sets the reference time and positions, so no gap or position change is counted.
            if (!tvObsValid || tvPrev == null) { TvReset(); tvLastObs = now; tvLastFrameObs = Time.frameCount; tvLastGameTime = Time.time; tvLastRealtime = Time.realtimeSinceStartup; tvObsValid = true; return; }
            double gap = now - tvLastObs; tvLastObs = now;
            int frames = Time.frameCount - tvLastFrameObs; tvLastFrameObs = Time.frameCount;
            float gameGap = Time.time - tvLastGameTime; tvLastGameTime = Time.time;
            float realGap = Time.realtimeSinceStartup - tvLastRealtime; tvLastRealtime = Time.realtimeSinceStartup;
            Current.maxObservationGap = Math.Max(Current.maxObservationGap, gap);
            if (gap > .75) throw new InvalidOperationException("Observation gap " + gap.ToString("F2") + " s too large to count position changes");
            var req = Get<int[]>(manager, "travelReq");
            if (tvFade != null)
            {
                float alpha = Get<float>(tvFade, "_alpha"); var pp = Networking.LocalPlayer.GetPosition();
                if (tvPrevPlayerValid && TvFlat(pp, tvPrevPlayer) > 5f)
                {
                    tvJumps++; tvJumpAlpha = alpha; tvJumpPrevAlpha = tvPrevAlpha; tvJumpAt = now;
                    tvJumpDarkGap = alpha >= 0.99f ? 0 : (tvLastDarkAt >= 0 ? now - tvLastDarkAt : -1);
                }
                if (alpha >= 0.99f) tvLastDarkAt = now;
                if (tvJumpAt >= 0 && tvFadeBackAt < 0 && alpha <= 0.001f && now > tvJumpAt) tvFadeBackAt = now;
                tvMaxAlpha = Mathf.Max(tvMaxAlpha, alpha); tvPrevAlpha = alpha; tvPrevPlayer = pp; tvPrevPlayerValid = true;
            }
            if (tvPuppetWalk && Utilities.IsValid(tvRemote))
            {
                // 合流の相手を道の正方向へ 2 m/s で歩かせる (T10 の「歩行中」)
                float step = Mathf.Min((float)(now - tvPuppetLast), .1f) * 2f; tvPuppetLast = now;
                int next = (tvPuppetNode + 1) % nodes.Length;
                var before = tvPuppetPos; tvPuppetPos = Vector3.MoveTowards(tvPuppetPos, nodes[next], step);
                if (Vector3.Distance(tvPuppetPos, nodes[next]) < .001f) tvPuppetNode = next;
                var cs = tvRemote.GetClientSimPlayer(); var heading = tvPuppetPos - before; heading.y = 0;
                cs.transform.position = tvPuppetPos;
                if (heading.sqrMagnitude > 1e-6f) cs.transform.rotation = Quaternion.LookRotation(heading);
            }
            for (int i = 0; i < 6; i++)
            {
                var p = roots[i].position;
                // 跳びの判定は観測間隔に比例させる (8 m/s = 全力 7.2 m/s より上)。0.3 秒の引っかかりで走っている犬を「跳んだ」と数えない
                // 犬の歩幅は「速さ × Time.deltaTime (ゲーム時間)」。壁時計ではなくゲーム時間の間隔で上限を決める。
                // 上限 = 9 m/s (全力 7.2 m/s より上) × ゲーム時間 + 0.3 m。1 フレームの差でも 1.5 m より小さい上限にはしない
                float stepLimit = Mathf.Max(frames <= 1 ? TvDisc : 0.5f, 9f * Mathf.Max(gameGap, (float)gap) + 0.3f);
                float moved = Vector3.Distance(tvPrev[i], p);
                if (moved > stepLimit)
                {
                    tvDisc[i]++;
                    if (tvDisc[i] >= 2 && tvJumpNotes.Count < 20)
                    {
                        string state = "";
                        try { state = " mode=" + Get<int>(wander[i], "leashMode") + " pauseMode=" + Get<int>(wander[i], "_pauseMode") + " next=" + Get<int>(wander[i], "_routeNext") + " resume=" + Get<int>(wander[i], "_routeResume"); } catch (Exception) { }
                        object wp; if (wander[i].TryGetProgramVariable("waterPose", out wp)) state += " waterPose=" + wp;
                        tvJumpNotes.Add("dog" + i + " jump#" + tvDisc[i] + " " + moved.ToString("F2") + " m in wall " + gap.ToString("F3") + " s / game " + gameGap.ToString("F3") + " s / real " + realGap.ToString("F3") + " s / frames=" + frames + " dt=" + Time.deltaTime.ToString("F3") + " limit=" + stepLimit.ToString("F2") + " speed=" + SafeSpeed(i) + " at t=" + (now - Current.startedAt).ToString("F1") + " from " + tvPrev[i] + " to " + p + " holder=" + Networking.LocalPlayer.GetPosition() + state);
                    }
                }
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

        static string SafeSpeed(int i) { try { return Get<float>(wander[i], "_speed").ToString("F2"); } catch (Exception) { return "?"; } }

        static void TvReset()
        {
            tvPrev = roots.Select(r => r.position).ToArray(); tvDisc = new int[6];
            tvJumps = 0; tvMaxAlpha = 0f; tvJumpAlpha = -1f; tvJumpPrevAlpha = -1f; tvPrevPlayerValid = false;
            tvJumpAt = -1; tvLastDarkAt = -1; tvJumpDarkGap = -1; tvFadeBackAt = -1;
            if (tvPrevNext == null) { tvPrevNext = new int[6]; tvForward = new int[6]; tvReverse = new int[6]; }
        }

        static void TvTrack(int mask)
        {
            tvTrackMask = mask; tvPrevNext = Enumerable.Repeat(-1, 6).ToArray(); tvForward = new int[6]; tvReverse = new int[6];
        }

        static void TravelLog(string condition, string stack, LogType type)
        {
            if (report == null || !report.travelOnly || !EditorApplication.isPlaying) return;
            if (condition.StartsWith("[KH_Leash]") || condition.StartsWith("[KH_Join]")) tvLogs.Add(condition);   // [KH_Join] は 82c の順序の検査 (IMP-WLD-002)
        }

        static void WriteTravel()
        {
            if (report == null || tvReport == null) return;
            tvReport.casesPass = tvReport.cases.Count(x => x.status == "PASS");
            tvReport.casesNotRun = tvReport.cases.Count(x => x.status == "NOT_RUN");
            tvReport.casesFail = tvReport.cases.Count(x => x.status == "FAIL");
            Directory.CreateDirectory(Dir + "/" + report.runId);
            File.WriteAllText(Dir + "/" + report.runId + "/travel.json", JsonUtility.ToJson(tvReport, true));
        }

        // ---- 手順の部品 ----
        static Func<double, bool> TvDo(Action a) { return now => { a(); return true; }; }
        /// <summary>例外の発生箇所 (型名とスタックの先頭 4 行)。ハーネスは例外を捕まえて記録するので、場所をここで残す。</summary>
        static string TvWhere(Exception e)
        {
            var lines = (e.ToString() ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(5);
            return string.Join(" | ", lines);
        }
        static Func<double, bool> TvWait(double seconds) { return now => now - tvStepAt >= seconds; }
        static Func<double, bool> TvUntil(Func<bool> condition, double limit, string what)
        { return now => { if (condition()) return true; if (now - tvStepAt > limit) throw new TimeoutException(what + " (" + limit + " s)"); return false; }; }
        static Func<double, bool> TvCooldown()
        {
            return TvUntil(() => Get<float>(manager, "_tvCooldownUntil") <= Time.time && Get<int>(manager, "_tvState") == 0, 15, "requester cooldown");
        }
        static Func<double, bool> TvWalk(double seconds, float speed = 1.2f)
        {
            return now =>
            {
                var lp = Networking.LocalPlayer;
                if (now - tvStepAt < 0.0001 || tvWalkLast < tvStepAt)
                {
                    tvWalkPos = lp.GetPosition(); tvWalkNode = Closest(tvWalkPos); tvWalkLast = now; return false;
                }
                float step = Mathf.Min((float)(now - tvWalkLast), .1f) * speed; tvWalkLast = now;
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
        /// <summary>今回の要求 ID の切符だけを数える (前のケースの Phase 2 を読んで先へ進まないように)。</summary>
        static bool TvReqPhase(int mask, int phase, int req)
        {
            var r = Get<int[]>(manager, "travelReq"); var ph = Get<int[]>(manager, "travelPhase");
            for (int i = 0; i < 6; i++) if ((mask & (1 << i)) != 0 && (r[i] != req || ph[i] != phase)) return false;
            return req != 0;
        }
        static bool TvReqWritten(int mask, int req)
        {
            var r = Get<int[]>(manager, "travelReq");
            for (int i = 0; i < 6; i++) if ((mask & (1 << i)) != 0 && r[i] != req) return false;
            return req != 0;
        }

        /// <summary>記録した時点から、どの犬も位置が変わっていない (ケースの途中から数える)。</summary>
        static bool TvDiscSince(int[] at) { for (int i = 0; i < 6; i++) if (tvDisc[i] != at[i]) return false; return true; }

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
