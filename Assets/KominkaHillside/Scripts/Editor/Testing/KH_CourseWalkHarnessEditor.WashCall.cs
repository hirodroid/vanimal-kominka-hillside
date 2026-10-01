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

            // ---- C-release (おわりで解放して歩き出す。台は 0 へ) ----
            s = WsCase("C-release", 125);
            float rlPath = 0f; Vector3 rlPrev = Vector3.zero;
            WcSetup(s);
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqLift", 1)));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease")));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0, 2, () => "ReqRelease did not release (heldDog " + Get<int>(wcStation, "heldDog") + ")"));
            s.Add(WsWait(0.3));   // the dog side sees the release edge on its next tick
            s.Add(WsDo(() =>
            {
                WsExpect(Get<float>(wcStation, "lift") == 0f, "lift reset to 0 on release");
                WsExpect(!Get<bool>(wsWander, "_stationLocked"), "the dog side unlocked");
                WsPlayerFar(12f);
                rlPrev = wsRoot.position;
            }));
            s.Add(WsUntil(() => { var p = wsRoot.position; rlPath += Flat(p - rlPrev); rlPrev = p; return rlPath >= 0.3f; }, 30,
                () => "the Shiba did not walk 0.3 m within 30 s after the release (" + rlPath.ToString("F2") + " m)"));
            s.Add(WsDo(() => WsNote("ReqRelease: heldDog -1, lift 0, unlocked; walked " + rlPath.ToString("F2") + " m after")));

            // ---- C-swap (別の犬を呼ぶと交代) ----
            s = WsCase("C-swap", 155);
            WcSetup(s);
            s.Add(WsDo(() => { if (Get<int[]>(manager, "walkers")[other] >= 0) pouch.SendCustomEvent("_WalkOff" + other); }));
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", other)));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") == other, 6, () => "dog " + other + " not held (heldDog " + Get<int>(wcStation, "heldDog") + "; busy dogs wait up to 5 s)"));
            s.Add(WsUntil(() => !Get<bool>(wsWander, "_stationLocked") && Get<int>(wsWander, "_stationIdx") < 0, 1.5, () => "the Shiba was not let go"));
            s.Add(WsUntil(() => Get<bool>(wcStation, "arrived") && Flat(roots[other].position - wcAnchor.position) < 0.35f, 50,
                () => "dog " + other + " did not arrive (" + Flat(roots[other].position - wcAnchor.position).ToString("F1") + " m)"));
            s.Add(WsDo(() =>
            {
                WsNote("called dog " + other + " while the Shiba stood: the Shiba let go, dog " + other + " arrived");
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
            s.Add(WsDo(() => { WsNote("gathering accepted while on the stand: the station let the Shiba go (_ReleaseDog on the accept edge), groupCalled"); WcCleanup(); }));

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
            s.Add(WsDo(() => pcFetchB.SendCustomEvent("_OnBallThrown")));
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
            WcSetup(s);
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() => wcStation.gameObject.SetActive(false)));
            s.Add(WsUntil(() => Get<int>(wsWander, "_stationIdx") < 0 && !Get<bool>(wsWander, "_stationLocked"), 1.5,
                () => "the dog side still treats the inactive station as its holder"));
            s.Add(WsDo(() => wcStation.gameObject.SetActive(true)));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wcStation, "heldDog") < 0, "the station's hold was cleared by the disable / enable (heldDog " + Get<int>(wcStation, "heldDog") + ")");
                WsExpect(Get<int>(wsWander, "_stationIdx") < 0, "the Shiba is not taken back");
                WsNote("station inactive: the Shiba let go at once; re-enabled: heldDog -1");
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
        static void WcFree(List<Func<double, bool>> s)
        {
            double from = 0, sentAt = -10; bool forced = false;
            s.Add(WsDo(() => { from = EditorApplication.timeSinceStartup; forced = false; sentAt = -10; }));
            s.Add(now =>
            {
                bool held = false, send = now - sentAt >= 1.1;   // the Owner spaces releases 1 s apart (Codex 5)
                foreach (var u in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>())
                    if (u.programSource != null && u.programSource.name == "KH_WashStationCall" && u.gameObject.activeInHierarchy && Get<int>(u, "heldDog") == wsDog)
                    { held = true; if (send) { u.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease"); sentAt = now; } }
                bool idle = wcFetch == null || Get<int>(wcFetch, "dogState") == 0;
                if (idle && !held) return true;
                if (now - from < 30.0) return false;
                if (!idle && Networking.IsOwner(wcFetch.gameObject)) { wcFetch.SetProgramVariable("dogState", 0); wcFetch.RequestSerialization(); forced = true; }
                return true;
            });
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                if (forced) WsNote("the Shiba was still fetching after 30 s (an earlier case): dogState forced to Idle");
                if (wcFetch != null) WsExpect(Get<int>(wcFetch, "dogState") == 0, "the Shiba Idle before the case (dogState " + Get<int>(wcFetch, "dogState") + ")");
            }));
        }

        static void WcCallAndArrive(List<Func<double, bool>> s, int dog, double limit)
        {
            s.Add(WsDo(() => wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", dog)));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") == dog, 6, () => "ReqCall(" + dog + ") not held (heldDog " + Get<int>(wcStation, "heldDog") + ", refused " + Get<int>(wcStation, "refusedDog") + "/" + Get<int>(wcStation, "refusedReason") + ")"));
            s.Add(WsUntil(() => Get<bool>(wcStation, "arrived") && Flat(roots[dog].position - wcAnchor.position) < 0.35f, limit,
                () => "dog " + dog + " did not arrive (" + Flat(roots[dog].position - wcAnchor.position).ToString("F1") + " m from the anchor, arrived " + Get<bool>(wcStation, "arrived") + ")"));
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
            if (!wcGatherSet || wcActivities == null) return;
            wcActivities.SetProgramVariable("mode", 0);
            wcActivities.SetProgramVariable("participants", 0);
            wcActivities.SetProgramVariable("revision", Get<int>(wcActivities, "revision") + 1);
            wcGatherSet = false;
        }
    }
}
