using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-004 Course/83d: ワープできる全地点 (地図の番号 1〜5) へ、地図の副画面のセルから順に移動する (travel の台本で回す)。
    ///
    /// ケース (T1 / T11 と同じ作法・同じ合格条件。TvMove をそのまま使う):
    /// - T13-dogs-1-L1 / T13-dogs-6-L3R3: 入口 (ノード 0) に置いて犬を繋ぎ、広場 → 奥の広場 → 上流の橋 → 川辺 → (下流の橋) → 古民家側の入口 の順に、
    ///   ワープできる地点だけを地図の [地点] → セル → [ここへワープ] で移動する (入口は出発点から 15 m 未満で「ここにいます」になるので最後)。
    ///   1 回ごとに: 新しい reqId が 1 つ、Phase 1 → 2、犬の位置の変更は全頭ちょうど 1 回、着いて 2 秒以内に全頭が手の 3.75 m 以内、暗転してから移動。
    ///   着いた直後に: 本人が「着地点を置かない円」の外 (余白込みの半径)、犬の置き場所 (travelPoint) が円の外 (余白なしの半径)。
    ///   その後 28 秒、道を正方向へ歩いて: Return 0・逆走 0・2 回目の跳び 0。
    /// - T13-nonwarp: ワープできない地点 (今は 6 下流の橋) のセルは隠れていて、押しても移動しない (新しい reqId 0・跳び 0)。
    /// 1 クライアントの ClientSim なので、Owner が同じ人の契約だけを見る (T1 と同じ)。実通信・Owner 交代・途中参加は PCVR 2〜3 人で。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/83d - Walk Harness Travel All Spots (IMP-WLD-004)")]
        public static void TravelSpots() { BeginTravel(false, false, false, true); }

        /// <summary>入口を最後にした移動の順 (地点の並び)。ワープできない地点は飛ばす。</summary>
        static readonly int[] SpotTourOrder = {
            KH_LeashManager.SpotPlaza, KH_LeashManager.SpotUpper, KH_LeashManager.SpotBridgeUp,
            KH_LeashManager.SpotRiver, KH_LeashManager.SpotBridgeDown, KH_LeashManager.SpotGate };

        static void MakeSpotCases(Vector3[] spots, bool[] warp)
        {
            var tour = SpotTourOrder.Where(k => warp[k]).ToArray();
            if (tour.Length < 2) throw new InvalidOperationException("Course/83d needs at least two warpable spots (spotWarpEnabled " + string.Join(",", warp) + ")");
            TvBridgeControl();
            var configs = new[] {
                ("T13-dogs-1-L1", new[] { 0 }, new[] { 0 }),
                ("T13-dogs-6-L3R3", new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 0, 0, 1, 1, 1 }) };
            foreach (var (name, dogs, hands) in configs)
            {
                var s = TvCase(name, false, dogs.Length);
                int mask = dogs.Aggregate(0, (a, d) => a | (1 << d));
                TvSetup(s, dogs, hands);
                foreach (int spot in tour)
                {
                    int sp = spot;
                    // 2026-10-03: Return は T1 と同じく FAIL。例外は既知の上流の橋の角の Return (IMP-DOG-038) だけで、標本で形を確かめたものに限り記録にする
                    // (TvKnownBridgeCornerReturn)。失敗の理由には直前 4 秒の標本が付く
                    TvMove(s, "_TravelPick" + sp, sp, mask, true, true, () => TvSpotKeepOutCheck(sp, mask), true);
                    if (sp == KH_LeashManager.SpotBridgeUp)
                        s.Add(TvDo(() => { for (int d = 0; d < 6; d++) if ((mask & (1 << d)) != 0) TvNoteTrailNear("bridge corner (warp)", d, TvBridgeCorner(), 3f); }));
                }
                s.Add(TvDo(() => TvNote("tour " + string.Join(" -> ", tour.Select(k => (k + 1) + ":" + SpotIdOf(k))) + " through the map cells (_TravelOpenMap, _TravelModeSpots, _TravelPick{k}, _TravelGo)")));
            }
            TvSpotNonWarp(warp);
        }

        // T13-bridge-control* (Return は記録だけ。合否に数えない): ワープせずに犬 0 を左手に繋ぎ、T13 と同じ 1.2 m/s で 28 秒、
        // 同じ橋の角 (ノード 240 → 241) を歩く。逆走と跳びは FAIL。角を通る間の標本を notes に残し、ワープ後の T13 の標本と比べる。

        /// <summary>上流の橋の角 (spot.bridgeUp のノード + 2 = 240。正方向で橋の床へ曲がる点)。</summary>
        static Vector3 TvBridgeCorner()
        {
            int k = (Get<int[]>(manager, "spotNodes")[KH_LeashManager.SpotBridgeUp] + 2) % nodes.Length;
            return nodes[k];
        }

        /// <summary>ワープ無しの比較を 2 本: 上流の橋の着地点の 2 ノード手前 (back 2、角まで約 21 s) と、着地点のノードそのもの (back 0、角まで約 10 s。
        /// ワープと同じ位置・同じ待ちで、違いは「繋いだ」か「切符で置いた」かだけ)。</summary>
        static void TvBridgeControl()
        {
            TvBridgeControl(2, "T13-bridge-control");
            TvBridgeControl(0, "T13-bridge-control-landing");
        }

        static void TvBridgeControl(int back, string name)
        {
            var s = TvCase(name, false, 1); var c = new TvCtx();
            s.Add(TvDo(() => { for (int i = 0; i < 6; i++) if (Get<int[]>(manager, "walkers")[i] == TvMe()) pouch.SendCustomEvent("_WalkOff" + i); }));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers").All(w => w != TvMe()), 5, "detach before the control walk"));
            s.Add(TvDo(() =>
            {
                int start = (Get<int[]>(manager, "spotNodes")[KH_LeashManager.SpotBridgeUp] - back + nodes.Length) % nodes.Length;
                holder = nodes[start]; var heading = nodes[(start + 1) % nodes.Length] - nodes[start]; heading.y = 0;
                Networking.LocalPlayer.TeleportTo(holder, Quaternion.LookRotation(heading));
                pouch.SendCustomEvent("_WalkLoopForward");
                TvNote("control: no warp; holder placed at node " + start + " " + nodes[start].ToString("F1"));
            }));
            s.Add(TvWait(1));
            s.Add(TvDo(() => { PlaceDog(0, Networking.LocalPlayer.GetRotation()); pouch.SendCustomEvent("_WalkLeft0"); }));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers")[0] == TvMe(), 5, "control: dog 0 not leashed"));
            s.Add(TvWait(back == 0 ? 1.5 : 3));   // landing: ワープの着地から歩き出すまで (約 1〜2 s) に合わせる
            s.Add(TvDo(() => { c.arrived = TvNow(); c.logFrom = tvLogs.Count; TvReset(); TvTrack(1); }));
            s.Add(TvWalk(28));
            s.Add(TvDo(() =>
            {
                var rets = TvReturnsSince(c.logFrom, 0);
                foreach (var r in rets)
                    TvNote("control Return " + (r.at - c.arrived).ToString("F1") + " s after start, holder " + r.holder.ToString("F1") + (TvNearBridge(r.holder, 4f) ? " (bridge corner)" : " (off the bridge)") + " (" + r.text + ") last 4 s: " + r.trail);
                TvNoteTrailNear("bridge corner (" + name + ")", 0, TvBridgeCorner(), 3f);
                TvExpect(tvReverse[0] == 0, "control: dog0 reverse steps 0 (" + tvReverse[0] + ")");
                TvExpect(tvDisc[0] == 0, "control: dog0 no jump (" + tvDisc[0] + ")");
                TvNote("control: Return " + rets.Count + ", reverse 0, forward steps " + tvForward[0]);
                tvTrackMask = 0;
            }));
        }

        static string SpotIdOf(int spot)
        {
            var ids = Get<string[]>(manager, "spotIds");
            return ids != null && spot < ids.Length ? ids[spot] : spot.ToString();
        }

        /// <summary>着いた直後: 本人は着地点を置かない円 (余白込み) の外、接続犬の置き場所は円 (余白なし = 洗い場の犬よけ・床、板・ポータルの外形) の外。</summary>
        static void TvSpotKeepOutCheck(int spot, int mask)
        {
            var centers = Get<Vector3[]>(manager, "travelKeepOutCenters");
            var radii = Get<float[]>(manager, "travelKeepOutRadii");
            TvExpect(centers != null && radii != null && centers.Length == radii.Length && centers.Length > 0, "travelKeepOutCenters / Radii present (run Course/80)");
            var me = Networking.LocalPlayer.GetPosition();
            var points = Get<Vector3[]>(manager, "travelPoint");   // 犬の置き場所 (Manager Owner が書いた値。着いた後に歩いた分を含めない)
            float pad = KH_CourseTravelEditor.KeepOutDogPad;
            string worst = ""; float worstMe = float.MaxValue, worstDog = float.MaxValue;
            for (int i = 0; i < centers.Length; i++)
            {
                float dMe = TvFlat(me, centers[i]) - radii[i];
                TvExpect(dMe >= -0.05f, "player landed outside keep-out " + i + " at " + centers[i].ToString("F1") + " (clearance " + dMe.ToString("F2") + " m)");
                if (dMe < worstMe) { worstMe = dMe; worst = "#" + i; }
                for (int d = 0; d < 6; d++)
                {
                    if ((mask & (1 << d)) == 0) continue;
                    float dDog = TvFlat(points[d], centers[i]) - (radii[i] - pad);
                    TvExpect(dDog >= -0.05f, "dog" + d + " placed outside keep-out core " + i + " at " + centers[i].ToString("F1") + " (clearance " + dDog.ToString("F2") + " m)");
                    worstDog = Mathf.Min(worstDog, dDog);
                }
            }
            TvExpect(TvFlat(me, TvSpot(spot)) < 6f, "player is at spot " + (spot + 1) + " " + SpotIdOf(spot));
            TvNote("spot " + (spot + 1) + " " + SpotIdOf(spot) + ": nearest keep-out " + worst + " clearance player " + worstMe.ToString("F1") + " m, dogs (core) " + (worstDog == float.MaxValue ? "-" : worstDog.ToString("F1")) + " m");
        }

        /// <summary>ワープできない地点: セルは隠れていて、押しても移動しない (新しい reqId 0・プレイヤーの跳び 0・犬の跳び 0)。</summary>
        static void TvSpotNonWarp(bool[] warp)
        {
            var off = Enumerable.Range(0, warp.Length).Where(k => !warp[k]).ToArray();
            var s = TvCase("T13-nonwarp", false, off.Length);
            var c = new TvCtx();
            s.Add(TvCooldown());
            if (off.Length == 0) { s.Add(TvDo(() => TvNote("every spot is warpable; nothing to refuse"))); return; }
            foreach (int k in off)
            {
                int kk = k;
                s.Add(TvDo(() => TvPressMap(c, "_TravelPick" + kk)));
                s.Add(TvWait(1.5));
                s.Add(TvDo(() =>
                {
                    string key = Get<string[]>(pouch, "_hsNoteKeys")[0];
                    TvExpect(key == "course.travel.map.pick" || key == "course.travel.unavailable", "spot " + (kk + 1) + " refused (note " + key + ")");
                    TvExpect(TvNewReqs(c.logFrom).Count == 0, "no reqId written for spot " + (kk + 1));
                    TvExpect(tvJumps == 0 && tvDisc.All(x => x == 0), "nobody moved for spot " + (kk + 1) + " (player jumps " + tvJumps + ", dogs " + string.Join(",", tvDisc) + ")");
                    object cells = pouch.GetProgramVariable("travelButtons");
                    var arr = cells as Array;
                    var cell = arr != null && kk < arr.Length ? arr.GetValue(kk) as Component : null;
                    TvExpect(cell != null && !cell.gameObject.activeSelf, "cell " + kk + " (spot " + (kk + 1) + ") hidden in the map list");
                    TvNote("spot " + (kk + 1) + " " + SpotIdOf(kk) + ": cell hidden, press refused with " + key + ", no reqId, no jump");
                }));
            }
            s.Add(TvDo(() => pouch.SendCustomEvent("_TravelMapBack")));
        }
    }
}
