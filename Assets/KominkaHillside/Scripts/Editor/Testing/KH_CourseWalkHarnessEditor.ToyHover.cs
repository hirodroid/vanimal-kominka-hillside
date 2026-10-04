using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-040 呼んだおもちゃを手の届く高さに浮かせる — Course/97d (PlayerData は空から)。
    /// 枠は Course/97 (Commerce.cs) と同じ: 本物の入口 (手元メニューの _Toy* / _Disc*) で呼び、Udon のヒープと Rigidbody を読む。
    /// 模擬 (ヒープ・位置への書き込み、Udon イベントだけのつかみ) は各ケースの notes に書く。ClientSim は Desktop なので、出現位置は
    /// KH_ToyRecall の hoverForwardFlat / hoverDownFlat (VR の値は PCVR で見る)。
    ///
    ///   HV1-hover-in-reach   門の道で 犬 0 のボール・犬 1 のディスクを呼ぶ → 頭から前 (hoverForwardFlat × 倍率)・下 (hoverDownFlat × 倍率) の
    ///                        予定点 ± 0.15 m に、重力なし・kinematic でなく浮く (ToyRecall の浮かせ中の印、ディスクは _hover も)。
    ///                        2 秒後も 0.02 m 以内・速度ほぼ 0 (落ちない・転がらない)
    ///   HV2-timeout-lands    HV1 の 2 つを置いたまま hoverSeconds (20 s) 待つ → 真下の地面 + 持ち上げ分 (± 0.05 m) に下り、重力あり・
    ///                        浮かせ中の印が消え「hover timeout」のログ。さらに 3 秒後も 0.03 m 以内
    ///   HV2b-timeout-slope   探した坂 (6〜25°、川の帯から 3 m 以上) で登り坂を向いて 犬 2 のボールを呼ぶ → 地面より下に出ない (真下の地面 + 半径以上)。
    ///                        時間切れで下りた後 3 秒で 0.05 m 以内 (坂で転がらない)。坂が見つからなければ門の道で行い、その旨を notes に書く
    ///   HV3-river-band       川の帯の縁 (帯の外 0.25 m) に立って水の方を向き 犬 3 のボールを呼ぶ → 帯の上に浮き、11 s (岸へ戻す 10 s を越える) 後も
    ///                        動かず重力なし・_floating なし・岸へ戻すログなし。犬 4 のディスクも同じ (その場で門が断れば門の道で呼んで帯の上へ
    ///                        動かす模擬)。1.5 s 後も岸へ戻されない
    ///   HV4-pickup-gravity   門の道で 犬 5 のボールを呼ぶ → OnPickup (Udon イベント _onPickup だけ、手は無い) → 重力が戻り、0.6 s 以内に
    ///                        浮かせ中の印が消え、落ちる (浮き直さない)。犬 0 のディスクも _onPickup → _onDrop (F5c-grab と同じ) で同じ
    /// ハーネス対象外 (PCVR / 2 人): VR の出現位置と手の届き方、本物の手でつかむ、他人の画面・途中参加で同じ所に浮いて見えること、
    /// 呼んだ人が抜けたら落ちること (Owner の交代)、Quest・スマホ。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/97d - Walk Harness Toy Recall Hover (HV1-HV8, fresh profile)")]
        public static void ToyHoverHarness() { BeginCommerce(CmSetToyHover); }

        class HvToy { public int dog, kind; public Vector3 at, p0, p05; public float radius, prevUntil, t05; public string what; }
        static readonly List<string> hvLogs = new List<string>();
        static bool hvLogHooked;

        static void MakeToyHoverCases()
        {
            cmReport.notCovered.Add("The VR placement (hoverForwardVR / hoverDownVR: in front of the chest) and whether a hand reaches it -> PCVR / Quest");
            cmReport.notCovered.Add("A real hand grab (VRC_Pickup) and a throw out of the hover -> PCVR; here OnPickup is the Udon event only");
            cmReport.notCovered.Add("The others' view and a late joiner (VRCObjectSync carries the position and the gravity), the caller leaving (the new Owner's river look drops it) -> 2 clients");
            hvLogs.Clear();
            if (!hvLogHooked) { Application.logMessageReceived += HvLog; hvLogHooked = true; }
            var h1Ball = new HvToy { dog = 0, kind = KH_ToyRecall.KindBall };
            var h1Disc = new HvToy { dog = 1, kind = KH_ToyRecall.KindDisc };

            // ---- HV1 ----
            var s = CmCase("HV1-hover-in-reach", false, 60);
            s.Add(CmDo(() => { CmToGate(); hvLogs.Clear(); }));
            s.Add(CmWait(1));
            HvCallSteps(s, h1Ball, true);
            HvCallSteps(s, h1Disc, true);
            s.Add(CmWait(2));
            s.Add(CmDo(() =>
            {
                HvExpectHeld(h1Ball); HvExpectHeld(h1Disc);
                float gap = Vector3.Distance(HvT(h1Ball).position, HvT(h1Disc).position), touch = h1Ball.radius + h1Disc.radius;
                CmExpect(gap >= touch, "ball 0 and disc 1 apart (" + gap.ToString("F3") + " m, touching below " + touch.ToString("F3") + "): the second one is not put into the first");
                CmNote("2 s after the call: ball 0 and disc 1 still hovering in place (gravity off, not kinematic, speed ~0), " + gap.ToString("F2") + " m apart");
            }));

            // ---- HV2 ----
            s = CmCase("HV2-timeout-lands", false, 60);
            s.Add(CmUntil(() => !HvHovering(h1Ball) && !HvHovering(h1Disc), HvSeconds() + 3, () => "the hover did not end by time (ball " + HvHovering(h1Ball) + ", disc " + HvHovering(h1Disc) + ")"));
            s.Add(CmWait(.5));
            s.Add(CmDo(() => { HvExpectLanded(h1Ball); HvExpectLanded(h1Disc); }));
            s.Add(CmWait(3));
            s.Add(CmDo(() =>
            {
                HvExpectStill(h1Ball, .03f, false); HvExpectStill(h1Disc, .03f, false);
                CmExpect(hvLogs.Any(l => l.StartsWith("[KH_ToyRecall] hover timeout ball=0 ")) && hvLogs.Any(l => l.StartsWith("[KH_ToyRecall] hover timeout disc=1 ")), "hover timeout logged for ball 0 and disc 1");
                CmNote("after " + HvSeconds().ToString("F0") + " s untaken: both on the ground below with gravity, asleep, still 3 s later");
            }));

            // ---- HV2b ----
            s = CmCase("HV2b-timeout-slope", false, 70);
            var h2 = new HvToy { dog = 2, kind = KH_ToyRecall.KindBall };
            s.Add(CmDo(() =>
            {
                Vector3 at, up; float deg;
                if (HvFindSlope(out at, out up, out deg)) { CmTeleport(at, up); CmNote("slope site " + at.ToString("F2") + " " + deg.ToString("F1") + " deg, facing uphill"); }
                else { CmToGate(); CmNote("no 6-25 deg site found near the course nodes: run on the gate road (flat) instead"); }
            }));
            s.Add(CmWait(1));
            HvCallSteps(s, h2, false);
            s.Add(CmDo(() =>
            {
                var t = HvT(h2); RaycastHit g;
                bool ground = Physics.Raycast(t.position + Vector3.up * 2f, Vector3.down, out g, 6f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore);
                CmExpect(ground && t.position.y >= g.point.y + h2.radius, "ball 2 above the ground below it (" + (ground ? (t.position.y - g.point.y).ToString("F3") : "no ground") + " m, radius " + h2.radius.ToString("F3") + ")");
            }));
            s.Add(CmUntil(() => !HvHovering(h2), HvSeconds() + 3, () => "ball 2 hover did not end by time"));
            s.Add(CmWait(.5));
            s.Add(CmDo(() => HvExpectLanded(h2)));
            s.Add(CmWait(3));
            s.Add(CmDo(() => { HvExpectStill(h2, .05f, false); CmNote("ball 2 landed below its hover point and did not roll for 3 s"); }));

            // ---- HV3 ----
            s = CmCase("HV3-river-band", true, 90);
            var h3Ball = new HvToy { dog = 3, kind = KH_ToyRecall.KindBall };
            var h3Disc = new HvToy { dog = 4, kind = KH_ToyRecall.KindDisc };
            Vector3 bankSite = Vector3.zero, water = Vector3.zero;
            bool discReal = false;
            s.Add(CmDo(() =>
            {
                if (!HvRiverSite(out bankSite, out water)) throw new InvalidOperationException("No bank site at the band edge (z away from the bridges and the water-play spot)");
                CmTeleport(bankSite, water - bankSite); hvLogs.Clear();
                CmNote("bank site " + bankSite.ToString("F2") + " (0.25 m outside the band edge), facing the water");
            }));
            s.Add(CmWait(1));
            HvCallSteps(s, h3Ball, false);
            s.Add(CmDo(() => CmExpect(RiverInBand(h3Ball.at), "ball 3 hovers over the river band (" + h3Ball.at.ToString("F2") + ", lateral " + RiverLateral(h3Ball.at).ToString("F2") + ")")));
            s.Add(CmUntil(() => Get<int>(cmRecall, "_pendingRow") < 0, 6, () => "ball 3 call still pending"));
            s.Add(CmDo(() =>
            {
                cmRecall.SetProgramVariable("_lastResult", -1);
                pouch.SendCustomEvent("_Disc" + h3Disc.dog);
                discReal = Get<int>(pouch, "_hsLastGate") == KH_ToyRecall.Ok;
                if (!discReal) CmNote("disc 4 refused at the bank (gate " + Get<int>(pouch, "_hsLastGate") + "): called on the gate road and moved over the band instead (simulated)");
            }));
            s.Add(CmDo(() => { if (!discReal) CmToGate(); }));
            s.Add(CmWait(.5));
            s.Add(now => discReal || HvPress(h3Disc));
            s.Add(CmUntil(() => HvPlaced(h3Disc), 6, () => "disc 4 not placed (result " + Get<int>(cmRecall, "_lastResult") + ")"));
            s.Add(CmDo(() =>
            {
                var t = HvT(h3Disc);
                if (!discReal)
                {
                    // simulated: the hovering disc moved over the band beside the ball (the Owner's teleport, gravity stays off)
                    var sync = t.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
                    t.position = h3Ball.at + new Vector3(0f, 0f, .4f); if (sync != null) sync.FlagDiscontinuity();
                    CmTeleport(bankSite, water - bankSite);
                }
                // the disc's press is not HvCallSteps: the held point is counted from here (>= 1 s after the placement; a simulated
                // move of > 0.3 m becomes KH_ToyRecall's new hold point)
                h3Disc.p0 = h3Disc.p05 = t.position; h3Disc.t05 = Time.time;
                h3Disc.at = t.position; h3Disc.radius = Get<float>(CmRefs(cmRecall, "discs")[h3Disc.dog], "radius"); h3Disc.what = "disc 4";
                CmExpect(RiverInBand(h3Disc.at), "disc 4 over the river band (" + h3Disc.at.ToString("F2") + ")");
                HvExpectHover(h3Disc);
            }));
            s.Add(CmWait(1.5));
            s.Add(CmDo(() =>
            {
                HvExpectHeld(h3Disc);
                CmExpect(!hvLogs.Any(l => l.Contains("moved to the bank") && l.Contains(" disc=" + HvT(h3Disc).name)), "disc 4 not sent to the bank");
                CmExpect(!Get<bool>(CmRefs(cmRecall, "discs")[h3Disc.dog], "_floating"), "disc 4 not floating");
            }));
            s.Add(CmWait(9.5));   // the ball: > 10 s since its call (a stranded ball in the band goes back after 10 s)
            s.Add(CmDo(() =>
            {
                HvExpectHeld(h3Ball);
                var fl = Get<bool[]>(cmRecall, "_floating");
                CmExpect(fl == null || fl.Length <= h3Ball.dog || !fl[h3Ball.dog], "ball 3 not floating");
                CmExpect(!hvLogs.Any(l => l.StartsWith("[KH_ToyRecall] river-return ball=" + h3Ball.dog + " ")), "ball 3 not sent back to the bank (river-return log)");
                CmNote("ball 3 over the band > 10 s (the stranded-ball return) and disc 4 over the band 1.5 s: both still hovering, never floated or sent to the bank");
            }));

            // ---- HV4 ----
            s = CmCase("HV4-pickup-gravity", true, 60);
            var h4Ball = new HvToy { dog = 5, kind = KH_ToyRecall.KindBall };
            var h4Disc = new HvToy { dog = 0, kind = KH_ToyRecall.KindDisc };
            float y0 = 0f;
            s.Add(CmDo(CmToGate));
            s.Add(CmWait(1));
            HvCallSteps(s, h4Ball, true);
            s.Add(CmWait(1));
            s.Add(CmDo(() =>
            {
                var ub = CmRefs(cmRecall, "balls")[h4Ball.dog];
                ub.SendCustomEvent("_onPickup");
                var rb = HvT(h4Ball).GetComponent<Rigidbody>();
                CmExpect(rb != null && rb.useGravity, "ball 5: OnPickup gives the gravity back at once");
                y0 = HvT(h4Ball).position.y;
                CmNote("simulated: ball 5 OnPickup as the Udon event _onPickup only (no hand, no _onDrop: a pickup shorter than one look)");
            }));
            s.Add(CmUntil(() => !HvHovering(h4Ball), .8, () => "ball 5 still marked hovering 0.8 s after the pickup"));
            s.Add(CmWait(1));
            s.Add(CmDo(() =>
            {
                var t = HvT(h4Ball); var rb = t.GetComponent<Rigidbody>();
                CmExpect(rb != null && rb.useGravity, "ball 5 gravity still on (not hovered again)");
                CmExpect(t.position.y < y0 - .3f, "ball 5 fell (" + (y0 - t.position.y).ToString("F2") + " m)");
            }));
            HvCallSteps(s, h4Disc, true);
            s.Add(CmWait(1));
            s.Add(CmDo(() =>
            {
                var ub = CmRefs(cmRecall, "discs")[h4Disc.dog];
                ub.SendCustomEvent("_onPickup");
                var rb = HvT(h4Disc).GetComponent<Rigidbody>();
                CmExpect(rb != null && rb.useGravity && !Get<bool>(ub, "_hover"), "disc 0: OnPickup gives the gravity back and clears _hover");
                ub.SendCustomEvent("_onDrop");
                y0 = HvT(h4Disc).position.y;
                CmNote("simulated: disc 0 _onPickup then _onDrop (as F5c-grab; no hand)");
            }));
            s.Add(CmUntil(() => !HvHovering(h4Disc), .8, () => "disc 0 still marked hovering 0.8 s after the pickup"));
            s.Add(CmWait(1));
            s.Add(CmDo(() =>
            {
                var t = HvT(h4Disc); var rb = t.GetComponent<Rigidbody>();
                CmExpect(rb != null && rb.useGravity, "disc 0 gravity still on");
                CmExpect(t.position.y < y0 - .3f, "disc 0 fell (" + (y0 - t.position.y).ToString("F2") + " m)");
                CmNote("ball 5 and disc 0: the pickup ends the hover (ToyRecall's mark cleared within one look), they fall with gravity");
            }));

            MakeToyHoverReviewCases();
        }

        // ============================================================
        // Codex review 2026-10-04: HV5 (1) / HV6 (2) / HV7 (3) / HV8 (4)
        // ============================================================
        //   HV5-water-chase-call  犬 1 のボールを川に浮かせて投げ (_OnBallThrown、Owner = 自分)、犬が水中で追っている (Fetch 6) 間に呼ぶ →
        //                         呼べて浮く。犬が追い続けている / 諦める間も 3 s 浮いたまま (呼ぶ前からの動作で浮かせを解かない)
        //   HV6-no-ground         浮いているボールを地面の 8 m 上へ動かし (模擬)、時間切れを早める (模擬: _ballHoverUntil) → 「no ground below」、
        //                         眠らず落ちる (1.5 s で 1 m 以上)、着地の settle なし
        //   HV7-grab-after-land   時間切れで地面に下りた直後 (settle 中)、見回りの間隔より短いつかみ (_onPickup) と放しの速度 (模擬: 速度の書き込み)
        //                         → 0.5 s 後も動いている (settle が速度を消さない)、settle が解ける
        //   HV8-wall-post         コースの近くで探した壁に 0.5 m・55° で近づいて呼ぶ / 細い物 (幹・柱) の横を中心線がかすめる向きで呼ぶ →
        //                         おもちゃが何にも重ならず、2 s 後も 0.02 m 以内
        static void MakeToyHoverReviewCases()
        {
            cmReport.notCovered.Add("HV5: the dog's Owner on another client (its chase state reaching the caller late) -> 2 clients on a real instance");

            // ---- HV5 ----
            var s = CmCase("HV5-water-chase-call", true, 90);
            var h5 = new HvToy { dog = 1, kind = KH_ToyRecall.KindBall };
            UdonBehaviour f5 = null; Vector3 site5 = Vector3.zero, water5 = Vector3.zero;
            s.Add(CmUntil(() => Get<int>(cmRecall, "_pendingRow") < 0, 6, () => "a previous call still pending"));
            s.Add(CmDo(() =>
            {
                if (!HvRiverSite(out site5, out water5)) throw new InvalidOperationException("No bank site (as HV3)");
                CmTeleport(site5, water5 - site5);
                f5 = Get<UdonBehaviour>(wander[h5.dog], "fetchController");
                CmExpect(Get<int>(f5, "dogState") == 0, "dog 1 Fetch idle before the case");
                holder = site5; PlaceDog(h5.dog, Networking.LocalPlayer.GetRotation());
                var t = HvT(h5); var sync = t.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); var rb = t.GetComponent<Rigidbody>();
                if (sync != null) sync.SetKinematic(false);
                float z = water5.z, hw = KH_CourseGroundEditor.HalfWidth(z); var r = KH_CourseGroundEditor.RiverAt(z);
                float side = site5.x >= r.x ? 1f : -1f;
                t.position = new Vector3(r.x + side * hw * .4f, r.y + .05f, z); if (sync != null) sync.FlagDiscontinuity();
                if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                CmNote("simulated: ball 1 put in the river at " + t.position.ToString("F2") + " (near half of the water), dog 1 placed by me on the bank " + site5.ToString("F2"));
            }));
            s.Add(CmUntil(() => { var rb = HvT(h5).GetComponent<Rigidbody>(); return rb != null && !rb.useGravity && !rb.isKinematic; }, 3, () => "ball 1 does not float here (KH_RiverData._FloatsAt)"));
            s.Add(CmDo(() => { f5.SendCustomEvent("_OnBallThrown"); CmNote("simulated: the throw is _OnBallThrown on dog 1's Fetch (Owner = me)"); }));
            s.Add(CmUntil(() => Get<int>(f5, "dogState") == KominkaDogRun.KD_DogFetchController.StateWaterChase, 25, () => "dog 1 never chased the ball in the water (Fetch " + Get<int>(f5, "dogState") + ")"));
            HvCallSteps(s, h5, false);
            s.Add(CmDo(() => CmNote("called while dog 1 was in Fetch " + KominkaDogRun.KD_DogFetchController.StateWaterChase + " (now " + Get<int>(f5, "dogState") + ")")));
            s.Add(CmWait(3));
            s.Add(CmDo(() =>
            {
                HvExpectHeld(h5);
                CmNote("3 s later ball 1 still hovers (the dog's chase from before the call does not end it; dog Fetch " + Get<int>(f5, "dogState") + ")");
            }));

            // ---- HV6 ----
            s = CmCase("HV6-no-ground", true, 60);
            var h6 = new HvToy { dog = 4, kind = KH_ToyRecall.KindBall };
            float y6 = 0f;
            s.Add(CmDo(CmToGate));
            s.Add(CmWait(1));
            HvCallSteps(s, h6, true);
            s.Add(CmDo(() =>
            {
                var t = HvT(h6); var sync = t.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
                var p = t.position + Vector3.up * 8f;
                CmExpect(!Physics.Raycast(p + Vector3.up * .1f, Vector3.down, 6.2f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore), "nothing within 6 m below the raised point " + p.ToString("F2"));
                t.position = p; if (sync != null) sync.FlagDiscontinuity();
                h6.at = p; y6 = p.y;
                HvSetUntil(h6, Time.time + .3f);
                CmNote("simulated: the hovering ball 4 moved 8 m up (no ground within the 6 m look) and its hover time cut to 0.3 s");
            }));
            s.Add(CmUntil(() => !HvHovering(h6), 2, () => "ball 4 hover did not end"));
            s.Add(CmWait(1.5));
            s.Add(CmDo(() =>
            {
                var t = HvT(h6); var rb = t.GetComponent<Rigidbody>();
                CmExpect(hvLogs.Any(l => l.StartsWith("[KH_ToyRecall] hover timeout ball=4 no ground below")), "the no-ground timeout logged");
                CmExpect(rb != null && rb.useGravity, "ball 4 gravity back");
                CmExpect(y6 - t.position.y >= 1f, "ball 4 fell (" + (y6 - t.position.y).ToString("F2") + " m in 1.5 s), not asleep in the air");
                CmExpect(Get<float[]>(cmRecall, "_ballLandSettle")[h6.dog] <= 0f, "no landing settle started");
            }));

            // ---- HV7 ----
            s = CmCase("HV7-grab-after-land", true, 60);
            var h7 = new HvToy { dog = 2, kind = KH_ToyRecall.KindBall };
            Vector3 p7 = Vector3.zero;
            s.Add(CmDo(CmToGate));
            s.Add(CmWait(1));
            HvCallSteps(s, h7, true);
            s.Add(CmDo(() => { HvSetUntil(h7, Time.time + .3f); CmNote("simulated: ball 2 hover time cut to 0.3 s"); }));
            s.Add(CmUntil(() => !HvHovering(h7) && Get<float[]>(cmRecall, "_ballLandSettle")[h7.dog] > Time.time + .5f, 2, () => "ball 2 did not land and settle"));
            s.Add(CmDo(() =>
            {
                CmRefs(cmRecall, "balls")[h7.dog].SendCustomEvent("_onPickup");   // gravity is already on: only the pickup count shows it
                var rb = HvT(h7).GetComponent<Rigidbody>();
                rb.velocity = new Vector3(0f, 2.5f, 0f) + Networking.LocalPlayer.GetRotation() * Vector3.forward * 2f;
                p7 = HvT(h7).position;
                CmNote("simulated: a grab shorter than one look on the landed ball 2 (_onPickup) and the release velocity written (no _onDrop, so no Fetch)");
            }));
            s.Add(CmWait(.5));
            s.Add(CmDo(() =>
            {
                float moved = Vector3.Distance(HvT(h7).position, p7);
                CmExpect(moved >= .5f, "ball 2 kept the release velocity (" + moved.ToString("F2") + " m in 0.5 s)");
                CmExpect(Get<float[]>(cmRecall, "_ballLandSettle")[h7.dog] <= 0f, "the landing settle ended at the pickup");
            }));

            // ---- HV8 ----
            s = CmCase("HV8-wall-post", true, 60);
            var h8w = new HvToy { dog = 5, kind = KH_ToyRecall.KindBall };
            var h8p = new HvToy { dog = 0, kind = KH_ToyRecall.KindBall };
            s.Add(CmDo(() =>
            {
                Vector3 stand, face; string what;
                if (!HvFindObstacle(false, out stand, out face, out what)) throw new InvalidOperationException("KH_Places/KH_GateBlock (BoxCollider) missing, or no face with NavMesh 0.5 m off it");
                CmTeleport(stand, face);
                CmNote("wall: " + what + ", standing " + stand.ToString("F2") + ", facing 55 deg off its normal");
            }));
            s.Add(CmWait(1));
            HvCallSteps(s, h8w, false);
            s.Add(CmDo(() => HvExpectClear(h8w)));
            s.Add(CmWait(2));
            s.Add(CmDo(() => { HvExpectHeld(h8w); HvExpectClear(h8w); }));
            s.Add(CmDo(() => CmNote("wall part: ball 5 overlaps nothing and is held 2 s")));
            // Run 20261004-071555: no corner stand point had NavMesh. The corner part now also accepts plain ground below (a teleport
            // needs no NavMesh); if no corner can be stood at, it is NOT run and is written as not made (notes + notCovered, the user's
            // Play), and HV8 is decided by the wall part alone.
            bool cornerSkip = false;
            s.Add(CmDo(() =>
            {
                Vector3 stand, face; string what;
                if (!HvFindObstacle(true, out stand, out face, out what))
                {
                    cornerSkip = true;
                    CmNote("NOT MADE: the corner (post) part - no corner of KH_GateBlock with NavMesh or ground to stand at the grazing point (4 corners x both faces tried). Left to the user's Play: call a toy with the line passing just beside a post / a wall end");
                    cmReport.notCovered.Add("HV8 corner part (a toy called with the line grazing a post / a wall end): no stand point at KH_GateBlock's corners in ClientSim -> user's Play");
                    return;
                }
                CmTeleport(stand, face);
                CmNote("post: " + what + ", standing " + stand.ToString("F2") + ", the centre line passing 0.03 m beside it");
            }));
            var corner = new List<Func<double, bool>>();
            corner.Add(CmWait(1));
            HvCallSteps(corner, h8p, false);
            corner.Add(CmDo(() => HvExpectClear(h8p)));
            corner.Add(CmWait(2));
            corner.Add(CmDo(() => { HvExpectHeld(h8p); HvExpectClear(h8p); CmNote("corner part: ball 0 overlaps nothing and is held 2 s"); }));
            foreach (var step in corner) { var st = step; s.Add(now => cornerSkip || st(now)); }
        }

        /// <summary>Simulated: the hover's end time of this toy (KH_ToyRecall heap; a copy of the array is written back).</summary>
        static void HvSetUntil(HvToy h, float until)
        {
            string key = h.kind == KH_ToyRecall.KindBall ? "_ballHoverUntil" : "_discHoverUntil";
            var a = (float[])Get<float[]>(cmRecall, key).Clone(); a[h.dog] = until; cmRecall.SetProgramVariable(key, a);
        }

        static void HvExpectClear(HvToy h)
        {
            var t = HvT(h);
            CmExpect(!Physics.CheckSphere(t.position, h.radius * .98f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore), h.what + " overlaps nothing at " + t.position.ToString("F2"));
        }

        /// <summary>Run 20261004-070719: the automatic search found no post near the course (14 m around the spawn only the
        /// hollow outer wall of the kominka and KH_Places/KH_GateBlock are solid at chest height). Both cases now use the box
        /// KH_GateBlock (BoxCollider, layer 11), its corners and faces from its own transform (rotation honoured).
        ///   wall (post = false): stand 0.5 m off the middle of a face, facing 55 deg off its inward normal (the shallow approach: the
        ///                        planned centre ends ~0.04 m from the face = inside the toy).
        ///   post (post = true):  the box corner (the end of a wall) grazed: the line ahead goes diagonally past the corner 0.03 m
        ///                        outside it, the planned centre just at the corner (the head 0.45 m clear of the face).
        /// Tries every corner / face until the standing point has NavMesh within 0.25 m. None = false (the case fails).</summary>
        static bool HvFindObstacle(bool post, out Vector3 stand, out Vector3 face, out string what)
        {
            stand = face = Vector3.zero; what = null;
            var go = GameObject.Find("KH_Places/KH_GateBlock");
            var bc = go == null ? null : go.GetComponent<BoxCollider>();
            if (bc == null) return false;
            var tr = bc.transform;
            Vector3 c = bc.center, hs = bc.size * .5f;
            var corners = new Vector3[4];
            for (int k = 0; k < 4; k++)
            {
                var w = tr.TransformPoint(new Vector3(c.x + (k == 1 || k == 2 ? hs.x : -hs.x), c.y, c.z + (k >= 2 ? hs.z : -hs.z)));
                corners[k] = new Vector3(w.x, 0f, w.z);
            }
            var lp = Networking.LocalPlayer;
            float scale = Mathf.Clamp(lp.GetAvatarEyeHeightAsMeters() / 1.6f, .5f, 2f);
            float ahead = Get<float>(cmRecall, lp.IsUserInVR() ? "hoverForwardVR" : "hoverForwardFlat") * scale;
            // pass 0: NavMesh within 0.25 m (the walkable outside of the gate first); pass 1 (run 20261004-071555: no corner had it):
            // plain ground straight below (groundMask ray, not the box itself, flat enough to stand) - TeleportTo needs no NavMesh
            for (int pass = 0; pass < 2; pass++)
            for (int k = 0; k < 4; k++)
                for (int swap = 0; swap < 2; swap++)
                {
                    Vector3 K = corners[k], A = corners[(k + (swap == 0 ? 1 : 3)) % 4], B = corners[(k + (swap == 0 ? 3 : 1)) % 4];
                    Vector3 a = (A - K).normalized, b = (B - K).normalized;   // along face A (the box is on the +b side of it), along face B
                    float la = Vector3.Distance(A, K);
                    Vector3 s, f;
                    if (!post)
                    {
                        var mid = K + a * (la * .5f);
                        s = mid - b * .5f;
                        f = (b * Mathf.Cos(55f * Mathf.Deg2Rad) + a * Mathf.Sin(55f * Mathf.Deg2Rad)).normalized;
                    }
                    else
                    {
                        f = (b - a).normalized;
                        s = K - b * .03f - f * ahead;
                    }
                    Vector3 at;
                    if (pass == 0)
                    {
                        NavMeshHit nav;
                        if (!NavMesh.SamplePosition(new Vector3(s.x, tr.position.y, s.z), out nav, 3f, NavMesh.AllAreas)) continue;
                        var flatErr = nav.position - s; flatErr.y = 0f;
                        if (flatErr.magnitude > .25f) continue;
                        at = nav.position;
                    }
                    else
                    {
                        RaycastHit g;
                        if (!Physics.Raycast(new Vector3(s.x, tr.position.y + 3f, s.z), Vector3.down, out g, 10f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)) continue;
                        if (g.collider == bc || g.normal.y < .8f) continue;
                        at = g.point;
                    }
                    stand = at; face = f;
                    what = "KH_GateBlock " + (post ? "corner " + K.ToString("F2") : "face from " + K.ToString("F2") + " to " + A.ToString("F2")) + " (ahead " + ahead.ToString("F2") + " m, "
                        + (pass == 0 ? "NavMesh" : "ground only, no NavMesh") + ")";
                    return true;
                }
            return false;
        }

        static void HvLog(string condition, string stack, LogType type)
        {
            if (report == null || !report.commerceOnly || report.commerceSet != CmSetToyHover || !EditorApplication.isPlaying || hvLogs.Count >= 400) return;
            if (condition.StartsWith("[KH_ToyRecall] ") || condition.StartsWith("[KH_Frisbee] "))
            {
                hvLogs.Add(condition);
                if (cmReport != null && cmReport.logs.Count < 200) cmReport.logs.Add(condition);
            }
        }

        static float HvSeconds() { return Get<float>(cmRecall, "hoverSeconds"); }

        static Transform HvT(HvToy h) { return CmToyTransform(h.dog, h.kind); }

        /// <summary>The menu press for this toy (gate must be Ok). Returns true (one-shot step).</summary>
        static bool HvPress(HvToy h)
        {
            cmRecall.SetProgramVariable("_lastResult", -1);
            pouch.SendCustomEvent((h.kind == KH_ToyRecall.KindBall ? "_Toy" : "_Disc") + h.dog);
            CmExpect(Get<int>(pouch, "_hsLastGate") == KH_ToyRecall.Ok, HvName(h) + ": menu gate Ok (gate " + Get<int>(pouch, "_hsLastGate") + ")");
            return true;
        }

        static bool HvPlaced(HvToy h)
        {
            return Get<int>(cmRecall, "_lastResult") == 0 && Get<int>(cmRecall, "_lastRow") == h.dog && Get<int>(cmRecall, "_lastKind") == h.kind;
        }

        static string HvName(HvToy h) { return (h.kind == KH_ToyRecall.KindBall ? "ball " : "disc ") + h.dog; }

        /// <summary>Press, wait for the placement and for the stage-2 park to settle, then check the hover (and, flat = true, the
        /// expected point from the head: ClientSim is Desktop).</summary>
        static void HvCallSteps(List<Func<double, bool>> s, HvToy h, bool expectPoint)
        {
            s.Add(CmUntil(() => Get<int>(cmRecall, "_pendingRow") < 0, 6, () => "a previous call still pending"));
            s.Add(CmDo(() => h.prevUntil = HvUntil(h)));
            s.Add(now => HvPress(h));
            // run 20261004-070719: the placement frame itself (the hover's end time renewed), then 0.5 s later - the early movement
            // (placement settling) and the late movement (HvExpectHeld: drifting while it hovers) are told apart
            s.Add(CmUntil(() => HvUntil(h) > h.prevUntil, 6, () => HvName(h) + ": hover not started"));
            s.Add(CmDo(() => h.p0 = HvT(h).position));
            s.Add(CmWait(.5));
            s.Add(CmDo(() => { h.p05 = HvT(h).position; h.t05 = Time.time; }));
            s.Add(CmUntil(() => HvPlaced(h), 6, () => HvName(h) + ": not placed (result " + Get<int>(cmRecall, "_lastResult") + ")"));
            s.Add(CmDo(() =>
            {
                var t = HvT(h);
                h.what = HvName(h);
                h.radius = h.kind == KH_ToyRecall.KindDisc ? Get<float>(CmRefs(cmRecall, "discs")[h.dog], "radius") : t.localScale.x * .5f;
                h.at = t.position;
                HvExpectHover(h);
                var lp = Networking.LocalPlayer;
                float up = h.at.y - lp.GetPosition().y, flat = TvFlat(h.at, lp.GetPosition());
                if (expectPoint)
                {
                    var head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    var fwd = head.rotation * Vector3.forward; fwd.y = 0f; fwd.Normalize();
                    float scale = Mathf.Clamp(lp.GetAvatarEyeHeightAsMeters() / 1.6f, .5f, 2f);
                    bool vr = lp.IsUserInVR();
                    float ahead = Get<float>(cmRecall, vr ? "hoverForwardVR" : "hoverForwardFlat") * scale, down = Get<float>(cmRecall, vr ? "hoverDownVR" : "hoverDownFlat") * scale;
                    var want = head.position + fwd * ahead + Vector3.down * down;
                    // a toy already there moves the new one to the side (KH_ToyRecall.AwayFromToys: +-1 / +-2 steps along the head's right)
                    var right = Vector3.Cross(Vector3.up, fwd);
                    float side = Vector3.Dot(h.at - want, right);
                    float err = Vector3.Distance(want + right * side, h.at);
                    float step = h.radius + .12f + .05f;
                    CmExpect(err <= .15f && Mathf.Abs(side) <= 2f * step + .02f, h.what + " at the planned point (head + " + ahead.ToString("F2") + " ahead, " + down.ToString("F2") + " down; error " + err.ToString("F3") + " m, sideways " + side.ToString("F2") + " m)");
                    if (Mathf.Abs(side) > .02f) CmNote(h.what + " moved " + side.ToString("F2") + " m sideways (another toy at the planned point)");
                }
                CmExpect(up >= .6f && flat <= 1.6f, h.what + " in reach (" + up.ToString("F2") + " m above the feet, " + flat.ToString("F2") + " m ahead)");
                CmNote(h.what + " hovers at " + h.at.ToString("F2") + ": " + up.ToString("F2") + " m above the feet, " + flat.ToString("F2") + " m ahead");
            }));
        }

        static float HvUntil(HvToy h)
        {
            object o;
            if (!cmRecall.TryGetProgramVariable(h.kind == KH_ToyRecall.KindBall ? "_ballHoverUntil" : "_discHoverUntil", out o) || !(o is float[] a)) return -1f;
            return h.dog < a.Length ? a[h.dog] : -1f;
        }

        /// <summary>Hovering and held where it was put. Early = placement frame -> +0.5 s (noted only), late = +0.5 s -> now (judged:
        /// <= HvLateTol). Run 20261004-070719 moved 2-3 cm in 2 s; late movement means it drifts out of the hand.</summary>
        const float HvLateTol = .01f;
        static void HvExpectHeld(HvToy h)
        {
            var t = HvT(h); var rb = t.GetComponent<Rigidbody>();
            float early = Vector3.Distance(h.p05, h.p0), late = Vector3.Distance(t.position, h.p05), span = Time.time - h.t05;
            CmNote(h.what + " movement: 0-0.5 s " + early.ToString("F3") + " m, 0.5-" + (span + .5f).ToString("F1") + " s " + late.ToString("F3") + " m");
            CmExpect(late <= HvLateTol, h.what + " held in place after the first 0.5 s (" + late.ToString("F3") + " m in " + span.ToString("F1") + " s, limit " + HvLateTol + ")");
            CmExpect(rb == null || rb.isKinematic || rb.velocity.magnitude < .05f, h.what + " at rest (speed " + (rb == null ? 0f : rb.velocity.magnitude).ToString("F3") + ")");
            HvExpectHover(h);
        }

        static bool HvHovering(HvToy h)
        {
            object o;
            if (!cmRecall.TryGetProgramVariable(h.kind == KH_ToyRecall.KindBall ? "_ballHoverUntil" : "_discHoverUntil", out o) || !(o is float[] a)) return false;
            return h.dog < a.Length && a[h.dog] > 0f;
        }

        static void HvExpectHover(HvToy h)
        {
            var rb = HvT(h).GetComponent<Rigidbody>();
            CmExpect(rb != null && !rb.useGravity && !rb.isKinematic, h.what + " hovers: gravity off, not kinematic (gravity " + (rb != null && rb.useGravity) + ", kinematic " + (rb != null && rb.isKinematic) + ")");
            CmExpect(HvHovering(h), h.what + " marked hovering in KH_ToyRecall");
            if (h.kind == KH_ToyRecall.KindDisc) CmExpect(Get<bool>(CmRefs(cmRecall, "discs")[h.dog], "_hover"), h.what + ": the disc's own _hover set");
        }

        /// <summary>Still where it was (h.at) within tol; hovering = also gravity off and marked.</summary>
        static void HvExpectStill(HvToy h, float tol, bool hovering = true)
        {
            var t = HvT(h); var rb = t.GetComponent<Rigidbody>();
            float moved = Vector3.Distance(t.position, h.at);
            CmExpect(moved <= tol, h.what + " did not move (" + moved.ToString("F3") + " m, limit " + tol + ")");
            CmExpect(rb == null || rb.isKinematic || rb.velocity.magnitude < .05f, h.what + " at rest (speed " + (rb == null ? 0f : rb.velocity.magnitude).ToString("F3") + ")");
            if (hovering) HvExpectHover(h);
        }

        /// <summary>Landed by the timeout: on the ground straight below (+ the lift: ball radius, disc 0.02) with gravity, mark cleared. h.at = here.</summary>
        static void HvExpectLanded(HvToy h)
        {
            var t = HvT(h); var rb = t.GetComponent<Rigidbody>(); RaycastHit g;
            CmExpect(!HvHovering(h), h.what + " no longer marked hovering");
            CmExpect(rb != null && rb.useGravity && !rb.isKinematic, h.what + " gravity back");
            bool ground = Physics.Raycast(t.position + Vector3.up * .5f, Vector3.down, out g, 4f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore);
            float lift = h.kind == KH_ToyRecall.KindDisc ? .02f : h.radius;
            CmExpect(ground && Mathf.Abs(t.position.y - g.point.y - lift) <= .05f, h.what + " on the ground below (" + (ground ? (t.position.y - g.point.y).ToString("F3") : "no ground") + " m above it, lift " + lift.ToString("F3") + ")");
            float flat = TvFlat(t.position, h.at);
            CmExpect(flat <= .15f, h.what + " straight below its hover point (" + flat.ToString("F3") + " m aside)");
            if (h.kind == KH_ToyRecall.KindDisc) CmExpect(!Get<bool>(CmRefs(cmRecall, "discs")[h.dog], "_hover"), h.what + ": the disc's _hover cleared");
            h.at = t.position;
        }

        /// <summary>A walkable slope of 6-25 deg near the course nodes, away from the river band. up = the uphill direction.</summary>
        static bool HvFindSlope(out Vector3 at, out Vector3 up, out float deg)
        {
            at = up = Vector3.zero; deg = 0f; float best = -1f;
            if (nodes == null) return false;
            foreach (var n in nodes)
                for (int k = 0; k < 16; k++)
                {
                    float r = k < 8 ? 2.5f : 5f, a = (k % 8) * 45f * Mathf.Deg2Rad;
                    var p = n + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    RaycastHit g; NavMeshHit nav;
                    if (!Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out g, 15f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)) continue;
                    float d = Vector3.Angle(g.normal, Vector3.up);
                    if (d < 6f || d > 25f || d <= best) continue;
                    if (RiverLateral(g.point) < 3f) continue;
                    if (!NavMesh.SamplePosition(g.point, out nav, .5f, NavMesh.AllAreas)) continue;
                    var downhill = new Vector3(g.normal.x, 0f, g.normal.z);
                    if (downhill.sqrMagnitude < 1e-6f) continue;
                    best = d; at = nav.position; up = -downhill.normalized; deg = d;
                }
            return best > 0f;
        }

        /// <summary>A standing point 0.25 m outside the band edge (band = half width + 1 m), away from the bridges and the water-play
        /// spot, with ground and NavMesh; water = a point on the band centre line at the same z (the facing).</summary>
        static bool HvRiverSite(out Vector3 site, out Vector3 water)
        {
            site = water = Vector3.zero;
            foreach (float z in new[] { 150f, 160f, 140f, 170f, 230f, 240f, 250f, 340f, 360f, 400f, 430f })
                foreach (int side in new[] { 1, -1 })
                {
                    var r = KH_CourseGroundEditor.RiverAt(z); float hw = KH_CourseGroundEditor.HalfWidth(z);
                    RaycastHit g; NavMeshHit nav;
                    if (!Physics.Raycast(new Vector3(r.x + side * (hw + 1.25f), r.y + 8f, z), Vector3.down, out g, 30f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)) continue;
                    if (!NavMesh.SamplePosition(g.point, out nav, .6f, NavMesh.AllAreas)) continue;
                    float lat = RiverLateral(nav.position);
                    if (lat < 0f || lat > .6f) continue;
                    site = nav.position; water = new Vector3(r.x, nav.position.y, z);
                    return true;
                }
            return false;
        }
    }
}
