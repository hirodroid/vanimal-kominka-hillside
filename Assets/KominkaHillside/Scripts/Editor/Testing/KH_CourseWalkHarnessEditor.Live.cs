using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>破壊監査 2026-10-07 Phase A の RC-13 (Course/128)。ライブの台帳 (KD_LiveSystem の liveId / startMs / hostId) を Owner の自分がヒープへ書き、
    /// startMs を過去へずらして「開始から t 秒」を作る (曲を 124 s 待たない)。読み出しは製品の公開プロパティの getter (get_IsPlaying など) を
    /// SendCustomEvent で呼んで戻り値の記号 (__0_get_X__ret) を読む = 製品と同じ式。首輪 (83b)・ナイトモード (83c) と同じく travel の台本で回す。
    ///   LV-end-zero      開始 (t = 0) → 夜だけ (IsPlaying false) → t = 3.5 でステージが鳴る → t = 3 + 124 − 0.5 で IsPlaying true →
    ///                    t = 3 + 124 + 0.1 で IsPlaying / IsNightWanted false、SecondsUntilAvailable 59〜60、Update がステージを止める
    ///                    (PlayableDirector が Playing でない、stageRoots[0] が無効、_playingId −1)
    ///   LV-liveid-reset  t = 3 + 124 + 59 で liveId 0 のまま・残り約 1 s → t = 3 + 124 + 60 + 0.1 で Owner の Update が liveId = −1、
    ///                    SecondsUntilAvailable 0、CanStart true。後始末: hostId / startMs を元へ、RequestSerialization
    /// 開催は模擬: 本番の入口 (_StartLive → KD_GroupActivities._Request(4) → _BeginFor) は購入済みの作品が要るので通らず、_BeginFor が書くのと同じ
    /// 3 つの値を書く。Owner でない端末 (60 s 明けに liveId を書かない側、受信した −1 で休眠に入る側) は ClientSim 1 人では作れないので対象外。
    /// 途中で落ちたケースは後始末を通らないが、Play を抜けると実行時の値は捨てられる (シーンは保存しない)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/128 - Walk Harness Live Duration and liveId (audit RC-13)")]
        public static void LiveDurationHarness()
        {
            BeginTravel(false);
            report.liveOnly = true;
            report.deadline = EditorApplication.timeSinceStartup + 180;   // 15 s join wait + about 5 s of cases
            report.limitations = "Single local ClientSim: the local player is the KD_LiveSystem Owner. The live is SIMULATED by heap writes of liveId / hostId / startMs "
                + "(the three values _BeginFor writes; the real start needs a purchased work), and elapsed time is made by moving startMs into the past. "
                + "Properties are read through the product's own getters. No second client: the non-Owner path (no write at the end of the cooldown, sleeping on the received -1), "
                + "late join mid-song, audio length and the look are not covered.";
            Save();
        }

        static UdonBehaviour lvLive;
        static IUdonProgram lvProg;
        static readonly Dictionary<string, string> lvRet = new Dictionary<string, string>();
        static float lvDur;
        static int lvOrigLive, lvOrigStart, lvOrigHost;
        static PlayableDirector lvDirector;
        static GameObject lvRoot;

        static string LiveReason()
        {
            return "Local ClientSim live ledger cases passed (simulated start by heap writes, time moved by startMs): the stage plays until 3 + " + lvDur + " s and stops at the end "
                + "(LV-end-zero); 60 s later the Owner resets liveId to -1 and a new live can start (LV-liveid-reset). Not covered: a second client (non-Owner), late join, the audio itself.";
        }

        /// <summary>製品の公開プロパティを getter で読む (U# は public の accessor を get_X の名前で書き出し、戻り値は __N_get_X__ret)。</summary>
        static T LvGet<T>(string prop)
        {
            string entry = "get_" + prop, ret;
            if (!lvRet.TryGetValue(prop, out ret))
            {
                if (!lvProg.EntryPoints.TryGetAddressFromSymbol(entry, out _)) throw new InvalidOperationException("KD_LiveSystem has no exported entry " + entry);
                var m = new List<string>();
                for (int n = 0; n <= 20; n++) if (lvProg.SymbolTable.TryGetAddressFromSymbol("__" + n + "_" + entry + "__ret", out _)) m.Add("__" + n + "_" + entry + "__ret");
                if (m.Count != 1) throw new InvalidOperationException("KD_LiveSystem return symbol of " + entry + ": " + m.Count + " matches (" + string.Join(",", m) + ")");
                ret = m[0]; lvRet[prop] = ret;
            }
            lvLive.SendCustomEvent(entry);
            object o;
            if (!lvLive.TryGetProgramVariable(ret, out o) || !(o is T)) throw new InvalidOperationException("return of " + entry + " unreadable (" + o + ")");
            return (T)o;
        }

        /// <summary>startMs を「今から t 秒前」にする (サーバー時刻の差は符号付き int、製品の Elapsed と同じ)。</summary>
        static void LvAt(float t) { lvLive.SetProgramVariable("startMs", unchecked(Networking.GetServerTimeInMilliseconds() - Mathf.RoundToInt(t * 1000f))); }
        static int LvLiveId() { return Get<int>(lvLive, "liveId"); }
        static int LvPlaying() { return Get<int>(lvLive, "_playingId"); }
        static string LvState()
        {
            return "liveId " + LvLiveId() + ", Elapsed " + LvGet<float>("Elapsed").ToString("F2") + ", IsPlaying " + LvGet<bool>("IsPlaying") + ", IsNightWanted " + LvGet<bool>("IsNightWanted")
                + ", SecondsUntilAvailable " + LvGet<float>("SecondsUntilAvailable").ToString("F2") + ", CanStart " + LvGet<bool>("CanStart") + ", _playingId " + LvPlaying()
                + ", root " + (lvRoot != null && lvRoot.activeSelf) + ", director " + (lvDirector != null ? lvDirector.state + " t=" + lvDirector.time.ToString("F2") : "null");
        }

        static void MakeLiveCases()
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            lvLive = all.Single(u => u.programSource != null && u.programSource.name == "KD_LiveSystem");
            lvProg = lvLive.programSource.SerializedProgramAsset.RetrieveProgram();
            lvRet.Clear();
            float nightIn = KominkaDogRun.KD_LiveSystem.NightInSeconds, cooldown = KominkaDogRun.KD_LiveSystem.CooldownSeconds;

            // ---- LV-end-zero ----
            var s = TvCase("LV-end-zero", true, 0);
            s.Add(TvDo(() =>
            {
                var durs = Get<float[]>(lvLive, "stageDurations"); var dirs = Get<PlayableDirector[]>(lvLive, "stageDirectors"); var roots = Get<GameObject[]>(lvLive, "stageRoots");
                TvExpect(durs.Length >= 1 && dirs.Length == durs.Length && roots.Length == durs.Length && dirs[0] != null && roots[0] != null, "live 0 is wired (stageDurations / stageDirectors / stageRoots, " + durs.Length + " works)");
                lvDur = durs[0]; lvDirector = dirs[0]; lvRoot = roots[0];
                TvExpect(Mathf.Abs(lvDur - 124f) < 0.05f, "stageDurations[0] is the song's end, 124 s (RC-13; was 135.9): " + lvDur);
                TvExpect(Mathf.Abs(LvGet<float>("CurrentDuration")) < 1e-4f, "no live chosen yet: CurrentDuration 0");
                // U# behaviour arrays live in the heap as Component[] (not the U# type), so read it as an Array
                object perfObj; lvLive.TryGetProgramVariable("dogPerformances", out perfObj); var perf = perfObj as Array;
                TvExpect((perf == null || perf.Length == 0 || (perf.GetValue(0) as UnityEngine.Object) == null) && Get<int>(lvLive, "candyLiveId") != 0, "live 0 is not a dog performance (a heap write would skip the Candy preparation)");
                TvExpect(Networking.IsOwner(lvLive.gameObject), "the local player owns KD_LiveSystem (ClientSim)");
                lvOrigLive = LvLiveId(); lvOrigStart = Get<int>(lvLive, "startMs"); lvOrigHost = Get<int>(lvLive, "hostId");
                TvExpect(lvOrigLive == -1 && LvPlaying() == -1 && !lvRoot.activeSelf, "nothing playing before the case (" + LvState() + ")");
                TvExpect(!Get<bool>(lvLive, "candyPreparing") && !LvGet<bool>("DogRecovering"), "no Candy preparation or dog recovery");
                TvExpect(LvGet<bool>("CanStart") && LvGet<float>("SecondsUntilAvailable") == 0f, "a live can start before the case");
                lvLive.SetProgramVariable("liveId", 0); lvLive.SetProgramVariable("hostId", Networking.LocalPlayer.playerId); LvAt(0f);
                lvLive.RequestSerialization();
                TvNote("simulated start: liveId 0, hostId " + Networking.LocalPlayer.playerId + ", startMs = now (the values _BeginFor writes). Before: liveId " + lvOrigLive + ", hostId " + lvOrigHost + ", startMs " + lvOrigStart);
            }));
            s.Add(TvWait(0.3));
            s.Add(TvDo(() =>
            {
                string st = LvState();
                TvExpect(LvGet<int>("CurrentLiveId") == 0 && LvGet<bool>("IsNightWanted") && !LvGet<bool>("IsPlaying") && !LvGet<bool>("CanStart"), "t ~0.3: night fade-in, no music yet, cannot start another (" + st + ")");
                float rest = LvGet<float>("SecondsUntilAvailable"), total = nightIn + lvDur + cooldown;
                TvExpect(rest > total - 1.5f && rest <= total, "t ~0.3: available again in about " + total + " s (" + rest.ToString("F2") + ")");
                LvAt(nightIn + 0.5f);
            }));
            s.Add(TvUntil(() => LvPlaying() == 0, 2, "the stage did not start at t = 3.5"));
            s.Add(TvDo(() =>
            {
                string st = LvState();
                TvExpect(LvGet<bool>("IsPlaying") && lvRoot.activeSelf && lvDirector.state == PlayState.Playing, "t ~3.5: the stage plays (" + st + ")");
                TvExpect(lvDirector.time >= 0.3 && lvDirector.time <= 2.0, "the director started at the offset (t - 3 = ~0.5 s): " + lvDirector.time.ToString("F2"));
                LvAt(nightIn + lvDur - 0.5f);
                st = LvState();
                TvExpect(LvGet<bool>("IsPlaying") && LvGet<bool>("IsNightWanted"), "t = 3 + " + lvDur + " - 0.5: still playing (" + st + ")");
                float rest = LvGet<float>("SecondsUntilAvailable");
                TvExpect(rest > cooldown && rest <= cooldown + 0.55f, "t = 3 + " + lvDur + " - 0.5: available again in about " + (cooldown + 0.5f) + " s (" + rest.ToString("F2") + ")");
                TvNote("t = " + (nightIn + lvDur - 0.5f) + ": " + st);
            }));
            s.Add(TvWait(0.1));
            s.Add(TvDo(() =>
            {
                // a long editor frame could already pass the end; then this observation is skipped (the end itself is judged in the next step)
                float e = LvGet<float>("Elapsed");
                if (e < nightIn + lvDur - 0.02f) TvExpect(LvPlaying() == 0 && lvDirector.state == PlayState.Playing, "the stage keeps playing before the end (Elapsed " + e.ToString("F2") + ")");
                else TvNote("Elapsed " + e.ToString("F2") + " already past the end on this frame: the before-the-end playing check was skipped");
                LvAt(nightIn + lvDur + 0.1f);
                string st = LvState();
                TvExpect(!LvGet<bool>("IsPlaying") && !LvGet<bool>("IsNightWanted"), "t = 3 + " + lvDur + " + 0.1: the song is over and the night is not wanted (" + st + ")");
                float rest = LvGet<float>("SecondsUntilAvailable");
                TvExpect(rest >= cooldown - 1f && rest <= cooldown, "t = 3 + " + lvDur + " + 0.1: the 60 s wait remains (" + rest.ToString("F2") + ", want 59..60)");
                TvExpect(LvGet<int>("CurrentLiveId") == 0 && !LvGet<bool>("CanStart"), "during the wait: liveId stays 0 and no live can start");
            }));
            s.Add(TvUntil(() => LvPlaying() == -1, 2, "Update did not stop the stage after the end (_playingId stayed 0)"));
            s.Add(TvDo(() =>
            {
                string st = LvState();
                TvExpect(!lvRoot.activeSelf && lvDirector.state != PlayState.Playing, "the end: PlayableDirector stopped and stageRoots[0] inactive (" + st + ")");
                TvNote("end at 3 + " + lvDur + " s: " + st + ". The stop is Update's reconcile (want -1 != _playingId -> Stop + SetActive(false))");
            }));

            // ---- LV-liveid-reset ----
            s = TvCase("LV-liveid-reset", true, 0);
            s.Add(TvDo(() =>
            {
                TvExpect(LvLiveId() == 0, "the ledger still holds live 0 from LV-end-zero (" + LvLiveId() + ")");
                LvAt(nightIn + lvDur + cooldown - 1f);
                float rest = LvGet<float>("SecondsUntilAvailable");
                TvExpect(rest > 0.9f && rest <= 1.01f && !LvGet<bool>("CanStart"), "t = 3 + " + lvDur + " + 59: about 1 s left, cannot start (" + rest.ToString("F2") + ")");
            }));
            s.Add(TvWait(0.3));
            s.Add(TvDo(() =>
            {
                float e = LvGet<float>("Elapsed"), rest = LvGet<float>("SecondsUntilAvailable");
                if (e < nightIn + lvDur + cooldown - 0.02f)
                    TvExpect(LvLiveId() == 0 && LvGet<int>("CurrentLiveId") >= 0 && rest > 0f, "before the 60 s wait ends the Owner keeps liveId (" + LvState() + ")");
                else TvNote("Elapsed " + e.ToString("F2") + " already past the wait on this frame: the keep check was skipped");
                LvAt(nightIn + lvDur + cooldown + 0.1f);
            }));
            s.Add(TvUntil(() => LvLiveId() == -1, 2, "the Owner did not reset liveId to -1 after the 60 s wait"));
            s.Add(TvDo(() =>
            {
                string st = LvState();
                TvExpect(LvGet<int>("CurrentLiveId") == -1 && LvGet<float>("SecondsUntilAvailable") == 0f && LvGet<bool>("CanStart"), "after the wait: CurrentLiveId -1, SecondsUntilAvailable 0, CanStart (" + st + ")");
                TvExpect(!LvGet<bool>("IsPlaying") && !LvGet<bool>("IsNightWanted") && LvPlaying() == -1 && !lvRoot.activeSelf, "nothing plays after the reset");
                int host = Get<int>(lvLive, "hostId");
                TvNote("reset at 3 + " + lvDur + " + 60 s: " + st + ". hostId after the reset " + host + " (the product resets only liveId)");
                // cleanup: the ledger as before the case (liveId is already -1)
                lvLive.SetProgramVariable("liveId", lvOrigLive); lvLive.SetProgramVariable("hostId", lvOrigHost); lvLive.SetProgramVariable("startMs", lvOrigStart);
                lvLive.RequestSerialization();
                TvExpect(LvLiveId() == lvOrigLive && Get<int>(lvLive, "hostId") == lvOrigHost && Get<int>(lvLive, "startMs") == lvOrigStart && LvGet<bool>("CanStart"),
                    "ledger restored (liveId " + lvOrigLive + ", hostId " + lvOrigHost + ", startMs " + lvOrigStart + ") and a live can start");
                TvNote("non-Owner path not covered (single ClientSim player)");
            }));
        }
    }
}
