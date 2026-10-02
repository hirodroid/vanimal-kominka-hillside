using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-031 首輪の「外す」Interact (KH_LeashCollar._SetReleasable) の有効／無効。travel の台本 (TravelTick) で回す。
    /// 犬 0 を手元メニューで繋ぎ、外し方を変えて 3 回確かめる。見るもの: 首輪の UdonBehaviour.DisableInteractive と首輪の Collider.enabled。
    ///   K0-none-walked      誰も散歩していない: 6 個とも Interact なし・Collider 無効
    ///   K1-collar-release   _WalkLeft0 で繋ぐ → walkers[0] が自分になった時点で犬 0 だけ有効 (Owner の ReqAttach で即時)。首輪の Interact で外す → 外れた時点で無効
    ///   K2-menu-release     繋ぐ → メニューの [はずす] (_WalkOff0) で外す → 無効 (首輪を通らない経路)
    ///   K3-heap-write       模擬: walkers[0] をヒープへ直接書く。自分 → 有効、-1 → 無効 (Release を通らないので 0.5 s の保守の照合だけで消えることを見る)、
    ///                       もう一度自分 → 有効、存在しない id 9999 → Owner の保守が解除して無効。イベントを通らない変化の代わり (遅着・取り逃し)
    /// 1 クライアントなので「他の人が散歩中の犬」「Owner の交代」「退室」「遅れて入った人」は見ない (2 人の Play / 実機)。
    /// Interact の表示・光線 (撫で・洗う道具・つかむ操作が取られないか) は ClientSim では見ない (PCVR)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/83b - Walk Harness Collar Interact (IMP-DOG-031)")]
        public static void CollarInteract()
        {
            BeginTravel(false);
            report.collarOnly = true;
            report.deadline = EditorApplication.timeSinceStartup + 300;
            report.limitations = "Single local ClientSim: the local player is the walker and the Manager Owner. Reads UdonBehaviour.DisableInteractive and the collar Collider.enabled; "
                + "no real interact ray, outline or hand contact. Other walkers, Owner handover, leaving and late join are not exercised (K3 only simulates a state change without an event by heap writes).";
            Save();
        }

        static UdonBehaviour[] kCollars;

        /// <summary>83b の End の reason (TravelTick が使う)。</summary>
        static string CollarReason()
        {
            return "Local ClientSim collar Interact cases passed (DisableInteractive + Collider.enabled of all six collars): not walked (K0), own walk on attach (K1/K2), "
                + "release by the collar Interact (K1), release by the menu (K2), walker changed without an event via heap writes -> me / -1 / me / missing holder (K3, simulated). "
                + "Not covered: a dog walked by another player, Owner handover, leaving and late join (need 2 players), the real interact ray / outline and hand petting or washing (PCVR).";
        }

        static void MakeCollarCases()
        {
            kCollars = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>().Where(u => u.programSource != null && u.programSource.name == "KH_LeashCollar")
                .OrderBy(u => Get<int>(u, "dogIndex")).ToArray();
            if (kCollars.Length != 6) throw new InvalidOperationException("six KH_LeashCollar behaviours required (" + kCollars.Length + ")");
            for (int i = 0; i < 6; i++)
            {
                if (Get<int>(kCollars[i], "dogIndex") != i) throw new InvalidOperationException("collar dogIndex order mismatch at " + i);
                if (kCollars[i].GetComponent<Collider>() == null) throw new InvalidOperationException("collar " + i + " has no Collider");
            }

            // ---- K0 ----
            var s = TvCase("K0-none-walked", false, 1);
            s.Add(TvDo(() => { for (int i = 0; i < 6; i++) if (Get<int[]>(manager, "walkers")[i] >= 0) pouch.SendCustomEvent("_WalkOff" + i); }));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers").All(w => w < 0), 5, "detach before the check"));
            s.Add(TvWait(1));   // past one 0.5 s maintenance pass, so a stale write would show
            s.Add(TvDo(() => { KExpectOnly(-1, "nobody walks"); TvNote("all six collars: DisableInteractive true, Collider disabled (" + KState() + ")"); }));

            // ---- K1 ----
            s = TvCase("K1-collar-release", false, 1);
            KAttach(s, "K1");
            s.Add(TvDo(() =>
            {
                kCollars[0].Interact();   // the collar's own Interact (UdonBehaviour.Interact; no ray)
            }));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers")[0] < 0, 5, "K1: collar Interact did not release dog 0"));
            s.Add(TvDo(() => { KExpectOnly(-1, "K1: the moment walkers[0] is free after the collar Interact (Owner Release)"); TvNote("collar Interact released dog 0; Interact off in the same step (" + KState() + ")"); }));

            // ---- K2 ----
            s = TvCase("K2-menu-release", false, 1);
            KAttach(s, "K2");
            s.Add(TvDo(() => pouch.SendCustomEvent("_WalkOff0")));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers")[0] < 0, 5, "K2: menu _WalkOff0 did not release dog 0"));
            s.Add(TvDo(() => { KExpectOnly(-1, "K2: the moment walkers[0] is free after the menu release"); TvNote("menu [はずす] released dog 0; Interact off in the same step (" + KState() + ")"); }));

            // ---- K3 (simulated) ----
            s = TvCase("K3-heap-write", true, 1);
            s.Add(TvDo(() =>
            {
                KExpectOnly(-1, "K3 start");
                var w = (int[])Get<int[]>(manager, "walkers").Clone(); w[0] = TvMe(); manager.SetProgramVariable("walkers", w);
                TvNote("simulated: heap write walkers[0] = me (no ReqAttach / OnDeserialization)");
            }));
            s.Add(TvUntil(() => KOn(0), 1.5, "K3: Interact did not turn on within 1.5 s of walkers[0] = me (0.5 s maintenance)"));
            s.Add(TvDo(() =>
            {
                KExpectOnly(0, "K3 after the write");
                var w = (int[])Get<int[]>(manager, "walkers").Clone(); w[0] = -1; manager.SetProgramVariable("walkers", w);
                TvNote("on via the maintenance pass (" + KState() + "); simulated: heap write walkers[0] = -1 (nothing calls Release for a free dog, so only the maintenance mask pass can turn it off)");
            }));
            // Both outputs (DisableInteractive and Collider) must go off: KOn is true while either is on.
            s.Add(TvUntil(() => { var c = kCollars[0]; return c.DisableInteractive && !c.GetComponent<Collider>().enabled; }, 1.5, "K3: Interact / Collider did not turn off within 1.5 s of walkers[0] = -1 (maintenance pass alone)"));
            s.Add(TvDo(() =>
            {
                KExpectOnly(-1, "K3 after walkers[0] = -1");
                TvNote("off via the maintenance mask pass alone (" + KState() + ")");
                var w = (int[])Get<int[]>(manager, "walkers").Clone(); w[0] = TvMe(); manager.SetProgramVariable("walkers", w);
                TvNote("simulated: heap write walkers[0] = me again (for the invalid-holder step)");
            }));
            s.Add(TvUntil(() => KOn(0), 1.5, "K3: Interact did not turn on again within 1.5 s of walkers[0] = me"));
            s.Add(TvDo(() =>
            {
                KExpectOnly(0, "K3 before the invalid holder");
                var w = (int[])Get<int[]>(manager, "walkers").Clone(); w[0] = 9999; manager.SetProgramVariable("walkers", w);
                TvNote("simulated: heap write walkers[0] = 9999 (no such player)");
            }));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers")[0] < 0, 2, "K3: the Owner's maintenance did not release the missing holder"));
            s.Add(TvDo(() => { KExpectOnly(-1, "K3 end"); TvNote("invalid holder: the Owner's maintenance released dog 0 and the Interact is off (" + KState() + ")"); }));
        }

        /// <summary>犬 0 を左手で繋ぎ (手元メニュー)、walkers[0] が自分になった最初の観測で犬 0 だけ有効を確かめる。</summary>
        static void KAttach(System.Collections.Generic.List<Func<double, bool>> s, string tag)
        {
            s.Add(TvDo(() =>
            {
                KExpectOnly(-1, tag + ": before the attach");
                holder = nodes[0]; var heading = nodes[1] - nodes[0]; heading.y = 0;
                Networking.LocalPlayer.TeleportTo(holder, Quaternion.LookRotation(heading));
            }));
            s.Add(TvWait(.5));
            s.Add(TvDo(() => { PlaceDog(0, Networking.LocalPlayer.GetRotation()); pouch.SendCustomEvent("_WalkLeft0"); }));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers")[0] == TvMe(), 5, tag + ": menu attach of dog 0 rejected"));
            s.Add(TvDo(() => { KExpectOnly(0, tag + ": the moment walkers[0] is me (Owner ReqAttach)"); TvNote("attached dog 0 by _WalkLeft0; Interact on for dog 0 only (" + KState() + ")"); }));
            s.Add(TvWait(1));   // past a maintenance pass: still on
            s.Add(TvDo(() => KExpectOnly(0, tag + ": 1 s after the attach")));
        }

        static bool KOn(int dog)
        {
            var c = kCollars[dog];
            return !c.DisableInteractive && c.GetComponent<Collider>().enabled;
        }

        /// <summary>dog だけ有効、他は無効 (dog = -1 なら全部無効)。DisableInteractive と Collider は同じ向きでなければならない。</summary>
        static void KExpectOnly(int dog, string what)
        {
            for (int i = 0; i < 6; i++)
            {
                var c = kCollars[i]; bool di = c.DisableInteractive, col = c.GetComponent<Collider>().enabled;
                bool want = i == dog;
                TvExpect(di == !want && col == want, what + ": collar " + i + " DisableInteractive " + di + " / Collider " + col + " (want " + (want ? "on" : "off") + ")");
            }
        }

        static string KState()
        {
            return string.Join(" ", Enumerable.Range(0, 6).Select(i => i + ":" + (kCollars[i].DisableInteractive ? "D" : "I") + (kCollars[i].GetComponent<Collider>().enabled ? "c" : "-")));
        }
    }
}
