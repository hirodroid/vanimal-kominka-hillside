using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 §17 V8-patterns (ClientSim, one client, simulated input; run it alone: --cases=V8, about 1-2 min):
    ///   1. 散歩 Vlog 1 (_Pattern2): the board lights it (colour, underline, "1/5"); 待って寄る ends with a REAL ShotMoveDone (pathHold 1 s for the
    ///      run, SIMULATED; a teleport toward the point as in V5) and the pattern goes to row 2 背中ごし by its condition; then each row's start is
    ///      moved back past its longest time (SIMULATED _patCutAt) → rows 3 横ならび, 4 犬追従 (the board's dog), 5 追いこし and back to row 1 (the
    ///      table repeats), each "by the longest time".
    ///   2. Stopping: a mode button, [とめる], ◀ ▶ and × each stop the pattern (the board goes back to its idle line); a reopen starts manual.
    ///   3. フリスビー 2 (_Pattern1) with ONE REAL THROW (V5's path: _pendingThrowVelocity + _CheckThrow → _Launch → ReqCatch): じぶん → "投げた"
    ///      at once → キャッチ待ち → (ShotCatch + 1.5 s, or the longest time) → おかえり (the internal mode: my side, while the dog carries MY throw)
    ///      → "受け渡した" (or the longest time) → じぶん.
    ///   4. おまかせ (_Pattern5): no leash, the dog at rest → the scene ふだん (じぶん); a second real throw → the scene フリスビー at once and
    ///      キャッチ待ち on the next frames.
    ///   5. dog-swap (Codex review of §17, before the throws while every dog rests): フリスビー 2's キャッチ待ち row with a latched catch and a
    ///      carry history (SIMULATED: _patRow / _mode / _patCondAt / _patSawCarry written), then the flow's dog changes (SIMULATED _patThrowDog):
    ///      the cut restarts on the new dog with the row's condition, time and carry history cleared, and the old catch's 1.5 s does not end it.
    ///   6. person-border (Codex review of §17, last; the V6 puppet and grant table): じぶん filming a puppet with a grant in use → a pattern
    ///      starts: _personLive drops, NewSubject runs (a cut), no person read while it runs; [とめる] → NewSubject again and the puppet is
    ///      framed again (the session is still in use).
    /// Which way a row ended is read from _patAdvBy: キャッチ待ち and おかえり may end only by their condition or their longest time (anything
    /// else fails); "by the longest time" goes to report.unproven.
    /// Not proven here: 散歩 / 洗う scenes of おまかせ (no leash / no wash call in the run), walking for real, the look of each cut (the user's PC).
    /// </summary>
    public static partial class KH_VideoHarnessEditor
    {
        const string C8 = "V8-patterns";
        static int v8Serial, v8Cuts;
        static float v8CatchAt = -1f, v8RowAt;
        static bool v8SawWelcome;
        static int v8Mark, v8Other = -1, v8Reads;

        static void V8Shift(float seconds)
        {
            uVideo.SetProgramVariable("_patCutAt", Get<float>("_patCutAt") - seconds);
            Note("simulated: the current cut started " + seconds + " s earlier");
        }

        /// <summary>W5: a key's text in every language of UITranslations.json (the label shows the client language's; ClientSim's language is a setting).</summary>
        static string[] AnyLang(string key)
        {
            var cat = KominkaDogRun.Editor.KD_LocalizationEditor.Load();
            return cat.languages.Select(l => KominkaDogRun.Editor.KD_LocalizationEditor.Text(cat, l.code, key)).Distinct().ToArray();
        }

        static void V8Board(int p, string tag)
        {
            var imgs = GetRaw("patternImages") as Image[];
            var lines = GetRaw("patternLines") as GameObject[];
            var label = GetRaw("patternLabel") as TMP_Text;
            Color on = Get<Color>("modeOnColor");
            bool lit = imgs != null && lines != null && imgs.Length == KH_VideoCamera.PatternCount && lines.Length == KH_VideoCamera.PatternCount;
            if (lit)
                for (int i = 0; i < KH_VideoCamera.PatternCount; i++)
                {
                    bool want = i == p;
                    if ((imgs[i] != null && (imgs[i].color == on) != want) || (lines[i] != null && lines[i].activeSelf != want)) lit = false;
                }
            Expect(lit, tag + (p >= 0 ? "the board lights pattern " + p + " only (colour + underline)" : "nothing lit on the board"));
            Expect(label != null && (p >= 0 ? AnyLang(KH_VideoCamera.PatternNameKey(p)).Any(s => label.text.Contains(s)) : AnyLang(KH_VideoCamera.KeyPatternIdle).Contains(label.text)),
                tag + "the board's line: '" + (label != null ? label.text : "null") + "'");
        }

        static void V8Throw(string tag)
        {
            var lp = Networking.LocalPlayer;
            var root = Get<Transform[]>("dogRoots")[0];
            Vector3 f = root.forward; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
            v5Dir = f;
            lp.TeleportTo(root.position - f * 2f, Quaternion.LookRotation(f));
            v5Disc.transform.position = root.position - f * 1.5f + Vector3.up * 1.3f;
            v5Disc.SetProgramVariable("_pendingThrowVelocity", f * 7f + Vector3.up * 2.5f);
            v5Disc.SetProgramVariable("_hasPendingThrow", true);
            v5Disc.SendCustomEvent("_CheckThrow");
            Expect(V5Get<bool>(v5Disc, "_flying"), tag + "the disc launched (real _Launch)");
            timeMark = Time.realtimeSinceStartup;
        }

        static void MakeV8Steps()
        {
            // ---- 1. 散歩 Vlog 1: the event path, then the longest time, the repeat ----
            steps.Add(Do(C8, () =>
            {
                V5Resolve();
                Open(); uVideo.SetProgramVariable("_dog", 0);
                uVideo.SetProgramVariable("pathHold", 1f);
                Note("simulated: pathHold 1 s for the run (restored at the end / in cleanup)");
                v8Cuts = Get<int>("_patCuts");
                uVideo.SendCustomEvent("_Pattern2");
                Expect(Get<bool>("_patRun") && Get<int>("_patIdx") == 2 && Get<int>("_patRow") == 0, "散歩 Vlog 1: running from row 1");
                Expect(Get<int>("_mode") == KH_VideoCamera.ModePath && Get<int>("_pathMove") == KH_VideoCamera.PathWaitApproach, "row 1 = みち 待って寄る");
                Expect(Get<int>("_patCuts") == v8Cuts + 1, "one cut started");
                V8Board(2, "散歩 Vlog 1: ");
                Expect((GetRaw("patternLabel") as TMP_Text)?.text.Contains("1/5") == true, "散歩 Vlog 1: the line says 1/5");
                rendersMark = Renders();
            }));
            steps.Add(Until(C8, () => Renders() >= rendersMark + 5, 4, "散歩 Vlog 1: no 5 real renders"));
            steps.Add(Do(C8, () =>
            {
                var lp = Networking.LocalPlayer;
                Vector3 feet = lp.GetPosition(), dir = Get<Vector3>("_pathDir");
                lp.TeleportTo(Ground(feet + dir * 6f), lp.GetRotation());   // inside pathApproachAt (12 → 6 m): 待って寄る glides, holds 1 s, ShotMoveDone
                Note("待って寄る: teleported 6 m toward its point");
                v8Serial = Get<int>("_shotSerial");
            }));
            steps.Add(Until(C8, () => Get<int>("_patRow") == 1, 1f + 3f * KH_VideoCameraEditor.PathGlideTau + 4f, "散歩 Vlog 1: ShotMoveDone did not take the pattern to row 2"));
            steps.Add(Do(C8, () =>
            {
                Expect(Get<int>("_shotSerial") > v8Serial && Get<int>("_shotKind") == KH_VideoCamera.ShotMoveDone, "row 1 ended with a real ShotMoveDone");
                Expect(Get<int>("_patAdvBy") == KH_VideoCamera.AdvByCondition, "row 1 ended by its condition (" + Get<int>("_patAdvBy") + ")");
                Expect(Get<int>("_mode") == KH_VideoCamera.ModeTps, "row 2 = 背中ごし");
                V8Shift(10.5f);
            }));
            int[] wantMode = { KH_VideoCamera.ModePath, KH_VideoCamera.ModeDogFollow, KH_VideoCamera.ModePath, KH_VideoCamera.ModePath };
            int[] wantArg = { KH_VideoCamera.PathBeside, -1, KH_VideoCamera.PathOvertake, KH_VideoCamera.PathWaitApproach };
            float[] shift = { 10.5f, 10.5f, 25.5f, 0f };
            for (int k = 0; k < 4; k++)
            {
                int row = (2 + k) % 5, m = wantMode[k], a = wantArg[k]; float sh = shift[k];
                steps.Add(Until(C8, () => Get<int>("_patRow") == row, 2, "散歩 Vlog 1: the longest time did not take it to row " + (row + 1)));
                steps.Add(Do(C8, () =>
                {
                    string tag = "row " + (row + 1) + " " + KH_VideoCamera.ModeWord(m) + ": ";
                    Expect(Get<int>("_patAdvBy") == KH_VideoCamera.AdvByMax, tag + "the previous row ended by its longest time");
                    Expect(Get<int>("_mode") == m && (a < 0 || Get<int>("_pathMove") == a), tag + "the mode / move of the table");
                    if (m == KH_VideoCamera.ModeDogFollow) Expect(Get<int>("_dog") == 0, tag + "the board's dog");
                    if (row == 0) Expect((GetRaw("patternLabel") as TMP_Text)?.text.Contains("1/5") == true && Get<bool>("_patRun"), tag + "the table repeats from the top");
                    if (sh > 0f) V8Shift(sh);
                }));
            }
            // ---- 2. what stops it ----
            steps.Add(Do(C8, () =>
            {
                uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeDogFollow);
                Expect(!Get<bool>("_patRun") && Get<int>("_mode") == KH_VideoCamera.ModeDogFollow, "a mode button stops the pattern and becomes the manual choice");
                V8Board(-1, "after the mode button: ");
                uVideo.SendCustomEvent("_Pattern4");
                Expect(Get<bool>("_patRun") && Get<int>("_patIdx") == 4 && Get<int>("_mode") == KH_VideoCamera.ModeDogEye, "犬とあそぶ starts on 犬目線");
                uVideo.SendCustomEvent("_PatternStop");
                Expect(!Get<bool>("_patRun"), "[とめる] stops it");
                V8Board(-1, "after [とめる]: ");
                uVideo.SendCustomEvent("_Pattern4");
                uVideo.SendCustomEvent("_DogNext");
                Expect(!Get<bool>("_patRun"), "◀ ▶ stop it");
                uVideo.SendCustomEvent("_Pattern3");
                Expect(Get<bool>("_patRun") && Get<int>("_mode") == KH_VideoCamera.ModePassBy, "散歩 Vlog 2 starts on 通り過ぎ");
                Close();
                Expect(!Get<bool>("_patRun"), "× stops it");
                Open();
                Expect(!Get<bool>("_patRun"), "a reopen starts manual");
                V8Board(-1, "after × and a reopen: ");
                uVideo.SetProgramVariable("pathHold", KH_VideoCameraEditor.PathHold);
                timeMark = Time.realtimeSinceStartup;
            }));
            // ---- 5. dog-swap (Codex review (1)): a new flow dog during the wait after a catch ----
            steps.Add(Do(C8, () =>
            {
                var roots = Get<Transform[]>("dogRoots");
                v8Other = -1;
                for (int i = 1; roots != null && i < roots.Length && v8Other < 0; i++) if (roots[i] != null && roots[i].gameObject.activeInHierarchy) v8Other = i;
                if (v8Other < 0) { Fail("dog-swap: no second active dog"); return; }
                uVideo.SetProgramVariable("_dog", 0);
                uVideo.SendCustomEvent("_Pattern1");
                Expect(Get<bool>("_patRun") && Get<int>("_patRow") == 0, "dog-swap: フリスビー 2 running");
                // simulated: row 2 キャッチ待ち of dog 0, its catch latched now (the 1.5 s wait running) and a carry seen
                uVideo.SetProgramVariable("_patThrowDog", 0);
                uVideo.SetProgramVariable("_patRow", 1);
                uVideo.SetProgramVariable("_mode", KH_VideoCamera.ModeCatchWait);
                uVideo.SetProgramVariable("_patCutAt", Time.time - 1f);
                uVideo.SetProgramVariable("_patCondAt", Time.time);
                uVideo.SetProgramVariable("_patSawCarry", true);
                v8Mark = Get<int>("_patDogSwaps");
                uVideo.SetProgramVariable("_patThrowDog", v8Other);
                Note("simulated: キャッチ待ち of dog 0 with its catch latched and a carry seen, then the flow's dog → " + v8Other);
            }));
            steps.Add(Until(C8, () => Get<int>("_patDogSwaps") > v8Mark, 1.5, "dog-swap: the new flow dog did not restart the cut"));
            steps.Add(Do(C8, () =>
            {
                Expect(Get<int>("_dog") == v8Other && Get<int>("_mode") == KH_VideoCamera.ModeCatchWait && Get<int>("_patRow") == 1, "dog-swap: キャッチ待ち again, on dog " + v8Other);
                Expect(Get<float>("_patCondAt") < 0f && !Get<bool>("_patSawCarry"), "dog-swap: the old dog's condition time and carry history are cleared");
                Expect(Time.time - Get<float>("_patCutAt") < 0.2f && Get<int>("_patDogSwaps") == v8Mark + 1, "dog-swap: the row's time starts again (once)");
                timeMark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C8, () => Time.realtimeSinceStartup - timeMark >= 2.5f, 4, "timer"));
            steps.Add(Do(C8, () =>
            {
                Expect(Get<bool>("_patRun") && Get<int>("_patRow") == 1 && Get<int>("_dog") == v8Other, "dog-swap: 2.5 s later (past the old catch + 1.5 s) the new dog's キャッチ待ち is still running");
                uVideo.SendCustomEvent("_PatternStop");
                uVideo.SetProgramVariable("_dog", 0);
                timeMark = Time.realtimeSinceStartup;
            }));
            // ---- 3. フリスビー 2 with one real throw ----
            steps.Add(Until(C8, () => Time.realtimeSinceStartup - timeMark > 1f && V5DogAtRest(), 20, "dog 0 did not come to rest (Idle / Ready) for the throw"));
            steps.Add(Do(C8, () =>
            {
                uVideo.SetProgramVariable("_dog", 0);
                uVideo.SendCustomEvent("_Pattern1");
                Expect(Get<bool>("_patRun") && Get<int>("_patRow") == 0 && Get<int>("_mode") == KH_VideoCamera.ModeSelfFollow, "フリスビー 2: row 1 = じぶん (waits for the throw)");
                v8Serial = Get<int>("_shotSerial"); v8CatchAt = -1f; v8SawWelcome = false;
                V8Throw("フリスビー 2: ");
            }));
            steps.Add(Until(C8, () => Get<int>("_patRow") == 1, 1.5, "フリスビー 2: the throw did not move the pattern to キャッチ待ち"));
            steps.Add(Do(C8, () =>
            {
                Note("投げた → キャッチ待ち after " + (Time.realtimeSinceStartup - timeMark).ToString("F2") + " s");
                Expect(Get<int>("_mode") == KH_VideoCamera.ModeCatchWait && Get<int>("_patAdvBy") == KH_VideoCamera.AdvByCondition, "row 2 = キャッチ待ち, by the throw");
                Expect(Get<int>("_patThrowDog") == 0 && Get<int>("_dog") == 0, "the flow's dog is the dog of my throw (0)");
            }));
            steps.Add(Until(C8, () =>
            {
                if (v8CatchAt < 0f && Get<int>("_shotSerial") > v8Serial && Get<int>("_shotKind") == KH_VideoCamera.ShotCatch) v8CatchAt = Time.realtimeSinceStartup;
                return Get<int>("_patRow") == 2;
            }, 15f + 1.5f + 2f, "フリスビー 2: キャッチ待ち never ended"));
            steps.Add(Do(C8, () =>
            {
                int by = Get<int>("_patAdvBy");
                Expect(Get<int>("_mode") == KH_VideoCamera.ModeWelcome, "row 3 = おかえり (the internal mode)");
                if (by == KH_VideoCamera.AdvByCondition)
                {
                    float wait = Time.realtimeSinceStartup - v8CatchAt;
                    Expect(v8CatchAt >= 0f && wait >= 1.3f, "おかえり came " + wait.ToString("F2") + " s after ShotCatch (the row's delay 1.5 s, sampled per frame)");
                }
                else if (by == KH_VideoCamera.AdvByMax)
                {
                    Note("キャッチ待ち ended by its longest time (no ShotCatch in time)");
                    report.unproven.Add("V8: ShotCatch + 1.5 s → おかえり (this throw's キャッチ待ち ended by its longest time)");
                }
                else Fail("キャッチ待ち ended by " + by + " (only 1 = the condition or 2 = the longest time are allowed; 3 = lost, 4 = scene)");
                var lbl = GetRaw("modeImages") as Image[];
                Color on = Get<Color>("modeOnColor");
                Expect(lbl == null || lbl.All(i => i == null || i.color != on), "no mode button lights for おかえり (it has none)");
            }));
            steps.Add(Until(C8, () =>
            {
                if (Get<int>("_mode") == KH_VideoCamera.ModeWelcome && Get<bool>("_welcomeMine"))
                {
                    if (!v8SawWelcome)
                    {
                        v8SawWelcome = true;
                        var lp = Networking.LocalPlayer;
                        Vector3 feet = lp.GetPosition(), want = Get<Vector3>("_wantPos"), d = want - feet; d.y = 0f;
                        Expect(d.magnitude <= KH_VideoCameraEditor.WelcomeDist + KH_VideoCameraEditor.WelcomeSide + 0.2f && Mathf.Abs(want.y - feet.y - KH_VideoCameraEditor.WelcomeHeight) < 0.1f,
                            "おかえり: the camera is on my side (" + d.magnitude.ToString("F2") + " m from my feet, " + (want.y - feet.y).ToString("F2") + " m up)");
                        Expect(Roll() < 0.01f, "おかえり: roll 0");
                    }
                }
                return Get<int>("_patRow") == 0;
            }, 25f + 2f, "フリスビー 2: おかえり never ended"));
            steps.Add(Do(C8, () =>
            {
                Expect(Get<int>("_mode") == KH_VideoCamera.ModeSelfFollow, "back to row 1 じぶん (the flow repeats)");
                int wby = Get<int>("_patAdvBy");
                Expect(wby == KH_VideoCamera.AdvByCondition || wby == KH_VideoCamera.AdvByMax, "おかえり ended by " + wby + " (allowed: 1 = 受け渡した, 2 = the longest time)");
                if (!v8SawWelcome) report.unproven.Add("V8: the おかえり framing of my throw (the dog was not carrying while the row ran)");
                if (wby == KH_VideoCamera.AdvByMax) report.unproven.Add("V8: 受け渡した (Drop / the carry ending) ended no row");
                uVideo.SendCustomEvent("_PatternStop");
                timeMark = Time.realtimeSinceStartup;
            }));
            // ---- 4. おまかせ: ふだん, then a throw switches at once ----
            steps.Add(Until(C8, () => Time.realtimeSinceStartup - timeMark > 1f && V5DogAtRest() && !V5Get<bool>(v5Disc, "_flying"), 25, "dog 0 did not come to rest for the second throw"));
            steps.Add(Do(C8, () =>
            {
                uVideo.SendCustomEvent("_Pattern5");
                Expect(Get<bool>("_patRun") && Get<int>("_patIdx") == KH_VideoCamera.PatternOmakase, "おまかせ running");
                Expect(Get<int>("_patScene") == KH_VideoCamera.SceneIdle && Get<int>("_patSeq") == KH_VideoCamera.SeqIdle && Get<int>("_mode") == KH_VideoCamera.ModeSelfFollow,
                    "おまかせ: no leash, no wash, the dog at rest → ふだん, じぶん (scene " + Get<int>("_patScene") + ")");
                V8Board(KH_VideoCamera.PatternOmakase, "おまかせ: ");
                V8Throw("おまかせ: ");
            }));
            steps.Add(Until(C8, () => Get<int>("_patScene") == KH_VideoCamera.SceneFrisbee, 1.0, "おまかせ: the throw did not switch the scene to フリスビー at once"));
            steps.Add(Do(C8, () =>
            {
                Note("おまかせ: フリスビー after " + (Time.realtimeSinceStartup - timeMark).ToString("F2") + " s (ふだん's shortest time is " + 5 + " s: the throw did not wait for it)");
                Expect(Get<int>("_patAdvBy") == KH_VideoCamera.AdvByScene || Get<int>("_patRow") >= 1, "おまかせ: switched by the scene");
            }));
            steps.Add(Until(C8, () => Get<int>("_mode") == KH_VideoCamera.ModeCatchWait, 1.5, "おまかせ: the frisbee flow did not reach キャッチ待ち (the throw is already in the air)"));
            steps.Add(Do(C8, () =>
            {
                Expect(Get<int>("_patSeq") == KH_VideoCamera.SeqFrisbee1, "おまかせ: the frisbee flow is フリスビー 1's table");
                Close();
                Expect(!Get<bool>("_patRun"), "× stops おまかせ");
                Note("not proven by this case: おまかせ's 散歩 / 洗う scenes, other players' throws, walking for real, the look of each cut (the user's PC)");
            }));
            AddPersonBorderSteps();
        }

        /// <summary>No throw state left for フリスビー 1's "投げた": dog 0 Idle / Ready, its disc not flying and not held (by anyone).</summary>
        static bool V8NoThrowState()
        {
            if (!V5DogAtRest() || V5Get<bool>(v5Disc, "_flying")) return false;
            var pk = v5Disc != null && v5Disc.TryGetProgramVariable("_pickup", out object po) ? po as VRC_Pickup : null;
            return pk == null || !pk.IsHeld;
        }

        /// <summary>6. person-border (Codex review (4)): the V6 puppet, asked through the real UI path, its grant table written (V6's simulation).</summary>
        static void AddPersonBorderSteps()
        {
            string keepCase = C6; C6 = C8;   // the V6 helpers tag their steps with C6
            // run 20261008-021024: after part 4's throw dog 0 was still busy with my disc, so フリスビー 1's じぶん row ended at once by "投げた"
            // and the stop came in キャッチ待ち (no border by design). Wait until no disc flies, nobody holds dog 0's disc and dog 0 rests
            steps.Add(Do(C8, () => { timeMark = Time.realtimeSinceStartup; }));
            steps.Add(Until(C8, () => Time.realtimeSinceStartup - timeMark > 1f && V8NoThrowState(), 40, "person-border: dog 0 did not come to rest / a disc still flies or is held (needed so フリスビー 1 stays on じぶん)"));
            steps.Add(Do(C8, () =>
            {
                Note("person-border: waited " + (Time.realtimeSinceStartup - timeMark).ToString("F1") + " s for dog 0 to rest and no disc in the air / in a hand");
                v6Box = V6BoxOf(Networking.LocalPlayer);
                if (v6Box == null) { Fail("person-border: my KH_TravelMailbox PlayerObject is missing (Course/85)"); return; }
                var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true);
                v6Notice = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_TravelNotice");
                v6Pouch = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_TreatPouch");
                v6Me = Networking.LocalPlayer.playerId;
                v6Active = true;
                v6Box.SetProgramVariable("debugJoinLoopback", 1);
                Open(); uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeSelfFollow);
            }));
            AddSpawn("KH_PatternTarget");
            AddAsk("person-border");
            AddInUse("person-border");
            steps.Add(Do(C8, () =>
            {
                Expect(Get<bool>("_personLive"), "person-border: じぶん films the puppet (grant in use)");
                v8Mark = Get<int>("_patPersonCuts"); v8Cuts = Cuts(); v8Reads = Reads();
                uVideo.SendCustomEvent("_Pattern0");   // フリスビー 1: row 1 = じぶん (the same mode number)
                Expect(Get<bool>("_patRun") && Get<int>("_mode") == KH_VideoCamera.ModeSelfFollow, "person-border: the pattern starts on じぶん");
                Expect(!Get<bool>("_personLive") && Get<int>("_patPersonCuts") == v8Mark + 1 && Get<int>("_subjectPoses") == 0,
                    "person-border: _personLive dropped and NewSubject ran at the start (subject poses 0)");
                rendersMark = Renders();
            }));
            steps.Add(Until(C8, () => Renders() >= rendersMark + 5, 3, "person-border: no renders in the pattern"));
            steps.Add(Do(C8, () =>
            {
                Expect(Reads() == v8Reads && !Get<bool>("_personLive"), "person-border: no read of the puppet while the pattern runs (reads " + (Reads() - v8Reads) + ")");
                Expect(Cuts() > v8Cuts, "person-border: a cut to my framing (no smoothing from the puppet's)");
                Expect(Phase() == KH_TravelMailbox.FilmInUse, "person-border: the session itself is still in use (it times out after 60 s like any other mode)");
                v8Mark = Get<int>("_patPersonCuts");
                int stopMode = Get<int>("_mode"), stopRow = Get<int>("_patRow");
                uVideo.SendCustomEvent("_PatternStop");
                string at = " (at the stop: mode " + stopMode + " " + KH_VideoCamera.ModeWord(stopMode) + ", row " + (stopRow + 1) + ")";
                if (stopMode == KH_VideoCamera.ModeSelfFollow)
                    Expect(!Get<bool>("_patRun") && Get<int>("_patPersonCuts") == v8Mark + 1 && Get<int>("_subjectPoses") == 0, "person-border: [とめる] on じぶん → NewSubject again" + at);
                else
                {
                    // not on じぶん: no person border by design (the stop keeps that cut). Go back to じぶん by hand so the next step can see the puppet again
                    Expect(!Get<bool>("_patRun") && Get<int>("_patPersonCuts") == v8Mark, "person-border: [とめる] off じぶん → no border" + at);
                    report.unproven.Add("V8 person-border: the stop on じぶん (the pattern had moved on" + at + ")");
                    uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeSelfFollow);
                    Note("person-border: back to じぶん with its mode button");
                }
            }));
            steps.Add(Until(C8, () => Get<bool>("_personLive") && Reads() > v8Reads, 3, "person-border: after [とめる] the puppet was not framed again"));
            steps.Add(Do(C8, () =>
            {
                Note("person-border: the puppet is framed again after the stop (a new subject)");
                Close();
                Expect(Phase() == KH_TravelMailbox.FilmNone, "person-border: × ends the session");
                V6Cleanup();
            }));
            C6 = keepCase;
        }
    }
}
