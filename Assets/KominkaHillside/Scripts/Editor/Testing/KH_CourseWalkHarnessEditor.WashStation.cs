using System;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-ENV-007 主洗い場の道具の戻り — Course/100 (Wash.cs) に足すケース。
    ///
    /// 枠・手順の部品は Wash.cs のもの (WsCase / WsDo / WsWait / WsUntil / WsExpect / WsNote / WsRef)。ケースの一覧への接続は
    /// Wash.cs の MakeWashCases の末尾 (WsWrite の前) で MakeWashStationCases() を呼ぶ。ここでは一覧へ自分でつながない。
    ///
    /// 道具 (KH_WashTool) の returnSeconds を短縮 (3 s) し、道具を定位置から 2 m 離して置く (Owner = ローカルが位置を書き、
    /// FlagDiscontinuity)。持っている状態は debugHeldOverride (1 = 持っている扱い) で作る (ClientSim の Pickup を掴む口は使わない。
    /// KH_FrisbeeCatch の debugHeldOverride と同じ扱い)。放水は KH_WashToolState.spraying を Owner として書いて _OnStateChanged で出す。
    /// 最後に returnSeconds を 20 へ、override を 0 へ戻し、道具を定位置へ戻す (シーンは保存しない)。
    ///
    /// 対象外: 実際の Pickup (掴む・離す・Owner の移譲)、他の人が持っている道具、途中参加者の見え方、VRCObjectSync の補間。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        static readonly string[] WtNames = { "KH_WashSponge_0", "KH_WashSponge_1", "KH_WashShower_0", "KH_WashShower_1", "KH_WashTowel_0", "KH_WashTowel_1" };
        const string WtStationName = "KH_WashStation";
        const float WtShort = 3f, WtProductionSeconds = 20f, WtAwayMeters = 2f, WtAtHome = 0.06f;
        static Transform wtStation;
        static Transform[] wtT, wtHome;
        static UdonBehaviour[] wtU;

        static void MakeWashStationCases()
        {
            // ---- W-tool-return: 2 m から、短縮した秒数の後に定位置へ戻る (早すぎない)。スポンジ・シャワー・タオル ----
            var s = WsCase("W-tool-return", 40);
            float movedAt = 0f; int[] which = { 0, 2, 4, 5 }; int[] count0 = new int[WtNames.Length];
            s.Add(WsDo(() => { WtBind(); WtReset(WtShort); WtHomeAll(); }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                foreach (int i in which) { WsExpect(Networking.IsOwner(wtT[i].gameObject), WtNames[i] + " owned by the local player"); count0[i] = Get<int>(wtU[i], "_returnCount"); WtAway(i); }
                movedAt = Time.time;
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() => { foreach (int i in which) WsExpect(WtDist(i) > WtAwayMeters - 0.1f, WtNames[i] + " did not return at once (" + WtDist(i).ToString("F2") + " m)"); }));
            s.Add(WsUntil(() => which.All(i => WtDist(i) < WtAtHome), WtShort + 3, () => "not every tool returned: " + string.Join(", ", which.Select(i => WtNames[i] + " " + WtDist(i).ToString("F2") + " m"))));
            s.Add(WsDo(() =>
            {
                float took = Time.time - movedAt;
                WsExpect(took >= WtShort - 0.3f, "returned after the set seconds, not early (" + took.ToString("F2") + " s, returnSeconds " + WtShort + ")");
                foreach (int i in which)
                {
                    WsExpect(Quaternion.Angle(wtT[i].rotation, wtHome[i].rotation) < 0.5f, WtNames[i] + " home rotation");
                    WsExpect(Get<int>(wtU[i], "_returnCount") == count0[i] + 1, WtNames[i] + " returned once (_returnCount " + count0[i] + " -> " + Get<int>(wtU[i], "_returnCount") + ")");
                }
                WsNote("sponge / shower / towel moved " + WtAwayMeters + " m from home: back at home after " + took.ToString("F2") + " s (returnSeconds " + WtShort + "), once each, home rotation");
            }));

            // ---- W-tool-held: 持っている間は戻らない。離した時点から数える ----
            s = WsCase("W-tool-held", 40);
            float releasedAt = 0f;
            s.Add(WsDo(() => { WtBind(); WtReset(WtShort); WtHomeAll(); }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() => { wtU[1].SetProgramVariable("debugHeldOverride", 1); WtAway(1); }));
            s.Add(WsWait(WtShort + 2));
            s.Add(WsDo(() =>
            {
                WsExpect(WtDist(1) > WtAwayMeters - 0.1f, "held: " + WtNames[1] + " stayed away for " + (WtShort + 2) + " s (" + WtDist(1).ToString("F2") + " m)");
                wtU[1].SetProgramVariable("debugHeldOverride", 0);
                releasedAt = Time.time;
            }));
            s.Add(WsWait(WtShort - 1));
            s.Add(WsDo(() => WsExpect(WtDist(1) > WtAwayMeters - 0.1f, "counted from the release, not from the move: still away " + (WtShort - 1) + " s after the release")));
            s.Add(WsUntil(() => WtDist(1) < WtAtHome, 4, () => WtNames[1] + " did not return after the release (" + WtDist(1).ToString("F2") + " m)"));
            s.Add(WsDo(() => WsNote("held (override 1) for " + (WtShort + 2) + " s: stayed " + WtAwayMeters + " m away; released: back at home " + (Time.time - releasedAt).ToString("F2") + " s after the release")));

            // ---- W-tool-spray: 戻る時に放水が止まる (戻りの経路そのもの = _DebugReturnHome。持っている扱いのまま) ----
            s = WsCase("W-tool-spray", 30);
            s.Add(WsDo(() =>
            {
                WtBind(); WtReset(WtShort); WtHomeAll();
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                var u = wtU[3];
                u.SetProgramVariable("debugHeldOverride", 1);
                WtAway(3);
                var state = WsRef(u, "state") as UdonBehaviour;
                WsExpect(state != null && Networking.IsOwner(state.gameObject), "the shower's State is owned by the local player");
                state.SetProgramVariable("spraying", true);
                u.SendCustomEvent("_OnStateChanged");
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                var u = wtU[3];
                var state = WsRef(u, "state") as UdonBehaviour;
                var ps = WsRef(u, "spray") as ParticleSystem; var au = WsRef(u, "sprayAudio") as AudioSource;
                WsExpect(Get<bool>(state, "spraying"), "control: spraying stays on while held (override 1)");
                WsExpect(ps != null && ps.isEmitting, "control: the spray particles emit");
                WsExpect(au == null || au.isPlaying, "control: the spray loop plays");
            }));
            s.Add(WsWait(WtShort + 1));
            s.Add(WsDo(() =>
            {
                WsExpect(WtDist(3) > WtAwayMeters - 0.1f, "held: the shower did not return while spraying");
                wtU[3].SendCustomEvent("_DebugReturnHome");
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                var u = wtU[3];
                var state = WsRef(u, "state") as UdonBehaviour;
                var ps = WsRef(u, "spray") as ParticleSystem; var au = WsRef(u, "sprayAudio") as AudioSource;
                WsExpect(WtDist(3) < WtAtHome, "the return put the shower at home (" + WtDist(3).ToString("F2") + " m)");
                WsExpect(!Get<bool>(state, "spraying"), "the return wrote spraying = false");
                WsExpect(ps == null || !ps.isEmitting, "the spray particles stopped emitting");
                WsExpect(au == null || !au.isPlaying, "the spray loop stopped");
                u.SetProgramVariable("debugHeldOverride", 0);
                WsNote("shower spraying (held by override) moved " + WtAwayMeters + " m; the return path stopped the spray (state false, particles and loop off) and put it home. "
                    + "The natural release path is covered by W-tool-held; there the Owner's orphaned-spray clean-up (existing) also stops a spray before the return");
            }));
            s.Add(WsDo(() => { WtReset(WtProductionSeconds); WtHomeAll(); }));
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
                if (wtT[i] == null) throw new InvalidOperationException(WtStationName + "/" + WtNames[i] + " missing");
                wtU[i] = wtT[i].GetComponent<UdonBehaviour>();
                if (wtU[i] == null) throw new InvalidOperationException(WtNames[i] + " has no UdonBehaviour (KH_WashTool)");
                wtHome[i] = WsRef(wtU[i], "home") as Transform;
                if (wtHome[i] == null) throw new InvalidOperationException(WtNames[i] + ".home is not wired (IMP-ENV-007: run Wash/11)");
                if (!wtHome[i].IsChildOf(wtStation) || wtHome[i].IsChildOf(wtT[i])) throw new InvalidOperationException(WtNames[i] + ".home is not under the station (or is under the tool)");
            }
        }

        static void WtReset(float seconds)
        {
            for (int i = 0; i < WtNames.Length; i++)
            {
                wtU[i].SetProgramVariable("debugHeldOverride", 0);
                wtU[i].SetProgramVariable("returnSeconds", seconds);
            }
        }

        static void WtHomeAll() { for (int i = 0; i < WtNames.Length; i++) wtU[i].SendCustomEvent("_DebugReturnHome"); }

        /// <summary>2 m to the station's west (local -X): away from the duckboard, the tub side is behind the shelf line.</summary>
        static void WtAway(int i)
        {
            var t = wtT[i];
            t.position = wtHome[i].position + wtStation.rotation * new Vector3(-WtAwayMeters, 0f, 0f);
            var sync = t.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
        }

        static float WtDist(int i) { return (wtT[i].position - wtHome[i].position).magnitude; }
    }
}
