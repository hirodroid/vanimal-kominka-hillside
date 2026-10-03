using System;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-ENV-007 主洗い場の道具 — Course/100 (Wash.cs) に足すケース。
    ///
    /// 枠・手順の部品は Wash.cs のもの (WsCase / WsDo / WsWait / WsUntil / WsExpect / WsNote / WsRef)。ケースの一覧への接続は
    /// Wash.cs の MakeWashCases の末尾 (WsWrite の前) で MakeWashStationCases() を呼ぶ。ここでは一覧へ自分でつながない。
    ///
    /// 2026-09-30 ユーザー決定: 道具は自動では戻らず置いた所に残り、リセットボタン (KH_WashStationReset) で全部が定位置へ戻る。
    /// W-tool-stay: 本番の returnSeconds (0) のまま 2 m 離して WtStillSeconds 置いても動かない。
    /// W-tool-reset: ボタンの処理 (_ResetTools) を直接呼ぶ → 持っていない道具が全部定位置へ、持っている道具は残る、放水が止まる。
    /// 道具は Owner = ローカルとして位置を書く (FlagDiscontinuity)。持っている状態は debugHeldOverride (1 = 持っている扱い、
    /// -1 = 持っていない扱い) の疑似 (KH_FrisbeeCatch と同じ扱い)。放水は KH_WashToolState.spraying を Owner として書いて出す。
    /// 最後に override を 0 へ戻し、道具を定位置へ戻す (シーンは保存しない)。
    ///
    /// 対象外: 実際の Pickup・Interact の光線、他の人が Owner / 持ち主の道具 (SetOwner の往復)、同時の Pickup、途中参加者の見え方。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        // IMP-DOG-039: + the shampoo bottle (tool 4, one per station). Index 6 = the bottle; the cases' fixed indices (0..5) are unchanged
        static readonly string[] WtNames = { "KH_WashSponge_0", "KH_WashSponge_1", "KH_WashShower_0", "KH_WashShower_1", "KH_WashTowel_0", "KH_WashTowel_1", "KH_WashBottle_0" };
        const string WtStationName = "KH_WashStation", WtResetName = "ResetButton";
        const float WtAwayMeters = 2f, WtAtHome = 0.06f;
        /// <summary>W-tool-stay の置いておく秒数 (短縮してよい。既定 30 s)。</summary>
        public static float WtStillSeconds = 30f;
        static Transform wtStation;
        static Transform[] wtT, wtHome;
        static UdonBehaviour[] wtU;
        static UdonBehaviour wtReset;

        static void MakeWashStationCases()
        {
            // ---- W-tool-stay: 自動では戻らない ----
            var s = WsCase("W-tool-stay", WtStillSeconds + 20);
            int[] which = { 0, 2, 4, 5 }; int[] count0 = new int[WtNames.Length];
            s.Add(WsDo(() =>
            {
                WtBind();
                for (int i = 0; i < WtNames.Length; i++)
                    WsExpect(Get<float>(wtU[i], "returnSeconds") <= 0f, WtNames[i] + " returnSeconds " + Get<float>(wtU[i], "returnSeconds") + " <= 0 (the production value: no automatic return)");
                WtOverride(0); WtHomeAll();
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                foreach (int i in which) { WsExpect(Networking.IsOwner(wtT[i].gameObject), WtNames[i] + " owned by the local player"); count0[i] = Get<int>(wtU[i], "_returnCount"); WtAway(i); }
            }));
            s.Add(WsWait(WtStillSeconds));
            s.Add(WsDo(() =>
            {
                foreach (int i in which)
                {
                    WsExpect(WtDist(i) > WtAwayMeters - 0.05f, WtNames[i] + " stayed where it was put (" + WtDist(i).ToString("F2") + " m from home)");
                    WsExpect(Get<int>(wtU[i], "_returnCount") == count0[i], WtNames[i] + " never returned (_returnCount " + count0[i] + " -> " + Get<int>(wtU[i], "_returnCount") + ")");
                }
                WsNote("sponge / shower / towel x2 put " + WtAwayMeters + " m from home with returnSeconds 0: still there after " + WtStillSeconds + " s");
            }));

            // ---- W-tool-reset: ボタンで全部戻る。持っている道具は残る。放水が止まる ----
            s = WsCase("W-tool-reset", 30);
            s.Add(WsDo(() => { WtBind(); WtOverride(0); WtHomeAll(); }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                for (int i = 0; i < WtNames.Length; i++) WtAway(i);
                wtU[1].SetProgramVariable("debugHeldOverride", 1);   // someone holds sponge 1
                var u = wtU[3];                                        // shower 1 sprays while "held"
                u.SetProgramVariable("debugHeldOverride", 1);
                var state = WsRef(u, "state") as UdonBehaviour;
                WsExpect(state != null && Networking.IsOwner(state.gameObject), "the shower's State is owned by the local player");
                state.SetProgramVariable("spraying", true);
                u.SendCustomEvent("_OnStateChanged");
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                var u = wtU[3];
                var ps = WsRef(u, "spray") as ParticleSystem;
                WsExpect(Get<bool>(WsRef(u, "state") as UdonBehaviour, "spraying") && ps != null && ps.isEmitting, "control: the shower sprays before the reset");
                // Let go of the shower and press in the same editor step (no Update in between): the reset's own path stops the
                // spray, not the Owner's orphaned-spray clean-up that would run on the next Update.
                u.SetProgramVariable("debugHeldOverride", -1);
                wtReset.SendCustomEvent("_ResetTools");
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                for (int i = 0; i < WtNames.Length; i++)
                {
                    if (i == 1) WsExpect(WtDist(i) > WtAwayMeters - 0.05f, "held " + WtNames[i] + " was not taken out of the hand (" + WtDist(i).ToString("F2") + " m)");
                    else WsExpect(WtDist(i) < WtAtHome && Quaternion.Angle(wtT[i].rotation, wtHome[i].rotation) < 0.5f, WtNames[i] + " back at home (" + WtDist(i).ToString("F2") + " m)");
                }
                WsExpect(Get<int>(wtReset, "_lastSkippedHeld") == 1, "the reset skipped exactly one held tool (" + Get<int>(wtReset, "_lastSkippedHeld") + ")");
                var u = wtU[3];
                var ps = WsRef(u, "spray") as ParticleSystem; var au = WsRef(u, "sprayAudio") as AudioSource;
                WsExpect(!Get<bool>(WsRef(u, "state") as UdonBehaviour, "spraying"), "the reset wrote spraying = false");
                WsExpect(ps == null || !ps.isEmitting, "the spray particles stopped emitting");
                WsExpect(au == null || !au.isPlaying, "the spray loop stopped");
                WsNote("reset: " + (WtNames.Length - 1) + " tools not held (the bottle included) went home (home rotation), the held sponge stayed " + WtAwayMeters + " m away, the spraying shower stopped (state, particles, loop)");
            }));
            s.Add(WsDo(() => { WtOverride(0); WtHomeAll(); }));

            // ---- W-tool-toggle: シャワーは Use で ON/OFF (離しても出たまま)、連打の 2 回目は無視、手放すと OFF ----
            // The holder's own events (_onPickup / _onPickupUseDown / _onPickupUseUp / _onDrop) are sent directly (the treat harness's way).
            s = WsCase("W-tool-toggle", 20);
            UdonBehaviour tgState = null; ParticleSystem tgPs = null;
            Func<bool> on = () => Get<bool>(wtU[2], "_sprayOn");
            Func<string> tgWhat = () => "(_sprayOn " + on() + ", state.spraying " + Get<bool>(tgState, "spraying") + ", emitting " + (tgPs != null && tgPs.isEmitting) + ")";
            s.Add(WsDo(() =>
            {
                WtBind(); WtOverride(0); WtHomeAll();
                tgState = WsRef(wtU[2], "state") as UdonBehaviour; tgPs = WsRef(wtU[2], "spray") as ParticleSystem;
                WsExpect(tgState != null && Networking.IsOwner(tgState.gameObject), "the shower's State is owned by the local player");
                wtU[2].SendCustomEvent("_onPickup");
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(!on() && !Get<bool>(tgState, "spraying"), "picked up: off " + tgWhat());
                wtU[2].SendCustomEvent("_onPickupUseDown");
                wtU[2].SendCustomEvent("_onPickupUseDown");   // a double tap within toggleGuardSeconds: the second is ignored
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(on() && Get<bool>(tgState, "spraying") && tgPs != null && tgPs.isEmitting, "Use down: on, the double tap did not flip it back " + tgWhat());
                wtU[2].SendCustomEvent("_onPickupUseUp");
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                WsExpect(on() && Get<bool>(tgState, "spraying") && tgPs.isEmitting, "Use up: still on (the toggle ignores the release) " + tgWhat());
                wtU[2].SendCustomEvent("_onPickupUseDown");
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(!on() && !Get<bool>(tgState, "spraying") && !tgPs.isEmitting, "second Use down: off " + tgWhat());
                wtU[2].SendCustomEvent("_onPickupUseDown");
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(on() && Get<bool>(tgState, "spraying"), "third Use down: on again " + tgWhat());
                wtU[2].SendCustomEvent("_onDrop");
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(!on() && !Get<bool>(tgState, "spraying") && !tgPs.isEmitting, "dropped while on: off " + tgWhat());
                WsNote("shower toggle: pick up off -> Use on (double tap ignored) -> release stays on -> Use off -> Use on -> drop off");
                WtHomeAll();
            }));
        }

        static void WtBind()
        {
            var go = GameObject.Find(WtStationName);
            if (go == null || go.transform.parent != null) throw new InvalidOperationException(WtStationName + " not found at the scene root (run Wash/11)");
            wtStation = go.transform;
            wtT = new Transform[WtNames.Length]; wtHome = new Transform[WtNames.Length]; wtU = new UdonBehaviour[WtNames.Length];
            for (int i = 0; i < WtNames.Length; i++)
            {
                wtT[i] = wtStation.Find(WtNames[i]);
                if (wtT[i] == null) throw new InvalidOperationException(WtStationName + "/" + WtNames[i] + " missing (run Wash/11)");
                wtU[i] = wtT[i].GetComponent<UdonBehaviour>();
                if (wtU[i] == null) throw new InvalidOperationException(WtNames[i] + " has no UdonBehaviour (KH_WashTool)");
                wtHome[i] = WsRef(wtU[i], "home") as Transform;
                if (wtHome[i] == null) throw new InvalidOperationException(WtNames[i] + ".home is not wired (IMP-ENV-007: run Wash/11)");
                if (!wtHome[i].IsChildOf(wtStation) || wtHome[i].IsChildOf(wtT[i])) throw new InvalidOperationException(WtNames[i] + ".home is not under the station (or is under the tool)");
            }
            var rb = wtStation.Find(WtResetName);
            wtReset = rb != null ? rb.GetComponent<UdonBehaviour>() : null;
            if (wtReset == null) throw new InvalidOperationException(WtStationName + "/" + WtResetName + " has no UdonBehaviour (KH_WashStationReset: run Wash/10b and 11)");
        }

        static void WtOverride(int value) { for (int i = 0; i < WtNames.Length; i++) wtU[i].SetProgramVariable("debugHeldOverride", value); }

        static void WtHomeAll() { for (int i = 0; i < WtNames.Length; i++) wtU[i].SendCustomEvent("_DebugReturnHome"); }

        /// <summary>2 m to the station's west (local -X), away from the duckboard.</summary>
        static void WtAway(int i)
        {
            var t = wtT[i];
            t.position = wtHome[i].position + wtStation.rotation * new Vector3(-WtAwayMeters, 0f, 0f);
            var sync = t.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
        }

        static float WtDist(int i) { return (wtT[i].position - wtHome[i].position).magnitude; }
    }
}
