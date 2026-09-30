using System;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-027 洗われ中の行動の切替 — Course/100 (Wash.cs) に足すケース。
    ///
    /// 枠・手順の部品・観測は Wash.cs のもの (WsCase / WsDo / WsUntil / WsDrive / WsPathHit / WsHold ...)。ケースの一覧への接続は
    /// Wash.cs の MakeWashCases の末尾 (WsWrite の前) で MakeWashBehaviorCases() を呼ぶ。ここでは一覧へ自分でつながない。
    ///
    /// 入口の起こし方は既存のハーネスの作法: リードは手元メニュー (pouch._WalkLeft{n} → KH_LeashManager.ReqAttach。Travel / Water)、
    /// 集合は KD_GroupActivities の同期値を直接書く (Water の W4-gather と同じ)、ボールの投げは Owner の入口 _OnBallThrown
    /// (StartFetch を直接通る唯一の公開の口)、ディスクは ReqCatch を SendCustomNetworkEvent で (NetStroke と同じく ClientSim は
    /// CallingPlayer = 自分)。持ち主の歩きは歩行ハーネスと同じく毎フレーム小さく TeleportTo する (TrackHolderSpeed が差分で速さを読む)。
    ///
    /// ClientSim は 1 人: 「持ち主でない人のストローク」は作れない (ストロークの送り主は NetStroke の CallingPlayer = 自分)。W-leash-other は SKIP。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        static UdonBehaviour wbFetch, wbCatcher, wbActivities;
        static bool wbGatherSet;

        static void MakeWashBehaviorCases()
        {
            wbFetch = WsRef(wsWander, "fetchController") as UdonBehaviour;
            wbCatcher = WsRef(wsWander, "frisbeeCatch") as UdonBehaviour;
            wbActivities = WsRef(wsWander, "activities") as UdonBehaviour;
            wbGatherSet = false;
            int me = Networking.LocalPlayer.playerId;

            // ---- W-gate-attach (洗われ中の新しい接続は断られる。解けたら同じ操作でつながる = 対照) ----
            var s = WsCase("W-gate-attach", 45);
            WsAnchor gaA = default; double gaEnd = 0;
            s.Add(WsDo(() => { WbCleanup(); WsPlaceShiba(); WsPlayerNear(1.3f); }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsDo(() => { gaA = WsAnchorOf(WsPathHit(0.5f)); }));
            s.Add(WsDrive(1.5f, 1, u => WsAt(gaA), WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge); gaEnd = EditorApplication.timeSinceStartup;
                WsExpect(WsHold(), "_WashHold() true after 1.5 s of sponge");
                WsExpect(Get<int[]>(manager, "walkers")[wsDog] < 0, "the Shiba starts unleashed");
                pouch.SendCustomEvent("_WalkLeft" + wsDog);
            }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                double since = EditorApplication.timeSinceStartup - gaEnd;
                WsExpect(WsHold(), "the hold still on when the refusal is read (" + since.ToString("F2") + " s after the last contact)");
                WsExpect(Get<int[]>(manager, "walkers")[wsDog] < 0, "ReqAttach refused while washed (walker " + Get<int[]>(manager, "walkers")[wsDog] + ")");
            }));
            s.Add(WsUntil(() => !WsHold(), wsHoldSeconds + 2, () => "the hold did not end"));
            s.Add(WsDo(() => { WsPlayerNear(1.3f); pouch.SendCustomEvent("_WalkLeft" + wsDog); }));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] == me, 5, () => "control: the same menu attach after the hold did not connect (walker " + Get<int[]>(manager, "walkers")[wsDog] + ")"));
            s.Add(WsDo(() => { WsNote("attach during the hold (1 s after the last contact) refused; the same menu attach after the hold connected (control)"); WsDetachShiba(); }));

            // ---- W-leash (リード接続中: 持ち主のストロークで止まり、持ち主が歩き出すと直ちに追従へ戻る) ----
            s = WsCase("W-leash", 70);
            WsAnchor lsA = default; double lsStart = 0; bool lsStopSeen = false; float lsSpeedEnd = 0f, lsHand = 0f;
            Vector3 lsDir = Vector3.forward, lsFrom = Vector3.zero, lsPrev = Vector3.zero; float lsT0 = -1f, lsPath = 0f, lsReleaseAt = -1f; string lsReleaseWhy = "";
            s.Add(WsDo(() => { WbCleanup(); WsPlaceShiba(); WsPlayerNear(1.3f); }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsDo(() => pouch.SendCustomEvent("_WalkLeft" + wsDog)));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] == me, 5, () => "the Shiba did not get leashed (walker " + Get<int[]>(manager, "walkers")[wsDog] + ")"));
            s.Add(WsWait(2.0));   // the dog starts leading on its leash
            s.Add(WsDo(() => { WsPlayerNear(1.0f); lsA = WsAnchorOf(WsPathHit(0.5f)); lsStart = EditorApplication.timeSinceStartup; }));
            s.Add(WsDrive(3f, 1, u =>
            {
                var h = WsAt(lsA);
                double t = EditorApplication.timeSinceStartup - lsStart;
                if (Get<int[]>(manager, "walkers")[wsDog] != me) throw new InvalidOperationException("the leash came off during the stroke");
                bool stop = Get<bool>(wsWander, "_washLeashStop");
                lsStopSeen |= stop;
                if (t > 1.5 && !stop) throw new InvalidOperationException("the leashed stop not active " + t.ToString("F2") + " s into the holder's contact (hold " + WsHold() + ", leashMode " + Get<int>(wsWander, "leashMode") + ", washActive " + Get<bool>(wsWander, "_washActive") + ")");
                return h;
            }, WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                lsSpeedEnd = WsSpeed(0.5f);
                WsExpect(lsStopSeen, "the leashed stop came on during the holder's contact");
                WsExpect(Get<bool>(wsWander, "_washActive"), "the wash frame flag is up on the leashed stop");
                WsExpect(lsSpeedEnd < 0.05f, "horizontal speed near 0 at the end of the 3 s contact (" + lsSpeedEnd.ToString("F3") + " m/s)");
                lsHand = Flat(Networking.LocalPlayer.GetPosition() - wsRoot.position);
                WsEnd(WsSponge);
                Vector3 d = nodes[1] - nodes[0]; d.y = 0f; lsDir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
                lsFrom = Networking.LocalPlayer.GetPosition(); lsPrev = wsRoot.position; lsT0 = -1f;
            }));
            // 持ち主が歩き出す (2 m/s、3 s)。保持 (3 s) が残っている間に停止が解け、犬が動くこと
            s.Add(now =>
            {
                if (lsT0 < 0f) lsT0 = Time.time;
                float t = Time.time - lsT0;
                Vector3 p = lsFrom + lsDir * (2f * t);
                try { p = CmGround(p); } catch (InvalidOperationException) { }
                CmTeleport(p, lsDir);
                var q = wsRoot.position; lsPath += Flat(q - lsPrev); lsPrev = q;
                if (lsReleaseAt < 0f && !Get<bool>(wsWander, "_washLeashStop"))
                {
                    lsReleaseAt = t;
                    lsReleaseWhy = WsHold() ? "hold still on" : "hold already over";
                }
                return t >= 3f;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(lsReleaseAt >= 0f && lsReleaseAt <= 1.0f, "the leashed stop released within 1 s of the holder walking off (" + (lsReleaseAt < 0f ? "never" : lsReleaseAt.ToString("F2") + " s, " + lsReleaseWhy) + ")");
                WsExpect(lsReleaseWhy == "hold still on", "released by the walk, not by the hold running out (" + lsReleaseWhy + ")");
                WsExpect(lsPath >= 0.5f, "the Shiba followed (moved " + lsPath.ToString("F2") + " m in the 3 s walk)");
                WsNote("leashed, holder 1.0 m beside (hand distance " + lsHand.ToString("F2") + " m): stop on during the 3 s sponge, speed at the end " + lsSpeedEnd.ToString("F3") + " m/s; holder walked at 2 m/s -> released at +" + lsReleaseAt.ToString("F2") + " s (" + lsReleaseWhy + "), dog moved " + lsPath.ToString("F2") + " m in 3 s");
                WsDetachShiba();
            }));

            // ---- W-leash-other (持ち主でない人のストロークでは止まらない) ----
            s = WsCase("W-leash-other", 5);
            s.Add(WsDo(() => { throw new WsSkip("ClientSim has one player: every stroke is the local player's, who is also the holder, so wash._StrokeAgeOf(holder.playerId) is always fresh. A stroke from someone other than the holder (the dog must not stop) -> PCVR with 2 people"); }));

            // ---- W-gate-fetch (洗われ中は投げに反応しない: Owner の _OnBallThrown → StartFetch が断る) ----
            s = WsCase("W-gate-fetch", 30);
            WsAnchor gfA = default; int gfState0 = -1;
            s.Add(WsDo(() =>
            {
                WbCleanup();
                if (wbFetch == null) throw new WsSkip("the Shiba's fetchController is not wired");
                var ball = WsRef(wbFetch, "ballController") as UnityEngine.Object;
                if (ball == null) throw new WsSkip("the Shiba's ballController is not wired: _OnBallThrown would not reach StartFetch (no thrower id)");
                WsPlaceShiba(); WsPlayerNear(1.3f);
            }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsDo(() => { gfA = WsAnchorOf(WsPathHit(0.5f)); gfState0 = Get<int>(wbFetch, "dogState"); }));
            s.Add(WsDrive(1.5f, 1, u => WsAt(gfA), WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                WsExpect(gfState0 == 0, "Fetch idle before the throw (dogState " + gfState0 + ")");
                WsExpect(WsHold(), "_WashHold() true before the throw");
                WsExpect(Get<int[]>(manager, "walkers")[wsDog] < 0, "unleashed (the leash gate would refuse anyway)");
                wbFetch.SendCustomEvent("_OnBallThrown");
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() => WsExpect(Get<int>(wbFetch, "dogState") == 0, "StartFetch refused while washed (dogState " + Get<int>(wbFetch, "dogState") + ")")));
            s.Add(WsUntil(() => !WsHold(), wsHoldSeconds + 2, () => "the hold did not end"));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                // The local path has no pending request (StartFetch is called once), so nothing starts when the hold ends
                WsExpect(Get<int>(wbFetch, "dogState") == 0, "no Fetch started after the hold ended (dogState " + Get<int>(wbFetch, "dogState") + ")");
                WsNote("_OnBallThrown on the Owner during the hold: dogState stayed Idle; still Idle 0.5 s after the hold. No positive control (a real Fetch would run off with the ball and disturb the next cases)");
            }));

            // ---- W-gate-frisbee (洗われ中は CanPlay が false: ReqCatch の断りの理由が washing) ----
            s = WsCase("W-gate-frisbee", 30);
            WsAnchor gdA = default;
            s.Add(WsDo(() =>
            {
                WbCleanup();
                if (wbCatcher == null) throw new WsSkip("the Shiba has no KH_FrisbeeCatch (frisbeeCatch null)");
                WsPlaceShiba(); WsPlayerNear(1.3f);
            }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsDo(() => { gdA = WsAnchorOf(WsPathHit(0.5f)); }));
            s.Add(WsDrive(1.5f, 1, u => WsAt(gdA), WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                WsExpect(Get<int>(wbCatcher, "state") == 0, "the disc catcher idle before the request (state " + Get<int>(wbCatcher, "state") + ")");
                WsExpect(WsHold(), "_WashHold() true before the request");
                wbCatcher.SetProgramVariable("_canPlayWhy", "");
                var lp = Networking.LocalPlayer;
                Vector3 p0 = lp.GetPosition() + Vector3.up * 1.2f, v0 = WsFlat(wsRoot.position - lp.GetPosition()) * 6f + Vector3.up * 2f;
                wbCatcher.SendCustomNetworkEvent(NetworkEventTarget.All, "ReqCatch", 0, p0, v0, Networking.GetServerTimeInSeconds(), lp.playerId, 0f);
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                string why = Get<string>(wbCatcher, "_canPlayWhy");
                WsExpect(why == "washing", "CanPlay refused with \"washing\" (\"" + why + "\"; empty = the request never reached CanPlay)");
                WsExpect(Get<int>(wbCatcher, "state") == 0, "the disc catcher stayed idle (state " + Get<int>(wbCatcher, "state") + ")");
                WsNote("ReqCatch (SendCustomNetworkEvent, thrower = me) during the hold: CanPlay -> \"washing\", catcher idle");
            }));

            // ---- W-gather (洗われ中に集合 → 停止が解けて集合へ向かう) ----
            s = WsCase("W-gather", 40);
            WsAnchor ghA = default; Vector3 ghAnchor = Vector3.zero, ghPrev = Vector3.zero; float ghPath = 0f, ghStart = 0f;
            s.Add(WsDo(() =>
            {
                WbCleanup();
                if (wbActivities == null) throw new WsSkip("the Shiba's activities (KD_GroupActivities) is not wired");
                WsPlaceShiba(); WsPlayerNear(1.3f);
            }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsDo(() => { ghA = WsAnchorOf(WsPathHit(0.5f)); }));
            s.Add(WsDrive(1.5f, 1, u => WsAt(ghA), WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                WsExpect(Get<bool>(wsWander, "_washActive"), "the wash branch active before the gathering");
                ghAnchor = CmGround(wsRoot.position + WsFlat(wsRoot.forward) * 5f);
                wbActivities.SetProgramVariable("participants", 1 << wsDog);
                wbActivities.SetProgramVariable("formation", 0);
                wbActivities.SetProgramVariable("anchor", ghAnchor);
                wbActivities.SetProgramVariable("yaw", 0f);
                wbActivities.SetProgramVariable("host", Networking.LocalPlayer.playerId);
                wbActivities.SetProgramVariable("startedMs", Networking.GetServerTimeInMilliseconds());
                wbActivities.SetProgramVariable("mode", 1);
                wbActivities.SetProgramVariable("revision", Get<int>(wbActivities, "revision") + 1);
                wbGatherSet = true;
                ghPrev = wsRoot.position; ghStart = Flat(ghAnchor - wsRoot.position);
            }));
            s.Add(WsUntil(() => !Get<bool>(wsWander, "_washActive") && Get<bool>(wsWander, "_groupCalled"), 2.0,
                () => "the wash stop did not yield to the gathering (washActive " + Get<bool>(wsWander, "_washActive") + ", groupCalled " + Get<bool>(wsWander, "_groupCalled") + ", hold " + WsHold() + ")"));
            // Codex 4-1: the accept edge releases the hold itself (KH_DogWash._ReleaseHold), so the gates open again too
            s.Add(WsDo(() => WsExpect(!WsHold(), "the gathering's accept edge released the hold (_WashHold() false)")));
            s.Add(WsUntil(() => { var p = wsRoot.position; ghPath += Flat(p - ghPrev); ghPrev = p; return ghPath >= 0.5f; }, 6,
                () => "the Shiba did not move 0.5 m toward the gathering (" + ghPath.ToString("F2") + " m)"));
            s.Add(WsDo(() =>
            {
                float left = Flat(ghAnchor - wsRoot.position);
                WsNote("gathering (mode 1, the Shiba the only participant, anchor " + ghStart.ToString("F1") + " m ahead) set during the hold: wash branch off, groupCalled on, hold released at the accept edge; moved " + ghPath.ToString("F2") + " m (anchor now " + left.ToString("F1") + " m)");
                WbCleanup();
            }));

            // ---- W-release (集合で解いた後、最後のストロークから holdSeconds 以内に集合が終わっても、古いストロークで再び止まらない) ----
            s = WsCase("W-release", 40);
            WsAnchor rlA = default; double rlEnd = 0; bool rlRestop = false;
            s.Add(WsDo(() =>
            {
                WbCleanup();
                if (wbActivities == null) throw new WsSkip("the Shiba's activities (KD_GroupActivities) is not wired");
                WsPlaceShiba(); WsPlayerNear(1.3f);
            }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsDo(() => { rlA = WsAnchorOf(WsPathHit(0.5f)); }));
            s.Add(WsDrive(1.5f, 1, u => WsAt(rlA), WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge); rlEnd = EditorApplication.timeSinceStartup;
                WsExpect(Get<bool>(wsWander, "_washActive"), "the wash branch active before the gathering");
                wbActivities.SetProgramVariable("participants", 1 << wsDog);
                wbActivities.SetProgramVariable("formation", 0);
                wbActivities.SetProgramVariable("anchor", CmGround(wsRoot.position + WsFlat(wsRoot.forward) * 5f));
                wbActivities.SetProgramVariable("yaw", 0f);
                wbActivities.SetProgramVariable("host", Networking.LocalPlayer.playerId);
                wbActivities.SetProgramVariable("startedMs", Networking.GetServerTimeInMilliseconds());
                wbActivities.SetProgramVariable("mode", 1);
                wbActivities.SetProgramVariable("revision", Get<int>(wbActivities, "revision") + 1);
                wbGatherSet = true;
            }));
            s.Add(WsUntil(() => !Get<bool>(wsWander, "_washActive") && !WsHold(), 2.0, () => "the gathering did not release the hold (washActive " + Get<bool>(wsWander, "_washActive") + ", hold " + WsHold() + ")"));
            s.Add(WsDo(() => WbCleanup()));   // the gathering ends well inside holdSeconds of the last stroke
            // until holdSeconds + 0.5 s after the last stroke: no stroke was made, so the dog must not stop again
            s.Add(now =>
            {
                if (Get<bool>(wsWander, "_washActive") || WsHold()) rlRestop = true;
                return now - rlEnd >= wsHoldSeconds + 0.5;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(!rlRestop, "no re-stop on the old stroke after the gathering ended inside holdSeconds (washActive / hold came back)");
                WsNote("gathering set right after the last stroke and ended once the hold was released (inside holdSeconds " + wsHoldSeconds + " s): hold and wash branch stayed off until last stroke + " + (wsHoldSeconds + 0.5f) + " s");
            }));
        }

        /// <summary>このファイルが立てた集合を畳む (失敗したケースの後にも次のケースの最初で呼ぶ)。</summary>
        static void WbCleanup()
        {
            if (!wbGatherSet || wbActivities == null) return;
            wbActivities.SetProgramVariable("mode", 0);
            wbActivities.SetProgramVariable("participants", 0);
            wbActivities.SetProgramVariable("revision", Get<int>(wbActivities, "revision") + 1);
            wbGatherSet = false;
        }
    }
}
