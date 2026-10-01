using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-ENV-008 道中の洗い場 (KH_WashStations/{River, Upper, Hillside}) — Course/100 (Wash.cs) に足すケース。
    ///
    /// 枠・手順の部品は Wash.cs (WsCase / WsDo / WsWait / WsExpect / WsNote / WsRef / WsArr)、主洗い場の道具の束ね方は WashStation.cs
    /// (WtBind / WtOverride / WtHomeAll / WtNames)、汚れの部品は WashDirt.cs (WdRestore / WdSet / WdZone / WdPlaceAt / WdFoot)。ケースの一覧への
    /// 接続は Wash.cs の MakeWashCases の末尾 (MakeWashDirtCases() の後、WsWrite() の前) で MakeWashStationsCases() を呼ぶ。ここでは一覧へ自分でつながない。
    /// 最後に置くこと: W-stations-grace の対照は wash.washStations を一時的に空にする (各ケースの最初と最後で戻す)。
    ///
    /// W-stations-tools: 実行時の全 KH_WashTool の washes = シーンの全 KH_DogWash、全 KH_DogWash の tools = 全道具、washStations = 全洗い場 (主が先頭)。
    /// W-stations-reset: 道中の洗い場ごとに、全洗い場の道具 (と主洗い場のスポンジ 0) を 2 m 動かしてからその洗い場のリセット (_ResetTools) を呼ぶ
    ///   → その洗い場の道具だけが自分の Home へ戻り、ほかの洗い場の道具と主洗い場の道具は動かない。
    /// W-stations-grace: 道中の洗い場ごとに、泥の地点 0 をその洗い場の正面 3 m (主洗い場の washStation の半径の外) へ一時的に動かし、柴を置く
    ///   → 6 s の間 _dirtBlock 4 (洗い場) で、泥の地点の時間も足の汚れも増えない。対照: washStations を空にすると同じ場所で増える。
    ///
    /// 対象外: 実際の Pickup・Interact の光線 (ボタンは _ResetTools を直接呼ぶ)、他の人が Owner の道具、道中の地点での洗浄そのもの
    /// (同じ KH_DogWash と道具の処理なので主洗い場のケースで見る)、見た目と導線 (Play / PCVR)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        static readonly string[] WssKeys = { "River", "Upper", "Hillside" };
        static readonly string[] WssKinds = { "Sponge", "Shower", "Towel" };
        const string WssRootName = "KH_WashStations";

        class WssStation { public string key; public Transform root; public Transform[] t, home; public UdonBehaviour[] u; public UdonBehaviour reset; }
        static List<WssStation> wss;
        static Transform[] wssSavedList;   // wash.washStations before W-stations-grace's control emptied it

        static void MakeWashStationsCases()
        {
            // ---- W-stations-tools: every tool knows every dog, every dog knows every tool and every station ----
            var s = WsCase("W-stations-tools", 10);
            s.Add(WsDo(() =>
            {
                WssRestoreList();
                WtBind(); WssBind();
                var dogs = WssPrograms("KH_DogWash");
                var tools = WssPrograms("KH_WashTool");
                int want = WtNames.Length + wss.Sum(x => x.u.Length);
                WsExpect(dogs.Count > 0, "at least one KH_DogWash at run time (" + dogs.Count + ")");
                WsExpect(tools.Count == want, "KH_WashTool at run time = the main " + WtNames.Length + " + " + (want - WtNames.Length) + " along the course (" + tools.Count + ")");
                foreach (var t in tools)
                {
                    var w = WsArr<UdonBehaviour>(t, "washes");
                    WsExpect(w.Length == dogs.Count && dogs.All(d => w.Contains(d)), t.name + ".washes = the " + dogs.Count + " KH_DogWash (" + w.Length + ")");
                }
                var stations = new List<Transform> { wtStation };
                stations.AddRange(wss.Select(x => x.root));
                foreach (var d in dogs)
                {
                    var tl = WsArr<UdonBehaviour>(d, "tools");
                    WsExpect(tl.Length == tools.Count && tools.All(x => tl.Contains(x)), d.name + ".tools = the " + tools.Count + " tools of every station (" + tl.Length + ")");
                    var sl = WsArr<Transform>(d, "washStations");
                    WsExpect(sl.Length == stations.Count && sl.Zip(stations, (a, b) => a == b).All(x => x), d.name + ".washStations = the " + stations.Count + " stations, main first ("
                        + string.Join(", ", sl.Select(x => x != null ? x.name : "null")) + ")");
                }
                WsNote(tools.Count + " tools (" + WtNames.Length + " main + " + string.Join(" + ", wss.Select(x => x.key + " " + x.u.Length)) + ") x " + dogs.Count + " KH_DogWash: washes / tools / washStations all complete");
            }));

            // ---- W-stations-reset: a station's reset sends its own tools home and nobody else's ----
            s = WsCase("W-stations-reset", 15 + 5 * WssKeys.Length);
            s.Add(WsDo(() => { WtBind(); WssBind(); WtOverride(0); WtHomeAll(); WssOverride(0); WssHomeAll(); }));
            s.Add(WsWait(0.3));
            for (int k = 0; k < WssKeys.Length; k++)
            {
                int kk = k;
                s.Add(WsDo(() =>
                {
                    foreach (var st in wss) for (int i = 0; i < st.t.Length; i++)
                        {
                            WsExpect(Networking.IsOwner(st.t[i].gameObject), st.t[i].name + " owned by the local player");
                            WssAway(st, i);
                        }
                    WtAway(0);   // the main station's sponge 0
                }));
                s.Add(WsWait(0.3));
                s.Add(WsDo(() => wss[kk].reset.SendCustomEvent("_ResetTools")));
                s.Add(WsWait(0.3));
                s.Add(WsDo(() =>
                {
                    var me = wss[kk];
                    for (int i = 0; i < me.t.Length; i++)
                        WsExpect(WssDist(me, i) < WtAtHome && Quaternion.Angle(me.t[i].rotation, me.home[i].rotation) < 0.5f, me.t[i].name + " back at its own home (" + WssDist(me, i).ToString("F2") + " m)");
                    foreach (var other in wss.Where(x => x != me))
                        for (int i = 0; i < other.t.Length; i++)
                            WsExpect(WssDist(other, i) > WtAwayMeters - 0.05f, other.t[i].name + " (another station) did not move (" + WssDist(other, i).ToString("F2") + " m from its home)");
                    WsExpect(WtDist(0) > WtAwayMeters - 0.05f, WtNames[0] + " (the main station) did not move (" + WtDist(0).ToString("F2") + " m)");
                    WsExpect(Get<int>(me.reset, "_lastSkippedHeld") == 0, me.key + " reset skipped no tool (" + Get<int>(me.reset, "_lastSkippedHeld") + ")");
                    WsNote(me.key + ": its reset sent its " + me.t.Length + " tools home; the other stations' tools and " + WtNames[0] + " stayed " + WtAwayMeters + " m away");
                }));
            }
            s.Add(WsDo(() => { WtOverride(0); WtHomeAll(); WssOverride(0); WssHomeAll(); }));

            // ---- W-stations-grace: no dirt near a station along the course (washStations, not the main washStation) ----
            s = WsCase("W-stations-grace", 20 + 14 * WssKeys.Length);
            for (int k = 0; k < WssKeys.Length; k++)
            {
                int kk = k; bool last = k == WssKeys.Length - 1;
                Transform zn = null; float znR = 0f, z0 = 0f, leg0 = 0f; Vector3 at = Vector3.zero; bool saw4 = false, saw0 = false, saw5 = false; double t0 = 0;
                s.Add(WsDo(() =>
                {
                    WssBind(); WdRestore(); WssRestoreList();
                    var st = wss[kk].root;
                    var list = WsArr<Transform>(wsWash, "washStations");
                    WsExpect(list.Contains(st), "wash.washStations has " + st.name);
                    var main = WsRef(wsWash, "washStation") as Transform;
                    float radius = Get<float>(wsWash, "dirtStationRadius");
                    zn = WdZone(0, out znR);
                    WdSet("dirtGraceSeconds", 0f);
                    WdSet("zoneDirtSeconds", 10f); WdSet("dirtApplyInterval", 2f);   // the test rates (release: 100 s / 20 s)
                    wsWash.SendCustomEvent("_DebugClear");
                    at = CmGround(st.position + st.rotation * new Vector3(0f, 0f, -3f));   // in front of the duckboard, toward the path
                    WsExpect(Flat(at - st.position) <= radius - 0.5f, "the spot " + Flat(at - st.position).ToString("F1") + " m from " + st.name + " is within its radius " + radius);
                    WsExpect(main == null || Flat(at - main.position) > radius + 1f, "the spot is outside the main washStation's radius (only washStations can block it)");
                    wdMovedZone = zn; wdMovedFrom = zn.position; zn.position = at;
                    WdPlaceAt(at); WsPlayerNear(1.3f);
                }));
                s.Add(WsWait(1.2));
                s.Add(WsDo(() => { leg0 = WdFoot("_dirt"); z0 = Get<float>(wsWash, "_dirtZoneTotal"); saw4 = saw0 = saw5 = false; t0 = UnityEditor.EditorApplication.timeSinceStartup; }));
                s.Add(now =>
                {
                    if (Flat(wsRoot.position - at) > znR * 0.6f) { WdPlaceAt(at); WsPlayerNear(1.3f); }
                    int b = Get<int>(wsWash, "_dirtBlock");
                    if (b == 4) saw4 = true; else if (b == 0) saw0 = true; else if (b == 5) saw5 = true;
                    return now - t0 >= 6.0;
                });
                s.Add(WsWait(2.6));
                s.Add(WsDo(() =>
                {
                    float leg = WdFoot("_dirt"), zt = Get<float>(wsWash, "_dirtZoneTotal");
                    WsExpect(saw4 && !saw0, WssKeys[kk] + ": blocked by the station (saw 4 " + saw4 + ", saw 0 " + saw0 + ", saw 5 (water) " + saw5 + ")");
                    WsExpect(zt == z0 && leg <= leg0 + 1e-4f, WssKeys[kk] + ": no dirt beside the station (zone time " + z0.ToString("F2") + " -> " + zt.ToString("F2") + ", legs " + leg0.ToString("F3") + " -> " + leg.ToString("F3") + ")");
                    WsNote(WssKeys[kk] + ": dirt zone moved 3 m in front of the station (" + at.ToString("F1") + "): 6 s blocked (4" + (saw5 ? ", some 5 = water" : "") + "), legs unchanged");
                    if (last)
                    {
                        // control: the same spot with washStations emptied (the main washStation is out of reach here)
                        wssSavedList = WsArr<Transform>(wsWash, "washStations");
                        wsWash.SetProgramVariable<Transform[]>("washStations", new Transform[0]);
                        z0 = zt; leg0 = leg;
                    }
                }));
                if (last)
                {
                    s.Add(WsUntil(() =>
                    {
                        if (Flat(wsRoot.position - at) > znR * 0.6f) { WdPlaceAt(at); WsPlayerNear(1.3f); }
                        return Get<float>(wsWash, "_dirtZoneTotal") - z0 >= 4f;
                    }, 12, () => "control: the zone did not count with washStations empty (block " + Get<int>(wsWash, "_dirtBlock") + ")"));
                    s.Add(WsWait(2.6));
                    s.Add(WsDo(() =>
                    {
                        float leg = WdFoot("_dirt");
                        WsExpect(leg >= leg0 + 0.1f, "control: with washStations empty the same spot dirties the legs (" + leg0.ToString("F3") + " -> " + leg.ToString("F3") + ")");
                        WsNote("control at " + WssKeys[kk] + ": washStations empty -> legs " + leg0.ToString("F3") + " -> " + leg.ToString("F3") + " (the block came from washStations)");
                        WssRestoreList(); WdRestore();
                    }));
                }
                else s.Add(WsDo(() => WdRestore()));
            }
        }

        // ============================================================
        // 部品
        // ============================================================

        static void WssBind()
        {
            var go = GameObject.Find(WssRootName);
            if (go == null || go.transform.parent != null) throw new InvalidOperationException(WssRootName + " not found at the scene root (IMP-ENV-008: run Wash/11)");
            wss = new List<WssStation>();
            foreach (var key in WssKeys)
            {
                var st = go.transform.Find(key);
                if (st == null) throw new InvalidOperationException(WssRootName + "/" + key + " missing (Wash/11 did not place it: see its log)");
                var w = new WssStation { key = key, root = st, t = new Transform[WssKinds.Length], home = new Transform[WssKinds.Length], u = new UdonBehaviour[WssKinds.Length] };
                for (int i = 0; i < WssKinds.Length; i++)
                {
                    string name = "KH_Wash" + WssKinds[i] + "_" + key + "_0";
                    w.t[i] = st.Find(name);
                    if (w.t[i] == null) throw new InvalidOperationException(st.name + "/" + name + " missing");
                    w.u[i] = w.t[i].GetComponent<UdonBehaviour>();
                    if (w.u[i] == null) throw new InvalidOperationException(name + " has no UdonBehaviour (KH_WashTool)");
                    w.home[i] = WsRef(w.u[i], "home") as Transform;
                    if (w.home[i] == null || !w.home[i].IsChildOf(st) || w.home[i].IsChildOf(w.t[i])) throw new InvalidOperationException(name + ".home is not under " + st.name + " (or is under the tool)");
                }
                var rb = st.Find("ResetButton");
                w.reset = rb != null ? rb.GetComponent<UdonBehaviour>() : null;
                if (w.reset == null) throw new InvalidOperationException(st.name + "/ResetButton has no UdonBehaviour (KH_WashStationReset)");
                wss.Add(w);
            }
        }

        static void WssOverride(int value) { foreach (var st in wss) foreach (var u in st.u) u.SetProgramVariable("debugHeldOverride", value); }

        static void WssHomeAll() { foreach (var st in wss) foreach (var u in st.u) u.SendCustomEvent("_DebugReturnHome"); }

        /// <summary>2 m to the station's local -X from the home (off the shelf, away from the duckboard).</summary>
        static void WssAway(WssStation st, int i)
        {
            var t = st.t[i];
            t.position = st.home[i].position + st.root.rotation * new Vector3(-WtAwayMeters, 0f, 0f);
            var sync = t.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
        }

        static float WssDist(WssStation st, int i) { return (st.t[i].position - st.home[i].position).magnitude; }

        /// <summary>The UdonBehaviours in the scene running a program asset of that name (KH_DogWash / KH_WashTool).</summary>
        static List<UdonBehaviour> WssPrograms(string program)
        {
            return UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true)
                .Where(u => u != null && u.programSource != null && u.programSource.name == program).ToList();
        }

        static void WssRestoreList()
        {
            if (wssSavedList == null || wsWash == null) return;
            wsWash.SetProgramVariable<Transform[]>("washStations", wssSavedList);
            wssSavedList = null;
        }
    }
}
