using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-035 洗い場へ犬を呼ぶ・留める — Course/100 の群 call (犬の側。設計 §5 の 100f のうち、自分の床を除く)。
    ///
    /// 枠・部品は Wash.cs (WsCase / WsDo / WsUntil / WsDrive / WsHold ...)。ケースの一覧への接続は書かない (統括が Wash.cs に群 call として
    /// MakeWashCallCases() を足す)。要求は洗い場 0 (主洗い場) の KH_WashStationCall へ ReqCall / ReqRelease / ReqLift を
    /// SendCustomNetworkEvent(Owner) で送る (ボードのボタンの _Call 等は、Owner の時は同じ裁定を直接呼ぶだけ。ClientSim は自分 = Owner)。
    /// 犬は柴 (wsDog)。交代の相手は柴の次の番号の犬。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        static UdonBehaviour wcStation, wcFetch, wcCatcher, wcActivities;
        static Transform wcAnchor, wcLiftRoot;
        static bool wcGatherSet;
        static float wcLiftBaseY;

        static void MakeWashCallCases()
        {
            wcStation = null; wcAnchor = null; wcLiftRoot = null; wcGatherSet = false;
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
            int me = Networking.LocalPlayer.playerId;
            int other = (wsDog + 1) % wander.Length;

            // ---- C-call-stay (呼ぶ → 来て立つ → 30 秒留まる。向きは standYaw) ----
            var s = WsCase("C-call-stay", 145);
            float csMaxOff = 0f, csMaxYaw = 0f; int csFrames0 = 0;
            WcSetup(s);
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() => { csFrames0 = Get<int>(wsWander, "_stationFrames"); }));
            s.Add(WsWait(2.0));   // the turn to standYaw (180 deg/s) is done
            double csAt = 0;
            s.Add(WsDo(() => { csAt = EditorApplication.timeSinceStartup; }));
            s.Add(now =>
            {
                WsExpect(Get<int>(wcStation, "heldDog") == wsDog, "the Shiba still held");
                // 2026-10-02: the keep-out never pushes the called dog (it is held; the walk out is never started)
                WsExpect(!Get<bool>(wsWander, "_leaveActive") && !Get<bool>(wsWander, "_leaveLowering"), "no walk out while held (keep-out exempt)");
                csMaxOff = Mathf.Max(csMaxOff, Flat(wsRoot.position - wcAnchor.position));
                csMaxYaw = Mathf.Max(csMaxYaw, Mathf.Abs(Mathf.DeltaAngle(wsRoot.eulerAngles.y, Get<float>(wcStation, "standYaw"))));
                if (csMaxOff > 0.35f) throw new InvalidOperationException("the Shiba left the stand (" + csMaxOff.ToString("F2") + " m from the anchor)");
                return EditorApplication.timeSinceStartup - csAt >= 30;
            });
            s.Add(WsDo(() =>
            {
                int fr = Get<int>(wsWander, "_stationFrames") - csFrames0;
                WsExpect(csMaxYaw < 10f, "faced standYaw (max off " + csMaxYaw.ToString("F1") + " deg)");
                WsExpect(fr > 300, "the station branch ran every frame (" + fr + " frames in ~32 s)");
                WsNote("called, arrived, stayed 30 s: max " + csMaxOff.ToString("F2") + " m from the anchor, max " + csMaxYaw.ToString("F1") + " deg from standYaw, " + fr + " station frames");
            }));

            // ---- C-lift (台を 0.3 m 上げるとルートの y が上がり、下げると戻る) ----
            s = WsCase("C-lift", 105);
            float lfY0 = 0f;
            WcSetup(s);
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsWait(1.0));
            s.Add(WsDo(() => { lfY0 = wsRoot.position.y; wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", 1); }));
            s.Add(WsWait(0.3)); s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", 1)));
            s.Add(WsWait(0.3)); s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", 1)));
            s.Add(WsWait(1.6));   // 0.3 m at 0.5 m/s + the settle
            s.Add(WsDo(() =>
            {
                float lift = Get<float>(wcStation, "lift"), dy = wsRoot.position.y - lfY0;
                WsExpect(Mathf.Abs(lift - 0.3f) < 0.01f, "synced lift 0.3 after 3 steps (" + lift.ToString("F2") + ")");
                if (wcLiftRoot != null) WsExpect(Mathf.Abs(wcLiftRoot.localPosition.y - wcLiftBaseY - 0.3f) < 0.02f, "liftRoot local y +0.3 (" + (wcLiftRoot.localPosition.y - wcLiftBaseY).ToString("F3") + ")");
                WsExpect(Mathf.Abs(dy - 0.3f) < 0.05f, "the Shiba's root y rose 0.3 m with the stand (" + dy.ToString("F3") + ")");
                WsExpect(Flat(wsRoot.position - wcAnchor.position) < 0.35f, "still on the anchor");
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", -1);
            }));
            // the Owner spaces lift steps 0.2 s apart (Codex 5): one request per 0.3 s
            s.Add(WsWait(0.3)); s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", -1)));
            s.Add(WsWait(0.3)); s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", -1)));
            s.Add(WsWait(1.8));
            s.Add(WsDo(() =>
            {
                float lift = Get<float>(wcStation, "lift"), dy = wsRoot.position.y - lfY0;
                WsExpect(lift < 0.01f, "lift back to 0 (" + lift.ToString("F2") + ")");
                WsExpect(Mathf.Abs(dy) < 0.05f, "the root back down (" + dy.ToString("F3") + ")");
                WsNote("3 x ReqLift(+1): lift 0.3, root +0.3 m on the anchor; 3 x ReqLift(-1): back to 0");
            }));

            // ---- C-gates (留まっている間: Fetch / フリスビー / 新しいリード接続を断る) ----
            s = WsCase("C-gates", 115);
            string cgWhy = "";
            WcSetup(s);
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() =>
            {
                WsExpect(!WsHold(), "no wash hold (the gates close for the station hold alone)");
                if (wcFetch != null && WsRef(wcFetch, "ballController") as UnityEngine.Object != null) wcFetch.SendCustomEvent("_OnBallThrown");
                if (wcCatcher != null)
                {
                    wcCatcher.SetProgramVariable("_canPlayWhy", "");
                    var lp = Networking.LocalPlayer;
                    Vector3 p0 = lp.GetPosition() + Vector3.up * 1.2f, v0 = WsFlat(wsRoot.position - lp.GetPosition()) * 6f + Vector3.up * 2f;
                    wcCatcher.SendCustomNetworkEvent(NetworkEventTarget.All, "ReqCatch", 0, p0, v0, Networking.GetServerTimeInSeconds(), lp.playerId, 0f);
                }
                pouch.SendCustomEvent("_WalkLeft" + wsDog);
            }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                if (wcFetch != null) WsExpect(Get<int>(wcFetch, "dogState") == 0, "Fetch refused (dogState " + Get<int>(wcFetch, "dogState") + ")");
                if (wcCatcher != null) { cgWhy = Get<string>(wcCatcher, "_canPlayWhy"); WsExpect(cgWhy == "washing", "CanPlay refused as washing (\"" + cgWhy + "\")"); }
                WsExpect(Get<int[]>(manager, "walkers")[wsDog] < 0, "the new leash refused (walker " + Get<int[]>(manager, "walkers")[wsDog] + ")");
                WsExpect(Get<int>(wcStation, "heldDog") == wsDog, "still held");
                WsNote("held on the stand, no wash hold: _OnBallThrown -> Idle" + (wcFetch == null ? " (no fetch: not checked)" : "") + ", ReqCatch -> \"" + cgWhy + "\"" + (wcCatcher == null ? " (no disc: not checked)" : "") + ", menu attach refused");
            }));

            // ---- C-release (2026-10-02「おわりにしたらすぐトコトコ出ていく」: 台 0.3 m から、おわりで台と一緒に下りて (はね落ちない)、
            //      座る・伏せる・止まって待つを挟まずに歩き出し、洗い場の立入りの円の外へ。人は台の横 1.5 m に立ったまま) ----
            s = WsCase("C-release", 125);
            s.Add(WsDo(() => wsFailDump = WcDump));   // round 5: a failure note carries the heap dump
            float rlLift = 0f, rlLimit = 0f, rlMaxDrop = 0f, rlMaxExcess = -1f, rlMaxGap = 0f, rlPrevT = 0f, rlMoveAt = -1f, rlOutAt = -1f; double rlAt = 0; Vector3 rlPrev = Vector3.zero, rlStart = Vector3.zero;
            bool rlShelfChecked = false;
            bool rlRest = false; string rlWhy = "";
            WcSetup(s);
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", 1)));
            s.Add(WsWait(0.3)); s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", 1)));
            s.Add(WsWait(0.3)); s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", 1)));
            s.Add(WsWait(1.6));   // 0.3 m at 0.5 m/s + the settle
            s.Add(WsDo(() =>
            {
                rlLift = Get<float>(wcStation, "lift");
                WsExpect(rlLift > 0.25f, "control: the stand raised before the release (" + rlLift.ToString("F2") + " m)");
                rlLimit = WcLeaveLimit(rlLift, true);
                rlPrev = wsRoot.position; rlStart = rlPrev; rlPrevT = Time.time;
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
                rlAt = EditorApplication.timeSinceStartup;
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0, 2, () => "ReqRelease did not release (heldDog " + Get<int>(wcStation, "heldDog") + ")"));
            s.Add(now =>
            {
                float t = (float)(now - rlAt);
                var p = wsRoot.position;
                // Round 3: a harness tick can span several frames (a stretched frame moves the stand 0.5 m/s x dt too), so the drop is
                // judged against the time it took: excess = drop - liftSpeed x (game time since the last tick) - 0.02 m. And while the
                // dog rides the stand (not easing itself), its root must stay on the anchor (gap = the pop itself)
                float tick = Mathf.Max(0f, Time.time - rlPrevT);
                float drop = rlPrev.y - p.y;
                rlMaxDrop = Mathf.Max(rlMaxDrop, drop);
                rlMaxExcess = Mathf.Max(rlMaxExcess, drop - Mathf.Max(0.05f, Get<float>(wcStation, "liftSpeed")) * tick - 0.02f);
                if (Get<bool>(wsWander, "_leaveLowering") && !Get<bool>(wsWander, "_leaveOwnEase")) rlMaxGap = Mathf.Max(rlMaxGap, Mathf.Abs(p.y - wcAnchor.position.y));
                rlPrev = p; rlPrevT = Time.time;
                bool inside = Flat(p - wcAnchor.position) < WcKeepOut();
                if (inside && (Get<int>(wsWander, "_restPhase") != 0 || Get<bool>(wsWander, "_wasLieDown") || Get<bool>(wsWander, "_wasSit")))
                { rlRest = true; rlWhy = "restPhase " + Get<int>(wsWander, "_restPhase") + ", lie " + Get<bool>(wsWander, "_wasLieDown") + ", sit " + Get<bool>(wsWander, "_wasSit") + " at +" + t.ToString("F2") + " s"; }
                if (t > 1.0f && rlMoveAt < 0f && !Get<bool>(wsWander, "_leaveActive") && !Get<bool>(wsWander, "_leaveLowering"))
                    throw new InvalidOperationException("no walk out started within 1 s of the release (selected " + Get<int>(wsWander, "_selectedBehavior") + ", washActive " + Get<bool>(wsWander, "_washActive") + ")");
                if (rlMoveAt < 0f && Flat(p - rlStart) >= 0.1f) rlMoveAt = t;
                // 2026-10-03 review: the walk-out point (after the NavMesh correction) does not lead past the shelf
                if (!rlShelfChecked && Get<bool>(wsWander, "_leaveActive") && !Get<bool>(wsWander, "_leaveLowering")) { rlShelfChecked = true; WcExpectShelfLine(p, Get<Vector3>(wsWander, "_leaveTarget")); }
                if (!inside && rlOutAt < 0f) rlOutAt = t;
                if (rlOutAt >= 0f) return true;
                if (t > rlLimit) throw new TimeoutException("the Shiba was still inside the keep-out (" + Flat(p - wcAnchor.position).ToString("F2") + " / " + WcKeepOut() + " m) " + rlLimit.ToString("F1") + " s after the release (moved at " + rlMoveAt.ToString("F2") + " s)");
                return false;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(Get<float>(wcStation, "lift") == 0f, "lift reset to 0 on release");
                WsExpect(!Get<bool>(wsWander, "_stationLocked"), "the dog side unlocked");
                WsExpect(!rlRest, "no rest / lie / sit inside the keep-out on the way out (" + rlWhy + ")");
                WsExpect(rlMaxExcess <= 0f, "rode the stand down: no pop (largest drop " + rlMaxDrop.ToString("F3") + " m in one tick, " + rlMaxExcess.ToString("F3") + " m beyond liftSpeed x tick + 0.02, from " + rlLift.ToString("F1") + " m)");
                WsExpect(rlMaxGap < 0.03f, "the root stayed on the anchor while the stand came down (largest gap " + rlMaxGap.ToString("F3") + " m)");
                float lowerT = rlLift / Mathf.Max(0.05f, Get<float>(wcStation, "liftSpeed"));
                WsExpect(rlMoveAt >= 0f && rlMoveAt <= lowerT + 1.0f, "walked off within the ride down (" + lowerT.ToString("F2") + " s) + 1 s (moved 0.1 m at " + rlMoveAt.ToString("F2") + " s)");
                WsNote("ReqRelease from " + rlLift.ToString("F1") + " m: rode down (max drop " + rlMaxDrop.ToString("F3") + " m/tick, excess " + rlMaxExcess.ToString("F3") + ", anchor gap " + rlMaxGap.ToString("F3") + "), moved at +" + rlMoveAt.ToString("F2") + " s, out of the " + WcKeepOut() + " m keep-out at +" + rlOutAt.ToString("F2")
                    + " s (limit " + rlLimit.ToString("F1") + " s = ride down + 1 + 1.5 x (radius + margin) / " + WcLeaveSpeed(true).ToString("F2") + " m/s + 2), me 1.5 m beside the stand; target " + Get<Vector3>(wsWander, "_leaveTarget").ToString("F1"));
                WsNote("dump: " + WcDump());
            }));

            // ---- C-swap (別の犬を呼ぶと交代) ----
            s = WsCase("C-swap", 155);
            WcSetup(s);
            s.Add(WsDo(() => { if (Get<int[]>(manager, "walkers")[other] >= 0) pouch.SendCustomEvent("_WalkOff" + other); }));
            WcCallAndArrive(s, wsDog, 60);
            // IMP-DOG-037: only dogs within callRange can be called; the other dog may be anywhere, so it is put by the gate first
            s.Add(WsDo(() => { WcBringNear(other); wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", other); }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") == other, 6, () => "dog " + other + " not held (heldDog " + Get<int>(wcStation, "heldDog") + "; busy dogs wait up to 5 s)"));
            s.Add(WsUntil(() => !Get<bool>(wsWander, "_stationLocked") && Get<int>(wsWander, "_stationIdx") < 0, 1.5, () => "the Shiba was not let go"));
            // 2026-10-02: replaced by another call = the Shiba's wash is over: it walks out of the keep-out (lift 0: no ride down)
            float swOut = -1f; double swAt = 0;
            s.Add(WsDo(() => { swAt = EditorApplication.timeSinceStartup; WsExpect(Get<bool>(wsWander, "_leaveActive"), "the replaced Shiba started its walk out"); }));
            s.Add(WsUntilF(() => Flat(wsRoot.position - wcAnchor.position) >= WcKeepOut(), () => WcLeaveLimit(0f, true),
                () => "the replaced Shiba still inside the keep-out (" + Flat(wsRoot.position - wcAnchor.position).ToString("F2") + " m)"));
            s.Add(WsDo(() => swOut = (float)(EditorApplication.timeSinceStartup - swAt)));
            s.Add(WsUntil(() => Get<bool>(wcStation, "arrived") && Flat(roots[other].position - wcAnchor.position) < 0.35f, 50,
                () => "dog " + other + " did not arrive (" + Flat(roots[other].position - wcAnchor.position).ToString("F1") + " m)"));
            s.Add(WsDo(() =>
            {
                WsNote("called dog " + other + " while the Shiba stood: the Shiba let go and was out of the keep-out " + swOut.ToString("F1") + " s later, dog " + other + " arrived");
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
            }));

            // ---- C-gather (集合で解放) ----
            s = WsCase("C-gather", 125);
            WcSetup(s);
            s.Add(WsDo(() => { if (wcActivities == null) throw new WsSkip("the Shiba's activities is not wired"); }));
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() =>
            {
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
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0 && Get<bool>(wsWander, "_groupCalled"), 2.0,
                () => "the gathering did not release the station (heldDog " + Get<int>(wcStation, "heldDog") + ", groupCalled " + Get<bool>(wsWander, "_groupCalled") + ")"));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                // 2026-10-02: an errand that releases the station wins; no walk out of the station is started
                WsExpect(!Get<bool>(wsWander, "_leaveActive") || !Get<bool>(wsWander, "_leaveFromStation"), "no walk out of the station after the gathering's release");
                WsNote("gathering accepted while on the stand: the station let the Shiba go (_ReleaseDog on the accept edge), groupCalled, no walk out (the errand wins)");
                WcCleanup();
            }));

            // ---- C-fetch-busy (Fetch 中は来ない: 待って、終われば来る / 5 秒で断る) ----
            s = WsCase("C-fetch-busy", 130);
            int fbRefuse0 = 0; bool fbBusyAtCheck = false;
            WcSetup(s);
            s.Add(WsDo(() =>
            {
                if (wcFetch == null || WsRef(wcFetch, "ballController") as UnityEngine.Object == null) throw new WsSkip("the Shiba's fetch / ball is not wired");
                WsExpect(Get<int>(wcFetch, "dogState") == 0, "Fetch idle before the throw");
                wcFetch.SendCustomEvent("_OnBallThrown");
            }));
            s.Add(WsUntil(() => Get<int>(wcFetch, "dogState") != 0, 2, () => "control: _OnBallThrown did not start a Fetch"));
            s.Add(WsDo(() => { fbRefuse0 = Get<int>(wcStation, "refuseSerial"); wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog); }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                fbBusyAtCheck = Get<int>(wcFetch, "dogState") != 0;
                if (!fbBusyAtCheck) throw new WsSkip("the Fetch ended within 1 s; the busy wait could not be observed");
                WsExpect(Get<int>(wcStation, "heldDog") != wsDog, "not held while fetching (waits)");
                WsExpect(Get<int>(wsWander, "_stationIdx") < 0, "the dog side did not start toward the station");
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") == wsDog || Get<int>(wcStation, "refuseSerial") != fbRefuse0, 6,
                () => "neither held after the Fetch nor refused after 5 s"));
            s.Add(WsDo(() =>
            {
                bool held = Get<int>(wcStation, "heldDog") == wsDog;
                if (!held) WsExpect(Get<int>(wcStation, "refusedReason") == KH_WashStationCall.RefuseBusy && Get<int>(wcStation, "refusedDog") == wsDog, "refused as busy for the Shiba");
                WsNote("ReqCall during a Fetch: not held at +1 s; then " + (held ? "held once the Fetch ended (within the 5 s wait)" : "refused as busy after 5 s"));
            }));
            WcFree(s);   // end the Fetch (and any hold) here, so the next cases start with an idle Shiba

            MakeWashCallReviewCases(other);
            MakeWashCallWaterCase();
            MakeWashKeepOutCases();   // 2026-10-02: free dogs keep out of the stations; a leashed one is not pushed
            MakeWashCallAvailCases(); // IMP-DOG-037 (WashCallAvail.cs): what the board shows per dog, the call range, the refusals
        }

        /// <summary>2026-10-02 user request 「洗い場には他の犬は勝手に近寄ってこない、台の上で休憩し始めたりもしない」
        /// (KD_DogWanderController.HillsideStationLeave.cs). Station 0 (main). The Shiba is free and unleashed unless stated.
        /// Round 3: the main station is part of this scene, so a missing one FAILs these cases (WcRequireMain), never SKIPs.</summary>
        static void MakeWashKeepOutCases()
        {
            // ---- C-keepout-free (円の中に置いた自由な犬は、休憩に入らずに円の外へ歩いて出る。休憩はすぐ入れる状態にしておく) ----
            var s = WsCase("C-keepout-free", 90);
            s.Add(WsDo(() => wsFailDump = WcDump));   // round 5: a failure note carries the heap dump
            Vector3 kfAt = Vector3.zero; float kfOutAt = -1f; double kfT0 = 0; bool kfRest = false, kfDumped = false; string kfWhy = ""; int kfExits0 = 0, kfDec0 = 0;
            WcRequireMain(s);
            WcSetup(s);
            s.Add(WsDo(() =>
            {
                // a walk out left over from an earlier case ends once the dog is clear of every circle (it is at the gate now)
                WsExpect(!Get<bool>(wsWander, "_leaveActive"), "control: no walk out running before the placement (left over from an earlier case)");
                // inside: 1.5 m in front of the duckboard (away from the shelf), on the ground; me far away (no paused stop)
                Vector3 front = WsFlat(wcAnchor.position - wcStation.transform.position);   // the Call sits at the shelf's centre
                kfAt = CmGround(wcAnchor.position + front * 1.5f);
                WcPlayerFarFrom(wcAnchor.position, 14f);
                wsRoot.SetPositionAndRotation(kfAt, Quaternion.LookRotation(front));
                var sync = wsRoot.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                wsPosHist.Clear();
                // a rest that is due right now (the run debt and the cooldown): only the keep-out keeps it from lying down here
                wsWander.SetProgramVariable("_breathRunAccum", 100f);
                wsWander.SetProgramVariable("_restReadyAt", 0f);
                wsWander.SetProgramVariable("_behaviorSelectionValid", false);
                kfExits0 = Get<int>(wsWander, "_keepOutExits");
                kfDec0 = Get<int>(wsWander, "_decisionCount");
                kfT0 = -1;   // round 7: taken from the first sample's clock (the WsDo's own read ran later in the same tick: "+-0.18 s")
            }));
            s.Add(now =>
            {
                if (kfT0 < 0) kfT0 = now;
                float t = (float)(now - kfT0);
                if (!kfDumped && t > 0.4f) { kfDumped = true; WsNote("dump +0.4 s after the placement: " + WcDump()); }   // round 5
                float dIn = Flat(wsRoot.position - wcAnchor.position);
                bool inside = dIn < WcKeepOut();
                // round 7: judged only on samples where the dog is inside AND has decided at least once since the placement (before
                // that the heap still holds the rest it had at the gate); the three values are read once, at this sample
                int rp = Get<int>(wsWander, "_restPhase"); bool lie = Get<bool>(wsWander, "_wasLieDown"), sit = Get<bool>(wsWander, "_wasSit");
                bool decided = Get<int>(wsWander, "_decisionCount") > kfDec0;
                if (inside && decided && (rp != 0 || lie || sit) && !kfRest)
                { kfRest = true; kfWhy = "restPhase " + rp + ", lie " + lie + ", sit " + sit + " at +" + t.ToString("F2") + " s, " + dIn.ToString("F2") + " m from the centre; " + WcDump(); }
                if (t > 1.5f && inside && !Get<bool>(wsWander, "_leaveActive"))
                    throw new InvalidOperationException("no walk out 1.5 s after being placed inside (selected " + Get<int>(wsWander, "_selectedBehavior") + ", restPhase " + Get<int>(wsWander, "_restPhase") + ")");
                if (!inside) { kfOutAt = t; return true; }
                float limit = 1.5f + WcLeaveLimit(0f, false);
                if (t > limit) throw new TimeoutException("still inside the keep-out " + limit.ToString("F1") + " s after (" + Flat(wsRoot.position - wcAnchor.position).ToString("F2") + " m)");
                return false;
            });
            double kfOutT = 0; float kfMin = 1e9f;
            s.Add(WsDo(() => kfOutT = EditorApplication.timeSinceStartup));
            s.Add(now => { kfMin = Mathf.Min(kfMin, Flat(wsRoot.position - wcAnchor.position)); return now - kfOutT >= 6.0; });
            s.Add(WsDo(() =>
            {
                WsExpect(!kfRest, "no rest / lie / sit inside the keep-out (" + kfWhy + ")");
                WsExpect(Get<int>(wsWander, "_keepOutExits") > kfExits0, "the walk out was the keep-out's (B) start (exits " + kfExits0 + " -> " + Get<int>(wsWander, "_keepOutExits") + ")");
                WsExpect(kfMin >= WcKeepOut() - 0.05f, "did not come back in within 6 s (closest " + kfMin.ToString("F2") + " m)");
                WsNote("placed 1.5 m in front of the duckboard with a rest due: walked out at +" + kfOutAt.ToString("F2") + " s (walk pace), no rest inside; then 6 s: closest "
                    + kfMin.ToString("F2") + " m, restPhase " + Get<int>(wsWander, "_restPhase") + " (a rest outside is allowed; this case does not test the wander, C-keepout-wander does)");
                WsNote("dump: " + WcDump());
            }));

            // ---- C-keepout-idle-lie (2026-10-03 review: 徘徊の窓の外の「足が止まったら伏せる」(idleLieDown) も円と縁の外 1.5 m の中では
            //      伏せない。円の中は出ていく歩きの休止中 (2 回失敗の後) を模擬。模擬: 窓を閉じる (wanderDurationMin/Max 0)・休憩を止める・
            //      休止の時刻・立ち寄り 0 をヒープへ。対照: 縁から 3.5 m 外では伏せる (この道が生きていることの証拠)) ----
            s = WsCase("C-keepout-idle-lie", 120);
            s.Add(WsDo(() => wsFailDump = WcDump));
            float ilSettle = 1f;
            WcRequireMain(s);
            WcSetup(s);
            s.Add(WsDo(() =>
            {
                WcSaveWander();
                wsWander.SetProgramVariable("idleLieDown", true);
                wsWander.SetProgramVariable("wanderDurationMin", 0f);
                wsWander.SetProgramVariable("wanderDurationMax", 0f);   // the window never opens: the idle lie-down path every decision
                wsWander.SetProgramVariable("_restReadyAt", 1e9f);       // the rest FSM stays out (it has its own check, C-keepout-free)
                wsWander.SetProgramVariable("dropByChance", 0f);         // no drop-by walk toward me
                WcInvalidateWander();
                ilSettle = Get<float>(wsWander, "restSettleTime");
                WcPlayerFarFrom(wcAnchor.position, 16f);
            }));
            WcIdleLiePhase(s, "inside the circle, walk-out paused", () => 2.0f, true, false, () => ilSettle);
            WcIdleLiePhase(s, "in the no-rest margin", () => WcKeepOut() + 0.8f, false, false, () => ilSettle);
            WcIdleLiePhase(s, "control beyond the margin", () => WcKeepOut() + Get<float>(wsWander, "stationRestMargin") + 2.0f, false, true, () => ilSettle);
            s.Add(WsDo(() => { WsNote("dump: " + WcDump()); WcRestoreWander(); }));

            // ---- C-keepout-wander (round 3, review 1 の回帰: 家を主洗い場の中心から 4.6 m (実際の犬の家と同じ距離) に置き、柴を円の反対側に
            //      置く。休憩なし・徘徊の窓を常に開け・目標点を 2 s ごとにして 60 s。円をまたぐ目標点・円の中への行き来が無いこと。模擬: 家・
            //      予定の値・休憩の時刻はハーネスが実行時のヒープ / Transform へ書く (WcCleanup が戻す)) ----
            s = WsCase("C-keepout-wander", 110);
            s.Add(WsDo(() => wsFailDump = WcDump));   // round 5: a failure note carries the heap dump
            double kwT0 = 0; float kwMin = 1e9f, kwHomeMin = 1e9f; int kwHops = 0, kwPrevHop = int.MinValue, kwPlayTicks = 0, kwDet0 = 0, kwStand0 = 0, kwExit0 = 0;
            Vector3 kwHome = Vector3.zero, kwEff = Vector3.zero; bool kwDumped = false, kwPre = false;
            WcRequireMain(s);
            WcSetup(s);
            s.Add(WsDo(() =>
            {
                Vector3 a = wcAnchor.position;
                Vector3 dir = WsFlat(nodes[0] - a);   // toward the gate: where the dogs' homes are
                kwHome = WcGroundNear(a + dir * 4.6f);
                Vector3 far = Vector3.zero; bool ok = false;
                foreach (float ang in new[] { 180f, 135f, -135f, 90f, -90f })   // the opposite side first
                {
                    try { far = CmGround(a + Quaternion.Euler(0f, ang, 0f) * dir * 4.8f, 1f); }
                    catch (InvalidOperationException) { continue; }
                    if (Flat(far - a) >= WcKeepOut() + 0.2f && Vector3.Angle(WsFlat(far - a), dir) >= 80f) { ok = true; break; }
                }
                if (!ok) throw new InvalidOperationException("no ground on the far side of the main station for the start");
                WcSaveWander();
                // Round 8 (full run 20261002-144815: the Shiba wandered round a home 31 m away): the roaming home is the leash manager's
                // synced releasedHomes[dog], re-applied to the areaCenter Transform by its Owner every 0.5 s (ApplyHomes), so a write to
                // the Transform alone is undone within 0.5 s. In 100f alone that home happened to be the gate (near the station), so the
                // case passed by chance. Write both; the precondition below checks the product's effective home
                WcSetHome(kwHome);
                kwEff = WcEffectiveHome(kwHome);
                float cyc = Get<float>(wsWander, "cyclePeriod");
                wsWander.SetProgramVariable("waypointPeriod", 2f);
                wsWander.SetProgramVariable("wanderDurationMin", cyc);
                wsWander.SetProgramVariable("wanderDurationMax", cyc);
                wsWander.SetProgramVariable("_restReadyAt", 1e9f);
                WcInvalidateWander();
                WcPlayerFarFrom(a, 16f);
                wsRoot.SetPositionAndRotation(far, Quaternion.LookRotation(WsFlat(kwHome - far)));
                var sync = wsRoot.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                kwDet0 = Get<int>(wsWander, "_wanderDetours"); kwStand0 = Get<int>(wsWander, "_wanderStands"); kwExit0 = Get<int>(wsWander, "_keepOutExits");
                kwT0 = EditorApplication.timeSinceStartup;
            }));
            s.Add(now =>
            {
                Vector3 p = wsRoot.position;
                float d = Flat(p - wcAnchor.position);
                if (!kwDumped && now - kwT0 > 0.3) { kwDumped = true; WsNote("dump +0.3 s after the placement: " + WcDump() + " (placed at " + Flat(p - wcAnchor.position).ToString("F2") + " m)"); }   // round 5
                // Round 8 precondition: within 4 s (two hops) the areaCenter is still the case's home (ApplyHomes did not undo it) and the
                // product's effective home (_dbgHome = KeepOutHome of it, written when a waypoint is computed) is the expected shifted one
                if (!kwPre)
                {
                    var hc = WsRef(wsWander, "areaCenter") as Transform;
                    float tDiff = hc != null ? Flat(hc.position - kwHome) : 1e9f, eDiff = Flat(Get<Vector3>(wsWander, "_dbgHome") - kwEff);
                    if (tDiff < 0.05f && eDiff < 0.15f) { kwPre = true; WsNote("precondition at +" + (now - kwT0).ToString("F1") + " s: home " + kwHome.ToString("F1") + ", effective (shifted) home " + kwEff.ToString("F1") + " (" + Flat(kwEff - wcAnchor.position).ToString("F1") + " m from the centre)"); }
                    else if (now - kwT0 > 4.0) throw new InvalidOperationException("precondition: the case's home did not take effect (areaCenter " + tDiff.ToString("F2") + " m off the case's home, effective home " + eDiff.ToString("F2") + " m off the expected " + kwEff.ToString("F1") + "; releasedHomes[dog] " + WcReleasedHome().ToString("F1") + ")");
                }
                if (Get<bool>(wsWander, "_playing")) kwPlayTicks++;   // a chase may cross a circle (exempt); counted, not judged
                else
                {
                    kwMin = Mathf.Min(kwMin, d);
                    if (d < WcKeepOut()) throw new InvalidOperationException("the wandering Shiba entered the keep-out (" + d.ToString("F2") + " m at +" + (now - kwT0).ToString("F1") + " s, hop " + Get<int>(wsWander, "_wanderTargetHop") + ", selected " + Get<int>(wsWander, "_selectedBehavior") + ")");
                }
                kwHomeMin = Mathf.Min(kwHomeMin, Flat(p - kwHome));
                int hop = Get<int>(wsWander, "_wanderTargetHop");
                if (hop != kwPrevHop) { kwHops++; kwPrevHop = hop; }
                return now - kwT0 >= 60.0;
            });
            s.Add(WsDo(() =>
            {
                int det = Get<int>(wsWander, "_wanderDetours") - kwDet0, stand = Get<int>(wsWander, "_wanderStands") - kwStand0, exits = Get<int>(wsWander, "_keepOutExits") - kwExit0;
                WsNote("dump: " + WcDump());
                WcRestoreWander();
                WsExpect(kwHops >= 10, "enough hops to exercise the wander (" + kwHops + " in 60 s)");
                WsExpect(kwHomeMin < Get<float>(wsWander, "areaRadius") + WcKeepOut(), "got round to the home side (closest to the home " + kwHomeMin.ToString("F1") + " m)");
                WsExpect(exits == 0, "no keep-out walk out was needed (the wander never led in; exits " + exits + ")");
                WsNote("home 4.6 m from the main station's centre, Shiba started on the far side: 60 s, " + kwHops + " hops, closest to the centre " + kwMin.ToString("F2") + " m (radius " + WcKeepOut()
                    + "), detours " + det + ", stood-out hops " + stand + ", play ticks " + kwPlayTicks + " (not judged), closest to the home " + kwHomeMin.ToString("F1") + " m. Simulated: home / waypointPeriod 2 s / window always open / no rest (heap)");
            }));

            // ---- C-keepout-probe (徘徊の目標点・じゃれ合いの共有の点が円の中に入らない。家 (areaCenter) を各洗い場の近くへ動かして
            //      200 ホップずつ直接調べる。模擬: 家を動かすのはハーネス (実行時の Transform。保存しない)) ----
            s = WsCase("C-keepout-probe", 60);
            WcRequireMain(s);
            WcSetup(s);
            s.Add(WsDo(() =>
            {
                var home = WsRef(wsWander, "areaCenter") as Transform;
                var calls = WsRef(wsWander, "washCalls") as Array;
                if (home == null || calls == null) throw new InvalidOperationException("the Shiba's areaCenter / washCalls is not wired");
                Vector3 home0 = home.position;
                int samples = 0, rawIn = 0, filtIn = 0, playIn = 0, playSamples = 0, stations = 0; float maxShift = 0f;
                var parts = new List<string>();
                try
                {
                    for (int i = 0; i < calls.Length; i++)
                    {
                        var u = calls.GetValue(i) as UdonBehaviour;
                        if (u == null) continue;
                        var anchor = WsRef(u, "anchor") as Transform;
                        if (anchor == null) continue;
                        stations++;
                        float r = Get<float>(u, "keepOutRadius");
                        // the home at the anchor, then 2 m and r m to the side: the raw 3 m wander disc lies mostly / partly inside
                        foreach (float off in new[] { 0f, 2f, r })
                        {
                            home.position = anchor.position + new Vector3(off, 0f, 0f);
                            wsWander.SetProgramVariable("_dbgKoHop0", 1000 * (i + 1) + (int)(off * 10f));
                            wsWander.SendCustomEvent("_DebugKeepOutProbe");
                            samples += Get<int>(wsWander, "_dbgKoSamples"); rawIn += Get<int>(wsWander, "_dbgKoRawIn");
                            filtIn += Get<int>(wsWander, "_dbgKoFilteredIn"); playIn += Get<int>(wsWander, "_dbgKoPlayIn");
                            playSamples += Get<int>(wsWander, "_dbgKoPlaySamples");
                            maxShift = Mathf.Max(maxShift, Get<float>(wsWander, "_dbgKoMaxShift"));
                        }
                        parts.Add("station " + Get<int>(u, "stationIndex") + " r " + r);
                    }
                }
                finally { home.position = home0; WcInvalidateWander(); }
                WsExpect(stations == 4, "all 4 stations exercised (" + stations + ")");
                WsExpect(samples > 0 && rawIn > 0, "control: raw waypoints inside a keep-out exist (" + rawIn + " / " + samples + ")");
                WsExpect(filtIn == 0, "no filtered wander waypoint inside a keep-out (" + filtIn + " / " + samples + ")");
                WsExpect(playSamples > 0, "the play part ran (the pack's shared anchors are set up; " + playSamples + " samples)");
                WsExpect(playIn == 0, "no play anchor (WanderAnchorAt / FleeHopTargetByHop) inside a keep-out (" + playIn + " / " + playSamples + ")");
                WsNote("home moved onto / 2 m / r m beside each of " + stations + " stations (" + string.Join(", ", parts) + "), 200 hops each: raw inside " + rawIn + " / " + samples
                    + ", filtered inside 0, play anchors inside 0 / " + playSamples + ", largest move " + maxShift.ToString("F2") + " m. Rest places: the rest lies where the dog stands (C-keepout-free covers it). Simulated: the home is moved by the harness");
            }));

            // ---- C-keepout-leash (リードの犬は円の中へ連れて来られても押し出されない。外すと猶予の後に出ていく) ----
            s = WsCase("C-keepout-leash", 150);
            s.Add(WsDo(() => wsFailDump = WcDump));   // round 5: a failure note carries the heap dump
            int klMe = Networking.LocalPlayer.playerId; float klMax = 0f; double klT0 = 0;
            const float KlGrace = 6f;
            int klGrace0 = 0;
            WcRequireMain(s);
            WcSetup(s);
            WcLeashShiba(s, klMe);
            s.Add(WsDo(() => klT0 = EditorApplication.timeSinceStartup));
            s.Add(now =>
            {
                WsExpect(Get<int[]>(manager, "walkers")[wsDog] == klMe, "still leashed");
                WsExpect(!Get<bool>(wsWander, "_leaveActive"), "no walk out while leashed");
                klMax = Mathf.Max(klMax, Flat(wsRoot.position - wcAnchor.position));
                return now - klT0 >= 8.0;
            });
            // Round 2 (coordinator 2026-10-02): unleashed inside -> stands for stationUnleashGrace (shortened to KlGrace s in the heap,
            // simulated), no rest; after it, walks out at the brisk gait (the wash is over)
            s.Add(WsDo(() =>
            {
                WsExpect(klMax < WcKeepOut(), "stayed with me inside the keep-out for 8 s (farthest " + klMax.ToString("F2") + " m)");
                WcSetGrace(KlGrace);
                klGrace0 = Get<int>(wsWander, "_graceFrames");
                pouch.SendCustomEvent("_WalkOff" + wsDog);
                klT0 = EditorApplication.timeSinceStartup;
            }));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] < 0, 3, () => "control: the leash did not come off"));
            s.Add(WsWait(0.5));   // round 4: the dog sees the unleash edge on its next decision (a frozen rest ends there)
            s.Add(now =>
            {
                WsExpect(Flat(wsRoot.position - wcAnchor.position) < WcKeepOut(), "stays inside during the grace");
                WsExpect(!Get<bool>(wsWander, "_leaveActive"), "no walk out during the grace");
                WsExpect(Get<int>(wsWander, "_restPhase") == 0 && !Get<bool>(wsWander, "_wasLieDown"), "no rest / lie-down during the grace (restPhase " + Get<int>(wsWander, "_restPhase") + ")");
                return now - klT0 >= KlGrace - 2f;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wsWander, "_graceStation") >= 0, "the grace is on a station (" + Get<int>(wsWander, "_graceStation") + ")");
                WsExpect(Get<int>(wsWander, "_graceFrames") > klGrace0, "the grace branch held the dog (frames " + (Get<int>(wsWander, "_graceFrames") - klGrace0) + ")");
                WsNote("dump at grace - 2 s: " + WcDump());
            }));
            bool klShelfChecked = false;
            s.Add(WsUntilF(() =>
            {
                // 2026-10-03 review: the walk-out point (after the NavMesh correction) does not lead past the shelf (the dog stands beside it)
                if (!klShelfChecked && Get<bool>(wsWander, "_leaveActive")) { klShelfChecked = true; WcExpectShelfLine(wsRoot.position, Get<Vector3>(wsWander, "_leaveTarget")); }
                return Flat(wsRoot.position - wcAnchor.position) >= WcKeepOut();
            }, () => KlGrace + 1.5f + WcLeaveLimit(0f, true),
                () => "unleashed inside the keep-out, the Shiba did not walk out after the grace (" + Flat(wsRoot.position - wcAnchor.position).ToString("F2") + " m)"));
            s.Add(WsDo(() =>
            {
                float t = (float)(EditorApplication.timeSinceStartup - klT0);
                WsExpect(t >= KlGrace - 0.5f, "did not leave before the grace ran out (" + t.ToString("F1") + " s)");
                WsExpect(Get<bool>(wsWander, "_leaveBrisk"), "left at the brisk gait (the wash is over)");
                WsNote("leashed and led to 1.5 m beside the stand: 8 s inside (farthest " + klMax.ToString("F2") + " m), no walk out; unleashed there with the grace shortened to " + KlGrace
                    + " s (simulated): stood without resting, out of the keep-out " + t.ToString("F1") + " s after the unleash (limit grace + 1.5 + " + WcLeaveLimit(0f, true).ToString("F1") + " s)");
                WsNote("dump: " + WcDump());
                WcRestoreGrace();
            }));

            // ---- C-keepout-unleash-wash (外して猶予の間に洗い始めると、洗われ中の保持へ渡り、猶予を過ぎても洗っている間は出ない) ----
            s = WsCase("C-keepout-unleash-wash", 160);
            s.Add(WsDo(() => wsFailDump = WcDump));   // round 5: a failure note carries the heap dump
            double uwT0 = 0; float uwHold = 3f;
            WcRequireMain(s);
            WcSetup(s);
            WcLeashShiba(s, klMe);
            s.Add(WsWait(1.0));
            s.Add(WsDo(() => { WcSetGrace(KlGrace); pouch.SendCustomEvent("_WalkOff" + wsDog); uwT0 = EditorApplication.timeSinceStartup; }));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] < 0, 3, () => "control: the leash did not come off"));
            s.Add(WsWait(1.5));   // within the grace
            // sponge on the flank for longer than the grace: the wash hold keeps the dog, the grace is restarted by each wash frame
            s.Add(WsDrive(KlGrace + 4f, 1, u =>
            {
                if (Get<bool>(wsWander, "_leaveActive")) throw new InvalidOperationException("walked out while being washed (" + (EditorApplication.timeSinceStartup - uwT0).ToString("F1") + " s after the unleash)");
                if (Flat(wsRoot.position - wcAnchor.position) >= WcKeepOut()) throw new InvalidOperationException("left the keep-out while being washed");
                return WsPath(0.4f + 0.2f * u);
            }, WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                WsExpect(WsHold() || Get<bool>(wsWander, "_washActive"), "under the wash hold after the strokes");
                WsExpect(Flat(wsRoot.position - wcAnchor.position) < WcKeepOut(), "still inside past the grace (" + (EditorApplication.timeSinceStartup - uwT0).ToString("F1") + " s after the unleash)");
                uwHold = Get<float>(wsWash, "holdSeconds");
                uwT0 = EditorApplication.timeSinceStartup;
            }));
            // Round 3: the limit is read when the step runs (it was 0 when the case was built)
            s.Add(WsUntilF(() => Flat(wsRoot.position - wcAnchor.position) >= WcKeepOut(), () => uwHold + KlGrace + 1.5f + WcLeaveLimit(0f, true),
                () => "after the last stroke, not out within the hold + the grace + the walk (" + Flat(wsRoot.position - wcAnchor.position).ToString("F2") + " m, limit " + (uwHold + KlGrace + 1.5f + WcLeaveLimit(0f, true)).ToString("F1") + " s)"));
            s.Add(WsDo(() =>
            {
                float t = (float)(EditorApplication.timeSinceStartup - uwT0);
                WsExpect(t >= KlGrace - 0.5f, "stayed for the grace counted from the last wash (" + t.ToString("F1") + " s)");
                WsNote("unleashed inside (grace " + KlGrace + " s, simulated), sponge from +1.5 s for " + (KlGrace + 4f) + " s: held by the wash, never walked out; after the last stroke out "
                    + t.ToString("F1") + " s later (hold " + uwHold + " s + grace " + KlGrace + " s from the last wash frame + the walk)");
                WcRestoreGrace();
            }));
        }

        /// <summary>2026-10-03 review: one phase of C-keepout-idle-lie. The Shiba placed `dist` m in front of the duckboard (away from the
        /// shelf), standing; `pause` = the walk-out's cool-down set (simulated: as after two failed walk-outs). For restSettleTime + 3 s
        /// it must not lie down (expectLie false), or must lie down by then (expectLie true, the control). Frames while playing are not
        /// judged (counted).</summary>
        static void WcIdleLiePhase(List<Func<double, bool>> s, string label, Func<float> dist, bool pause, bool expectLie, Func<float> settle)
        {
            double t0 = 0; bool lay = false; int playTicks = 0, dec0 = 0; float placed = 0f;
            s.Add(WsDo(() =>
            {
                Vector3 front = WsFlat(wcAnchor.position - wcStation.transform.position);
                Vector3 at = CmGround(wcAnchor.position + front * dist(), 1.5f);
                placed = Flat(at - wcAnchor.position);
                wsWander.SetProgramVariable("_keepOutRestUntil", pause ? Time.time + 60f : 0f);
                wsRoot.SetPositionAndRotation(at, Quaternion.LookRotation(front));
                var sync = wsRoot.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                wsWander.SetProgramVariable("_behaviorSelectionValid", false);
                t0 = -1; lay = false; playTicks = 0; dec0 = Get<int>(wsWander, "_decisionCount");
            }));
            s.Add(now =>
            {
                if (t0 < 0) t0 = now;
                if (Get<int>(wsWander, "_decisionCount") <= dec0) return false;   // the heap still holds the pose from before the placement
                if (Get<bool>(wsWander, "_playing")) { playTicks++; return now - t0 >= settle() + 3.0; }
                bool lie = Get<bool>(wsWander, "_wasLieDown");
                float d = Flat(wsRoot.position - wcAnchor.position), noRest = WcKeepOut() + Get<float>(wsWander, "stationRestMargin");
                if (lie) lay = true;
                if (!expectLie && lie && d < noRest)
                    throw new InvalidOperationException(label + ": lay down " + d.ToString("F2") + " m from the centre (no rest within " + noRest.ToString("F1") + " m) at +" + (now - t0).ToString("F1") + " s");
                if (expectLie && lay) return true;
                return now - t0 >= settle() + 3.0;
            });
            s.Add(WsDo(() =>
            {
                if (expectLie) WsExpect(lay, label + ": control - the idle lie-down did not happen " + placed.ToString("F1") + " m from the centre within restSettleTime + 3 s (the path under test is not live)");
                WsNote(label + ": placed " + placed.ToString("F2") + " m from the centre" + (pause ? " (walk-out paused)" : "") + ", " + (lay ? "lay down" : "stayed standing") + (playTicks > 0 ? ", play ticks " + playTicks : "") + "; walk-outs " + Get<int>(wsWander, "_keepOutExits"));
            }));
        }

        /// <summary>2026-10-03 review: the walk-out's straight line from -> to keeps clear of the shelf by the product's rule (ShelfBlocks:
        /// not closer than 0.8 m to the shelf's centre AND closer than the start, less 0.05 m).</summary>
        static void WcExpectShelfLine(Vector3 from, Vector3 to)
        {
            Vector3 sc = wcStation.transform.position;
            float abx = to.x - from.x, abz = to.z - from.z, apx = sc.x - from.x, apz = sc.z - from.z;
            float len2 = abx * abx + abz * abz;
            float t = len2 > 1e-6f ? Mathf.Clamp01((apx * abx + apz * abz) / len2) : 0f;
            float dx = apx - abx * t, dz = apz - abz * t;
            float near = Mathf.Sqrt(dx * dx + dz * dz), start = Flat(sc - from);
            WsExpect(!(near < 0.8f && near < start - 0.05f), "the walk-out line " + from.ToString("F1") + " -> " + to.ToString("F1") + " passes the shelf (" + near.ToString("F2") + " m from its centre, start " + start.ToString("F2") + " m)");
            WsNote("walk-out line clear of the shelf: closest " + near.ToString("F2") + " m (start " + start.ToString("F2") + " m), target " + to.ToString("F1"));
        }

        /// <summary>Round 3: these cases need the main station (part of this scene): its absence is a FAIL, not a SKIP.</summary>
        static void WcRequireMain(List<Func<double, bool>> s)
        {
            s.Add(WsDo(() => { if (wcStation == null || wcAnchor == null) throw new InvalidOperationException("the main wash station (KH_WashStationCall stationIndex 0, anchor) is missing in this scene"); }));
        }

        /// <summary>Round 3: like WsUntil, but the limit is evaluated when the step runs (a value computed by an earlier step).</summary>
        static Func<double, bool> WsUntilF(Func<bool> condition, Func<double> limit, Func<string> what)
        {
            return now =>
            {
                if (condition()) return true;
                double l = limit();
                if (now - wsStepAt > l) throw new TimeoutException(what() + " (" + l.ToString("F1") + " s)");
                return false;
            };
        }

        /// <summary>Round 3: the Shiba at the gate placement again, me beside it (a NavMesh point near it, else the raw point, as
        /// WcPlayerAtStation does), leashed to me, then me 1.5 m beside the stand and the Shiba led into the circle.</summary>
        static void WcLeashShiba(List<Func<double, bool>> s, int me)
        {
            s.Add(WsDo(() =>
            {
                WsPlaceShiba();
                Vector3 d = wsRoot.position, p = d + WsFlat(wsRoot.right) * 1.3f;
                try { p = CmGround(p); } catch (InvalidOperationException) { }
                CmTeleport(p, d - p);
                pouch.SendCustomEvent("_WalkLeft" + wsDog);
            }));
            s.Add(WsUntil(() => Get<int[]>(manager, "walkers")[wsDog] == me, 6, () => "control: the leash did not attach (walker " + Get<int[]>(manager, "walkers")[wsDog] + ", Shiba at " + wsRoot.position.ToString("F1") + ")"));
            s.Add(WsDo(() => WcPlayerAtStation()));
            s.Add(WsUntil(() => Flat(wsRoot.position - wcAnchor.position) < WcKeepOut() - 1f, 40,
                () => "the leashed Shiba did not follow me into the keep-out (" + Flat(wsRoot.position - wcAnchor.position).ToString("F1") + " m)"));
        }

        /// <summary>A ground point at p (NavMesh-checked by CmGround), else p itself.</summary>
        static Vector3 WcGroundNear(Vector3 p)
        {
            try { return CmGround(p); } catch (InvalidOperationException) { return p; }
        }

        // Round 3: the wander values C-keepout-wander writes (simulated), put back by WcCleanup (every WcSetup) and at its end
        static bool wcWanderSaved;
        static Vector3 wcHome0, wcReleased0;
        static float wcWaypoint0, wcWanderMin0, wcWanderMax0, wcDropBy0;
        static bool wcIdleLie0;
        static void WcSaveWander()
        {
            if (wcWanderSaved) return;
            wcHome0 = (WsRef(wsWander, "areaCenter") as Transform).position;
            wcReleased0 = WcReleasedHome();
            wcWaypoint0 = Get<float>(wsWander, "waypointPeriod"); wcWanderMin0 = Get<float>(wsWander, "wanderDurationMin"); wcWanderMax0 = Get<float>(wsWander, "wanderDurationMax");
            wcIdleLie0 = Get<bool>(wsWander, "idleLieDown"); wcDropBy0 = Get<float>(wsWander, "dropByChance");
            wcWanderSaved = true;
        }
        static void WcRestoreWander()
        {
            if (!wcWanderSaved || wsWander == null) return;
            WcSetHome(wcHome0);   // round 8: the Transform and the leash manager's releasedHomes[dog]
            WcSetReleased(wcReleased0);
            wsWander.SetProgramVariable("waypointPeriod", wcWaypoint0);
            wsWander.SetProgramVariable("wanderDurationMin", wcWanderMin0);
            wsWander.SetProgramVariable("wanderDurationMax", wcWanderMax0);
            wsWander.SetProgramVariable("_restReadyAt", 0f);
            wsWander.SetProgramVariable("idleLieDown", wcIdleLie0);
            wsWander.SetProgramVariable("dropByChance", wcDropBy0);
            wsWander.SetProgramVariable("_keepOutRestUntil", 0f);
            WcInvalidateWander();
            wcWanderSaved = false;
        }
        /// <summary>Round 8: the Shiba's roaming home where the product keeps it - the leash manager's synced releasedHomes[dog] (its Owner
        /// copies it onto the areaCenter Transform every 0.5 s, ApplyHomes) - and the Transform itself. ClientSim: I am that Owner.</summary>
        static void WcSetHome(Vector3 p)
        {
            WcSetReleased(p);
            var home = WsRef(wsWander, "areaCenter") as Transform;
            if (home != null) home.position = p;
        }
        static Vector3 WcReleasedHome() { var a = Get<Vector3[]>(manager, "releasedHomes"); return a != null && wsDog >= 0 && wsDog < a.Length ? a[wsDog] : Vector3.zero; }
        static void WcSetReleased(Vector3 p)
        {
            var a = Get<Vector3[]>(manager, "releasedHomes");
            if (a == null || wsDog < 0 || wsDog >= a.Length) throw new InvalidOperationException("the leash manager's releasedHomes is not readable");
            a = (Vector3[])a.Clone(); a[wsDog] = p;
            manager.SetProgramVariable("releasedHomes", a);
        }

        /// <summary>Round 8: KD_DogWanderController.KeepOutHome as the harness sees it: a home closer to a station's anchor than
        /// keepOutRadius + areaRadius + 0.3 (KeepOutPad) moves straight away from it to that distance.</summary>
        static Vector3 WcEffectiveHome(Vector3 home)
        {
            float disc = Get<float>(wsWander, "areaRadius");
            var calls = WsRef(wsWander, "washCalls") as Array;
            if (calls == null) return home;
            for (int k = 0; k < calls.Length; k++)
            {
                var u = calls.GetValue(k) as UdonBehaviour; if (u == null) continue;
                var an = WsRef(u, "anchor") as Transform; if (an == null) continue;
                float r = Get<float>(u, "keepOutRadius"); if (r <= 0f) continue;
                float need = r + disc + 0.3f;
                Vector3 d = home - an.position; d.y = 0f;
                if (d.magnitude >= need) continue;
                Vector3 dir = d.magnitude > 0.01f ? d.normalized : WsFlat(an.position - u.transform.position);
                home = new Vector3(an.position.x + dir.x * need, home.y, an.position.z + dir.z * need);
            }
            return home;
        }

        /// <summary>The wander's caches (the schedule, the filtered waypoint, the per-hop pass) recompute on the next decision.</summary>
        static void WcInvalidateWander()
        {
            if (wsWander == null) return;
            wsWander.SetProgramVariable("_wanderScheduleValid", false);
            wsWander.SetProgramVariable("_wanderTargetValid", false);
            wsWander.SetProgramVariable("_wanderKoValid", false);
            wsWander.SetProgramVariable("_wanderPassHop", int.MinValue);
            wsWander.SetProgramVariable("_behaviorSelectionValid", false);
        }

        // ---- 2026-10-02 round 5: one-line heap dump of the Shiba's keep-out / leave state (failure notes via wsFailDump, PASS notes) ----
        static Func<string> wsFailDump;   // set by a case's first step; WashTick appends it to a FAIL; cleared at each case start

        static string WcDumpGet<T>(UdonBehaviour u, string key, Func<T, string> fmt)
        {
            try { return fmt(Get<T>(u, key)); } catch (Exception) { return "?"; }
        }

        /// <summary>Owner, decisions, where the last decision returned (tickExit 1 leash, 2 candy, 3 no fetch, 4 Fetch / disc,
        /// 5 wash branch [washExit 1 station, 2 riding down, 3 wash hold], 6 continued, 7 reselected), the selected branch, the walk
        /// out (running / lowering / own ease / brisk / target / fails / pause and deadline from now / last fail reason), the last
        /// LeaveTarget (tried, rejected by shelf / NavMesh / too close after correction / river / player line, pick 1 clean 2 player
        /// 3 best-available 0 none, ring), the grace (circle, left), water, leash, rest, wash, play / paused, the circle the dog is in
        /// (harness: every washCalls anchor and keepOutRadius) and its distance, the product's last circle check, the shifted home.</summary>
        static string WcDumpWalker() { try { return Get<int[]>(manager, "walkers")[wsDog].ToString(); } catch (Exception) { return "?"; } }

        static string WcDump()
        {
            var w = wsWander;
            if (w == null || wsRoot == null) return "no Shiba bound";
            float now = Time.time;
            Func<float, string> f2 = v => v.ToString("F2");
            Func<float, string> rel = v => (v - now).ToString("F1");
            Func<bool, string> b = v => v ? "1" : "0";
            Func<int, string> i = v => v.ToString();
            Func<Vector3, string> v3 = v => "(" + v.x.ToString("F1") + "," + v.z.ToString("F1") + ")";
            Vector3 p = wsRoot.position;
            string inside = "none"; float best = float.MaxValue;
            var calls = WsRef(w, "washCalls") as Array;
            if (calls != null)
                for (int k = 0; k < calls.Length; k++)
                {
                    var u = calls.GetValue(k) as UdonBehaviour; if (u == null) continue;
                    var an = WsRef(u, "anchor") as Transform; if (an == null) continue;
                    float d = Flat(p - an.position), r = Get<float>(u, "keepOutRadius");
                    if (d - r < best) { best = d - r; inside = (d < r ? "IN " : "out ") + k + " d " + d.ToString("F2") + "/" + r; }
                }
            string home = "?";
            try { home = Flat(p - Get<Vector3>(w, "_dbgHome")).ToString("F1"); } catch (Exception) { }
            string fetch = wcFetch != null ? "fetch " + WcDumpGet<int>(wcFetch, "dogState", i) + " pose " + WcDumpGet<int>(wcFetch, "waterPose", i) : "fetch -";
            string disc = wcCatcher != null ? " disc " + WcDumpGet<int>(wcCatcher, "state", i) : "";
            string hold = "?"; try { hold = WsHold() ? "1" : "0"; } catch (Exception) { }
            return "owner " + WcDumpGet<bool>(w, "_dogOwner", b) + "/" + (Networking.IsOwner(wsRoot.gameObject) ? "1" : "0")
                + " | dec " + WcDumpGet<int>(w, "_decisionCount", i) + " tickExit " + WcDumpGet<int>(w, "_dbgTickExit", i) + " washExit " + WcDumpGet<int>(w, "_dbgWashExit", i)
                + " sel " + WcDumpGet<int>(w, "_selectedBehavior", i) + " selValid " + WcDumpGet<bool>(w, "_behaviorSelectionValid", b)
                + " | leave run " + WcDumpGet<bool>(w, "_leaveActive", b) + " low " + WcDumpGet<bool>(w, "_leaveLowering", b) + " ease " + WcDumpGet<bool>(w, "_leaveOwnEase", b)
                + " brisk " + WcDumpGet<bool>(w, "_leaveBrisk", b) + " tgt " + WcDumpGet<Vector3>(w, "_leaveTarget", v3) + " st " + WcDumpGet<int>(w, "_leaveStation", i)
                + " starts " + WcDumpGet<int>(w, "_leaveCount", i) + " B " + WcDumpGet<int>(w, "_keepOutExits", i) + " fails " + WcDumpGet<int>(w, "_leaveFails", i)
                + " failsAll " + WcDumpGet<int>(w, "_leaveTimeouts", i) + " lastFail " + WcDumpGet<int>(w, "_dbgLeaveFail", i)
                + " pause " + WcDumpGet<float>(w, "_keepOutRestUntil", rel) + " until " + WcDumpGet<float>(w, "_leaveUntil", rel)
                + " | LT tried " + WcDumpGet<int>(w, "_dbgLtTried", i) + " shelf " + WcDumpGet<int>(w, "_dbgLtShelf", i) + " nav " + WcDumpGet<int>(w, "_dbgLtNav", i)
                + " close " + WcDumpGet<int>(w, "_dbgLtClose", i) + " river " + WcDumpGet<int>(w, "_dbgLtRiver", i) + " player " + WcDumpGet<int>(w, "_dbgLtPlayer", i)
                + " pick " + WcDumpGet<int>(w, "_dbgLtPick", i) + " ring " + WcDumpGet<int>(w, "_dbgLtRing", i)
                + " | grace " + WcDumpGet<int>(w, "_graceStation", i) + " left " + WcDumpGet<float>(w, "_graceUntil", rel) + " washLast " + WcDumpGet<float>(w, "_washLastAt", rel)
                + " | water " + WcDumpGet<int>(w, "waterState", i) + " " + fetch + disc + " leash " + WcDumpGet<int>(w, "leashMode", i) + " walker " + WcDumpWalker()
                + " rest " + WcDumpGet<int>(w, "_restPhase", i) + " lie " + WcDumpGet<bool>(w, "_wasLieDown", b) + " washAct " + WcDumpGet<bool>(w, "_washActive", b) + " hold " + hold
                + " playPrev " + WcDumpGet<bool>(w, "_playingPrev", b) + " paused " + WcDumpGet<bool>(w, "_paused", b)
                + " | circle " + inside + " koK " + WcDumpGet<int>(w, "_dbgKoK", i) + " @" + WcDumpGet<float>(w, "_dbgKoAt", rel)
                + " home d " + home + " pos " + v3(p) + " t " + f2(now);
        }

        // ---- 2026-10-02 keep-out helpers ----
        static float WcKeepOut() { return Get<float>(wcStation, "keepOutRadius"); }

        /// <summary>The walk out's pace, as KD_DogWanderController.LeaveSpeed: brisk (the wash is over: A, or B after an unleash
        /// grace) = stationLeaveGait; else (a dog that drifted in) walkSpeed.</summary>
        static float WcLeaveSpeed(bool brisk)
        {
            int g = Get<int>(wsWander, "stationLeaveGait");
            float walk = Get<float>(wsWander, "walkSpeed"), run = Get<float>(wsWander, "runSpeed"), ras = Get<float>(wsWander, "runAnimSpeed");
            if (!brisk) return walk;
            return g >= 2 ? run : g == 1 ? Mathf.Min(run, ras + 0.1f) : walk;
        }

        /// <summary>Released from a stand at `lift` m: the ride down (lift / liftSpeed) + 1 s to start (a decision, the acceleration)
        /// + 1.5 x (keepOutRadius + stationLeaveMargin) at the walk out's pace (a path is longer than the straight line) + 2 s.</summary>
        static float WcLeaveLimit(float lift, bool brisk)
        {
            float liftSpeed = Mathf.Max(0.05f, Get<float>(wcStation, "liftSpeed"));
            float reach = WcKeepOut() + Mathf.Max(0.3f, Get<float>(wsWander, "stationLeaveMargin"));
            return lift / liftSpeed + 1f + 1.5f * reach / Mathf.Max(0.3f, WcLeaveSpeed(brisk)) + 2f;
        }

        // Round 2: the Shiba's stationUnleashGrace shortened in the heap for a case (simulated); WcCleanup (every WcSetup) puts it back
        static float wcGrace0 = -1f;
        static void WcSetGrace(float seconds)
        {
            if (wcGrace0 < 0f) wcGrace0 = Get<float>(wsWander, "stationUnleashGrace");
            wsWander.SetProgramVariable("stationUnleashGrace", seconds);
        }
        static void WcRestoreGrace()
        {
            if (wcGrace0 < 0f || wsWander == null) return;
            wsWander.SetProgramVariable("stationUnleashGrace", wcGrace0);
            wcGrace0 = -1f;
        }

        /// <summary>Me on the course node nearest `dist` m from p (no paused stop near the dog).</summary>
        static void WcPlayerFarFrom(Vector3 p, float dist)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++) { float x = Mathf.Abs(Flat(nodes[i] - p) - dist); if (x < bd) { bd = x; best = i; } }
            CmTeleport(nodes[best], p - nodes[best]);
        }

        /// <summary>Simulated: the Shiba's keep-out off (its cached circles zeroed in the heap) for cases that test something else at
        /// a station (the dirt block). WsKeepOutRestore lets it read the stations again.</summary>
        static void WsKeepOutOff()
        {
            if (wsWander == null) return;
            var calls = WsRef(wsWander, "washCalls") as Array;
            wsWander.SetProgramVariable("_koRadius", new float[calls != null ? calls.Length : 0]);
            wsWander.SetProgramVariable("_koReady", true);
            WcInvalidateWander();   // round 3: the filtered waypoint / the per-hop pass were made with the circles on
        }

        static void WsKeepOutRestore()
        {
            if (wsWander == null) return;
            wsWander.SetProgramVariable("_koReady", false);
            WcInvalidateWander();   // round 3: ... and with them off
        }

        /// <summary>Codex review 5: the re-call keeps the caller (1), a new choice cancels an older wait (6), an inactive station
        /// lets the dog go (3). Called from MakeWashCallCases' end (kept apart so the first seven stay as they were).</summary>
        static void MakeWashCallReviewCases(int other)
        {
            // ---- C-recall-caller (留めている犬の再呼び出しで呼び主が変わらない) ----
            var s = WsCase("C-recall-caller", 115);
            int rcMe = Networking.LocalPlayer.playerId;
            WcSetup(s);
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() =>
            {
                // ClientSim has one player: stand in for "someone else called it" by a fake caller id, with the Owner's 0.5 s
                // checks held off (the caller-left rule would release at once)
                wcStation.SetProgramVariable("_nextCheck", Time.time + 5f);
                wcStation.SetProgramVariable("callerId", -77);
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog);
            }));
            s.Add(WsWait(0.4));
            s.Add(WsDo(() =>
            {
                int c = Get<int>(wcStation, "callerId");
                wcStation.SetProgramVariable("callerId", rcMe);
                wcStation.SetProgramVariable("_nextCheck", 0f);
                WsExpect(c == -77, "the re-call did not overwrite the caller (" + c + ")");
                WsExpect(Get<int>(wcStation, "heldDog") == wsDog, "still held");
                WsNote("re-call of the held dog by me left the caller (-77 stand-in) unchanged");
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
            }));

            // ---- C-pending-cancel (A を留めて、忙しい B を呼び、A を選び直すと B の待ちが消える) ----
            s = WsCase("C-pending-cancel", 125);
            UdonBehaviour pcFetchB = null;
            WcSetup(s);
            s.Add(WsDo(() =>
            {
                pcFetchB = WsRef(wander[other], "fetchController") as UdonBehaviour;
                if (pcFetchB == null || WsRef(pcFetchB, "ballController") as UnityEngine.Object == null) throw new WsSkip("dog " + other + " has no fetch / ball");
                if (Get<int[]>(manager, "walkers")[other] >= 0) pouch.SendCustomEvent("_WalkOff" + other);
            }));
            WcCallAndArrive(s, wsDog, 60);
            // IMP-DOG-037: within callRange when the Fetch starts (the call is sent on its first frame), so the wait is a busy wait
            s.Add(WsDo(() => { WcBringNear(other); pcFetchB.SendCustomEvent("_OnBallThrown"); }));
            s.Add(WsUntil(() => Get<int>(pcFetchB, "dogState") != 0, 2, () => "control: dog " + other + " did not start a Fetch"));
            s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", other)));
            s.Add(WsWait(0.4));
            s.Add(WsDo(() =>
            {
                if (Get<int>(pcFetchB, "dogState") == 0) throw new WsSkip("dog " + other + "'s Fetch ended before the wait could be seen");
                WsExpect(Get<int>(wcStation, "_pendingDog") == other, "dog " + other + " waits (pending " + Get<int>(wcStation, "_pendingDog") + ")");
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog);
            }));
            s.Add(WsWait(0.4));
            s.Add(WsDo(() => WsExpect(Get<int>(wcStation, "_pendingDog") < 0, "choosing the held Shiba again cancelled the wait (pending " + Get<int>(wcStation, "_pendingDog") + ")")));
            s.Add(WsUntil(() => Get<int>(pcFetchB, "dogState") == 0, 30, () => "dog " + other + "'s Fetch did not end"));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wcStation, "heldDog") == wsDog, "the Shiba still held after dog " + other + " became free (heldDog " + Get<int>(wcStation, "heldDog") + ")");
                WsNote("Shiba held, ReqCall(" + other + ") while it fetched -> pending; ReqCall(Shiba) -> pending cleared; the Shiba stayed after the Fetch ended");
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
            }));

            // ---- C-inactive (無効にした洗い場は保持先にならない。再有効化で状態が清算される) ----
            s = WsCase("C-inactive", 125);
            // Round 3 (review 5): the stand is raised 0.3 m first. A switched-off station's Update no longer lowers its stand, so the
            // released dog must come down by itself (no pop) while the station is still off, then walk out of the circle
            float inBaseY = 0f, inMaxExcess = -1f, inPrevT = 0f; Vector3 inPrev = Vector3.zero; bool inOwnEase = false;
            WcSetup(s);
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", 1)));
            s.Add(WsWait(0.3)); s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", 1)));
            s.Add(WsWait(0.3)); s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", 1)));
            s.Add(WsWait(1.6));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<float>(wcStation, "lift") > 0.25f, "control: the stand raised before switching the station off");
                inBaseY = wcAnchor.position.y - (wcLiftRoot != null ? wcLiftRoot.localPosition.y - wcLiftBaseY : 0f);   // the anchor at height 0
                inPrev = wsRoot.position; inPrevT = Time.time;
                wcStation.gameObject.SetActive(false);
            }));
            s.Add(WsUntil(() => Get<int>(wsWander, "_stationIdx") < 0 && !Get<bool>(wsWander, "_stationLocked"), 1.5,
                () => "the dog side still treats the inactive station as its holder"));
            s.Add(WsUntilF(() =>
            {
                var p = wsRoot.position;
                inMaxExcess = Mathf.Max(inMaxExcess, (inPrev.y - p.y) - Mathf.Max(0.05f, Get<float>(wcStation, "liftSpeed")) * Mathf.Max(0f, Time.time - inPrevT) - 0.02f);
                inPrev = p; inPrevT = Time.time;
                if (Get<bool>(wsWander, "_leaveOwnEase")) inOwnEase = true;
                return !Get<bool>(wsWander, "_leaveLowering") && p.y <= inBaseY + 0.01f;
            }, () => 0.3 / Mathf.Max(0.05f, Get<float>(wcStation, "liftSpeed")) + 2.0,
                () => "the Shiba did not come down while the station was off (root y " + wsRoot.position.y.ToString("F3") + ", stand base " + inBaseY.ToString("F3") + ", lowering " + Get<bool>(wsWander, "_leaveLowering") + ")"));
            s.Add(WsDo(() =>
            {
                WsExpect(inOwnEase, "it eased itself down (the station's Update was off)");
                WsExpect(inMaxExcess <= 0f, "no pop on the way down (" + inMaxExcess.ToString("F3") + " m beyond liftSpeed x tick + 0.02)");
                wcStation.gameObject.SetActive(true);
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wcStation, "heldDog") < 0, "the station's hold was cleared by the disable / enable (heldDog " + Get<int>(wcStation, "heldDog") + ")");
                WsExpect(Get<int>(wsWander, "_stationIdx") < 0, "the Shiba is not taken back");
            }));
            s.Add(WsUntilF(() => Flat(wsRoot.position - wcAnchor.position) >= WcKeepOut(), () => WcLeaveLimit(0f, true),
                () => "after coming down, the Shiba did not walk out of the keep-out (" + Flat(wsRoot.position - wcAnchor.position).ToString("F2") + " m)"));
            s.Add(WsDo(() => WsNote("stand 0.3 m, station switched off: the Shiba let go at once, eased itself down to the ground (no pop) with the station off, walked out of the keep-out; re-enabled: heldDog -1")));
        }

        /// <summary>2026-10-01 user decision 「川で遊んでいる時は、呼んだら来てほしい」: C-water-call. The Shiba is put into its water
        /// play the water harness's way (placed within 25 m of the spot's entry, the first-play flags cleared: it starts by itself),
        /// then called. Expected: held at once (the play is not busy), out through the play's own exit, no swim height left, no
        /// new play while held, on the stand. Uses a station under a "River" object when there is one, else station 0.</summary>
        static void MakeWashCallWaterCase()
        {
            var s = WsCase("C-water-call", 180);
            UdonBehaviour spot = null, st = null; Transform anchor = null; double heldAt = 0; float landedAt = -1f; bool reentered = false, landedSeen = false;
            WcSetup(s);
            s.Add(WsDo(() =>
            {
                spot = WsRef(wsWander, "waterSpot") as UdonBehaviour;
                if (spot == null || wcFetch == null) throw new WsSkip("the Shiba has no waterSpot / fetchController");
                foreach (var u in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>())
                {
                    if (u.programSource == null || u.programSource.name != "KH_WashStationCall" || !u.gameObject.activeInHierarchy) continue;
                    bool river = false; for (var t = u.transform; t != null; t = t.parent) if (t.name.Contains("River")) { river = true; break; }
                    if (river || (st == null && Get<int>(u, "stationIndex") == 0)) st = u;
                    if (river) break;
                }
                if (st == null) throw new WsSkip("no active station");
                anchor = WsRef(st, "anchor") as Transform;
                if (anchor == null) throw new InvalidOperationException("the station's anchor is not wired");
                // the water harness's start: me and the dog near the entry, the first play due now
                Vector3 entry = Get<Vector3>(spot, "entry");
                UnityEngine.AI.NavMeshHit h;
                Vector3 toward = entry + (anchor.position - entry).normalized * 8f;
                if (!UnityEngine.AI.NavMesh.SamplePosition(toward, out h, 4f, UnityEngine.AI.NavMesh.AllAreas)) throw new InvalidOperationException("no NavMesh near the entry");
                var face = Quaternion.LookRotation(new Vector3(entry.x - h.position.x, 0f, entry.z - h.position.z));
                Networking.LocalPlayer.TeleportTo(h.position, face);
                UnityEngine.AI.NavMeshHit d;
                if (!UnityEngine.AI.NavMesh.SamplePosition(h.position + face * new Vector3(-.8f, 0f, .3f), out d, 2f, UnityEngine.AI.NavMesh.AllAreas)) throw new InvalidOperationException("dog placement lacks NavMesh");
                wsRoot.SetPositionAndRotation(d.position, face);
                var sync = wsRoot.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                wsWander.SetProgramVariable("_waterFirstDue", 0f);
                wsWander.SetProgramVariable("_waterFirstUsed", false);
            }));
            s.Add(WsUntil(() => Get<int>(wsWander, "waterState") == 2 && Get<int>(wcFetch, "waterPose") != 0, 60,
                () => "the Shiba did not start wading (waterState " + Get<int>(wsWander, "waterState") + ", pose " + Get<int>(wcFetch, "waterPose") + ")"));
            s.Add(WsWait(2.0));
            s.Add(WsDo(() => { st.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", wsDog); heldAt = EditorApplication.timeSinceStartup; }));
            s.Add(WsUntil(() => Get<int>(st, "heldDog") == wsDog, 2, () => "the call during the water play was not accepted (heldDog " + Get<int>(st, "heldDog") + ", refused " + Get<int>(st, "refusedDog") + "/" + Get<int>(st, "refusedReason") + ")"));
            s.Add(WsUntil(() =>
            {
                bool wet = Get<int>(wsWander, "waterState") != 0 || Get<int>(wcFetch, "waterPose") != 0;
                if (!wet && !landedSeen) { landedSeen = true; landedAt = (float)(EditorApplication.timeSinceStartup - heldAt); }
                if (landedSeen && wet) reentered = true;
                if (Get<int>(st, "heldDog") != wsDog) throw new InvalidOperationException("the hold was lost before arrival");
                return landedSeen && Get<bool>(st, "arrived") && Flat(wsRoot.position - anchor.position) < 0.35f;
            }, 120, () => "not out and on the stand (landed " + landedSeen + " at " + landedAt.ToString("F1") + " s, waterState " + Get<int>(wsWander, "waterState") + ", pose " + Get<int>(wcFetch, "waterPose") + ", " + Flat(wsRoot.position - anchor.position).ToString("F1") + " m)"));
            s.Add(WsDo(() =>
            {
                WsExpect(!reentered, "no new water play while held");
                WsExpect(!Get<bool>(wsWander, "_waterSwimming") && !Get<bool>(wsWander, "_swimYValid"), "no swim height left (WaterClearFloat)");
                WsNote("called while wading: held at once, landed " + landedAt.ToString("F1") + " s after the call (timeout 20 s), no re-entry, on the stand of " + st.gameObject.name);
                WcPlayerAt(st);   // round 6: ReqRelease is accepted only within operateRange (6 m) of that station
                st.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
                // Round 4: this case forced the first water play due (_waterFirstDue / _waterFirstUsed). Released beside the river
                // station, the free Shiba would start another play there and carry it into the next cases (run 20261002-135252:
                // C-keepout-free / -wander saw BehaviorWater 12 at the main station). No new play by itself for 10 min (simulated)
                wsWander.SetProgramVariable("_waterFirstUsed", true);
                wsWander.SetProgramVariable("_waterNextRoll", Time.time + 600f);
            }));
        }

        /// <summary>Common start: no gathering, the station released, the Shiba unleashed at the gate, me 1.5 m from the anchor.</summary>
        static void WcSetup(List<Func<double, bool>> s)
        {
            s.Add(WsDo(() =>
            {
                WcCleanup();
                if (wcStation == null) throw new WsSkip("no KH_WashStationCall with stationIndex 0 in the scene (Wash builder not run)");
                if (wcAnchor == null) throw new InvalidOperationException("station 0 anchor is not wired");
                var calls = WsRef(wsWander, "washCalls") as Array;
                if (calls == null || calls.Length == 0) throw new InvalidOperationException("the Shiba's washCalls is not wired");
                // Release / lift are accepted only from within operateRange (Codex 5): stand at the station first
                WcPlayerAtStation();
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
                WsPlaceShiba();
            }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0 && !WsHold(), 6, () => "the station / a wash hold did not clear before the case"));
            WcFree(s);
        }

        /// <summary>The Shiba Idle and held at no station: up to 30 s (releasing any station that holds it), then the Fetch is
        /// forced to Idle on the Owner's synced value (the same last resort as WashHeight.cs; ClientSim = the only player).</summary>
        // Round 6 (run 20261002-141941: the River station still held the Shiba from C-water-call - its ReqRelease came from a player
        // more than operateRange (6 m) from that station and the Owner refused it - so the next cases saw a dog walking 450 m back):
        // every active station that holds ANY dog is released through the product path - me 1.5 m beside THAT station's stand, then
        // ReqRelease, one at a time 1.1 s apart - and back beside the main station afterwards; a hold that remains FAILs with the dump.
        static void WcFree(List<Func<double, bool>> s)
        {
            double from = 0, sentAt = -10; bool forced = false, moved = false;
            s.Add(WsDo(() => { from = EditorApplication.timeSinceStartup; forced = false; sentAt = -10; moved = false; }));
            s.Add(now =>
            {
                bool held = false, send = now - sentAt >= 1.1;   // the Owner spaces releases 1 s apart (Codex 5)
                foreach (var u in WcStations())
                    if (u.gameObject.activeInHierarchy && Get<int>(u, "heldDog") >= 0)
                    {
                        held = true;
                        if (send) { WcPlayerAt(u); moved = true; u.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease"); sentAt = now; send = false; }
                    }
                // round 4: and no water play left running (its approach / exit outranks the keep-out walk and the wander)
                bool fetchIdle = wcFetch == null || Get<int>(wcFetch, "dogState") == 0;
                // round 5: the disc too (KH_FrisbeeCatch not Idle returns the dog's decision before any behaviour is chosen, like Fetch)
                bool discIdle = wcCatcher == null || Get<int>(wcCatcher, "state") == 0;
                bool idle = fetchIdle && discIdle && Get<int>(wsWander, "waterState") == 0;
                if (idle && !held) return true;
                if (now - from < 30.0) return false;
                if (!fetchIdle && Networking.IsOwner(wcFetch.gameObject)) { wcFetch.SetProgramVariable("dogState", 0); wcFetch.RequestSerialization(); forced = true; }
                return true;
            });
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                if (moved && wcStation != null && wcAnchor != null) WcPlayerAtStation();   // round 6: back beside the main station
                var still = new List<string>();
                foreach (var u in WcStations()) if (u.gameObject.activeInHierarchy && Get<int>(u, "heldDog") >= 0) still.Add("station " + Get<int>(u, "stationIndex") + " holds dog " + Get<int>(u, "heldDog"));
                WsExpect(still.Count == 0, "no station holds a dog before the case (" + string.Join(", ", still) + "; " + WcDump() + ")");
                if (forced) WsNote("the Shiba was still fetching after 30 s (an earlier case): dogState forced to Idle");
                if (wcFetch != null) WsExpect(Get<int>(wcFetch, "dogState") == 0, "the Shiba Idle before the case (dogState " + Get<int>(wcFetch, "dogState") + ")");
                WsExpect(Get<int>(wsWander, "waterState") == 0, "no water play left from an earlier case (waterState " + Get<int>(wsWander, "waterState") + ")");
                if (wcCatcher != null) WsExpect(Get<int>(wcCatcher, "state") == 0, "the disc catcher Idle before the case (state " + Get<int>(wcCatcher, "state") + "; " + WcDump() + ")");
            }));
        }

        static void WcCallAndArrive(List<Func<double, bool>> s, int dog, double limit)
        {
            s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", dog)));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") == dog, 6, () => "ReqCall(" + dog + ") not held (heldDog " + Get<int>(wcStation, "heldDog") + ", refused " + Get<int>(wcStation, "refusedDog") + "/" + Get<int>(wcStation, "refusedReason") + ")"));
            s.Add(WsUntil(() => Get<bool>(wcStation, "arrived") && Flat(roots[dog].position - wcAnchor.position) < 0.35f, limit,
                () => "dog " + dog + " did not arrive (" + Flat(roots[dog].position - wcAnchor.position).ToString("F1") + " m from the anchor, arrived " + Get<bool>(wcStation, "arrived") + ")"));
        }

        /// <summary>Round 6: every KH_WashStationCall in the scene (active or not).</summary>
        static List<UdonBehaviour> WcStations()
        {
            var r = new List<UdonBehaviour>();
            foreach (var u in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true))
                if (u.programSource != null && u.programSource.name == "KH_WashStationCall") r.Add(u);
            return r;
        }

        /// <summary>Round 6: me 1.5 m beside station u's stand (within its operateRange), facing it.</summary>
        static void WcPlayerAt(UdonBehaviour u)
        {
            var an = WsRef(u, "anchor") as Transform;
            if (an == null) return;
            Vector3 a = an.position;
            Vector3 side = Quaternion.Euler(0f, Get<float>(u, "standYaw") + 90f, 0f) * Vector3.forward;
            Vector3 p;
            try { p = CmGround(a + side * 1.5f); } catch (InvalidOperationException) { p = a + side * 1.5f; }
            CmTeleport(p, a - p);
        }

        static void WcPlayerAtStation()
        {
            Vector3 a = wcAnchor.position;
            Vector3 side = Quaternion.Euler(0f, Get<float>(wcStation, "standYaw") + 90f, 0f) * Vector3.forward;
            Vector3 p;
            try { p = CmGround(a + side * 1.5f); } catch (InvalidOperationException) { p = a + side * 1.5f; }
            CmTeleport(p, a - p);
        }

        static void WcCleanup()
        {
            WcRestoreGrace();   // round 2
            WcRestoreWander();  // round 3 (C-keepout-wander)
            WaCleanupStandIns();   // IMP-DOG-037 (WashCallAvail.cs): a remote player / its leash / its wash hold left by a failed case
            PaCleanup();           // audit 2026-10-07 Phase A (WashAuditA.cs): the simulated ticket, washes, arriveTimeout, my placed call
            if (!wcGatherSet || wcActivities == null) return;
            wcActivities.SetProgramVariable("mode", 0);
            wcActivities.SetProgramVariable("participants", 0);
            wcActivities.SetProgramVariable("revision", Get<int>(wcActivities, "revision") + 1);
            wcGatherSet = false;
        }
    }
}
