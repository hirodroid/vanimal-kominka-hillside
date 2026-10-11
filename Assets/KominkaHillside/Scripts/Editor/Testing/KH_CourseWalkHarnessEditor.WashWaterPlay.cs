using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-049 段 B 犬の水の反応 (ホースでじゃれる・水鉄砲で跳ねる) — Course/100 の群 water-play (メニュー 100b1、100b にも入る)。
    ///
    /// 枠・部品は Wash.cs (WsCase / WsDo / WsDrive / WsCover ...)、台に呼ぶのは WashCall.cs (WcBind / WcSetup / WcCallAndArrive)、品を出すのは
    /// WashPremium.cs (PremiumBind / PremiumTake / PremiumAck)。犬は柴 (wsDog)、洗い場は主洗い場 (KH_WashStation、stationIndex 0)。
    ///
    /// 「本物のストロークの経路」(段 B の注記) をこう通す: ストロークは道具が毎フレーム呼ぶ入口 KH_DogWash._LocalContact (WsDrive、tool = シャワーの種類)。
    /// 道具の区別 (KH_DogWash.WaterToolMode) は呼び出しではなく、送り主 (自分) が「持っている」水の道具を tools と KH_WashTool._IsHeldBy で引く。
    /// _IsHeldBy は VRC_Pickup.currentPlayer を見る (debugHeldOverride は見ない = 段 B の注記の「効かない」) ので、ClientSim の Pickup の補助
    /// (ClientSimPickupHelper.Pickup: ClientSim が VRC_Pickup._GetCurrentPlayer をこれへ向けている) で自分を持ち主にし、_onPickup も送る (_held)。
    /// 当たりの記録 (_LastStrokePart と同じ瞬間の NoteWaterHit)・洗いの保持・洗いのフレーム (_washActive)・足上げとの取り合い・Owner の判断と
    /// 同期の番号 (waterPlay) の受け取りは実行時のまま。通らないのは道具のノズルからの Raycast (SprayRays) と Use の押下だけ (P-pistol-shot /
    /// P-hose-hold が入力を見る)。2 本持ちは補助を 2 つの Pickup で別々に持たせる (ClientSim の手の仕組みは通さない)。
    ///
    /// ケースの間の濡れのブルブル: ホースの反応を止めるので、ケースの間だけ _washShakeWetNextAt を 60 s 先へ (W-shake が別に見る)。WpRecover で戻す。
    /// 対象外: 非 Owner の受け取り (waterPlay の番号の辺)・途中参加の基準・Owner 交代 (1 人)、跳ぶ見た目・顎の開き方の見た目・声 → PCVR / 2 人。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        static readonly List<UdonBehaviour> wpHeld = new List<UdonBehaviour>();
        static float wpDropAt = -100f;
        static float wpShakeNext0 = float.NaN;   // _washShakeWetNextAt before the hold-off (NaN = not held off)
        static bool wpCalled;                    // a water-play case called the Shiba to the main station (WpRecover releases it)
        static Animator wpAnim;
        static UdonBehaviour wpFree;             // KH_WashStation/KH_WashShower_0 (the free shower)
        const float WpGrace = KH_WashTool.HoldGrace + 0.2f;   // a dropped tool still counts as held for HoldGrace (_IsHeldBy)

        static void MakeWashWaterPlayCases()
        {
            WcBind();
            wpAnim = WsRef(wsWander, "dogAnimator") as Animator;

            // ---- W-water-pistol: the pistol held, hip -> front leg -> one hop (Jump up and down, root straight up and back), no paw lift;
            //      a second hit within WaterPlayPistolSpacing -> no second hop ----
            var s = WsCase("W-water-pistol", 170);
            WsAnchor ppHip = default, ppLeg = default, ppLeg2 = default;
            int ppPaw0 = 0, ppHop0 = 0, ppHose0 = 0, ppReq0 = 0, ppSeq0 = 0, ppSeq1 = 0, ppLp = -1, ppHitPart = -1;   // ppLp / ppHitPart / ppSeq1: when the hop ended
            float ppY0 = 0f, ppRise = 0f, ppDrift = 0f, ppHopH = 0f, ppStart = -1f, ppEnd = -1f, ppT0 = 0f;
            bool ppActive = false, ppJumpOn = false, ppJumpOff = false; Vector3 ppXZ0 = Vector3.zero; string ppSecond = "";
            Action ppSample = () =>
            {
                float now = Time.time, dy = wsRoot.position.y - wcAnchor.position.y - ppY0;
                int kind = Get<int>(wsWander, "_wpKind");
                if (kind == 2) { if (ppStart < 0f) { ppStart = now; ppHopH = Get<float>(wsWander, "_wpHopHeight"); } ppRise = Mathf.Max(ppRise, dy); }
                ppDrift = Mathf.Max(ppDrift, Flat(wsRoot.position - ppXZ0));
                bool j = wpAnim != null && wpAnim.GetBool("Jump");
                if (j) ppJumpOn = true; else if (ppJumpOn) ppJumpOff = true;
                ppActive |= Get<bool>(wsWander, "_washActive");
            };
            WpStart(s, PWaterPistol);
            s.Add(WsDo(() =>
            {
                var cover = WsCover(0.05f);
                var hips = cover.Where(a => wsColPart[a.ci] == 0).ToList(); var legs = cover.Where(a => wsColPart[a.ci] == 3 || wsColPart[a.ci] == 4).ToList();
                if (hips.Count == 0 || legs.Count == 0) throw new WsSkip("no hip (part 0) or front leg (part 3 / 4) collider on the Shiba");
                ppHip = hips[0]; ppLeg = legs[0];
                var other = legs.Where(a => wsColPart[a.ci] != wsColPart[ppLeg.ci]).ToList(); ppLeg2 = other.Count > 0 ? other[0] : ppLeg;
                WpGrab(premiumWater, VRC_Pickup.PickupHand.Right);
            }));
            s.Add(WsDrive(0.5f, 1, u => WsAt(ppHip), WsShower, -1f, null));   // the hip first: the paw lift needs a part change onto the leg
            s.Add(WsDo(() =>
            {
                WsExpect(WsLastPart() == 0, "the hip stroke reached the wash (last part " + WsLastPart() + ")");
                ppPaw0 = WrCount("_washPawCount"); ppHop0 = WrCount("_wpHopCount"); ppHose0 = WrCount("_wpHoseCount"); ppReq0 = WrCount("_wpRequestCount");
                ppSeq0 = Get<int>(wsWash, "_pistolHitSeq");
                ppY0 = wsRoot.position.y - wcAnchor.position.y; ppXZ0 = wsRoot.position; ppT0 = Time.time;
                WsExpect(wpAnim == null || !wpAnim.GetBool("Jump"), "Jump is down before the shot");
            }));
            s.Add(WsDrive(0.4f, 1, u => { ppSample(); return WsAt(ppLeg); }, WsShower, -1f, null));
            s.Add(WsDo(() => WsEnd(WsShower)));
            s.Add(now =>
            {
                ppSample();
                if (ppStart >= 0f && Get<int>(wsWander, "_wpKind") == 0)
                {
                    ppEnd = Time.time; ppLp = WsLastPart(); ppHitPart = Get<int>(wsWash, "_waterHitPart"); ppSeq1 = Get<int>(wsWash, "_pistolHitSeq");
                    return true;
                }
                if (Time.time - ppT0 > 4f) throw new TimeoutException("no hop that ended within 4 s of the leg stroke (hops " + ppHop0 + " -> " + WrCount("_wpHopCount") + ", requests " + ppReq0 + " -> " + WrCount("_wpRequestCount") + ", pistol hits " + ppSeq0 + " -> " + Get<int>(wsWash, "_pistolHitSeq") + ", paw " + ppPaw0 + " -> " + WrCount("_washPawCount") + ", kind " + Get<int>(wsWander, "_wpKind") + ")");
                return false;
            });
            // the second pistol hit at once after the hop ends (~1-1.5 s after the request, well inside WaterPlayPistolSpacing 3 s; the other
            // front leg when there is one: a part change again). Issued before the first hop's checks so a slow check never eats the margin
            s.Add(WsDrive(0.3f, 1, u => { ppSample(); return WsAt(ppLeg2); }, WsShower, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsShower);
                float left = Get<float>(wsWander, "_wpPistolNextAt") - Time.time;
                ppSecond = "second hit on " + wsCols[ppLeg2.ci].name + " ended " + (KH_DogWash.WaterPlayPistolSpacing - left).ToString("F2") + " s after the request, " + left.ToString("F2") + " s before the spacing ran out";
                // its echo (ReceiveStroke records the hit again) arrives up to a segment later: 0.5 s of margin, else the spacing is NOT verified -> FAIL
                WsExpect(left >= 0.5f, "spacing check NOT verified: the second hit must end >= 0.5 s inside WaterPlayPistolSpacing (" + ppSecond + "; stretched frames: rerun alone)");
            }));
            s.Add(now => { ppSample(); return Time.time - ppEnd >= 0.3f; });   // the root eases back onto the anchor (the second stroke took 0.3 s already)
            s.Add(WsDo(() =>
            {
                int lp = ppLp, seq = ppSeq1;
                float back = wsRoot.position.y - wcAnchor.position.y - ppY0;
                WsExpect(lp == wsColPart[ppLeg.ci] && ppHitPart == lp, "the leg stroke was the last part and the recorded water hit when the hop ended (last part " + lp + ", hit part " + ppHitPart + ")");
                WsExpect(seq > ppSeq0, "the held pistol identified the stroke as a pistol hit (pistol hit seq " + ppSeq0 + " -> " + seq + ")");
                WsExpect(ppActive, "the wash frame ran during the leg stroke (_washActive): the paw lift was reachable");
                WsExpect(WrCount("_wpHopCount") == ppHop0 + 1 && WrCount("_wpRequestCount") == ppReq0 + 1 && WrCount("_wpHoseCount") == ppHose0, "exactly one hop (hops " + ppHop0 + " -> " + WrCount("_wpHopCount") + ", requests " + ppReq0 + " -> " + WrCount("_wpRequestCount") + ", hose " + ppHose0 + " -> " + WrCount("_wpHoseCount") + ")");
                WsExpect(WrCount("_washPawCount") == ppPaw0, "no ordinary paw lift for the pistol hit on the leg (" + ppPaw0 + " -> " + WrCount("_washPawCount") + ")");
                WsExpect(ppHopH >= KH_DogWash.WaterPlayHopMin - 1e-4f && ppHopH <= KH_DogWash.WaterPlayHopMax + 1e-4f, "hop height within " + KH_DogWash.WaterPlayHopMin + ".." + KH_DogWash.WaterPlayHopMax + " m (" + ppHopH.ToString("F3") + ")");
                WsExpect(ppRise >= 0.5f * ppHopH && ppRise <= ppHopH + 0.01f, "the root rose " + ppRise.ToString("F3") + " m above the anchor (hop height " + ppHopH.ToString("F3") + ")");
                WsExpect(ppDrift < 0.01f, "straight up only: x / z moved " + (ppDrift * 100f).ToString("F2") + " cm");
                WsExpect(Mathf.Abs(back) < 0.01f, "back on the anchor after the hop (" + back.ToString("F3") + " m)");
                if (wpAnim != null) WsExpect(ppJumpOn && ppJumpOff && !wpAnim.GetBool("Jump"), "the Animator's Jump went up and came down (up " + ppJumpOn + ", down " + ppJumpOff + ")");
                float len = ppEnd - ppStart;
                WsExpect(len >= KH_DogWash.WaterPlayHopMinSeconds - 0.05f && len <= KH_DogWash.WaterPlayHopMaxSeconds + 0.15f, "the hop lasted the Jump clip (" + len.ToString("F2") + " s)");
                WsNote("pistol held (ClientSim pickup), hip 0.5 s -> " + wsCols[ppLeg.ci].name + " 0.4 s through _LocalContact: pistol hits " + ppSeq0 + " -> " + seq + ", hop +1 at +" + (ppStart - ppT0).ToString("F2") + " s for " + len.ToString("F2") + " s, rise " + ppRise.ToString("F3") + " m (height " + ppHopH.ToString("F3") + "), x / z drift " + (ppDrift * 100f).ToString("F2") + " cm, Jump up / down, paw lifts unchanged (" + ppPaw0 + ")");
            }));
            // the second hit (issued above, inside the spacing by >= 0.5 s): 1 s later still no second hop / paw lift
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wsWash, "_pistolHitSeq") > ppSeq1, "the second stroke was a pistol hit too (seq " + ppSeq1 + " -> " + Get<int>(wsWash, "_pistolHitSeq") + ")");
                WsExpect(WrCount("_wpHopCount") == ppHop0 + 1 && WrCount("_wpRequestCount") == ppReq0 + 1 && WrCount("_washPawCount") == ppPaw0, "no second hop / paw lift within the spacing (hops " + WrCount("_wpHopCount") + ", requests " + WrCount("_wpRequestCount") + ", paw " + WrCount("_washPawCount") + "; " + ppSecond + ")");
                WsNote(ppSecond + ": no hop, no paw lift (spacing verified)");
            }));
            WpEnd(s);

            // ---- W-water-hose: the hose held, chest 1.0 s -> one play (paw pose + jaw) about 0.5 s in; it ends about 0.5 s after the water stops ----
            s = WsCase("W-water-hose", 170);
            WsAnchor hsChest = default; int hsPaw0 = 0, hsHop0 = 0, hsHose0 = 0, hsReq0 = 0;
            float hsT0 = -1f, hsPlay = -1f, hsStop = 0f, hsEnd = -1f, hsJaw = 0f, hsJawSeen = 0f; bool hsPose = false, hsJawRef = false;
            Action hsSample = () =>
            {
                if (hsT0 < 0f) hsT0 = Time.time;
                if (hsPlay < 0f && WrCount("_wpHoseCount") > hsHose0) hsPlay = Time.time - hsT0;
                if (Get<int>(wsWander, "_wpKind") != 1) return;
                if (Get<bool>(wsWander, "_washActive") && Get<float>(wsWander, "_washPawUntil") > Time.time) hsPose = true;   // WashPawPose (no satisfied sign runs)
                var jaw = WsRef(wsWander, "_wpJaw") as Transform; hsJawRef |= jaw != null;
                if (Get<bool>(wsWander, "_wpJawWrote"))
                {
                    var b = Get<Quaternion>(wsWander, "_wpJawBase");
                    hsJaw = Mathf.Max(hsJaw, Quaternion.Angle(b, Get<Quaternion>(wsWander, "_wpJawLast")));   // what LateUpdate wrote over the pose
                    if (jaw != null) hsJawSeen = Mathf.Max(hsJawSeen, Quaternion.Angle(b, jaw.localRotation));   // the bone as read here (after the Animator or after us)
                }
            };
            WpStart(s, PHose);
            s.Add(WsDo(() =>
            {
                var chest = WsCover(0.05f).Where(a => wsColPart[a.ci] == 1).ToList();
                if (chest.Count == 0) throw new WsSkip("no chest (part 1) collider on the Shiba");
                hsChest = chest[0];
                WpGrab(premiumWater, VRC_Pickup.PickupHand.Right);
                hsPaw0 = WrCount("_washPawCount"); hsHop0 = WrCount("_wpHopCount"); hsHose0 = WrCount("_wpHoseCount"); hsReq0 = WrCount("_wpRequestCount");
            }));
            s.Add(WsDrive(1.0f, 1, u => { hsSample(); return WsAt(hsChest); }, WsShower, -1f, null));
            s.Add(WsDo(() => { WsEnd(WsShower); hsStop = Time.time; }));
            s.Add(now =>
            {
                hsSample();
                if (hsPlay >= 0f && Get<int>(wsWander, "_wpKind") == 0) { hsEnd = Time.time - hsStop; return true; }
                if (Time.time - hsStop > 2.5f) throw new TimeoutException("the hose play did not start and end (hose plays " + hsHose0 + " -> " + WrCount("_wpHoseCount") + ", requests " + hsReq0 + " -> " + WrCount("_wpRequestCount") + ", kind " + Get<int>(wsWander, "_wpKind") + ", run " + WsRet<float>("_HoseRunSeconds").ToString("F2") + " s, water age " + WsRet<float>("_WaterOnAge").ToString("F2") + " s)");
                return false;
            });
            s.Add(WsWait(0.15));
            s.Add(WsDo(() =>
            {
                WsExpect(WrCount("_wpHoseCount") == hsHose0 + 1 && WrCount("_wpRequestCount") == hsReq0 + 1 && WrCount("_wpHopCount") == hsHop0, "exactly one hose play (hose " + hsHose0 + " -> " + WrCount("_wpHoseCount") + ", requests " + hsReq0 + " -> " + WrCount("_wpRequestCount") + ", hops " + hsHop0 + " -> " + WrCount("_wpHopCount") + ")");
                WsExpect(hsPlay >= KH_DogWash.WaterPlayHoseFirst - 0.05f && hsPlay <= KH_DogWash.WaterPlayHoseFirst + 0.5f, "the play started after WaterPlayHoseFirst of hose on the chest (+" + hsPlay.ToString("F2") + " s)");
                WsExpect(hsPose, "the paw pose was on during the play (_washActive and _washPawUntil ahead)");
                WsExpect(WrCount("_washPawCount") == hsPaw0, "the hose's paw is the play's, not an ordinary paw lift (" + hsPaw0 + " -> " + WrCount("_washPawCount") + ")");
                WsExpect(hsJawRef && hsJaw >= 10f, "the jaw snapped (written up to " + hsJaw.ToString("F1") + " deg over the pose, jaw bone " + (hsJawRef ? "found" : "missing") + ")");
                WsExpect(hsEnd >= KH_DogWash.WaterPlayHoseEnd - 0.1f && hsEnd <= KH_DogWash.WaterPlayHoseEnd + 0.4f, "the play ended about WaterPlayHoseEnd after the water stopped (+" + hsEnd.ToString("F2") + " s)");
                WsExpect(Get<float>(wsWander, "_washPawUntil") <= Time.time && !Get<bool>(wsWander, "_wpJawWrote"), "the paw ended with the play and the jaw went back (paw until " + (Get<float>(wsWander, "_washPawUntil") - Time.time).ToString("F2") + " s, jaw wrote " + Get<bool>(wsWander, "_wpJawWrote") + ")");
                WsNote("hose held (ClientSim pickup), " + wsCols[hsChest.ci].name + " 1.0 s through _LocalContact: play at +" + hsPlay.ToString("F2") + " s, paw pose on, jaw written max " + hsJaw.ToString("F1") + " deg (bone as read here max " + hsJawSeen.ToString("F1") + " deg), water stopped -> play ended +" + hsEnd.ToString("F2") + " s, ordinary paw lifts unchanged (" + hsPaw0 + "), hops unchanged");
            }));
            WpEnd(s);

            // ---- W-water-shower-control: the free shower held alone, hip -> front leg -> the ordinary paw lift, no water play ----
            s = WsCase("W-water-shower-control", 170);
            WsAnchor scHip = default, scLeg = default; int scPaw0 = 0, scHop0 = 0, scHose0 = 0, scReq0 = 0, scSeq0 = 0; float scHit0 = 0f, scHose = 0f; bool scActive = false;
            WpStart(s, -1);
            s.Add(WsDo(() =>
            {
                var cover = WsCover(0.05f);
                var hips = cover.Where(a => wsColPart[a.ci] == 0).ToList(); var legs = cover.Where(a => wsColPart[a.ci] == 3 || wsColPart[a.ci] == 4).ToList();
                if (hips.Count == 0 || legs.Count == 0) throw new WsSkip("no hip (part 0) or front leg (part 3 / 4) collider on the Shiba");
                scHip = hips[0]; scLeg = legs[0];
                WpGrab(wpFree, VRC_Pickup.PickupHand.Right);
                WsExpect(Get<float>(wpFree, "pulseSeconds") == 0f, "the free shower is a toggle (pulseSeconds 0 = no water play)");
            }));
            s.Add(WsDrive(0.5f, 1, u => WsAt(scHip), WsShower, -1f, null));
            s.Add(WsDo(() =>
            {
                scPaw0 = WrCount("_washPawCount"); scHop0 = WrCount("_wpHopCount"); scHose0 = WrCount("_wpHoseCount"); scReq0 = WrCount("_wpRequestCount");
                scSeq0 = Get<int>(wsWash, "_pistolHitSeq"); scHit0 = Get<float>(wsWash, "_waterHitAt"); scHose = Get<float>(wsWash, "_hoseHitAt");
            }));
            s.Add(WsDrive(0.4f, 1, u => { scActive |= Get<bool>(wsWander, "_washActive"); return WsAt(scLeg); }, WsShower, -1f, null));
            s.Add(WsDo(() => WsEnd(WsShower)));
            s.Add(WsWait(1.5));
            s.Add(WsDo(() =>
            {
                WsExpect(WsLastPart() == wsColPart[scLeg.ci], "the leg stroke reached the wash (last part " + WsLastPart() + ")");
                WsExpect(scActive, "the wash frame ran during the leg stroke (_washActive)");
                WsExpect(WrCount("_washPawCount") == scPaw0 + 1, "positive control: the free shower onto the front leg lifts the paw (" + scPaw0 + " -> " + WrCount("_washPawCount") + ")");
                WsExpect(Get<int>(wsWash, "_pistolHitSeq") == scSeq0 && Get<float>(wsWash, "_waterHitAt") == scHit0 && Get<float>(wsWash, "_hoseHitAt") == scHose, "no hose / pistol hit recorded for the free shower");
                WsExpect(WrCount("_wpHopCount") == scHop0 && WrCount("_wpHoseCount") == scHose0 && WrCount("_wpRequestCount") == scReq0, "no water play (hops " + WrCount("_wpHopCount") + ", hose " + WrCount("_wpHoseCount") + ", requests " + WrCount("_wpRequestCount") + ")");
                WsNote("free shower held alone (ClientSim pickup), hip 0.5 s -> " + wsCols[scLeg.ci].name + " 0.4 s: paw lift +1, no water hit recorded, no water play");
            }));
            WpEnd(s);

            // ---- W-water-two-tools-control: the hose (not spraying) and the free shower held together, the chest 1.0 s -> no water play ----
            s = WsCase("W-water-two-tools-control", 170);
            WsAnchor ttChest = default; int ttHose0 = 0, ttReq0 = 0, ttHop0 = 0; float ttHose = 0f, ttOn0 = 0f; bool ttKind = false;
            WpStart(s, PHose);
            s.Add(WsDo(() =>
            {
                var chest = WsCover(0.05f).Where(a => wsColPart[a.ci] == 1).ToList();
                if (chest.Count == 0) throw new WsSkip("no chest (part 1) collider on the Shiba");
                ttChest = chest[0];
                WpGrab(premiumWater, VRC_Pickup.PickupHand.Left);
                WpGrab(wpFree, VRC_Pickup.PickupHand.Right);
                ttHose0 = WrCount("_wpHoseCount"); ttReq0 = WrCount("_wpRequestCount"); ttHop0 = WrCount("_wpHopCount");
                ttHose = Get<float>(wsWash, "_hoseHitAt"); ttOn0 = Get<float>(wsWash, "_waterOnAt");
            }));
            s.Add(WsDrive(1.0f, 1, u => { ttKind |= Get<int>(wsWander, "_wpKind") != 0; return WsAt(ttChest); }, WsShower, -1f, null));
            s.Add(WsDo(() => WsEnd(WsShower)));
            s.Add(WpWatch(1.0, () => { ttKind |= Get<int>(wsWander, "_wpKind") != 0; }));   // a late play would show here
            s.Add(WsDo(() =>
            {
                WsExpect(Get<float>(wsWash, "_waterOnAt") > ttOn0 && WsLastPart() == 1, "the chest strokes landed (water on " + (Time.time - Get<float>(wsWash, "_waterOnAt")).ToString("F2") + " s ago, last part " + WsLastPart() + ")");
                WsExpect(Get<float>(wsWash, "_hoseHitAt") == ttHose && WsRet<float>("_HoseRunSeconds") < 0f, "two water tools held: no hose hit recorded (hose run " + WsRet<float>("_HoseRunSeconds").ToString("F2") + ")");
                WsExpect(!ttKind && WrCount("_wpHoseCount") == ttHose0 && WrCount("_wpRequestCount") == ttReq0 && WrCount("_wpHopCount") == ttHop0, "no water play with two water tools held (hose " + ttHose0 + " -> " + WrCount("_wpHoseCount") + ", requests " + ttReq0 + " -> " + WrCount("_wpRequestCount") + ")");
                WsNote("hose (left, not spraying) + free shower (right) held through two ClientSim pickups, chest 1.0 s + 1 s: no hose hit, no play (W-water-hose is the positive control of the same chest stroke)");
            }));
            WpEnd(s);
        }

        /// <summary>Wait `seconds` of game time while `watch` runs every tick.</summary>
        static Func<double, bool> WpWatch(double seconds, Action watch)
        {
            float t0 = -1f;
            return now => { if (t0 < 0f) t0 = Time.time; watch(); return Time.time - t0 >= seconds; };
        }

        /// <summary>Common start: nothing held, the premium water item out through the board (-1 = none: the selection cleared), the Shiba
        /// freed, called to the main stand and arrived, legs stopped, cells cleared, the spacings of earlier plays / paw lifts run out,
        /// the wet shake held off, and every tool dropped at least HoldGrace ago (the held count must be what the case sets).</summary>
        static void WpStart(List<Func<double, bool>> s, int item)
        {
            s.Add(WsDo(() =>
            {
                wsFailDump = WcDump;
                WpRecover();
                if (UnityEngine.Object.FindObjectsOfType<KH_WashSelection>(true).Length == 0) throw new WsSkip("no KH_WashSelection in the scene (premium tools not built)");
                var st = GameObject.Find("KH_WashStation");
                var fs = st != null ? st.transform.Find("KH_WashShower_0") : null;
                wpFree = fs != null ? fs.GetComponent<UdonBehaviour>() : null;
                if (wpFree == null) throw new InvalidOperationException("KH_WashStation/KH_WashShower_0 has no UdonBehaviour (KH_WashTool)");
                if (wpAnim == null) throw new WsSkip("the Shiba's dogAnimator is not wired");
                WsExpect(wpAnim.parameters.Any(p => p.name == "Jump" && p.type == AnimatorControllerParameterType.Bool), "the Shiba's Animator has the Jump Bool (Frisbee/2)");
                PremiumBind();
                var tools = WsArr<UdonBehaviour>(wsWash, "tools");
                WsExpect(tools.Contains(premiumWater) && tools.Contains(wpFree), "the Shiba's KH_DogWash.tools has the main station's water slot and free shower (" + tools.Length + " tools)");
            }));
            if (item >= 0)
            {
                s.Add(WsUntil(PremiumNear, 3, () => "tester within the selector's 6 m"));
                s.Add(WsDo(() => PremiumTake(item)));
                s.Add(PremiumAck("item " + item));
                s.Add(WsDo(() =>
                {
                    WsExpect(PremiumCode() == KH_WashSelection.ResultApplied && PremiumSel("selectedWater") == item && PremiumShown()[1] == item, "item " + item + " out on the water slot (" + PremiumState() + ")");
                    float p = Get<float>(premiumWater, "pulseSeconds");
                    WsExpect(item == PHose ? p == KH_WashTool.PulseHold : p > 0f, "the water slot's input for item " + item + " (pulseSeconds " + p + ")");
                }));
            }
            WcSetup(s);
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsDo(() => { wpCalled = true; wsWash.SendCustomEvent("_DebugClear"); }));
            s.Add(WsWait(2.0));   // legs stopped (_wasMoving), the turn to standYaw done
            s.Add(WsUntil(() => !Get<bool>(wsWander, "_wasMoving") && Get<int>(wsWander, "_wpKind") == 0
                    && Time.time >= Get<float>(wsWander, "_washPawNextAt") + 0.3f && Time.time >= Get<float>(wsWander, "_wpPistolNextAt") + 0.1f
                    && Time.time >= Get<float>(wsWander, "_wpHoseNextAt") + 0.1f && Time.time - wpDropAt >= WpGrace, 25,
                () => "the Shiba not ready on the stand (moving " + Get<bool>(wsWander, "_wasMoving") + ", play " + Get<int>(wsWander, "_wpKind") + ", paw next in " + (Get<float>(wsWander, "_washPawNextAt") - Time.time).ToString("F1") + " s, pistol next in " + (Get<float>(wsWander, "_wpPistolNextAt") - Time.time).ToString("F1") + " s, hose next in " + (Get<float>(wsWander, "_wpHoseNextAt") - Time.time).ToString("F1") + " s)"));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wcStation, "heldDog") == wsDog && Get<bool>(wcStation, "arrived") && Get<bool>(wsWander, "_stationLocked"), "the Shiba held and arrived on the main stand");
                if (float.IsNaN(wpShakeNext0)) wpShakeNext0 = Get<float>(wsWander, "_washShakeWetNextAt");
                wsWander.SetProgramVariable("_washShakeWetNextAt", Time.time + 60f);   // no wet shake during the case (it would stop a play; W-shake covers it)
            }));
        }

        /// <summary>Common end: tools dropped, the shake hold-off put back, the selection cleared, the Shiba released from the stand.</summary>
        static void WpEnd(List<Func<double, bool>> s)
        {
            s.Add(WsDo(() => { WpRecover(); if (premiumSelection != null) { PremiumClear(); PremiumRecover(); } }));
            s.Add(WsUntil(() => Get<int>(wcStation, "heldDog") < 0, 3, () => "the main station did not release the Shiba (heldDog " + Get<int>(wcStation, "heldDog") + ")"));
        }

        /// <summary>The local player holds `tool` as far as every reader can tell: ClientSim's pickup helper answers VRC_Pickup.currentPlayer
        /// (what KH_WashTool._IsHeldBy / TrackHolder read) and the tool gets its own _onPickup (_held). No hand of ClientSim moves it.</summary>
        static void WpGrab(UdonBehaviour tool, VRC_Pickup.PickupHand hand)
        {
            var pu = tool.GetComponent<VRC_Pickup>();
            if (pu == null) throw new InvalidOperationException(tool.name + " has no VRC_Pickup");
            WsExpect(pu.pickupable, tool.name + " is pickupable");
            var h = pu.GetComponent<ClientSimPickupHelper>();
            if (h == null) throw new WsSkip(tool.name + ": no ClientSimPickupHelper (ClientSim did not set up this pickup): the holder _IsHeldBy reads cannot be simulated");
            var lp = Networking.LocalPlayer;
            h.Pickup(lp, hand, null);
            tool.SendCustomEvent("_onPickup");
            if (!wpHeld.Contains(tool)) wpHeld.Add(tool);
            var cur = pu.currentPlayer;
            WsExpect(Utilities.IsValid(cur) && cur.playerId == lp.playerId, tool.name + ": VRC_Pickup.currentPlayer is the local player (" + (Utilities.IsValid(cur) ? cur.playerId.ToString() : "none") + ")");
            HwInvoke(tool, "_IsHeldBy", "playerId", lp.playerId);
            WsExpect(HwRet<bool>(tool, "_IsHeldBy"), tool.name + "._IsHeldBy(local) true");
        }

        static void WpRelease(UdonBehaviour tool)
        {
            if (tool == null) return;
            var pu = tool.GetComponent<VRC_Pickup>();
            var h = pu != null ? pu.GetComponent<ClientSimPickupHelper>() : null;
            if (h != null && h.IsHeld()) h.Drop(Networking.LocalPlayer);
            tool.SendCustomEvent("_onDrop");
            wpDropAt = Time.time;
        }

        /// <summary>WsRecover (every case's failure) and each water-play case's start / end: the ClientSim-held tools let go, the wet shake
        /// hold-off put back, a Shiba the water play called released (me beside the stand: the Owner takes a release from operateRange).</summary>
        static void WpRecover()
        {
            foreach (var t in wpHeld.ToArray()) { try { WpRelease(t); } catch (Exception) { } }
            wpHeld.Clear();
            if (!float.IsNaN(wpShakeNext0) && wsWander != null) { wsWander.SetProgramVariable("_washShakeWetNextAt", wpShakeNext0); wpShakeNext0 = float.NaN; }
            if (wpCalled && wcStation != null && wcAnchor != null)
            {
                wpCalled = false;
                WcPlayerAtStation();
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
            }
        }
    }
}
