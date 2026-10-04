using System;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 §15.5 V7-controls-drone (ClientSim, one client + a puppet; simulated input, never a pass of real networking).
    ///   1  じぶん: [遠く] moves the wanted camera ×1.25 from the subject; [遠く] / [近く] / [↑] / [↓] stop at their limits; [←] turns the offset 20°;
    ///      [向き▲] tilts the camera (its position stays), limited to fov / 2 − 3°; [戻す] restores this mode
    ///   2  定点 (not movable): [遠く] / [↑] / [←] change nothing and the camera stays at its data position; [向き▲] still tilts
    ///   3  close / reopen: every mode back to the defaults, the drone shown
    ///   4  my drone: shown at the camera's pose after a tick, on a layer the video camera does not draw; nothing sent while filming only me
    ///   5  [ドローン: 見せない]: hidden and not sent; hidden near my head (droneHideNearMe raised for one tick)
    ///   6  filming the puppet with a grant (V6 helpers): the pose is sent for (puppet, seq) about 5 times a second; [見せない] stops it at once;
    ///      [取り消す] (session ended) stops it
    ///   7  me as the subject (the puppet "films" me: its row, its declaration and its drone's synced values written into its PlayerObject):
    ///      its drone appears on my screen and eases to the pose; hidden when the pose is within 1 m of my head, when shown = false,
    ///      when its declaration no longer matches, and the moment I revoke the row
    /// Not made here (two real clients): S really seeing F's drone move at 5 Hz, a third player never seeing it, late joiners, Quest / phone.
    /// </summary>
    public static partial class KH_VideoHarnessEditor
    {
        const string C7 = "V7-controls-drone";
        static UdonBehaviour v7MyDrone, v7PDrone;
        static Vector3 v7Off, v7From, v7To;
        static int v7Sends, v7Frame;

        static UdonBehaviour DroneOf(UdonBehaviour box)
        {
            if (box == null) return null;
            return box.GetComponentsInChildren<UdonBehaviour>(true).FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_FilmDrone");
        }

        static float[] Cv(string field) { return Get<float[]>(field); }
        static Vector3 WantOff() { return Get<Vector3>("_wantPos") - Get<Vector3>("_subject"); }
        static bool DroneVisible(UdonBehaviour d) { var r = d != null ? d.GetComponent<Renderer>() : null; return r != null && r.enabled; }

        static void Press(string evt, int times) { for (int i = 0; i < times; i++) uVideo.SendCustomEvent(evt); }

        /// <summary>Wait for the next render tick (so _wantPos / the drone reflect the last press).</summary>
        static void AddTick(int n)
        {
            steps.Add(Do(C7, () => { rendersMark = Renders(); }));
            steps.Add(Until(C7, () => Renders() >= rendersMark + n, 3, "no render tick"));
        }

        static void MakeV7Steps()
        {
            string keepCase = C6; C6 = C7;   // the V6 helpers tag their steps with C6
            steps.Add(Do(C7, () =>
            {
                v6Box = V6BoxOf(Networking.LocalPlayer);
                if (v6Box == null) { Fail("my KH_TravelMailbox PlayerObject is missing (Course/85)"); return; }
                v7MyDrone = DroneOf(v6Box);
                if (v7MyDrone == null) { Fail("no KH_FilmDrone under my mailbox (Video/6)"); return; }
                var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true);
                v6Notice = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_TravelNotice");
                v6Pouch = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_TreatPouch");
                v6Me = Networking.LocalPlayer.playerId;
                v6Active = true;
                v6Box.SetProgramVariable("debugJoinLoopback", 1);
                Open(); uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeSelfFollow);
            }));
            AddTick(5);
            // ---- 1. じぶん ----
            steps.Add(Do(C7, () => { v7Off = WantOff(); uVideo.SendCustomEvent("_CtlFar"); }));
            AddTick(2);
            steps.Add(Do(C7, () =>
            {
                float ratio = WantOff().magnitude / Mathf.Max(0.01f, v7Off.magnitude);
                Expect(Mathf.Abs(ratio - KH_VideoCameraEditor.CtlDistStep) < 0.05f, "1: [遠く] put the wanted camera ×" + ratio.ToString("F3") + " from the subject (want " + KH_VideoCameraEditor.CtlDistStep + ")");
                int m = KH_VideoCamera.ModeSelfFollow;
                Press("_CtlFar", 12); Expect(Mathf.Abs(Cv("_cvDist")[m] - KH_VideoCameraEditor.CtlDistMax) < 1e-3f, "1: [遠く] stops at ×" + KH_VideoCameraEditor.CtlDistMax);
                Press("_CtlNear", 20); Expect(Mathf.Abs(Cv("_cvDist")[m] - KH_VideoCameraEditor.CtlDistMin) < 1e-3f, "1: [近く] stops at ×" + KH_VideoCameraEditor.CtlDistMin);
                Press("_CtlUp", 20); Expect(Mathf.Abs(Cv("_cvUp")[m] - KH_VideoCameraEditor.CtlUpMax) < 1e-3f, "1: [↑] stops at +" + KH_VideoCameraEditor.CtlUpMax + " m");
                Press("_CtlDown", 30); Expect(Mathf.Abs(Cv("_cvUp")[m] - KH_VideoCameraEditor.CtlUpMin) < 1e-3f, "1: [↓] stops at " + KH_VideoCameraEditor.CtlUpMin + " m");
                uVideo.SendCustomEvent("_CtlReset");
                Expect(Cv("_cvDist")[m] == 1f && Cv("_cvUp")[m] == 0f && Cv("_cvYaw")[m] == 0f && Cv("_cvPitch")[m] == 0f, "1: [戻す] restores this mode");
                v7Off = WantOff();
                uVideo.SendCustomEvent("_CtlLeft");
            }));
            AddTick(2);
            steps.Add(Do(C7, () =>
            {
                Vector3 a = v7Off, b = WantOff(); a.y = 0f; b.y = 0f;
                float ang = Vector3.Angle(a, b);
                Expect(Mathf.Abs(ang - KH_VideoCameraEditor.CtlYawStep) < 3f, "1: [←] turned the camera " + ang.ToString("F1") + "° around the subject (want " + KH_VideoCameraEditor.CtlYawStep + ")");
                uVideo.SendCustomEvent("_CtlReset");
                Press("_CtlTiltUp", 10);
                Expect(Mathf.Abs(Cv("_cvPitch")[KH_VideoCamera.ModeSelfFollow] - KH_VideoCameraEditor.CtlPitchMax) < 1e-3f, "1: [向き▲] stops at +" + KH_VideoCameraEditor.CtlPitchMax + "°");
            }));
            AddTick(30);   // ~1 s: the tilt eases in
            steps.Add(Do(C7, () =>
            {
                Vector3 look = Get<Vector3>("_look") - cam.transform.position;
                float tilt = Vector3.SignedAngle(look, cam.transform.forward, cam.transform.right);
                float limit = cam.fieldOfView * 0.5f - 3f;
                Expect(-tilt > 0.5f && Mathf.Abs(tilt) <= limit + 0.5f, "1: the camera tilts up (" + (-tilt).ToString("F1") + "°) and stays within fov / 2 − 3° (" + limit.ToString("F1") + "°)");
                Expect(Roll() < 0.01f, "1: roll 0 with the tilt");
                uVideo.SendCustomEvent("_CtlReset");
                // ---- 2. 定点 ----
                uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeSpot);
            }));
            AddTick(3);
            steps.Add(Do(C7, () =>
            {
                int m = KH_VideoCamera.ModeSpot;
                Press("_CtlFar", 2); Press("_CtlUp", 2); Press("_CtlLeft", 2);
                Expect(Cv("_cvDist")[m] == 1f && Cv("_cvUp")[m] == 0f && Cv("_cvYaw")[m] == 0f, "2: 定点 ignores [遠く] [↑] [←]");
                uVideo.SendCustomEvent("_CtlTiltUp");
                Expect(Mathf.Abs(Cv("_cvPitch")[m] - KH_VideoCameraEditor.CtlPitchStep) < 1e-3f, "2: [向き▲] still works in 定点");
                var imgs = Get<UnityEngine.UI.Image[]>("ctlImages");
                Expect(imgs != null && imgs.Length == 9 && imgs[0].color != imgs[8].color, "2: the movable controls are dimmed in 定点 (tilt / reset are not)");
            }));
            AddTick(3);
            steps.Add(Do(C7, () =>
            {
                var sp = Get<Vector3[]>("spotPositions"); int k = Get<int>("_spot");
                Expect(sp != null && k >= 0 && k < sp.Length && (cam.transform.position - sp[k]).magnitude < 0.05f, "2: the camera stays at its data position");
                // ---- 3. close / reopen ----
                uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeSelfFollow); Press("_CtlFar", 3); uVideo.SendCustomEvent("_ToggleDrone");
                Close(); Open(); uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeSelfFollow);
                Expect(Cv("_cvDist").All(x => x == 1f) && Cv("_cvPitch").All(x => x == 0f) && Get<bool>("_droneShow"), "3: reopening restores every mode's controls and shows the drone");
                v7Sends = Get<int>("_droneSends");
            }));
            // ---- 4. my drone ----
            AddTick(3);
            steps.Add(Do(C7, () =>
            {
                Expect(DroneVisible(v7MyDrone) && (v7MyDrone.transform.position - cam.transform.position).magnitude < 0.01f && Quaternion.Angle(v7MyDrone.transform.rotation, cam.transform.rotation) < 0.5f,
                    "4: my drone is at the camera's pose");
                Expect((cam.cullingMask & (1 << v7MyDrone.gameObject.layer)) == 0, "4: the video camera does not draw the drone's layer " + v7MyDrone.gameObject.layer);
                Expect(Get<int>("_droneSends") == v7Sends && !V6<bool>(v7MyDrone, "shown"), "4: filming only me: nothing sent (shown false)");
                // ---- 5. hidden ----
                uVideo.SendCustomEvent("_ToggleDrone");
                Expect(!DroneVisible(v7MyDrone) && !Get<bool>("_droneShow"), "5: [見せない] hides it at once");
            }));
            AddTick(3);
            steps.Add(Do(C7, () =>
            {
                Expect(!DroneVisible(v7MyDrone), "5: still hidden after render ticks");
                uVideo.SendCustomEvent("_ToggleDrone");
                uVideo.SetProgramVariable("droneHideNearMe", 1000f);   // simulated: "near my head" for one check
            }));
            AddTick(2);
            steps.Add(Do(C7, () =>
            {
                Expect(!DroneVisible(v7MyDrone), "5: hidden when within droneHideNearMe of my head");
                uVideo.SetProgramVariable("droneHideNearMe", KH_VideoCameraEditor.DroneHideNearMe);
            }));
            // ---- 6. filming the puppet: the pose is sent ----
            AddSpawn("KH_DroneTarget");
            AddAsk("6");
            AddInUse("6");
            steps.Add(Do(C7, () => { v7Sends = Get<int>("_droneSends"); timeMark = Time.realtimeSinceStartup; }));
            steps.Add(Until(C7, () => Time.realtimeSinceStartup - timeMark > 1.0f, 2, "timer"));
            steps.Add(Do(C7, () =>
            {
                int n = Get<int>("_droneSends") - v7Sends;
                Expect(n >= 3 && n <= 7, "6: about 5 sends per second while filming the puppet (" + n + " in 1 s)");
                Expect(V6<bool>(v7MyDrone, "shown") && V6<int>(v7MyDrone, "forTarget") == v6Remote.playerId && V6<int>(v7MyDrone, "forSeq") == v6SeqA
                    && (V6<Vector3>(v7MyDrone, "camPos") - cam.transform.position).magnitude < 2f, "6: the synced pose is for (puppet, seq) and near the camera");
                uVideo.SendCustomEvent("_ToggleDrone");
                Expect(!V6<bool>(v7MyDrone, "shown"), "6: [見せない] stops sending at once (shown false)");
                v7Sends = Get<int>("_droneSends");
            }));
            AddTick(10);
            steps.Add(Do(C7, () =>
            {
                Expect(Get<int>("_droneSends") == v7Sends, "6: nothing sent while hidden");
                uVideo.SendCustomEvent("_ToggleDrone");
            }));
            AddTick(10);
            steps.Add(Do(C7, () =>
            {
                Expect(V6<bool>(v7MyDrone, "shown"), "6: shown again → sent again");
                uVideo.SendCustomEvent("_PersonCancel");
                Expect(!V6<bool>(v7MyDrone, "shown"), "6: [取り消す] (session ended) → shown false");
                Table(v6RBox, -1, 0);
            }));
            // ---- 6b (Codex review 1). the table goes first, before the mailbox's next FilmCheck, between render ticks: the send stops that frame ----
            AddAsk("6b"); AddInUse("6b");
            AddTick(8);
            steps.Add(Do(C7, () =>
            {
                Expect(Get<bool>("_droneSending") && V6<bool>(v7MyDrone, "shown"), "6b: sending while in use");
                v6Box.SetProgramVariable("_filmNextCheck", Time.time + 30f);   // simulated: the mailbox has not run its FilmCheck yet (phase still in use)
                uVideo.SetProgramVariable("_nextRender", Time.time + 5f);       // simulated: between render ticks
                Table(v6RBox, -1, 0);
                v7Sends = Get<int>("_droneSends"); v7Frame = Time.frameCount;
            }));
            steps.Add(Until(C7, () => Time.frameCount > v7Frame, 2, "frame"));
            steps.Add(Do(C7, () =>
            {
                Expect(Phase() == KH_TravelMailbox.FilmInUse, "6b: the session is still 'in use' in the mailbox (FilmCheck held back)");
                Expect(!Get<bool>("_droneSending") && !V6<bool>(v7MyDrone, "shown") && Get<int>("_droneSends") == v7Sends, "6b: the frame after the grant row went, the send stopped (shown false), nothing more sent");
                v6Box.SetProgramVariable("_filmNextCheck", 0f); uVideo.SetProgramVariable("_nextRender", Time.time);
            }));
            steps.Add(Until(C7, () => Phase() == KH_TravelMailbox.FilmNone, 2, "6b: the session did not end"));
            // ---- 6c. other ends: × closes, choosing myself (a target change), the puppet leaving: shown false and no more sends ----
            AddAsk("6c ×"); AddInUse("6c ×"); AddTick(8);
            steps.Add(Do(C7, () =>
            {
                Expect(V6<bool>(v7MyDrone, "shown"), "6c ×: sending");
                Close();
                Expect(!V6<bool>(v7MyDrone, "shown") && !DroneVisible(v7MyDrone), "6c ×: closing the monitor stops the send and hides my drone");
                Table(v6RBox, -1, 0); Open(); uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeSelfFollow);
            }));
            AddAsk("6c self"); AddInUse("6c self"); AddTick(8);
            steps.Add(Do(C7, () =>
            {
                Expect(V6<bool>(v7MyDrone, "shown"), "6c self: sending");
                uVideo.SendCustomEvent("_PersonList"); uVideo.SendCustomEvent("_PersonPick0");   // cell 0 = me: a target change
                Expect(!V6<bool>(v7MyDrone, "shown") && Phase() == KH_TravelMailbox.FilmNone, "6c self: choosing myself ends the session and stops the send");
                Table(v6RBox, -1, 0);
            }));
            AddAsk("6c leave"); AddInUse("6c leave"); AddTick(8);
            steps.Add(Do(C7, () =>
            {
                Expect(V6<bool>(v7MyDrone, "shown"), "6c leave: sending");
                ClientSimMain.RemovePlayer(v6Remote);
            }));
            steps.Add(Until(C7, () => !V6<bool>(v7MyDrone, "shown") && Phase() == KH_TravelMailbox.FilmNone, 3, "6c leave: the send did not stop after the puppet left"));
            steps.Add(Do(C7, () =>
            {
                Note("6c: a transit timeout never sends (sending needs 'in use'); real two-client delivery of shown = false is for two clients");
                // ---- 6d (Codex review 3). height + distance near the subject: the minimum distance holds on the final position ----
                int m = KH_VideoCamera.ModeSelfFollow;
                Press("_CtlNear", 10); Press("_CtlDown", 10);
                uVideo.SetProgramVariable("ctlMinDist", 5f);   // simulated: a minimum far beyond the framing, so the clamp must act
            }));
            AddTick(2);
            steps.Add(Do(C7, () =>
            {
                float d = WantOff().magnitude;
                Expect(Mathf.Abs(d - 5f) < 0.02f, "6d: ×0.6 and −0.6 m with a 5 m minimum: the final wanted camera is 5 m from the subject (" + d.ToString("F3") + ")");
                uVideo.SetProgramVariable("ctlMinDist", KH_VideoCameraEditor.CtlMinDist);
                uVideo.SendCustomEvent("_CtlReset");
                Note("6d: the zero-offset escape (straight up) is not reachable from the panel; covered by the source check (Video/7)");
                Close();
            }));
            AddSpawn("KH_DroneTarget2");
            steps.Add(Do(C7, () =>
            {
                // ---- 7. me as the subject ----
                v7PDrone = DroneOf(v6RBox);
                if (v7PDrone == null) { Fail("7: no KH_FilmDrone under the puppet's mailbox"); return; }
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 200);
                timeMark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C7, () => Time.realtimeSinceStartup - timeMark > 0.6f, 2, "timer"));
            steps.Add(Do(C7, () =>
            {
                v6Notice.SendCustomEvent("_FilmYes");
                Expect(V6<int[]>(v6Box, "filmGrantIds")[0] == v6Remote.playerId, "7: I allowed the puppet (row 0)");
                Expect(!DroneVisible(v7PDrone), "7: no declaration yet: its drone is not shown");
                var head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                v6RBox.SetProgramVariable("filmTargetId", v6Me); v6RBox.SetProgramVariable("filmSeq", 200);
                v7PDrone.SetProgramVariable("shown", true); v7PDrone.SetProgramVariable("forTarget", v6Me); v7PDrone.SetProgramVariable("forSeq", 200);
                v7PDrone.SetProgramVariable("camPos", head.position + (head.rotation * Vector3.forward) * 3f); v7PDrone.SetProgramVariable("camRot", head.rotation);
                Note("simulated: the puppet's declaration and drone values written into its PlayerObject (its own client would serialise them)");
                timeMark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C7, () => DroneVisible(v7PDrone), 2, "7: the filmer's drone did not appear on the subject's screen"));
            steps.Add(Until(C7, () => (v7PDrone.transform.position - V6<Vector3>(v7PDrone, "camPos")).magnitude < 0.05f, 2, "7: the drone did not reach the received pose"));
            // ---- 7a (Codex review 4). an update while shown eases through intermediate positions (not the first-show snap) ----
            steps.Add(Do(C7, () =>
            {
                var head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                v7From = v7PDrone.transform.position;
                v7To = head.position + (head.rotation * Vector3.right) * 4f + Vector3.up * 1f;   // a valid pose ~4 m away from the shown one
                v7PDrone.SetProgramVariable("camPos", v7To);
                v7Frame = Time.frameCount;
            }));
            steps.Add(Until(C7, () => Time.frameCount > v7Frame, 2, "frame"));
            steps.Add(Do(C7, () =>
            {
                Vector3 p = v7PDrone.transform.position;
                float total = (v7To - v7From).magnitude, left = (v7To - p).magnitude;
                Expect(DroneVisible(v7PDrone) && left > 0.05f && left < total - 0.01f, "7a: one frame after the update the drone is between the old and new pose (" + left.ToString("F2") + " of " + total.ToString("F2") + " m left)");
            }));
            steps.Add(Until(C7, () => (v7PDrone.transform.position - v7To).magnitude < 0.05f, 3, "7a: the drone did not converge to the new pose"));
            // ---- 7b (Codex review 4). declaration and table right, but the drone's own target / seq wrong: not shown ----
            steps.Add(Do(C7, () => { v7PDrone.SetProgramVariable("forTarget", v6Me + 1000); v7Frame = Time.frameCount; }));
            steps.Add(Until(C7, () => Time.frameCount > v7Frame + 1, 2, "frame"));
            steps.Add(Do(C7, () => { Expect(!DroneVisible(v7PDrone), "7b: a drone pose for another target is not shown"); v7PDrone.SetProgramVariable("forTarget", v6Me); v7PDrone.SetProgramVariable("forSeq", 199); v7Frame = Time.frameCount; }));
            steps.Add(Until(C7, () => Time.frameCount > v7Frame + 1, 2, "frame"));
            steps.Add(Do(C7, () => { Expect(!DroneVisible(v7PDrone), "7b: a drone pose for an old seq is not shown"); v7PDrone.SetProgramVariable("forSeq", 200); }));
            steps.Add(Until(C7, () => DroneVisible(v7PDrone), 2, "7b: the matching target / seq did not bring it back"));
            steps.Add(Do(C7, () =>
            {
                var head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                v7PDrone.SetProgramVariable("camPos", head.position + Vector3.forward * 0.5f);   // closer than 1 m to my head
            }));
            steps.Add(Until(C7, () => !DroneVisible(v7PDrone), 2, "7: the drone was not hidden within 1 m of my head"));
            steps.Add(Do(C7, () =>
            {
                var head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                v7PDrone.SetProgramVariable("camPos", head.position + Vector3.forward * 3f);
                v7PDrone.SetProgramVariable("shown", false);
                v7Frame = Time.frameCount;
            }));
            // the monitor is closed here: wait for frames, not render ticks (Codex review 2; the subject side runs without the camera)
            steps.Add(Until(C7, () => Time.frameCount > v7Frame + 2, 2, "frame"));
            steps.Add(Do(C7, () => { Expect(!DroneVisible(v7PDrone), "7: shown = false hides it"); v7PDrone.SetProgramVariable("shown", true); }));
            steps.Add(Until(C7, () => DroneVisible(v7PDrone), 2, "7: shown again did not bring it back"));
            steps.Add(Do(C7, () => { v6RBox.SetProgramVariable("filmSeq", 199); }));   // the filmer's declaration no longer matches the row
            steps.Add(Until(C7, () => !DroneVisible(v7PDrone), 2, "7: a declaration that no longer matches did not hide it"));
            steps.Add(Do(C7, () => { v6RBox.SetProgramVariable("filmSeq", 200); }));
            steps.Add(Until(C7, () => DroneVisible(v7PDrone), 2, "7: the matching declaration did not bring it back"));
            steps.Add(Do(C7, () =>
            {
                v6Pouch.SendCustomEvent("_FilmStop0");   // [やめてもらう]
                Expect(!DroneVisible(v7PDrone) && V6<int[]>(v6Box, "filmGrantIds").All(x => x == -1), "7: [やめてもらう] removes the row and hides the drone in the same call");
                v6RBox.SetProgramVariable("filmTargetId", -1); v7PDrone.SetProgramVariable("shown", false);
                Note("not made here (two real clients): the subject really receiving the 5 Hz pose, a third player never seeing the drone, late joiners, Quest / phone");
                V6Cleanup();
            }));
            C6 = keepCase;
        }
    }
}
