using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-ENV-005 オート／マニュアルのナイトモード (Course/83c)。首輪 (83b) と同じく travel の台本 (TravelTick) で回す。
    /// ワープ・地点・暗転・犬は使わない。PlayerData は 96・97 と同じく「初めて来た人」で Play に入る (退避は Finish が戻す)。
    /// 40 分を待たないため、KH_AutoNight の daySeconds / duskSeconds / nightDarkness を実行時にだけ縮める (シーンは保存しない)。
    ///   N0-fresh-auto            保存なし → オートで始まる。Owner (= 自分) が入室後に共通の開始時刻を 1 回だけ書いた。昼 (0)。保存キーは書かれていない
    ///   N1-auto-progress         時計の進みで暗くなり、夜 (nightDarkness) で止まる。開始時刻は変わらない
    ///   N2-manual-ignores-clock  [マニュアル] → 自分の値 (初回は OFF)。スライダー 0.4。共通の値が変わってもマニュアルの表示と保存値は変わらない
    ///   N3-slider-flips-manual   [オート] → 共通の値へ。保存したマニュアル値は上書きされない。オート中にスライダーを触る → マニュアルへ切替・その値
    ///   N4-manual-to-auto-fade   マニュアル → オートで 3 s かけて共通の値へ寄る (途中の値を観測 = 飛ばない)。時計はリセットされない
    ///   N5-late-join-mock        模擬: 開始時刻を 0 (未受信) → 実値 (受信) とヒープへ書く。0 の間は昼、受信後はフェードして合流
    ///   N6-owner-handover-mock   模擬: 値があれば書き換えない。未受信・未 settle の新 Owner (_firstPlayer / _received / _debugUnsettled をヒープへ書く) は
    ///                            入室時の _TryStart・引継ぎの _TryStart・再試行 2 回で書かず、受信した値を保つ。最初の参加者と settle 済みの Owner は 0 なら 1 回書く
    ///   N7-live-wins             ライブの暗さ (liveDarkness) が強ければライブ、弱ければオート、終われば 0 でなくオートへ戻る
    /// 1 クライアントなので実通信の遅着・Owner の退出・2 人の一致・再入室の復元 (OnPlayerRestored の 2 回目) は見ない (2 人の Play / 実機)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/83c - Walk Harness Night Mode Auto Manual (IMP-ENV-005)")]
        public static void NightModeHarness()
        {
            BeginTravel(false);
            report.nightOnly = true;
            report.deadline = EditorApplication.timeSinceStartup + 400;
            report.limitations = "Single local ClientSim: the local player is the instance master and the KH_AutoNight Owner. Mode buttons and the slider go through the real "
                + "KD_MenuSettings events (_NightAuto / _NightManual / Slider.value -> _NightMode); no ray click. The timeline is shortened at runtime (daySeconds / duskSeconds / nightDarkness). "
                + "Late join (N5) and Owner handover (N6) are SIMULATED by heap writes of _epochMs and a direct _TryStart; no real network order, second client, Owner leaving or re-entry restore.";
            try { CmPrepareStorage(CmSetLedger, null); }   // fresh profile (no KH_NightMode.* keys): the initial mode must be Auto
            catch (Exception e) { End("BLOCKED", "PlayerData preparation failed (nothing tested): " + e.Message); return; }
            Save();
        }

        static UdonBehaviour nNight, nAuto, nSettings;
        static UnityEngine.UI.Slider nSlider;
        static int nEpoch;
        static float nMid;

        static string NightReason()
        {
            return "Local ClientSim night mode cases passed: fresh profile starts in Auto and the Owner set the shared start once (N0); Auto darkens with the clock and stops at night (N1); "
                + "Manual ignores the clock and the shared value (N2); the slider in Auto switches to Manual and Auto never overwrites the saved manual value (N3); Manual -> Auto fades to the shared value "
                + "without resetting the clock (N4); a late value arrival fades in (N5, simulated); the handover check never rewrites a set start (N6, simulated); the live wins only when stronger (N7). "
                + "Not covered: two real clients agreeing, real late join, the Owner leaving, re-entry restore, the look on PCVR / Quest / iOS.";
        }

        static void MakeNightCases()
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            nNight = all.Single(u => u.programSource != null && u.programSource.name == "KH_NightMode");
            nAuto = all.Single(u => u.programSource != null && u.programSource.name == "KH_AutoNight");
            nSettings = all.Single(u => u.programSource != null && u.programSource.name == "KD_MenuSettings");
            nSlider = Get<UnityEngine.UI.Slider>(nSettings, "nightMode");
            if (nSlider == null) throw new InvalidOperationException("KD_MenuSettings.nightMode slider missing (run Menu/15)");

            // ---- N0 ----
            var s = TvCase("N0-fresh-auto", false, 0);
            s.Add(TvUntil(() => Get<bool>(nNight, "_restored"), 20, "OnPlayerRestored did not reach KH_NightMode"));
            s.Add(TvUntil(() => Get<int>(nAuto, "_epochMs") != 0, 10, "the Owner did not start the shared clock after joining"));
            s.Add(TvDo(() =>
            {
                nEpoch = Get<int>(nAuto, "_epochMs");
                TvExpect(Get<bool>(nNight, "_auto"), "fresh profile starts in Auto");
                TvExpect(!PdHas(KH_NightMode.ModeKey) && !PdHas(KH_NightMode.SaveKey), "nothing saved before the player chooses (mode / manual keys absent)");
                TvExpect(Get<float>(nNight, "autoDarkness") == 0f && NShown() == 0f && NApplied() == 0f, "day: autoDarkness / shown / applied 0 (elapsed " + NElapsed().ToString("F1") + " s < day " + KH_AutoNight.DefaultDaySeconds + " s)");
                TvExpect(Mathf.Approximately(Get<float>(nAuto, "daySeconds"), KH_AutoNight.DefaultDaySeconds) && Mathf.Approximately(Get<float>(nAuto, "duskSeconds"), KH_AutoNight.DefaultDuskSeconds)
                    && Mathf.Approximately(Get<float>(nAuto, "nightDarkness"), KH_AutoNight.DefaultNightDarkness), "scene timeline is the default (10 min + 30 min -> 0.8)");
                TvNote("fresh profile: Auto, epoch " + nEpoch + " written by the Owner " + NElapsed().ToString("F1") + " s ago, day (0)");
            }));

            // ---- N1 ----
            s = TvCase("N1-auto-progress", false, 0);
            s.Add(TvDo(() =>
            {
                // day ends 1 s from now, dusk 6 s, night 0.8 (only the runtime heap; the scene is not saved)
                nAuto.SetProgramVariable("daySeconds", NElapsed() + 1f); nAuto.SetProgramVariable("duskSeconds", 6f);
                TvNote("runtime timeline: day = elapsed + 1 s, dusk 6 s, night " + KH_AutoNight.DefaultNightDarkness);
            }));
            s.Add(TvUntil(() => { float a = Get<float>(nNight, "autoDarkness"); return a > 0.05f && a < 0.7f; }, 6, "autoDarkness did not pass through the dusk"));
            s.Add(TvDo(() => { nMid = Get<float>(nNight, "autoDarkness"); TvNote("mid-dusk autoDarkness " + nMid.ToString("F3")); }));
            s.Add(TvUntil(() => Mathf.Approximately(Get<float>(nNight, "autoDarkness"), KH_AutoNight.DefaultNightDarkness), 10, "autoDarkness did not reach the night value"));
            s.Add(TvUntil(() => Mathf.Abs(NShown() - KH_AutoNight.DefaultNightDarkness) < 1e-4f && !Get<bool>(nNight, "_fading"), 5, "shown did not settle at the night value"));
            s.Add(TvWait(2.5));
            s.Add(TvDo(() =>
            {
                TvExpect(Mathf.Approximately(Get<float>(nNight, "autoDarkness"), KH_AutoNight.DefaultNightDarkness), "night is the end state (no cycle) 2.5 s later");
                TvExpect(Mathf.Abs(NApplied() - KH_AutoNight.DefaultNightDarkness) < 1e-4f, "materials written at the night value (applied " + NApplied().ToString("F3") + ")");
                TvExpect(Get<int>(nAuto, "_epochMs") == nEpoch, "the shared start did not change");
                TvNote("dusk " + nMid.ToString("F3") + " -> night " + KH_AutoNight.DefaultNightDarkness + ", stays; epoch unchanged");
            }));

            // ---- N2 ----
            s = TvCase("N2-manual-ignores-clock", false, 0);
            s.Add(TvDo(() => nSettings.SendCustomEvent("_NightManual")));
            s.Add(TvDo(() =>
            {
                TvExpect(!Get<bool>(nNight, "_auto"), "[Manual] switched to Manual");
                TvExpect(PdInt(KH_NightMode.ModeKey) == KH_NightMode.ModeManual, "mode saved as Manual");
            }));
            s.Add(TvUntil(() => NShown() == 0f && !Get<bool>(nNight, "_fading"), 5, "Manual with no saved value did not fade to OFF (0)"));
            s.Add(TvDo(() => nSlider.value = 0.4f));
            s.Add(TvUntil(() => Mathf.Abs(NShown() - 0.4f) < 1e-4f && !Get<bool>(nNight, "_fading"), 5, "slider 0.4 not shown"));
            s.Add(TvDo(() =>
            {
                TvExpect(Mathf.Approximately(PdFloat(KH_NightMode.SaveKey), 0.4f), "manual value 0.4 saved");
                nAuto.SetProgramVariable("nightDarkness", 0.3f);   // the shared value moves (0.8 -> 0.3 at the next tick)
            }));
            s.Add(TvUntil(() => Mathf.Approximately(Get<float>(nNight, "autoDarkness"), 0.3f), 3, "the shared value did not move to 0.3"));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                TvExpect(Mathf.Abs(NShown() - 0.4f) < 1e-4f && Mathf.Abs(NApplied() - 0.4f) < 1e-4f, "Manual keeps 0.4 while the shared value is 0.3 (shown " + NShown().ToString("F3") + ")");
                TvExpect(Mathf.Approximately(PdFloat(KH_NightMode.SaveKey), 0.4f), "saved manual value untouched by the shared value");
                TvExpect(Get<int>(nAuto, "_epochMs") == nEpoch, "Manual did not touch the shared start");
                nAuto.SetProgramVariable("nightDarkness", KH_AutoNight.DefaultNightDarkness);
                TvNote("Manual 0.4 held while the shared value went 0.8 -> 0.3; saved 0.4; epoch unchanged");
            }));
            s.Add(TvUntil(() => Mathf.Approximately(Get<float>(nNight, "autoDarkness"), KH_AutoNight.DefaultNightDarkness), 3, "the shared value did not return to the night value"));

            // ---- N3 ----
            s = TvCase("N3-slider-flips-manual", false, 0);
            s.Add(TvDo(() => nSettings.SendCustomEvent("_NightAuto")));
            s.Add(TvDo(() => TvExpect(Get<bool>(nNight, "_auto") && PdInt(KH_NightMode.ModeKey) == KH_NightMode.ModeAuto, "[Auto] switched to Auto and saved the mode")));
            s.Add(TvUntil(() => Mathf.Abs(NShown() - KH_AutoNight.DefaultNightDarkness) < 1e-4f && !Get<bool>(nNight, "_fading"), 5, "Auto did not reach the shared value"));
            s.Add(TvDo(() =>
            {
                TvExpect(Mathf.Approximately(PdFloat(KH_NightMode.SaveKey), 0.4f), "Auto never overwrites the saved manual value (still 0.4)");
                nSlider.value = 0.55f;   // the slider showed the shared value; a touch is a manual choice
            }));
            s.Add(TvDo(() =>
            {
                TvExpect(!Get<bool>(nNight, "_auto"), "touching the slider in Auto switched to Manual");
                TvExpect(PdInt(KH_NightMode.ModeKey) == KH_NightMode.ModeManual && Mathf.Approximately(PdFloat(KH_NightMode.SaveKey), 0.55f), "mode Manual and value 0.55 saved");
                TvExpect(Get<int>(nAuto, "_epochMs") == nEpoch, "the shared start did not change");
            }));
            s.Add(TvUntil(() => Mathf.Abs(NShown() - 0.55f) < 1e-4f && !Get<bool>(nNight, "_fading"), 5, "slider value 0.55 not shown"));
            s.Add(TvDo(() => TvNote("Auto 0.8 -> slider 0.55 -> Manual 0.55 (mode and value saved; saved 0.4 was kept while in Auto)")));

            // ---- N4 ----
            s = TvCase("N4-manual-to-auto-fade", false, 0);
            s.Add(TvDo(() => nSettings.SendCustomEvent("_NightAuto")));
            s.Add(TvWait(1.2));
            s.Add(TvDo(() =>
            {
                float v = NShown();
                TvExpect(Get<bool>(nNight, "_fading") && v > 0.55f + 1e-3f && v < KH_AutoNight.DefaultNightDarkness - 1e-3f, "1.2 s after [Auto] the shown value is between Manual 0.55 and the shared 0.8 (" + v.ToString("F3") + ")");
                TvNote("mid-fade " + v.ToString("F3"));
            }));
            s.Add(TvUntil(() => Mathf.Abs(NShown() - KH_AutoNight.DefaultNightDarkness) < 1e-4f && !Get<bool>(nNight, "_fading"), 4, "Manual -> Auto did not land on the shared value"));
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(nAuto, "_epochMs") == nEpoch, "returning to Auto did not reset the shared start");
                TvExpect(Mathf.Approximately(PdFloat(KH_NightMode.SaveKey), 0.55f), "manual value 0.55 kept");
            }));

            // ---- N5 (simulated) ----
            s = TvCase("N5-late-join-mock", true, 0);
            s.Add(TvDo(() => { nAuto.SetProgramVariable("_epochMs", 0); TvNote("simulated: _epochMs = 0 (a late joiner before the synced value arrives)"); }));
            s.Add(TvUntil(() => Get<float>(nNight, "autoDarkness") == 0f, 3, "autoDarkness did not drop to 0 with no start time"));
            s.Add(TvUntil(() => NShown() == 0f && !Get<bool>(nNight, "_fading"), 5, "shown did not reach day (0) while the start is unknown"));
            s.Add(TvDo(() => { nAuto.SetProgramVariable("_epochMs", nEpoch); TvNote("simulated: _epochMs = " + nEpoch + " (the value arrives; OnDeserialization and the 1 s tick run the same Recompute)"); }));
            s.Add(TvUntil(() => Mathf.Approximately(Get<float>(nNight, "autoDarkness"), KH_AutoNight.DefaultNightDarkness), 3, "autoDarkness did not jump to the current shared value"));
            s.Add(TvWait(1));
            s.Add(TvDo(() =>
            {
                float v = NShown();
                TvExpect(Get<bool>(nNight, "_fading") && v > 1e-3f && v < KH_AutoNight.DefaultNightDarkness - 1e-3f, "the arrival fades in (shown " + v.ToString("F3") + " mid-way)");
            }));
            s.Add(TvUntil(() => Mathf.Abs(NShown() - KH_AutoNight.DefaultNightDarkness) < 1e-4f && !Get<bool>(nNight, "_fading"), 4, "did not join the shared value"));

            // ---- N6 (simulated): the start gate (round 2 of the review: a new Owner that has not received must not reset the clock) ----
            s = TvCase("N6-owner-handover-mock", true, 0);
            bool n6First = false;
            s.Add(TvDo(() =>
            {
                TvExpect(Networking.IsOwner(nAuto.gameObject), "the local player owns AutoNight (ClientSim)");
                nAuto.SendCustomEvent("_TryStart");
                TvExpect(Get<int>(nAuto, "_epochMs") == nEpoch, "a set start is never rewritten (_TryStart with " + nEpoch + ")");
                // Reproduce the order of the bug: a late joiner (not first, not received, network not settled) becomes the Owner
                // before its first receive. Its join-time _TryStart is still pending and the handover delay follows.
                n6First = Get<bool>(nAuto, "_firstPlayer");
                nAuto.SetProgramVariable("_firstPlayer", false); nAuto.SetProgramVariable("_received", false); nAuto.SetProgramVariable("_debugUnsettled", true);
                nAuto.SetProgramVariable("_blockedSince", -1f);
                nAuto.SetProgramVariable("_epochMs", 0);
                TvNote("simulated: late joiner as new Owner before its first receive (_firstPlayer=false, _received=false, _debugUnsettled=true, _epochMs=0)");
                nAuto.SendCustomEvent("_TryStart");   // the pending join-time check fires
                TvExpect(Get<int>(nAuto, "_epochMs") == 0 && Get<bool>(nAuto, "_retryPending"), "join-time _TryStart did not write; one retry pending");
                nAuto.SendCustomEvent("_TryStart");   // the handover delay elapses
                TvExpect(Get<int>(nAuto, "_epochMs") == 0, "handover _TryStart did not write");
            }));
            s.Add(TvWait(KH_AutoNight.RetrySeconds * 2 + 1));   // two retries pass through the same gate
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(nAuto, "_epochMs") == 0 && Get<bool>(nAuto, "_retryPending"), "retries did not write while the state is unknown (still waiting, fallback " + KH_AutoNight.FallbackSeconds + " s)");
                // The old Owner's value arrives
                nAuto.SetProgramVariable("_epochMs", nEpoch); nAuto.SetProgramVariable("_received", true);
                nAuto.SendCustomEvent("_onDeserialization");
                TvNote("simulated: receive of " + nEpoch + " (heap write + _onDeserialization)");
            }));
            s.Add(TvWait(KH_AutoNight.RetrySeconds + 1));   // the pending retry runs after the receive
            s.Add(TvDo(() =>
            {
                TvExpect(Get<int>(nAuto, "_epochMs") == nEpoch && !Get<bool>(nAuto, "_retryPending"), "the received start was kept (retry ended without writing)");
                TvExpect(Mathf.Approximately(Get<float>(nNight, "autoDarkness"), KH_AutoNight.DefaultNightDarkness), "the darkness follows the received start");
                // Genuinely first player: writes once, even if not settled
                nAuto.SetProgramVariable("_received", false); nAuto.SetProgramVariable("_firstPlayer", true); nAuto.SetProgramVariable("_epochMs", 0);
                nAuto.SendCustomEvent("_TryStart");
                int first = Get<int>(nAuto, "_epochMs");
                TvExpect(first != 0, "the first player (alone at its join) starts the clock");
                nAuto.SendCustomEvent("_TryStart");
                TvExpect(Get<int>(nAuto, "_epochMs") == first, "and only once");
                // Settled path: not first, not received, network settled -> a real 0 is started
                nAuto.SetProgramVariable("_firstPlayer", false); nAuto.SetProgramVariable("_debugUnsettled", false); nAuto.SetProgramVariable("_epochMs", 0);
                nAuto.SendCustomEvent("_TryStart");
                TvExpect(Get<int>(nAuto, "_epochMs") != 0, "a settled Owner with a real 0 starts the clock");
                TvNote("first player wrote " + first + " once; settled Owner wrote " + Get<int>(nAuto, "_epochMs") + ". Restored " + nEpoch);
                nAuto.SetProgramVariable("_epochMs", nEpoch); nAuto.SetProgramVariable("_firstPlayer", n6First); nAuto.SetProgramVariable("_received", false);
            }));
            s.Add(TvUntil(() => Mathf.Abs(NShown() - KH_AutoNight.DefaultNightDarkness) < 1e-4f && !Get<bool>(nNight, "_fading"), 6, "did not return to the shared value after the restore"));

            // ---- N7 ----
            s = TvCase("N7-live-wins", true, 0);
            s.Add(TvDo(() => { nNight.SetProgramVariable("liveDarkness", 0.95f); TvNote("simulated: liveDarkness = 0.95 (KH_LiveNight writes only on change, so it does not overwrite while no live runs)"); }));
            s.Add(TvUntil(() => Mathf.Abs(NApplied() - 0.95f) < 1e-4f, 2, "a stronger live did not win"));
            s.Add(TvDo(() => nNight.SetProgramVariable("liveDarkness", 0.3f)));
            s.Add(TvUntil(() => Mathf.Abs(NApplied() - KH_AutoNight.DefaultNightDarkness) < 1e-4f, 2, "a weaker live did not yield to Auto"));
            s.Add(TvDo(() => nNight.SetProgramVariable("liveDarkness", 0f)));
            s.Add(TvWait(.5));
            s.Add(TvDo(() =>
            {
                TvExpect(Mathf.Abs(NApplied() - KH_AutoNight.DefaultNightDarkness) < 1e-4f, "after the live the darkness returns to Auto (" + NApplied().ToString("F3") + "), not to 0");
                nAuto.SetProgramVariable("daySeconds", KH_AutoNight.DefaultDaySeconds); nAuto.SetProgramVariable("duskSeconds", KH_AutoNight.DefaultDuskSeconds);
                nAuto.SetProgramVariable("nightDarkness", KH_AutoNight.DefaultNightDarkness);
                TvNote("live 0.95 > auto 0.8 -> 0.95; live 0.3 -> 0.8; live 0 -> 0.8. Runtime timeline restored to the defaults");
            }));
        }

        static float NShown() { return Get<float>(nNight, "_shown"); }
        static float NApplied() { return Get<float>(nNight, "_applied"); }
        static float NElapsed() { int e = Get<int>(nAuto, "_epochMs"); return e == 0 ? -1f : (Networking.GetServerTimeInMilliseconds() - e) * 0.001f; }
        static int PdInt(string key) { int v; return VRC.SDK3.Persistence.PlayerData.TryGetInt(Networking.LocalPlayer, key, out v) ? v : -1; }
        static float PdFloat(string key) { float v; return VRC.SDK3.Persistence.PlayerData.TryGetFloat(Networking.LocalPlayer, key, out v) ? v : -1f; }
    }
}
