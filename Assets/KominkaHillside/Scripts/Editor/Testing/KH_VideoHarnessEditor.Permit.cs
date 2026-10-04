using System;
using System.Linq;
using TMPro;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 W4 V6-permit (design §9.2 V6, §13.9, §14.3). One local ClientSim client with ClientSim puppets (their Udon does not act).
    /// My mailbox runs with debugJoinLoopback = 1: nothing is sent; the subject's answer is injected with _DebugAnswerNow, its revoke with
    /// _DebugFilmRevokedNow; the subject's synced grant table is written straight into the puppet's own KH_TravelMailbox (the PlayerObject ClientSim
    /// spawns for every player) - **simulated, not a pass of real two-client networking** (CLAUDE.md).
    /// F side (me filming the puppet): the confirmation sends nothing (also a press within 0.5 s and [やめる]); pending reads no pose; × then a late yes
    /// stays ended; a yes without the table never starts (transit) and starts once the table holds (me, seq) (in use, the declaration is synced, the pose
    /// is read); emptying the table stops the reads on the next tick; the old row coming back does not reopen; a stale revoke (old seq) does not stop the
    /// new session, the right one does; a revoke before the yes wins; transit times out at 10 s (EndFilm sent); the puppet leaving ends the session and
    /// じぶん frames me again; the confirmation closes when its puppet leaves; a no starts the 30 s wait and the list refuses without a confirmation.
    /// S side (the puppet filming me): filmAccepting OFF refuses film (code 3) but not a join; a full table refuses (code 2); [いいよ] writes the row and
    /// replies 1 (the notice shows the film buttons, then the 4 s after-line); the grants page says 撮影中 once the puppet's declaration matches; [やめてもらう]
    /// clears the row and sends FilmRevoked; EndFilm with another seq keeps the row, with the row's seq removes it; an unstarted row survives 5 s and is
    /// swept between 20 and 30 s.
    /// Codex review 3 (2026-10-04): answers go through the real sender / seq check (_DebugAnswerFromNow). 19-21: the picture is off the monitor and the
    /// RecordSnap the frame after the grant ends - between render ticks, during the (simulated) fade, and in the same call when the mailbox ends the
    /// session. 22: another id / another seq / a box not owned by the subject - no pose read. 23: while pending, an old-seq yes and a third player's
    /// yes / revoke change nothing. 24: CancelJoin before / after [いいよ] leaves no row. 25: [撮影を許可中] opens the page and [やめてもらう] works
    /// while another request is pending (three 174-wide buttons fit). 26: two film requests at once, film ⇄ join replacement. 27: crossing.
    /// Not proven here: real event order / latency, Quest and phone input, the 60 s away rule (too long), PlayerData persistence of filmAccepting.
    /// </summary>
    public static partial class KH_VideoHarnessEditor
    {
        // the case name the shared helpers (AddSpawn / AddAsk / AddInUse) tag their steps with; V7 borrows them (KH_VideoHarnessEditor.Controls.cs)
        static string C6 = "V6-permit";
        static UdonBehaviour v6Box, v6RBox, v6Notice, v6Pouch;
        static VRCPlayerApi v6Remote;
        static int v6Me, v6Seq0, v6SeqA, v6SeqB, v6Serial, v6Reads, v6Sent, v6JoinSerial;
        static float v6Mark;
        static bool v6Active;

        static TT V6<TT>(UdonBehaviour u, string key)
        {
            if (u == null || !u.TryGetProgramVariable(key, out object v) || !(v is TT)) throw new InvalidOperationException("missing runtime symbol " + key + " on " + (u != null ? u.name : "null"));
            return (TT)v;
        }

        static UdonBehaviour V6BoxOf(VRCPlayerApi p)
        {
            if (p == null || !Utilities.IsValid(p)) return null;
            var objs = Networking.GetPlayerObjects(p);
            if (objs == null) return null;
            foreach (var o in objs)
            {
                if (o == null) continue;
                var u = o.GetComponents<UdonBehaviour>().FirstOrDefault(b => b.programSource != null && b.programSource.name == "KH_TravelMailbox");
                if (u != null) return u;
            }
            return null;
        }

        static VRCPlayerApi V6FindRemote()
        {
            var all = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; VRCPlayerApi.GetPlayers(all);
            return all.FirstOrDefault(p => p != null && !p.isLocal);
        }

        static int Phase() { return V6<int>(v6Box, "_filmPhase"); }
        static int OutSeq() { return V6<int>(v6Box, "_outSeq"); }
        static int Reads() { return Get<int>("_personReads"); }
        static void Table(UdonBehaviour box, int id, int seq) { box.SetProgramVariable("filmGrantIds", new[] { id, -1, -1, -1 }); box.SetProgramVariable("filmGrantSeqs", new[] { seq, 0, 0, 0 }); }
        /// <summary>The puppet's answer to the last asked seq, through the real AnsJoin check (sender == pending target, seq == pending seq).</summary>
        static void Answer(int code) { AnswerFrom(v6Remote.playerId, v6SeqA, code); }
        static void AnswerFrom(int from, int seq, int code)
        {
            v6Box.SetProgramVariable("debugAnswerCode", code); v6Box.SetProgramVariable("debugIncomingFrom", from); v6Box.SetProgramVariable("debugIncomingSeq", seq);
            v6Box.SendCustomEvent("_DebugAnswerFromNow");
        }
        static void Incoming(int from, int kind, int seq)
        {
            v6Box.SetProgramVariable("_closedAt", -1000f);   // the 20 s gap after the last notice (simulated: shortened between steps)
            v6Box.SetProgramVariable("debugIncomingFrom", from); v6Box.SetProgramVariable("debugIncomingKind", kind); v6Box.SetProgramVariable("debugIncomingSeq", seq);
            v6Box.SendCustomEvent("_DebugIncomingNow");
        }
        static void Revoked(int from, int seq) { v6Box.SetProgramVariable("debugIncomingFrom", from); v6Box.SetProgramVariable("debugIncomingSeq", seq); v6Box.SendCustomEvent("_DebugFilmRevokedNow"); }
        static void Ended(int from, int seq) { v6Box.SetProgramVariable("debugIncomingFrom", from); v6Box.SetProgramVariable("debugIncomingSeq", seq); v6Box.SendCustomEvent("_DebugFilmEndNow"); }

        /// <summary>Open the list and press the puppet's cell (page 0: me is cell 0).</summary>
        static void PickRemote()
        {
            uVideo.SendCustomEvent("_PersonList");
            var ids = Get<int[]>("_peopleIds"); int n = Get<int>("_peopleCount");
            int cell = -1;
            for (int i = 0; i < n && i < KH_VideoCamera.PeoplePerPage; i++) if (ids[i] == v6Remote.playerId) cell = i;
            if (cell < 0) { Fail("the puppet " + v6Remote.playerId + " is not on the first page of the person list"); return; }
            uVideo.SendCustomEvent("_PersonPick" + cell);
        }

        /// <summary>Spawn a puppet and wait for it and its PlayerObject mailbox.</summary>
        static void AddSpawn(string name)
        {
            steps.Add(Do(C6, () => { v6Remote = null; v6RBox = null; ClientSimMain.SpawnRemotePlayer(name); }));
            steps.Add(Until(C6, () => (v6Remote = V6FindRemote()) != null && (v6RBox = V6BoxOf(v6Remote)) != null, 6,
                "ClientSim puppet '" + name + "' or its KH_TravelMailbox PlayerObject did not appear (the table cannot be simulated without it)"));
            steps.Add(Do(C6, () =>
            {
                var o = Networking.GetOwner(v6RBox.gameObject);
                Expect(o != null && o.playerId == v6Remote.playerId, "the puppet's mailbox is owned by the puppet (the gate's owner check can pass)");
            }));
        }

        /// <summary>Ask the puppet through the real UI path: list → cell → confirmation → 0.6 s → [依頼する].</summary>
        static void AddAsk(string tag)
        {
            steps.Add(Do(C6, () => { PickRemote(); v6Mark = Time.realtimeSinceStartup; }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 0.6f, 2, "timer"));
            steps.Add(Do(C6, () =>
            {
                Expect(Get<int>("_confirmId") == v6Remote.playerId, tag + ": the confirmation is up for the puppet");
                int before = OutSeq();
                uVideo.SendCustomEvent("_PersonAskYes");
                Expect(OutSeq() == before + 1 && V6<int>(v6Box, "_outTo") == v6Remote.playerId && V6<int>(v6Box, "_outKind") == KH_TravelMailbox.KindFilm && Phase() == KH_TravelMailbox.FilmPending,
                    tag + ": [依頼する] sent one film request (seq " + OutSeq() + ", pending)");
                Expect(Get<int>("_person") == v6Remote.playerId && Get<int>("_confirmId") == -1, tag + ": じぶん now targets the puppet; the confirmation closed");
                v6SeqA = OutSeq();
            }));
        }

        static void AddInUse(string tag)
        {
            steps.Add(Do(C6, () => { Answer(KH_TravelMailbox.CodeYes); Expect(Phase() == KH_TravelMailbox.FilmTransit, tag + ": a yes alone is grant-in-transit (not in use)"); Table(v6RBox, v6Me, v6SeqA); }));
            steps.Add(Until(C6, () => Phase() == KH_TravelMailbox.FilmInUse, 2, tag + ": the table with (me, seq) did not make the session in use"));
            steps.Add(Until(C6, () => Get<bool>("_personLive") && Reads() > 0, 3, tag + ": the camera did not frame the puppet once in use"));
        }

        static void MakeV6Steps()
        {
            // ---- setup ----
            steps.Add(Do(C6, () =>
            {
                v6Box = V6BoxOf(Networking.LocalPlayer);
                if (v6Box == null) { Fail("my KH_TravelMailbox PlayerObject is missing (Course/85)"); return; }
                var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true);
                v6Notice = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_TravelNotice");
                v6Pouch = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_TreatPouch");
                v6Me = Networking.LocalPlayer.playerId;
                v6Active = true; v6Deferred.Clear();
                v6Box.SetProgramVariable("debugJoinLoopback", 1);
                Note("simulated: debugJoinLoopback = 1 (nothing sent), answers / revokes injected, the puppet's grant table written into its PlayerObject mailbox");
                Open(); uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeSelfFollow);
                v6JoinSerial = V6<int>(v6Box, "joinResultSerial");
            }));
            AddSpawn("KH_FilmTarget");

            // ---- 1. the confirmation alone sends nothing (§14.3) ----
            steps.Add(Do(C6, () =>
            {
                v6Seq0 = OutSeq(); v6Reads = Reads();
                PickRemote();
                Expect(Get<int>("_confirmId") == v6Remote.playerId, "1: picking the puppet opens the confirmation");
                Expect(OutSeq() == v6Seq0 && V6<int>(v6Box, "_outTo") == -1 && Phase() == KH_TravelMailbox.FilmNone, "1: the confirmation sent nothing (no seq, no pending)");
                uVideo.SendCustomEvent("_PersonAskYes");   // within 0.5 s of the confirmation
                Expect(OutSeq() == v6Seq0 && Get<int>("_confirmId") == v6Remote.playerId, "1: [依頼する] within 0.5 s is ignored (no request, the confirmation stays)");
                uVideo.SendCustomEvent("_PersonAskNo");
                Expect(OutSeq() == v6Seq0 && Get<int>("_confirmId") == -1 && Get<bool>("_listOpen"), "1: [やめる] sends nothing and goes back to the list");
                Expect(!V6<int[]>(v6Box, "_deniedIds").Contains(v6Remote.playerId), "1: [やめる] does not start the 30 s wait");
                Expect(Reads() == v6Reads, "1: no pose of the puppet was read");
                uVideo.SendCustomEvent("_PersonListClose");
            }));

            // ---- 2. pending: no pose, no join display ----
            AddAsk("2");
            steps.Add(Do(C6, () => { v6Mark = Time.realtimeSinceStartup; v6Reads = Reads(); }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 1f, 2, "timer"));
            steps.Add(Do(C6, () =>
            {
                Expect(Reads() == v6Reads && !Get<bool>("_personLive"), "2: while pending nothing of the puppet is read (the dog framing runs)");
                Expect(V6<int>(v6Box, "joinResultSerial") == v6JoinSerial, "2: the film request is not reported as a join (joinResult untouched)");
                // ---- 3. × then a late yes: stays ended ----
                Close();
                Expect(Phase() == KH_TravelMailbox.FilmNone && V6<int>(v6Box, "_outTo") == -1, "3: × ends the pending session");
                Answer(KH_TravelMailbox.CodeYes);
                Expect(Phase() == KH_TravelMailbox.FilmNone, "3: a yes after × is ignored (no session reopens)");
                Expect(!V6<int[]>(v6Box, "_deniedIds").Contains(v6Remote.playerId), "3: × is not a refusal (no 30 s wait)");
                Open(); uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeSelfFollow);
            }));

            // ---- 4. transit → in use ----
            AddAsk("4");
            steps.Add(Do(C6, () => { Answer(KH_TravelMailbox.CodeYes); v6Mark = Time.realtimeSinceStartup; v6Reads = Reads(); }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 0.6f, 2, "timer"));
            steps.Add(Do(C6, () =>
            {
                Expect(Phase() == KH_TravelMailbox.FilmTransit && Reads() == v6Reads && V6<int>(v6Box, "filmTargetId") == -1, "4: a yes without the table: transit, nothing read, no declaration");
                Table(v6RBox, v6Me, v6SeqA);
            }));
            steps.Add(Until(C6, () => Phase() == KH_TravelMailbox.FilmInUse, 2, "4: the table did not make the session in use"));
            steps.Add(Do(C6, () => Expect(V6<int>(v6Box, "filmTargetId") == v6Remote.playerId && V6<int>(v6Box, "filmSeq") == v6SeqA, "4: in use: my declaration (target, seq) is set")));
            steps.Add(Until(C6, () => Get<bool>("_personLive") && Reads() > 0, 3, "4: the camera did not frame the puppet"));

            // ---- 5. the table empties: the next tick reads nothing ----
            steps.Add(Do(C6, () => { Table(v6RBox, -1, 0); v6Reads = Reads(); v6Mark = Time.realtimeSinceStartup; }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 0.6f, 2, "timer"));
            steps.Add(Do(C6, () =>
            {
                Expect(Reads() == v6Reads, "5: no pose read after the grant row disappeared (" + (Reads() - v6Reads) + " reads)");
                Expect(!Get<bool>("_personLive") && Phase() == KH_TravelMailbox.FilmNone && V6<int>(v6Box, "filmTargetId") == -1, "5: ended, declaration cleared, the person framing dropped");
                Expect(V6<int>(v6Box, "filmResultCode") == KH_TravelMailbox.FilmRevokedBy, "5: reported as revoked");
                // ---- 6. the old row comes back: never reopens ----
                Table(v6RBox, v6Me, v6SeqA); v6Mark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 1f, 2, "timer"));
            steps.Add(Do(C6, () => { Expect(Phase() == KH_TravelMailbox.FilmNone && Reads() == v6Reads, "6: an old grant row (ended seq) does not reopen the session"); Table(v6RBox, -1, 0); }));

            // ---- 7. ask again after the revoke; a stale revoke keeps it, the right one ends it ----
            AddAsk("7");
            steps.Add(Do(C6, () => { v6SeqB = v6SeqA; }));
            AddInUse("7");
            steps.Add(Do(C6, () =>
            {
                Revoked(v6Remote.playerId, v6SeqB - 1);
                Expect(Phase() == KH_TravelMailbox.FilmInUse, "7: a revoke with an older seq does not stop the new session");
                Revoked(v6Remote.playerId, v6SeqB);
                Expect(Phase() == KH_TravelMailbox.FilmNone && V6<int>(v6Box, "filmResultCode") == KH_TravelMailbox.FilmRevokedBy, "7: the revoke with the session's seq ends it");
                Table(v6RBox, -1, 0);
            }));

            // ---- 8. revoke before the yes (reversed order) ----
            AddAsk("8");
            steps.Add(Do(C6, () =>
            {
                Revoked(v6Remote.playerId, v6SeqA);
                Expect(Phase() == KH_TravelMailbox.FilmNone && V6<int>(v6Box, "_outTo") == -1, "8: a revoke for the pending seq ends it and frees the slot");
                Answer(KH_TravelMailbox.CodeYes);
                Expect(Phase() == KH_TravelMailbox.FilmNone, "8: the yes arriving after the revoke is ignored");
            }));

            // ---- 9. transit times out (RC-08) ----
            AddAsk("9");
            steps.Add(Do(C6, () => { v6Sent = V6<int>(v6Box, "debugFilmSentCount"); Answer(KH_TravelMailbox.CodeYes); v6Mark = Time.realtimeSinceStartup; }));
            steps.Add(Until(C6, () => Phase() == KH_TravelMailbox.FilmNone, 12, "9: transit did not end within 10 s without the table"));
            steps.Add(Do(C6, () =>
            {
                float t = Time.realtimeSinceStartup - v6Mark;
                Expect(t >= 9.5f, "9: transit lasted " + t.ToString("F1") + " s (≥ 10 s)");
                Expect(V6<int>(v6Box, "filmResultCode") == KH_TravelMailbox.FilmNoTable && V6<int>(v6Box, "debugFilmSentCount") == v6Sent + 1 && V6<int>(v6Box, "debugFilmSentKind") == 0, "9: reported 'could not confirm', EndFilm sent");
            }));

            AddFilmerExtras();   // Codex review 3 (a)(b)(c): KH_VideoHarnessEditor.Permit.cs, below

            // ---- 10. the puppet leaves while in use ----
            AddAsk("10");
            AddInUse("10");
            steps.Add(Do(C6, () => { ClientSimMain.RemovePlayer(v6Remote); }));
            steps.Add(Until(C6, () => Phase() == KH_TravelMailbox.FilmNone && Get<int>("_person") == -1 && !Get<bool>("_personLive"), 3, "10: the session did not end / じぶん did not return to me after the puppet left"));
            steps.Add(Do(C6, () => Expect(V6<int>(v6Box, "filmTargetId") == -1, "10: declaration cleared after the leave")));

            // ---- 11. the confirmation closes when its puppet leaves ----
            AddSpawn("KH_FilmTarget2");
            steps.Add(Do(C6, () => { v6Seq0 = OutSeq(); PickRemote(); Expect(Get<int>("_confirmId") == v6Remote.playerId, "11: confirmation up"); ClientSimMain.RemovePlayer(v6Remote); }));
            steps.Add(Until(C6, () => Get<int>("_confirmId") == -1, 2, "11: the confirmation did not close after its puppet left"));
            steps.Add(Do(C6, () => Expect(OutSeq() == v6Seq0, "11: nothing was sent")));

            // ---- 12. a no: 30 s wait; the list refuses without a confirmation ----
            AddSpawn("KH_FilmTarget3");
            AddAsk("12");
            steps.Add(Do(C6, () =>
            {
                Answer(KH_TravelMailbox.CodeNo);
                Expect(Phase() == KH_TravelMailbox.FilmNone && V6<int>(v6Box, "filmResultCode") == KH_TravelMailbox.CodeNo, "12: a no ends the request");
                Expect(V6<int[]>(v6Box, "_deniedIds").Contains(v6Remote.playerId), "12: the 30 s wait starts");
                v6Seq0 = OutSeq();
                PickRemote();
                Expect(Get<int>("_confirmId") == -1 && Get<string>("_listReason").Length > 0 && OutSeq() == v6Seq0, "12: the denied puppet shows a reason in the list (" + Get<string>("_listReason") + "), no confirmation, nothing sent");
                Close();
            }));

            // ---- 13. S side: filmAccepting OFF refuses film, not a join ----
            steps.Add(Do(C6, () =>
            {
                int r = V6<int>(v6Box, "debugReplyCount");
                v6Box.SendCustomEvent("_ToggleFilmAccepting");
                Expect(!V6<bool>(v6Box, "filmAccepting"), "13: the film toggle is OFF");
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 100);
                Expect(V6<int>(v6Box, "debugReplyCount") == r + 1 && V6<int>(v6Box, "debugLastReplyCode") == KH_TravelMailbox.CodePaused && V6<int>(v6Box, "_inFrom") == -1, "13: film refused with code 3 while OFF");
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindJoin, 101);
                Expect(V6<int>(v6Box, "_inFrom") == v6Remote.playerId && V6<int>(v6Box, "_inKind") == KH_TravelMailbox.KindJoin, "13: a join is still received (separate toggle)");
                v6Box.SetProgramVariable("debugIncomingSeq", 101); v6Box.SendCustomEvent("_DebugIncomingCancelNow");
                Expect(V6<int>(v6Box, "_inFrom") == -1, "13: the join withdrawn");
                v6Box.SendCustomEvent("_ToggleFilmAccepting");
                Expect(V6<bool>(v6Box, "filmAccepting"), "13: the film toggle is ON again");
                // ---- 14. a full table refuses (code 2) ----
                v6Box.SetProgramVariable("filmGrantIds", new[] { 9001, 9002, 9003, 9004 }); v6Box.SetProgramVariable("filmGrantSeqs", new[] { 1, 1, 1, 1 });
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 102);
                Expect(V6<int>(v6Box, "debugLastReplyCode") == KH_TravelMailbox.CodeBusy && V6<int>(v6Box, "_inFrom") == -1, "14: a 5th filmer is refused with code 2");
                Table(v6Box, -1, 0);
                // ---- 15. [いいよ] ----
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 103);
                Expect(V6<int>(v6Box, "_inFrom") == v6Remote.playerId && V6<int>(v6Box, "_inKind") == KH_TravelMailbox.KindFilm, "15: the film request is pending");
                v6Mark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 0.6f, 2, "timer"));
            steps.Add(Do(C6, () =>
            {
                var board = V6<GameObject>(v6Notice, "board");
                Func<string, bool> act = nm => { var t = board.transform.Find(nm); return t != null && t.gameObject.activeSelf; };
                Expect(board.activeSelf && act("Button6") && act("Button7") && !act("Button0") && !act("Button4") && act("Button2") && act("Button3"), "15: the notice shows the film buttons (+ pause / block), not join / call");
                v6Notice.SendCustomEvent("_FilmYes");
                var ids = V6<int[]>(v6Box, "filmGrantIds"); var seqs = V6<int[]>(v6Box, "filmGrantSeqs");
                Expect(ids[0] == v6Remote.playerId && seqs[0] == 103, "15: [いいよ] wrote (puppet, 103) into my table");
                Expect(V6<int>(v6Box, "debugLastReplyCode") == KH_TravelMailbox.CodeYes && V6<int>(v6Box, "_inFrom") == -1, "15: replied 1 and closed the request");
                Expect(board.activeSelf && !act("Button6") && !act("Button2") && V6<float>(v6Notice, "_afterUntil") > Time.time, "15: the 4 s after-line is up without buttons");
                // ---- 16. the grants page: 撮影中 once the puppet declares; [やめてもらう] ----
                v6RBox.SetProgramVariable("filmTargetId", v6Me); v6RBox.SetProgramVariable("filmSeq", 103);
                v6Pouch.SendCustomEvent("_FilmGrantsOpen");   // the walk tab's [撮影を許可中] opens 撮影を許した人
                v6Pouch.TryGetProgramVariable("filmGrantLabels", out object lo);
                var row0 = lo is Array la && la.Length > 0 ? la.GetValue(0) as TMP_Text : null;
                Expect(row0 != null && row0.gameObject.activeSelf && row0.text.Contains(KominkaDogRun.KD_TreatPouch.WordFilmRowActive), "16: the row says 撮影中 (" + (row0 != null ? row0.text.Replace("\n", " / ") : "-") + ")");
                v6Sent = V6<int>(v6Box, "debugFilmSentCount");
                v6Pouch.SendCustomEvent("_FilmStop0");
                Expect(V6<int[]>(v6Box, "filmGrantIds").All(x => x == -1), "16: [やめてもらう] cleared the row");
                Expect(V6<int>(v6Box, "debugFilmSentCount") == v6Sent + 1 && V6<int>(v6Box, "debugFilmSentKind") == 1 && V6<int>(v6Box, "debugFilmSentSeq") == 103, "16: FilmRevoked(103) sent to the filmer");
                v6RBox.SetProgramVariable("filmTargetId", -1);
                // ---- 17. EndFilm: only the row's own seq ----
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 104);
                v6Mark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 0.6f, 2, "timer"));
            steps.Add(Do(C6, () =>
            {
                v6Notice.SendCustomEvent("_FilmYes");
                Expect(V6<int[]>(v6Box, "filmGrantIds")[0] == v6Remote.playerId, "17: row (puppet, 104) written");
                Ended(v6Remote.playerId, 103);
                Expect(V6<int[]>(v6Box, "filmGrantIds")[0] == v6Remote.playerId, "17: EndFilm with another seq keeps the row");
                Ended(v6Remote.playerId, 104);
                Expect(V6<int[]>(v6Box, "filmGrantIds")[0] == -1, "17: EndFilm with the row's seq removes it");
            }));
            AddSubjectExtras();   // Codex review 3 (d)(e)(f): KH_VideoHarnessEditor.Permit.cs, below
            steps.Add(Do(C6, () =>
            {
                // ---- 18. an unstarted row: kept at 5 s, swept by 30 s ----
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 120);   // above every seq this puppet sent in 13-17 and 24-26
                v6Mark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 0.6f, 2, "timer"));
            steps.Add(Do(C6, () => { v6Notice.SendCustomEvent("_FilmYes"); v6Mark = Time.realtimeSinceStartup; Expect(V6<int[]>(v6Box, "filmGrantIds")[0] == v6Remote.playerId, "18: row (puppet, 120) written"); }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 5f, 6, "timer"));
            steps.Add(Do(C6, () => Expect(V6<int[]>(v6Box, "filmGrantIds")[0] == v6Remote.playerId, "18: a just-written row is not swept (5 s)")));
            steps.Add(Until(C6, () => V6<int[]>(v6Box, "filmGrantIds")[0] == -1, 36, "18: the unstarted row was not swept within 36 s"));
            steps.Add(Do(C6, () =>
            {
                float t = Time.realtimeSinceStartup - v6Mark;
                Expect(t >= 19.5f, "18: swept after " + t.ToString("F1") + " s (rule: 20 s without the filmer's declaration, checked every 10 s)");
                Note("not proven by V6: real two-client order / latency, Quest and phone touch, the 60 s away rule, filmAccepting persistence (PlayerData)");
                V6Cleanup();
                if (v6Deferred.Count > 0) Fail("deferred: " + string.Join(" | ", v6Deferred));
            }));
        }

        // ================================================================ Codex review 3 (2026-10-04)

        static VRCPlayerApi v6Other;
        static UdonBehaviour v6OBox, v6Fade;
        static int v6Frame, v6OSeq;

        static VRCPlayerApi V6FindOther(int exclude)
        {
            var all = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; VRCPlayerApi.GetPlayers(all);
            return all.FirstOrDefault(p => p != null && !p.isLocal && p.playerId != exclude);
        }

        /// <summary>A second puppet next to v6Remote (a third party / a second requester).</summary>
        static void AddSpawnOther(string name)
        {
            steps.Add(Do(C6, () => { v6Other = null; v6OBox = null; v6OSeq = 0; ClientSimMain.SpawnRemotePlayer(name); }));
            steps.Add(Until(C6, () => (v6Other = V6FindOther(v6Remote != null && Utilities.IsValid(v6Remote) ? v6Remote.playerId : -1)) != null && (v6OBox = V6BoxOf(v6Other)) != null, 6,
                "second ClientSim puppet '" + name + "' or its mailbox did not appear"));
        }

        static bool PictureOff()
        {
            var snapR = snap.GetComponent<Renderer>();
            return !Get<bool>("_pictureShown") && (screen.sharedMaterial == null || screen.sharedMaterial.mainTexture == null) && (snapR.sharedMaterial == null || snapR.sharedMaterial.mainTexture == null);
        }

        /// <summary>In use with the puppet AND its picture attached to the monitor and the RecordSnap.</summary>
        static void AddLive(string tag)
        {
            AddAsk(tag);
            AddInUse(tag);
            steps.Add(Until(C6, () => Get<bool>("_pictureShown"), 3, tag + ": the puppet's picture was never attached"));
        }

        /// <summary>(a) the picture goes the frame after the permission ends, also while waiting for the next render tick and during the fade;
        /// the mailbox's end of a session detaches it in the same call. (b) another owner / another id / another seq in the table: no pose read.
        /// (c) while a new request is pending, an old-seq answer and a third party's answer / revoke change nothing.</summary>
        static void AddFilmerExtras()
        {
            // ---- 19 (a). revoked while waiting for the next render tick ----
            AddLive("19");
            steps.Add(Do(C6, () =>
            {
                uVideo.SetProgramVariable("_nextRender", Time.time + 5f);   // the camera is now between ticks for 5 s
                Table(v6RBox, -1, 0); v6Reads = Reads(); v6Frame = Time.frameCount;
            }));
            steps.Add(Until(C6, () => Time.frameCount > v6Frame, 2, "frame"));
            steps.Add(Do(C6, () =>
            {
                Expect(PictureOff() && !Get<bool>("_personLive"), "19: the frame after the grant row went (between render ticks), the picture is off the monitor and the RecordSnap");
                Expect(Reads() == v6Reads, "19: no pose read after the row went");
                uVideo.SetProgramVariable("_nextRender", Time.time);
            }));
            steps.Add(Until(C6, () => Phase() == KH_TravelMailbox.FilmNone, 2, "19: the mailbox did not end the session after the row went"));
            // ---- 20 (a). revoked during the travel fade ----
            AddLive("20");
            steps.Add(Do(C6, () =>
            {
                v6Fade = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true).FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_TravelFade");
                if (v6Fade == null) { Note("20: no KH_TravelFade in the scene; the fade part is skipped"); Table(v6RBox, -1, 0); v6Frame = Time.frameCount; return; }
                HoldFade();   // simulated: black (the camera's fade branch returns before drawing)
                Note("simulated: KH_TravelFade._state = 2 written every frame of case 20");
                v6Frame = Time.frameCount;
            }));
            steps.Add(Until(C6, () => { HoldFade(); return Time.frameCount > v6Frame + 5; }, 2, "frame"));
            steps.Add(Do(C6, () =>
            {
                if (v6Fade != null) Expect(Get<int>("_state") == KH_VideoCamera.StateFade, "20: the camera is in its fade branch");
                Table(v6RBox, -1, 0); v6Reads = Reads(); v6Frame = Time.frameCount;
            }));
            steps.Add(Until(C6, () => { HoldFade(); return Time.frameCount > v6Frame; }, 2, "frame"));
            steps.Add(Do(C6, () =>
            {
                Expect(PictureOff() && !Get<bool>("_personLive") && Reads() == v6Reads, "20: during the fade, the frame after the row went, the picture is off and nothing was read");
                if (v6Fade != null) { v6Fade.SetProgramVariable("_state", 0); v6Fade.SetProgramVariable("_alpha", 0f); }
            }));
            steps.Add(Until(C6, () => Phase() == KH_TravelMailbox.FilmNone, 2, "20: the mailbox did not end the session after the row went"));
            // ---- 21 (a). the mailbox ends the session: detached in the same call (_OnFilmEnded), even between ticks ----
            AddLive("21");
            steps.Add(Do(C6, () =>
            {
                uVideo.SetProgramVariable("_nextRender", Time.time + 5f);
                Revoked(v6Remote.playerId, v6SeqA);   // the table is still intact: only the event ends it
                Expect(Phase() == KH_TravelMailbox.FilmNone && PictureOff() && !Get<bool>("_personLive"), "21: FilmRevoked → FilmClear → _OnFilmEnded detached the picture in the same call");
                uVideo.SetProgramVariable("_nextRender", Time.time);
                Table(v6RBox, -1, 0);
            }));
            // ---- 22 (b). the table names someone else / another seq; the box is not owned by the subject ----
            foreach (var variant in new[] { "other id", "other seq", "other owner" })
            {
                string v = variant;
                AddLive("22 " + v);
                steps.Add(Do(C6, () =>
                {
                    v6Reads = Reads();
                    if (v == "other id") Table(v6RBox, v6Me + 1000, v6SeqA);
                    else if (v == "other seq") Table(v6RBox, v6Me, v6SeqA + 7);
                    else { Table(v6Box, v6Me, v6SeqA); v6Box.SetProgramVariable("_filmBox", v6Box); }   // a table with (me, seq) but in a box owned by me, not the subject
                    v6Mark = Time.realtimeSinceStartup;
                }));
                steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 0.5f, 2, "timer"));
                steps.Add(Do(C6, () =>
                {
                    Expect(Reads() == v6Reads && !Get<bool>("_personLive") && Phase() == KH_TravelMailbox.FilmNone, "22: " + v + ": no pose read, the session ended");
                    Table(v6RBox, -1, 0); Table(v6Box, -1, 0);
                }));
            }
            // ---- 23 (c). pending: an old-seq answer, a third party's answer / revoke ----
            AddSpawnOther("KH_FilmThird");
            AddAsk("23");
            steps.Add(Do(C6, () =>
            {
                AnswerFrom(v6Remote.playerId, v6SeqA - 1, KH_TravelMailbox.CodeYes);
                Expect(Phase() == KH_TravelMailbox.FilmPending && V6<int>(v6Box, "_outTo") == v6Remote.playerId, "23: a yes with an older seq is ignored");
                AnswerFrom(v6Other.playerId, v6SeqA, KH_TravelMailbox.CodeYes);
                Expect(Phase() == KH_TravelMailbox.FilmPending, "23: a yes from a third player with the right seq is ignored");
                Revoked(v6Other.playerId, v6SeqA);
                Expect(Phase() == KH_TravelMailbox.FilmPending, "23: a revoke from a third player is ignored");
                Note("23: the sender identity itself (NetworkCalling.CallingPlayer) is only exercised by real networking: two clients");
                uVideo.SendCustomEvent("_PersonCancel");
                Expect(Phase() == KH_TravelMailbox.FilmNone, "23: [取り消す] ended it");
                ClientSimMain.RemovePlayer(v6Other); v6Other = null; v6OBox = null;
            }));
            steps.Add(Until(C6, () => V6FindOther(v6Remote.playerId) == null, 3, "the third puppet did not leave"));
        }

        /// <summary>(d) CancelJoin vs [いいよ] in both orders; (e) the grants page from the walk tab while another request is pending;
        /// (f) two film requests at once, kind replacement and crossing (one client + puppets; real order is for two clients).</summary>
        static void AddSubjectExtras()
        {
            // ---- 24 (d). cancel then yes / yes then cancel ----
            steps.Add(Do(C6, () =>
            {
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 110);
                v6Box.SendCustomEvent("_DebugIncomingCancelNow");   // CancelJoin(110) first
                v6Mark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 0.6f, 2, "timer"));
            steps.Add(Do(C6, () =>
            {
                v6Notice.SendCustomEvent("_FilmYes");
                Expect(V6<int>(v6Box, "_inFrom") == -1 && V6<int[]>(v6Box, "filmGrantIds").All(x => x == -1), "24: cancel before [いいよ]: no pending, no row");
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 111);
                v6Mark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 0.6f, 2, "timer"));
            steps.Add(Do(C6, () =>
            {
                v6Notice.SendCustomEvent("_FilmYes");
                Expect(V6<int[]>(v6Box, "filmGrantIds")[0] == v6Remote.playerId, "24: [いいよ] wrote the row");
                v6Box.SetProgramVariable("debugIncomingSeq", 111); v6Box.SendCustomEvent("_DebugIncomingCancelNow");   // CancelJoin(111) crossing the yes
                Expect(V6<int[]>(v6Box, "filmGrantIds").All(x => x == -1), "24: cancel after [いいよ]: the row is removed");
            }));
            // ---- 25 (e). a row + another pending request: [撮影を許可中] still opens the page and [やめてもらう] works ----
            AddSpawnOther("KH_FilmSecond");
            steps.Add(Do(C6, () => { Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 112); v6Mark = Time.realtimeSinceStartup; }));
            steps.Add(Until(C6, () => Time.realtimeSinceStartup - v6Mark > 0.6f, 2, "timer"));
            steps.Add(Do(C6, () =>
            {
                v6Notice.SendCustomEvent("_FilmYes");
                Incoming(v6Other.playerId, KH_TravelMailbox.KindJoin, ++v6OSeq);
                Expect(V6<int>(v6Box, "_inFrom") == v6Other.playerId && V6<int[]>(v6Box, "filmGrantIds")[0] == v6Remote.playerId, "25: a row for the puppet and a pending join from the second puppet");
                // the hand menu draws only while it is open (the product skips redraws while closed): open it and show the walk tab, the same way
                // the other harnesses do (KH_CourseWalkHarnessEditor.ToyRow.cs TrMenu: _Toggle if panelRoot is hidden, _WelcomeMenu, then the tab)
                v6Pouch.TryGetProgramVariable("panelRoot", out object pr);
                if (!(pr is GameObject prg) || !prg.activeSelf) v6Pouch.SendCustomEvent("_Toggle");
                if (v6Pouch.TryGetProgramVariable("_welcome", out object wl) && wl is bool w && w) v6Pouch.SendCustomEvent("_WelcomeMenu");
                v6Pouch.SendCustomEvent("_Tab2");   // さんぽ: Redraw → RedrawWalk → RedrawTravel lays out the bottom row
                v6Mark = Time.realtimeSinceStartup;
            }));
            // case 25's checks are soft: a miss is recorded and failed at the end of V6, so 26 / 27 still run in the same run
            steps.Add(Until(C6, () => (V6Active(v6Pouch, "filmGrantsButton") && V6Active(v6Pouch, "travelJoinPendingButton")) || Time.realtimeSinceStartup - v6Mark > 3f, 5, "timer"));
            steps.Add(Do(C6, () =>
            {
                bool both = V6Active(v6Pouch, "filmGrantsButton") && V6Active(v6Pouch, "travelJoinPendingButton");
                Soft(both, "25: with the menu open on the walk tab, both [申し込み 1 件] and [撮影を許可中] are shown");
                if (both)
                {
                    var g = (RectTransform)V6<GameObject>(v6Pouch, "filmGrantsButton").transform; var pend = (RectTransform)V6<GameObject>(v6Pouch, "travelJoinPendingButton").transform;
                    Soft(Mathf.Abs(g.sizeDelta.x - KominkaDogRun.KD_TreatPouch.FilmThirdWidth) < 0.5f && Mathf.Abs(pend.sizeDelta.x - KominkaDogRun.KD_TreatPouch.FilmThirdWidth) < 0.5f, "25: the bottom row is three 174-wide buttons");
                    foreach (var t in new[] { g, pend }) { var l = t.GetComponentInChildren<TMP_Text>(); if (l != null) { l.ForceMeshUpdate(); Soft(!l.isTextOverflowing, "25: '" + l.text + "' fits at 174"); } }
                }
                v6Pouch.SendCustomEvent("_FilmGrantsOpen");
                v6Pouch.TryGetProgramVariable("filmGrantsPage", out object gp);
                Soft(gp is GameObject go && go.activeInHierarchy, "25: 撮影を許した人 opened while the join request is still pending");
                v6Pouch.SendCustomEvent("_FilmStop0");
                Soft(V6<int[]>(v6Box, "filmGrantIds").All(x => x == -1) && V6<int>(v6Box, "_inFrom") == v6Other.playerId, "25: [やめてもらう] worked; the pending join is untouched");
                Table(v6Box, -1, 0); v6Box.SetProgramVariable("_filmRows", 0);   // 26 / 27 start from an empty table even if 25 missed
                v6Box.SetProgramVariable("debugIncomingFrom", v6Other.playerId); v6Box.SetProgramVariable("debugIncomingSeq", v6OSeq); v6Box.SendCustomEvent("_DebugIncomingCancelNow");
                // ---- 26 (f). two film requests at once: the second is busy (one pending) ----
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 113);
                Incoming(v6Other.playerId, KH_TravelMailbox.KindFilm, ++v6OSeq);
                Expect(V6<int>(v6Box, "_inFrom") == v6Remote.playerId && V6<int>(v6Box, "debugLastReplyCode") == KH_TravelMailbox.CodeBusy, "26: a second film request while one is pending gets code 2");
                // kind replacement by the same sender: film → join is taken (join accepting ON)
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindJoin, 114);
                Expect(V6<int>(v6Box, "_inFrom") == v6Remote.playerId && V6<int>(v6Box, "_inKind") == KH_TravelMailbox.KindJoin, "26: film → join replacement from the same sender");
                // join → film while film accepting is OFF: refused (code 3) and the old pending closes
                v6Box.SendCustomEvent("_ToggleFilmAccepting");
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindFilm, 115);
                Expect(V6<int>(v6Box, "debugLastReplyCode") == KH_TravelMailbox.CodePaused && V6<int>(v6Box, "_inFrom") == -1, "26: join → film replacement refused while film accepting is OFF");
                v6Box.SendCustomEvent("_ToggleFilmAccepting");
                Expect(V6<bool>(v6Box, "filmAccepting"), "26: film accepting ON again");
                // ---- 27 (f). crossing: my film request to the second puppet meets its join to me ----
                Open(); uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeSelfFollow);
                var keep = v6Remote; var keepBox = v6RBox; v6Remote = v6Other; v6RBox = v6OBox; v6Other = keep; v6OBox = keepBox;   // ask the second puppet (the first is in its 30 s wait)
            }));
            AddAsk("27");
            steps.Add(Do(C6, () =>
            {
                Incoming(v6Remote.playerId, KH_TravelMailbox.KindJoin, ++v6OSeq);   // the second puppet's join crosses my film request
                bool iWin = v6Me < v6Remote.playerId;
                if (iWin) Expect(Phase() == KH_TravelMailbox.FilmPending && V6<int>(v6Box, "debugLastReplyCode") == KH_TravelMailbox.CodeBusy, "27: crossing, smaller id (me) wins: my film request stays, the join gets code 2");
                else Expect(Phase() == KH_TravelMailbox.FilmNone && V6<int>(v6Box, "filmResultCode") == KH_TravelMailbox.CrossYield, "27: crossing, I yield: my film request ended (CrossYield)");
                if (iWin)
                {
                    v6Box.SetProgramVariable("debugCrossInvert", 1);
                    Incoming(v6Remote.playerId, KH_TravelMailbox.KindJoin, ++v6OSeq);
                    Expect(Phase() == KH_TravelMailbox.FilmNone && V6<int>(v6Box, "filmResultCode") == KH_TravelMailbox.CrossYield && V6<int>(v6Box, "_inFrom") == v6Remote.playerId,
                        "27: crossing inverted: my film request yields (ended, CrossYield) and the join is pending");
                    v6Box.SetProgramVariable("debugCrossInvert", 0);
                }
                v6Box.SetProgramVariable("debugIncomingFrom", v6Remote.playerId); v6Box.SetProgramVariable("debugIncomingSeq", v6OSeq); v6Box.SendCustomEvent("_DebugIncomingCancelNow");
                Close();
                var keep = v6Remote; v6Remote = v6Other; v6RBox = v6OBox;   // back to the first puppet for case 18
                ClientSimMain.RemovePlayer(keep); v6Other = null; v6OBox = null;
                Note("not made here (two real clients): simultaneous requests arriving over the network, a real CancelJoin / AnsJoin race, crossing with real CallingPlayer");
            }));
        }

        /// <summary>Simulated black of the travel fade (state 2, and the black time kept fresh so its 3 s release never runs).</summary>
        static void HoldFade() { if (v6Fade == null) return; v6Fade.SetProgramVariable("_state", 2); v6Fade.SetProgramVariable("_blackAt", Time.time); }

        static readonly System.Collections.Generic.List<string> v6Deferred = new System.Collections.Generic.List<string>();

        /// <summary>A check that does not stop the run: recorded now, failed at the end of V6 (case 25 only).</summary>
        static void Soft(bool ok, string what)
        {
            if (ok) { Note("ok " + what); return; }
            Note("FAIL (deferred to the end of V6) " + what);
            v6Deferred.Add(what);
        }

        static bool V6Active(UdonBehaviour u, string field)
        {
            return u != null && u.TryGetProgramVariable(field, out object o) && o is GameObject g && g != null && g.activeSelf;
        }

        /// <summary>Puts my mailbox back (hooks 0, film ON, empty table), closes the notice / grants page, removes the puppets. Safe to call twice.</summary>
        static void V6Cleanup()
        {
            if (!v6Active) return;
            v6Active = false;
            if (v6Box != null)
            {
                foreach (var hook in new[] { "debugJoinLoopback", "debugAnswerCode", "debugIncomingFrom", "debugIncomingKind", "debugIncomingSeq", "debugCrossInvert" }) v6Box.SetProgramVariable(hook, 0);
                v6Box.SetProgramVariable("_filmBox", null);
                v6Box.SetProgramVariable("filmAccepting", true);
                Table(v6Box, -1, 0);
                v6Box.SetProgramVariable("_filmRows", 0);
            }
            if (v6Fade != null) { v6Fade.SetProgramVariable("_state", 0); v6Fade.SetProgramVariable("_alpha", 0f); }
            if (v6Notice != null) v6Notice.SendCustomEvent("_Hide");
            if (v6Pouch != null) v6Pouch.SendCustomEvent("_FilmGrantsBack");
            var all = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; VRCPlayerApi.GetPlayers(all);
            foreach (var p in all) if (p != null && !p.isLocal) ClientSimMain.RemovePlayer(p);
            v6Remote = null; v6RBox = null;
        }
    }
}
