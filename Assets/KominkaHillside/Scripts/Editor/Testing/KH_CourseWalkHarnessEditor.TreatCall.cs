using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using KD_TreatBox = KominkaDogRun.KD_TreatBox;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-006 おやつで接続中の犬を 1 匹ずつ呼ぶ — Course/97c (PlayerData は空から。2026-10-03 ユーザー決定の 5 点)。
    /// 設計は docs/plans/2026-10-03-treat-call.md。枠は Course/97 (Commerce.cs) と同じ: 本物の入口 (手元メニューの _Row* / _PutAway / _Walk* / _WalkOff*、
    /// おやつの実体の _onPickupUseDown / _onPickupUseUp) で操作し、Udon のヒープを読む。模擬 (ヒープへの書き込み) は各ケースの notes に書く。
    ///
    ///   TC0-setup             復元済み・おじいさんの 200 pt・骨型を 20 まで買う・門の道に立つ
    ///   TC1-nearest-waits     自分が 3 匹つなぐ + 未接続の犬を 1 匹わざと手前に置く (模擬) → 出した時点で自分の接続犬の最寄りが掴む
    ///                         (未接続は来ない) → 手元まで寄る → あげる → 食べる → お礼 → 待つ (leashMode 10) → 約 8 s で散歩へ戻る (理由 1)。
    ///                         ずっとリード長 × 1.25 (+余裕) の内
    ///   TC2-next-dog-petting  次のおやつは別の接続犬が来る (続けて同じ犬を呼ばない) → 待つ間に撫でる (模擬: _localPetActive) → 8 s を越えて待つ →
    ///                         撫でをやめると猶予の後に終わる。合計 20 s 以下
    ///   TC3-release-in-wait   待つ間にメニューで外す (首輪の Interact と同じ ReqDetach) → 待ちが消える (理由 5)・自由行動の中心 = 外した位置。
    ///                         次のおやつは足元の外した犬ではなく、残りの接続犬が来る
    ///   TC4-out-of-reach      あげたおやつを 6 m 先へ置く (模擬) → 犬はリード長 × 1.25 で止まって見つめる (食べない)。[しまう] で散歩へ戻る (待たない)
    ///   TC5-last-released     全部外す → 次のおやつは最寄りの犬 (外したばかりの犬でよい)。[しまう]
    ///   TC6-none-can-come     1 匹だけつなぎ、その犬に移動の切符 (模擬) → [出す] は DogsBusy・理由文は「つないでいる犬は、いま来られません」・在庫そのまま
    ///                         (未接続の犬が居ても回さない)。切符を消すと出せて、その犬が掴む。[しまう]
    ///   TC7-others-never      人形 (他人) が 3 匹つなぐ (模擬)。人形のそばで出す → 掴むのは未接続の犬だけ。[しまう]
    /// ハーネス対象外 (PCVR / 2 人): 本物の手で撫でる・首輪の Interact に手が届くか・待つ秒数の体感・おやつを見せたときの距離感、
    /// 2 人が同時に出す (それぞれ自分の接続犬だけ)・途中参加・Owner の交代で待ちが消えること、Fetch / フリスビー / 洗い場の円の中で外したときとの重なり。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/97c - Walk Harness Treat Call on the Leash (TC0-TC7, fresh profile)")]
        public static void TreatCallHarness() { BeginCommerce(CmSetTreatCall); }

        const int TcLeashTreat = 9, TcLeashTreatWait = 10;   // KD_DogWanderController.HillsideTreatCall.cs
        const int TcTicketReq = 987654;                       // a travel request id no pouch ever makes (TC6)

        class TcCall { public int dog = -1, slot = -1, outMs, stockE1; public double waitAt = -1; public float maxReach; }

        static void MakeTreatCallCases()
        {
            cmReport.notCovered.Add("Petting with a real hand, reaching the collar's Interact, how long the wait feels, how close the dog stands while the treat is shown -> PCVR / Quest");
            cmReport.notCovered.Add("Two players giving treats at once (each gets only their own leashed dogs), a late joiner, an Owner change dropping the wait -> 2 clients");
            cmReport.notCovered.Add("Overlap with a ball Fetch / the disc (the leash yields the root above the treat call), and a release inside a wash station's circle (the 20 s grace) -> Play");
            int biscuit = -1;
            float leash = 3f;
            tcLastCalled = -1;

            // ---- TC0 ----
            var s = CmCase("TC0-setup", true, 90);
            s.Add(CmUntil(CmLoaded, 20, () => "ledger not restored (_loaded)"));
            s.Add(CmUntil(() => Get<bool>(cmCatalog, "_resolved") && CmSeeded(CmBiscuit), 5, () => "catalog not resolved / initial bone biscuits not seeded"));
            s.Add(CmDo(() => { biscuit = CmTreatRow(CmBiscuit); leash = Get<float>(manager, "leashLength"); CmFreezeStay(true); CmToKeeper(); }));
            s.Add(CmWait(1));
            s.Add(CmDo(() => cmKeeper.Interact()));
            s.Add(CmUntil(() => CmEarned() >= 200, 3, () => "keeper 200 pt (earned " + CmEarned() + ")"));
            s.Add(CmDo(() =>
            {
                cmKeeper.SendCustomEvent("_Close");
                CmMenu("_Tab0");
                for (int k = 0; k < 3 && CmStock(CmBiscuit) < 20; k++) { pouch.SendCustomEvent("_TreatBuy" + biscuit); pouch.SendCustomEvent("_ConfirmYes"); }
                CmExpect(CmStock(CmBiscuit) == 20, "biscuit 20 (" + CmStock(CmBiscuit) + ")");
                pouch.SendCustomEvent("_Close");
                CmToGate();
                CmNote("simulated: stay accrual frozen; keeper 200 pt; biscuits bought to 20; leash length " + leash.ToString("F1") + " m (reach x1.25 = " + (leash * 1.25f).ToString("F2") + " m)");
            }));

            // ---- TC1 ----
            s = CmCase("TC1-nearest-waits", true, 150);
            var c1 = new TcCall();
            TcLeashThree(s);
            s.Add(CmDo(() =>
            {
                // an unleashed dog right in front of me: nearer than every leashed dog, and still not the one that comes
                TcPlaceInFront(3, 0.9f);
                CmNote("simulated: unleashed dog 3 placed 0.9 m in front of me just before the treat (closer than the leashed dogs)");
            }));
            s.Add(CmWait(0.3));
            TcCallSteps(s, c1, () => biscuit, () => leash, -1);
            s.Add(CmUntil(() => { TcSample(c1); return EditorApplication.timeSinceStartup - c1.waitAt >= 6; }, 8, () => "6 s into the wait"));
            s.Add(CmDo(() => CmExpect(TcWaiting(c1.dog) && TcMode(c1.dog) == TcLeashTreatWait, "still waiting at 6 s (mode " + TcMode(c1.dog) + ")")));
            s.Add(CmUntil(() => { TcSample(c1); return !TcWaiting(c1.dog); }, 6, () => "the wait did not end by itself"));
            s.Add(CmDo(() =>
            {
                double waited = EditorApplication.timeSinceStartup - c1.waitAt;
                CmExpect(Get<int>(wander[c1.dog], "_ltWaitEndReason") == 1, "the wait ended by time (reason " + Get<int>(wander[c1.dog], "_ltWaitEndReason") + ")");
                CmExpect(waited >= 7.3 && waited <= 9.5, "waited " + waited.ToString("F1") + " s (8 s)");
                CmExpect(c1.maxReach <= leash * 1.25f + 0.6f, "dog " + c1.dog + " stayed within the leash reach of me (max " + c1.maxReach.ToString("F2") + " m)");
                CmNote("dog " + c1.dog + " (nearest of my 3 leashed dogs; unleashed dog 3 nearer but not called) came while the treat was in my hand, ate, thanked, waited "
                    + waited.ToString("F1") + " s; max " + c1.maxReach.ToString("F2") + " m from me");
            }));
            s.Add(CmUntil(() => TcMode(c1.dog) != TcLeashTreat && TcMode(c1.dog) != TcLeashTreatWait, 2, () => "dog " + c1.dog + " back on the walk (mode " + TcMode(c1.dog) + ")"));

            // ---- TC2 ----
            s = CmCase("TC2-next-dog-petting", true, 120);
            var c2 = new TcCall();
            UdonBehaviour fetch2 = null;
            TcReady(s);
            TcCallSteps(s, c2, () => biscuit, () => leash, -1);
            s.Add(CmDo(() =>
            {
                CmExpect(c2.dog != c1.dog, "the next treat called another leashed dog (" + c2.dog + "), not dog " + c1.dog + " again");
                fetch2 = Get<UdonBehaviour>(wander[c2.dog], "fetchController");
                fetch2.SetProgramVariable("_localPetActive", true);
            }));
            s.Add(CmUntil(() => Get<int>(fetch2, "petState") >= 0, 3, () => "pet state not raised (petState " + Get<int>(fetch2, "petState") + ")"));
            s.Add(CmUntil(() => EditorApplication.timeSinceStartup - c2.waitAt >= 11, 13, () => "11 s into the wait"));
            s.Add(CmDo(() =>
            {
                CmExpect(TcWaiting(c2.dog) && Get<bool>(wander[c2.dog], "_ltPetExtended"), "petting kept the wait past 8 s");
                fetch2.SetProgramVariable("_localPetActive", false);
            }));
            s.Add(CmUntil(() => !TcWaiting(c2.dog), 6, () => "the wait did not end after the petting stopped"));
            s.Add(CmDo(() =>
            {
                double waited = EditorApplication.timeSinceStartup - c2.waitAt;
                CmExpect(waited >= 11 && waited <= 20.6, "waited " + waited.ToString("F1") + " s (petting extends, cap 20 s)");
                CmNote("dog " + c2.dog + " came (not dog " + c1.dog + " again), petted from the wait start (simulated _localPetActive) -> waited " + waited.ToString("F1") + " s");
            }));
            s.Add(CmUntil(() => Get<int>(fetch2, "petState") < 0, 3, () => "pet state not lowered"));

            // ---- TC3 ----
            s = CmCase("TC3-release-in-wait", false, 120);
            var c3 = new TcCall();
            var c3b = new TcCall();
            Vector3 released = Vector3.zero;
            TcReady(s);
            TcCallSteps(s, c3, () => biscuit, () => leash, -1);
            s.Add(CmDo(() => pouch.SendCustomEvent("_WalkOff" + c3.dog)));
            s.Add(CmUntil(() => !CmMine(c3.dog), 3, () => "dog " + c3.dog + " not released"));
            s.Add(CmWait(0.5));
            s.Add(CmDo(() =>
            {
                CmExpect(!TcWaiting(c3.dog) && Get<int>(wander[c3.dog], "_ltWaitEndReason") == 5, "the release dropped the wait (reason " + Get<int>(wander[c3.dog], "_ltWaitEndReason") + ")");
                released = roots[c3.dog].position;
                Vector3 home = Get<Vector3[]>(manager, "releasedHomes")[c3.dog];
                CmExpect(TvFlat(home, released) < 1.0f, "free-roam centre = where it was released (" + TvFlat(home, released).ToString("F2") + " m)");
            }));
            TcCallSteps(s, c3b, () => biscuit, () => leash, c3.dog, false);
            s.Add(CmDo(() =>
            {
                CmExpect(c3b.dog != c3.dog && CmMine(c3b.dog), "the next treat went to a leashed dog (" + c3b.dog + "), not the released dog " + c3.dog + " at my feet");
                pouch.SendCustomEvent("_PutAway");
            }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispIdle, 4, () => "put away"));
            s.Add(CmUntil(() => TcMode(c3b.dog) != TcLeashTreat && !TcWaiting(c3b.dog), 2, () => "dog " + c3b.dog + " back on the walk without waiting (mode " + TcMode(c3b.dog) + ")"));
            s.Add(CmDo(() => CmNote("released dog " + c3.dog + " during its wait (menu = the collar's ReqDetach): wait dropped, home = release point; next treat -> leashed dog " + c3b.dog + "; put away -> walk, no wait")));

            // ---- TC4 ----
            s = CmCase("TC4-out-of-reach", true, 90);
            var c4 = new TcCall();
            int clamped0 = 0;
            Vector3 far = Vector3.zero;
            TcReady(s);
            TcCallSteps(s, c4, () => biscuit, () => leash, -1, false);
            s.Add(CmDo(() =>
            {
                clamped0 = Get<int>(wander[c4.dog], "_ltClamped");
                CmBody(c4.slot).SendCustomEvent("_onPickupUseDown");
            }));
            s.Add(CmWait(0.1));
            s.Add(CmDo(() =>
            {
                var body = CmBody(c4.slot);
                body.SendCustomEvent("_onPickupUseUp");
                var me = Networking.LocalPlayer;
                Vector3 fwd = me.GetRotation() * Vector3.forward; fwd.y = 0;
                far = CmGround(me.GetPosition() + fwd.normalized * 6f);
                body.transform.position = far + Vector3.up * 0.05f;
                var rb = body.GetComponent<Rigidbody>(); if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                c4.maxReach = 0;
                CmNote("simulated: the given treat moved to " + TvFlat(far, me.GetPosition()).ToString("F1") + " m in front of me (a throw)");
            }));
            s.Add(CmWait(1));
            double t4 = 0;
            s.Add(CmDo(() => t4 = EditorApplication.timeSinceStartup));
            s.Add(CmUntil(() =>
            {
                TcSample(c4);
                CmExpect(Get<int[]>(cmBox, "dogEatMs")[c4.dog] == 0, "dog " + c4.dog + " did not start eating out of reach");
                return EditorApplication.timeSinceStartup - t4 >= 6;
            }, 8, () => "6 s out of reach"));
            s.Add(CmDo(() =>
            {
                CmExpect(c4.maxReach <= leash * 1.25f + 0.6f, "dog stopped at the leash reach (max " + c4.maxReach.ToString("F2") + " m from me, reach " + (leash * 1.25f).ToString("F2") + ")");
                CmExpect(Get<int>(wander[c4.dog], "_ltClamped") > clamped0, "the treat step was clamped (_ltClamped)");
                CmExpect(CmClaimer(c4.slot, c4.outMs) == c4.dog, "the claim is kept while it looks at the treat");
                pouch.SendCustomEvent("_PutAway");
            }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispIdle, 4, () => "put away"));
            s.Add(CmUntil(() => TcMode(c4.dog) != TcLeashTreat && !TcWaiting(c4.dog), 2, () => "dog " + c4.dog + " back on the walk without waiting"));
            s.Add(CmDo(() => CmNote("treat 6 m away: dog " + c4.dog + " stayed within " + c4.maxReach.ToString("F2") + " m of me (reach " + (leash * 1.25f).ToString("F2") + "), never ate; [しまう] -> walk")));

            // ---- TC5 ----
            s = CmCase("TC5-last-released", false, 60);
            int c5 = -1;
            s.Add(CmDo(CmDetachAll));
            s.Add(CmUntil(CmNoneMine, 6, () => "detach all"));
            s.Add(CmWait(0.5));
            s.Add(CmDo(() => pouch.SendCustomEvent("_Row" + biscuit)));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispOut, 4, () => "not out (reason " + CmReason() + ")"));
            int slot5 = -1, out5 = 0;
            s.Add(CmDo(() => { slot5 = Get<int>(pouch, "_slotIndex"); out5 = CmOutMs(slot5); }));
            s.Add(CmUntil(() => (c5 = CmClaimer(slot5, out5)) >= 0, 6, () => "no dog claimed it"));
            s.Add(CmDo(() =>
            {
                var bp = CmBody(slot5).transform.position;
                float mine = TvFlat(roots[c5].position, bp);
                float best = Enumerable.Range(0, 6).Min(d => TvFlat(roots[d].position, bp));
                CmExpect(Get<int[]>(manager, "walkers")[c5] < 0, "the claimer is not on a leash");
                CmExpect(mine <= best + 0.5f, "the nearest dog came (" + mine.ToString("F2") + " m, nearest " + best.ToString("F2") + " m)");
                CmNote("no leashed dog left: dog " + c5 + " (just released, at my feet) came - decision 3");
                pouch.SendCustomEvent("_PutAway");
            }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispIdle, 4, () => "put away"));

            // ---- TC6 ----
            s = CmCase("TC6-none-can-come", true, 60);
            int e6 = 0;
            s.Add(CmDo(() => { CmToGate(); }));
            s.Add(CmWait(1));
            s.Add(CmDo(() => CmAttach(0, 0)));
            s.Add(CmUntil(() => CmMine(0), 4, () => "dog 0 not leashed"));
            s.Add(CmWait(1));
            s.Add(CmDo(() =>
            {
                TcSetTicket(0);
                e6 = CmStockE(CmBiscuit);
                CmMenu("_Tab0");
                pouch.SendCustomEvent("_Row" + biscuit);
                CmExpect(CmReason() == KD_TreatBox.GateDogsBusy && CmState() == KD_TreatBox.DispIdle && CmStockE(CmBiscuit) == e6,
                    "my only leashed dog cannot come: DogsBusy (" + CmReason() + "), nothing taken, not given to the 5 free dogs");
            }));
            s.Add(CmUntil(() => TcOwnLeashTexts().Contains(CmText(pouch, "statusLabel")), 2, () => "reason text is not WordTreatBusyOwnLeash ('" + CmText(pouch, "statusLabel") + "')"));
            s.Add(CmDo(() => { TcClearTicket(); pouch.SendCustomEvent("_Row" + biscuit); }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispOut, 4, () => "not out after the ticket cleared (reason " + CmReason() + ")"));
            int slot6 = -1, out6 = 0;
            s.Add(CmDo(() => { slot6 = Get<int>(pouch, "_slotIndex"); out6 = CmOutMs(slot6); }));
            s.Add(CmUntil(() => CmClaimer(slot6, out6) == 0, 6, () => "dog 0 did not claim (claimer " + CmClaimer(slot6, out6) + ")"));
            s.Add(CmDo(() => { pouch.SendCustomEvent("_PutAway"); CmNote("simulated: travel ticket (phase Ready, req " + TcTicketReq + ") on my only leashed dog -> refused with 'your leashed dogs cannot come', stock kept; cleared -> dog 0 claims; put away"); }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispIdle, 4, () => "put away"));
            s.Add(CmDo(CmDetachAll));
            s.Add(CmUntil(CmNoneMine, 6, () => "detach"));

            // ---- TC7 ----
            s = CmCase("TC7-others-never", true, 90);
            int c7 = -1, slot7 = -1, out7 = 0;
            s.Add(CmDo(() => { if (cmPuppets.Count == 0) CmSpawnPuppets(1); }));
            s.Add(CmUntil(() => CmCollectPuppets(1), 5, () => "puppet did not join"));
            s.Add(CmDo(() =>
            {
                var pp = cmPuppets[0].GetPosition();
                var p = CmGround(pp + Vector3.right * 1.5f);
                CmTeleport(p, pp - p);
                holder = p;
                for (int d = 0; d < 3; d++) PlaceDog(d, Networking.LocalPlayer.GetRotation());   // beside me = beside the puppet
                CmLeashToPuppet(new[] { 0, 1, 2 });
                for (int d = 3; d < 6; d++) TcPlaceInFront(d, 3.5f + d * 0.4f);
                CmNote("simulated: puppet " + cmPuppets[0].playerId + " holds dogs 0-2 (walkers heap), standing 1.5 m from me; dogs 3-5 placed 3.5-5.5 m in front of me");
            }));
            s.Add(CmWait(1.5));
            s.Add(CmDo(() => pouch.SendCustomEvent("_Row" + biscuit)));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispOut, 4, () => "not out (reason " + CmReason() + ")"));
            s.Add(CmDo(() => { slot7 = Get<int>(pouch, "_slotIndex"); out7 = CmOutMs(slot7); }));
            s.Add(CmUntil(() => (c7 = CmClaimer(slot7, out7)) >= 0, 6, () => "no dog claimed it"));
            s.Add(CmUntil(() =>
            {
                int k = CmClaimer(slot7, out7);
                CmExpect(k < 0 || k >= 3, "another player's leashed dog " + k + " took my treat");
                return EditorApplication.timeSinceStartup - cmStepAt >= 3;
            }, 5, () => "3 s of watching the claim"));
            s.Add(CmDo(() =>
            {
                CmExpect(c7 >= 3, "an unleashed dog claimed it (" + c7 + ")");
                pouch.SendCustomEvent("_PutAway");
                CmClearPuppetLeashes();
                CmNote("dogs 0-2 on the puppet's leash 1.5 m away were never assigned; unleashed dog " + c7 + " claimed it; put away");
            }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispIdle && Get<int[]>(manager, "walkers").All(w => w < 0), 4, () => "put away / puppet leashes cleared"));
            s.Add(CmDo(() => { CmRemovePuppets(); CmFreezeStay(false); }));
        }

        /// <summary>Detach everything, stand at the gate and leash dogs 0, 1 (left) and 2 (right) through the menu, then let them walk a little.</summary>
        static void TcLeashThree(List<Func<double, bool>> s)
        {
            TcIdle(s);
            s.Add(CmDo(CmDetachAll));
            s.Add(CmUntil(CmNoneMine, 6, () => "detach before the case"));
            s.Add(CmDo(CmToGate));
            s.Add(CmWait(1));
            for (int d = 0; d < 3; d++)
            {
                int dog = d;
                s.Add(CmDo(() => CmAttach(dog, dog < 2 ? 0 : 1)));
                s.Add(CmWait(0.5));
            }
            s.Add(CmUntil(() => CmMine(0) && CmMine(1) && CmMine(2), 6, () => "dogs 0-2 not leashed (walkers " + string.Join(",", Get<int[]>(manager, "walkers")) + ")"));
            s.Add(CmWait(3));
        }

        static int tcLastCalled = -1;   // mirror of the master's "called last" memory (my leashed dog that claimed most recently)

        /// <summary>Each case starts from: no treat of mine out (a failed case's put-away may still be returning), no wait running,
        /// dogs 0-2 on my leash (a failed case's recovery detaches everything). Re-leashes only the dogs that are missing.</summary>
        static void TcReady(List<Func<double, bool>> s)
        {
            TcIdle(s);
            s.Add(CmDo(() =>
            {
                if (CmMine(0) && CmMine(1) && CmMine(2)) return;
                CmClearPuppetLeashes();
                CmToGate();
            }));
            s.Add(CmWait(0.5));
            for (int d = 0; d < 3; d++)
            {
                int dog = d;
                s.Add(CmDo(() => { if (!CmMine(dog)) { CmAttach(dog, dog < 2 ? 0 : 1); CmNote("re-leashed dog " + dog + " (left over from the previous case)"); } }));
                s.Add(CmWait(0.5));
            }
            s.Add(CmUntil(() => CmMine(0) && CmMine(1) && CmMine(2), 6, () => "dogs 0-2 not leashed (walkers " + string.Join(",", Get<int[]>(manager, "walkers")) + ")"));
            s.Add(CmUntil(() => Enumerable.Range(0, 6).All(d => !TcWaiting(d) && TcMode(d) != TcLeashTreat), 12, () => "a wait / treat call from the previous case still running"));
            s.Add(CmWait(1));
        }

        /// <summary>No dispense of mine in progress and no slot given by me (a put-away body may still be on its way home).</summary>
        static void TcIdle(List<Func<double, bool>> s)
        {
            s.Add(CmDo(() => { if (CmState() == KD_TreatBox.DispOut) pouch.SendCustomEvent("_PutAway"); }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispIdle && Enumerable.Range(0, 3).All(i => CmGiver(i) != CmMe() && !Get<bool>(cmSlots[i], "_returning")), 8,
                () => "my previous treat not back (state " + CmState() + ")"));
        }

        /// <summary>出す → (手の中で) 自分の接続犬が掴む → 手元へ寄る → (give なら) あげる → 食べる → 枠が空く → お礼 → 待つ。
        /// <paramref name="notDog"/> &gt;= 0 ならその犬が掴んだら失敗。最寄りは掴んだ瞬間のおやつからの距離で (動いている分 0.5 m の余裕)。</summary>
        static void TcCallSteps(List<Func<double, bool>> s, TcCall c, Func<int> row, Func<float> leash, int notDog, bool give = true)
        {
            s.Add(CmDo(() => { pouch.SendCustomEvent("_Row" + row()); }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispOut, 4, () => "not out (state " + CmState() + ", reason " + CmReason() + ")"));
            s.Add(CmDo(() => { c.slot = Get<int>(pouch, "_slotIndex"); c.outMs = CmOutMs(c.slot); c.stockE1 = CmStockE(CmBiscuit); c.maxReach = 0; }));
            s.Add(CmUntil(() => (c.dog = CmClaimer(c.slot, c.outMs)) >= 0, 6, () => "no dog claimed the treat in my hand (dogSlot " + string.Join(",", Get<int[]>(cmBox, "dogSlot")) + ")"));
            s.Add(CmDo(() =>
            {
                CmExpect(Get<bool>(CmBody(c.slot), "_attaching"), "claimed while the treat is still in my hand (decision 1)");
                CmExpect(CmMine(c.dog), "the claimer (dog " + c.dog + ") is one of my leashed dogs");
                if (notDog >= 0) CmExpect(c.dog != notDog, "not dog " + notDog);
                var bp = CmBody(c.slot).transform.position;
                float mine = TvFlat(roots[c.dog].position, bp);
                // the rule (BuildAllowWith): my leashed dogs; the dog I called last is skipped while another of them can come
                var cand = Enumerable.Range(0, 6).Where(d => CmMine(d) && d != notDog).ToList();
                bool skip = tcLastCalled >= 0 && cand.Contains(tcLastCalled) && cand.Count >= 2;
                if (skip) { cand.Remove(tcLastCalled); CmExpect(c.dog != tcLastCalled, "not dog " + tcLastCalled + " (called last) again"); }
                float best = cand.Min(d => TvFlat(roots[d].position, bp));
                CmExpect(mine <= best + 0.5f, "the nearest of my leashed dogs" + (skip ? " except dog " + tcLastCalled + " (called last)" : "") + " (" + mine.ToString("F2") + " m, nearest " + best.ToString("F2") + " m)");
                tcLastCalled = c.dog;
            }));
            s.Add(CmUntil(() =>
            {
                TcSample(c);
                return TvFlat(roots[c.dog].position, Networking.LocalPlayer.GetPosition()) <= 1.3f;
            }, 10, () => "dog " + c.dog + " did not come to me (" + TvFlat(roots[c.dog].position, Networking.LocalPlayer.GetPosition()).ToString("F2") + " m, mode " + TcMode(c.dog) + ")"));
            s.Add(CmDo(() => CmExpect(TcMode(c.dog) == TcLeashTreat, "leashMode LeashTreat while called (" + TcMode(c.dog) + ")")));
            if (!give) return;
            s.Add(CmWait(0.3));
            s.Add(CmDo(() => CmBody(c.slot).SendCustomEvent("_onPickupUseDown")));
            s.Add(CmWait(0.1));
            s.Add(CmDo(() => CmBody(c.slot).SendCustomEvent("_onPickupUseUp")));
            s.Add(CmUntil(() => { TcSample(c); return Get<int[]>(cmBox, "dogEatMs")[c.dog] != 0; }, 15, () => "dog " + c.dog + " did not start eating"));
            s.Add(CmUntil(() => { TcSample(c); return CmGiver(c.slot) < 0; }, 8, () => "slot not freed after eating"));
            s.Add(CmUntil(() => { TcSample(c); return TcWaiting(c.dog); }, 6, () => "dog " + c.dog + " did not start waiting (mode " + TcMode(c.dog) + ")"));
            s.Add(CmDo(() =>
            {
                c.waitAt = EditorApplication.timeSinceStartup;
                CmExpect(TcMode(c.dog) == TcLeashTreatWait && CmMine(c.dog), "waiting on the leash (mode " + TcMode(c.dog) + ")");
                CmExpect(CmStockE(CmBiscuit) == c.stockE1, "eating took nothing more");
            }));
        }

        static int TcMode(int d) { return Get<int>(wander[d], "leashMode"); }
        static bool TcWaiting(int d) { return Get<bool>(wander[d], "_ltWaiting"); }
        static void TcSample(TcCall c)
        {
            if (c.dog < 0) return;
            float r = TvFlat(roots[c.dog].position, Networking.LocalPlayer.GetPosition());
            if (r > c.maxReach) c.maxReach = r;
        }

        /// <summary>Puts dog d on the NavMesh <paramref name="ahead"/> m in front of me (a root write = the walk harnesses' placement injection).</summary>
        static void TcPlaceInFront(int d, float ahead)
        {
            var me = Networking.LocalPlayer;
            Vector3 fwd = me.GetRotation() * Vector3.forward; fwd.y = 0;
            var p = CmGround(me.GetPosition() + fwd.normalized * ahead, 1.5f);
            roots[d].SetPositionAndRotation(p, Quaternion.LookRotation(-fwd.normalized));
            var sync = roots[d].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
        }

        /// <summary>Simulated: another player (the first puppet) holds these dogs (walkers / hands heap; no Udon runs for the puppet).</summary>
        static void CmLeashToPuppet(int[] dogs)
        {
            if (cmPuppets.Count == 0 || !Utilities.IsValid(cmPuppets[0])) throw new InvalidOperationException("no puppet to hold the leash");
            var w = (int[])Get<int[]>(manager, "walkers").Clone(); var h = (int[])Get<int[]>(manager, "hands").Clone();
            foreach (int d in dogs) { w[d] = cmPuppets[0].playerId; h[d] = d % 2; }
            manager.SetProgramVariable("walkers", w); manager.SetProgramVariable("hands", h);
        }

        /// <summary>Clears the simulated puppet leashes (any walker that is not a valid local / menu leash of mine).</summary>
        static void CmClearPuppetLeashes()
        {
            if (manager == null) return;
            var w = (int[])Get<int[]>(manager, "walkers").Clone();
            bool changed = false;
            int me = CmMe();
            for (int i = 0; i < w.Length; i++) if (w[i] >= 0 && w[i] != me) { w[i] = -1; changed = true; }
            if (changed) manager.SetProgramVariable("walkers", w);
        }

        /// <summary>Simulated: a pending travel ticket (phase Ready, never applied: the dog's travel branch acts on Moved only) on dog d.</summary>
        static void TcSetTicket(int d)
        {
            var req = (int[])Get<int[]>(manager, "travelReq").Clone(); var hold = (int[])Get<int[]>(manager, "travelHolder").Clone();
            var ph = (int[])Get<int[]>(manager, "travelPhase").Clone(); var at = (int[])Get<int[]>(manager, "travelAtMs").Clone();
            req[d] = TcTicketReq; hold[d] = CmMe(); ph[d] = KH_LeashManager.TravelPhaseReady; at[d] = Networking.GetServerTimeInMilliseconds();
            manager.SetProgramVariable("travelReq", req); manager.SetProgramVariable("travelHolder", hold);
            manager.SetProgramVariable("travelPhase", ph); manager.SetProgramVariable("travelAtMs", at);
        }

        static void TcClearTicket()
        {
            if (manager == null) return;
            var req = (int[])Get<int[]>(manager, "travelReq").Clone(); var hold = (int[])Get<int[]>(manager, "travelHolder").Clone();
            var ph = (int[])Get<int[]>(manager, "travelPhase").Clone();
            bool changed = false;
            for (int i = 0; i < req.Length; i++)
                if (req[i] == TcTicketReq) { req[i] = 0; hold[i] = -1; ph[i] = KH_LeashManager.TravelPhaseNone; changed = true; }
            if (!changed) return;
            manager.SetProgramVariable("travelReq", req); manager.SetProgramVariable("travelHolder", hold); manager.SetProgramVariable("travelPhase", ph);
        }

        [Serializable] class TcCatalog { public TcLang[] languages; }
        [Serializable] class TcLang { public string code; public TcText[] texts; }
        [Serializable] class TcText { public string key, text; }
        static HashSet<string> tcOwnLeashTexts;
        /// <summary>WordTreatBusyOwnLeash in every language of the base catalog (the label shows the client language's).</summary>
        static HashSet<string> TcOwnLeashTexts()
        {
            if (tcOwnLeashTexts != null) return tcOwnLeashTexts;
            var cat = JsonUtility.FromJson<TcCatalog>(File.ReadAllText("Assets/KominkaHillside/Imported/KominkaDogRun/Data/UITranslations.json"));
            tcOwnLeashTexts = new HashSet<string>(cat.languages.SelectMany(l => l.texts).Where(t => t.key == "KD_TreatPouch.WordTreatBusyOwnLeash").Select(t => t.text));
            if (tcOwnLeashTexts.Count == 0) throw new InvalidOperationException("KD_TreatPouch.WordTreatBusyOwnLeash missing from UITranslations.json");
            return tcOwnLeashTexts;
        }
    }
}
