using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using KD_TreatBox = KominkaDogRun.KD_TreatBox;
using KD_TreatPouch = KominkaDogRun.KD_TreatPouch;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-014 おやつの固定手順 — Course/97 (PlayerData は空から = 骨型 5 個・0 pt で始まる)。
    ///
    ///   TR0-setup            復元済み・骨型 5 / 他 0・枠 3 つとも空・手順 Idle。滞在の加算を止める (模擬。残高を固定して購入の差を正確にする)
    ///   TR1-buy-short        0 pt で [買う] → 確認カードに [購入] が出ない (不足)。押しても (強制) 払わず在庫も増えない・「買えません」
    ///   TR2-keeper-grant     おじいさんに話しかける (復元済みの通常経路) → 200 pt・贈り物の挨拶 (以降の購入の元手)
    ///   TR3-buy-cancel       [買う] → [やめる] → 「購入をやめました」・払わない
    ///   TR4-buy-and-cap      [買う] → [購入] で 骨型 +5 / 1 pt (×3 で 20)。20 で [買う] → [購入] が出ない (上限)、押しても払わない。ジャーキー 5 pt で +5。ファイルに保存
    ///   TR5-gate-no-stock    在庫 0 の肉球クッキーを [出す] → 理由「おやつがありません」(NoStock)・何も減らない
    ///   TR6-give-any-dog     一番近い犬の前で 骨型を [出す] → 手に吸着・在庫 -1 (ファイル)。もう一度 [出す] → 「もう出している」。
    ///                        あげる (_onPickupUseDown → _onPickupUseUp) → 犬が枠を掴み・食べ始め・約 2.6 s で食べ終わって枠が空き・お礼 (出した人を見る)。在庫は二重に減らない
    ///   TR7-put-away         ジャーキーを [出す] → [しまう] → 枠が空き実体が定位置へ戻る。在庫は戻らない
    ///   TR8-slots-full       (模擬 T-009) 人形 3 人が枠 3 つを予約中 → [出す] は「いま出せません」(SlotsFull)・在庫そのまま。1 枠空く → 出せる → しまう
    ///   TR9-all-dogs-busy    (T-009 後半) 6 匹ともリードにつなぐ → [出す] は DogsBusy・在庫そのまま
    ///   TR10-dog0..5         犬 d 以外の 5 匹をつないで (= d だけが受けられる) 骨型をあげる → d が食べてお礼。6 匹すべて
    ///   TR11-saved           在庫・Spent がファイルに保存されている (再入室の復元は Course/96b が台帳として見る)
    /// ハーネス対象外 (PCVR / 実機): 4 人が本当に同時に出す枠の取り合い (実通信・Owner 移譲・ReserveSettle)、本物の手の掴み・VR の Use の手の振り分け、
    /// 食べ・お礼のアニメと FX の見え方、途中参加。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/97 - Walk Harness Treats (TR0-TR11, fresh profile)")]
        public static void TreatHarness() { BeginCommerce(CmSetTreat); }

        class CmGive { public int dog = -1, slot = -1, outMs, stockE0, stockE1, eater = -1; public double eatAt = -1, freeAt = -1; public string product; }

        static void MakeTreatCases()
        {
            cmReport.notCovered.Add("T-009 with 4 real clients pressing at once (network order, Owner transfer, ReserveSettle between clients) -> PCVR; TR8 only makes the 3 slots look taken by puppets");
            cmReport.notCovered.Add("Real hand: grabbing the treat (VRC_Pickup) and the VR Use routed to the holding hand (InputUse). TR6 sends the treat's own Udon events _onPickupUseDown / _onPickupUseUp -> PCVR");
            cmReport.notCovered.Add("Eat / thanks animation and FX position per breed (visual) -> PCVR");
            cmReport.notCovered.Add("Late joiner seeing a treat already out; the giver leaving -> PCVR");
            cmReport.notCovered.Add("Stock restore on re-entry: Course/96b checks StockB/StockE through the ledger snapshot of Course/96");
            int biscuit = -1, paw = -1, jerky = -1;

            // ---- TR0 ----
            var s = CmCase("TR0-setup", true, 60);
            s.Add(CmUntil(CmLoaded, 20, () => "ledger not restored (_loaded)"));
            s.Add(CmUntil(() => Get<bool>(cmCatalog, "_resolved") && CmSeeded(CmBiscuit), 5, () => "catalog not resolved / initial bone biscuits not seeded"));
            s.Add(CmDo(() =>
            {
                biscuit = CmTreatRow(CmBiscuit); paw = CmTreatRow(CmPawCookie); jerky = CmTreatRow(CmJerky);
                CmExpect(CmStock(CmBiscuit) == 5 && CmStock(CmPawCookie) == 0 && CmStock(CmJerky) == 0, "stock 5 / 0 / 0");
                CmExpect(CmSpent() == 0 && CmPoints() == 0, "0 pt (" + CmPoints() + ")");
                for (int i = 0; i < 3; i++) CmExpect(CmGiver(i) < 0, "slot " + i + " free");
                CmExpect(CmState() == KD_TreatBox.DispIdle, "dispense state Idle");
                CmFreezeStay(true);
                CmToGate();
                CmNote("fresh profile: biscuit 5, others 0, 0 pt, 3 free slots; rows biscuit=" + biscuit + " paw=" + paw + " jerky=" + jerky);
                CmNote("simulated: stay accrual frozen for the whole run (ledger heap minuteSeconds=1e6) so every balance check is exact");
            }));

            // ---- TR1 ----
            s = CmCase("TR1-buy-short", false, 30);
            s.Add(CmDo(() =>
            {
                CmMenu("_Tab0");
                int b = CmStockB(CmBiscuit);
                pouch.SendCustomEvent("_TreatBuy" + biscuit);
                CmExpect(Get<int>(pouch, "_treatConfirmRow") == biscuit && CmConfirmKind() == KD_TreatPouch.ConfirmTreat, "[買う] opens the treat card (kind " + CmConfirmKind() + ")");
                CmExpect(!CmConfirmYesShown(), "0 pt: the card hides [購入]");
                pouch.SendCustomEvent("_ConfirmYes");   // 隠れているボタンを強制で押す (台帳が最後に弾くこと)
                CmExpect(CmSpent() == 0 && CmStockB(CmBiscuit) == b && CmFeedback() == "KD_ShopShelf.WordCantBuy" && CmConfirmKind() == 0, "forced [購入] pays nothing, adds nothing, says 'cannot buy', closes the card ('" + CmFeedback() + "')");
                CmNote("0 pt: card without [購入]; a forced press -> no spend / no stock, 'cannot buy'");
            }));

            // ---- TR2 ----
            s = CmCase("TR2-keeper-grant", false, 30);
            s.Add(CmDo(CmToKeeper));
            s.Add(CmWait(1));
            s.Add(CmDo(() => cmKeeper.Interact()));
            s.Add(CmUntil(() => CmEarned() == 200, 3, () => "keeper 200 pt (earned " + CmEarned() + ")"));
            s.Add(CmDo(() =>
            {
                CmExpect(CmStr(cmKeeper, "_topicBody") == "KD_ShopKeeper.WordGreetingGift", "gift greeting while the board is open (" + CmStr(cmKeeper, "_topicBody") + ")");
                cmKeeper.SendCustomEvent("_Close");
                CmToGate();
                CmNote("keeper (ledger already restored = the normal path): +200 once, gift greeting");
            }));

            // ---- TR3 ----
            s = CmCase("TR3-buy-cancel", false, 30);
            s.Add(CmDo(() =>
            {
                CmMenu("_Tab0");
                int sp = CmSpent(), b = CmStockB(CmBiscuit);
                pouch.SendCustomEvent("_TreatBuy" + biscuit);
                CmExpect(CmConfirmKind() == KD_TreatPouch.ConfirmTreat && CmConfirmYesShown(), "card with [購入] at 200 pt");
                pouch.SendCustomEvent("_ConfirmNo");
                CmExpect(CmSpent() == sp && CmStockB(CmBiscuit) == b && CmFeedback() == "KD_TreatPouch.WordTreatBuyCancelled" && CmConfirmKind() == 0, "[やめる]: no spend, 'purchase cancelled' ('" + CmFeedback() + "')");
                CmNote("[買う] -> [やめる]: nothing paid, 'cancelled'");
            }));

            // ---- TR4 ----
            s = CmCase("TR4-buy-and-cap", false, 40);
            s.Add(CmDo(() =>
            {
                CmMenu("_Tab0");
                for (int k = 0; k < 3; k++)
                {
                    int sp = CmSpent(), b = CmStockB(CmBiscuit);
                    pouch.SendCustomEvent("_TreatBuy" + biscuit);
                    CmExpect(CmConfirmYesShown(), "pack " + (k + 1) + ": [購入] shown");
                    pouch.SendCustomEvent("_ConfirmYes");
                    CmExpect(CmStockB(CmBiscuit) == b + 5 && CmSpent() == sp + 1 && CmFeedback() == "KD_ShopShelf.WordBought", "pack " + (k + 1) + ": +5 for 1 pt ('" + CmFeedback() + "')");
                }
                CmExpect(CmStock(CmBiscuit) == 20, "biscuit 20 (" + CmStock(CmBiscuit) + ")");
                int sp2 = CmSpent();
                pouch.SendCustomEvent("_TreatBuy" + biscuit);
                CmExpect(CmConfirmKind() == KD_TreatPouch.ConfirmTreat && !CmConfirmYesShown(), "at 20 the card hides [購入] (cap)");
                pouch.SendCustomEvent("_ConfirmYes");
                CmExpect(CmSpent() == sp2 && CmStock(CmBiscuit) == 20 && CmFeedback() == "KD_ShopShelf.WordEnough", "forced [購入] at the cap: no spend, 'enough' ('" + CmFeedback() + "')");
                int sp3 = CmSpent();
                pouch.SendCustomEvent("_TreatBuy" + jerky);
                pouch.SendCustomEvent("_ConfirmYes");
                CmExpect(CmStock(CmJerky) == 5 && CmSpent() == sp3 + 5, "jerky pack: 5 for 5 pt");
            }));
            s.Add(CmUntil(() =>
            {
                var f = CmFile();
                return CmFileTotalsMatch() && CmFileInt(f, CmKey("StockB", CmBiscuit)) == 20 && CmFileInt(f, CmKey("StockB", CmJerky)) == 5;
            }, 3, () => "purchases not saved: " + CmFileSummary()));
            s.Add(CmDo(() => { CmNote("3 biscuit packs (1 pt each) -> 20, card at the cap hides [購入], forced press refused; jerky +5 for 5 pt; StockB and Spent " + CmSpent() + " saved"); pouch.SendCustomEvent("_Close"); }));

            // ---- TR5 ----
            s = CmCase("TR5-gate-no-stock", false, 20);
            s.Add(CmDo(() =>
            {
                int e = CmStockE(CmPawCookie);
                pouch.SendCustomEvent("_Row" + paw);
                CmExpect(CmReason() == KD_TreatBox.GateNoStock && CmState() == KD_TreatBox.DispIdle && CmStockE(CmPawCookie) == e, "paw cookie (stock 0): reason NoStock (" + CmReason() + "), nothing taken");
                CmNote("stock 0 -> reason NoStock, Idle");
            }));

            // ---- TR6 ----
            s = CmCase("TR6-give-any-dog", false, 90);
            var g6 = new CmGive { product = CmBiscuit };
            s.Add(CmDo(() => CmNearDog(CmNearestDog())));
            s.Add(CmWait(1));
            CmGiveSteps(s, g6, () => biscuit, false);

            // ---- TR7 ----
            s = CmCase("TR7-put-away", false, 30);
            int e7 = 0, slot7 = -1;
            s.Add(CmDo(() => { e7 = CmStockE(CmJerky); pouch.SendCustomEvent("_Row" + jerky); }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispOut, 4, () => "jerky not out (state " + CmState() + ", reason " + CmReason() + ")"));
            s.Add(CmDo(() =>
            {
                slot7 = Get<int>(pouch, "_slotIndex");
                CmExpect(CmStockE(CmJerky) == e7 + 1 && CmStr(cmSlots[slot7], "product") == CmJerky, "jerky out in slot " + slot7 + ", one consumed");
                pouch.SendCustomEvent("_PutAway");
            }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispIdle && CmGiver(slot7) < 0, 4, () => "put away did not free the slot"));
            s.Add(CmUntil(() => CmBodyHome(slot7), 4, () => "treat body not back at its home (" + CmBodyHomeGap(slot7).ToString("F2") + " m)"));
            s.Add(CmDo(() =>
            {
                CmExpect(CmStockE(CmJerky) == e7 + 1, "[しまう] does not refund (world-ui-spec treat.putaway)");
                CmNote("jerky out -> [しまう]: slot free, body home, stock stays -1 (no refund)");
            }));

            // ---- TR8 (模擬 T-009) ----
            s = CmCase("TR8-slots-full", true, 40);
            int e8 = 0;
            s.Add(CmDo(() => CmSpawnPuppets(3)));
            s.Add(CmUntil(() => CmCollectPuppets(3), 5, () => "3 puppets did not join"));
            s.Add(CmDo(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    CmExpect(CmGiver(i) < 0, "slot " + i + " free before the simulation");
                    cmSlots[i].SetProgramVariable("giverId", cmPuppets[i].playerId);
                    cmSlots[i].SetProgramVariable("product", CmBiscuit);
                    cmSlots[i].SetProgramVariable("outMs", 0);
                }
                e8 = CmStockE(CmBiscuit);
                pouch.SendCustomEvent("_Row" + biscuit);
                CmExpect(CmReason() == KD_TreatBox.GateSlotsFull && CmState() == KD_TreatBox.DispIdle && CmStockE(CmBiscuit) == e8, "3 slots taken: reason SlotsFull (" + CmReason() + "), nothing taken");
                cmSlots[0].SendCustomEvent("_Release");
                CmExpect(CmGiver(0) < 0, "slot 0 freed");
            }));
            // 解放した枠は実体が定位置へ戻るまで Returning (空きに数えない)。戻ってから押す
            s.Add(CmUntil(() => CmGiver(0) < 0 && !Get<bool>(cmSlots[0], "_returning"), 4, () => "slot 0 still returning (" + CmBodyHomeGap(0).ToString("F2") + " m from home)"));
            s.Add(CmDo(() => pouch.SendCustomEvent("_Row" + biscuit)));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispOut, 4, () => "not out after a slot freed (state " + CmState() + ", reason " + CmReason() + ")"));
            s.Add(CmDo(() =>
            {
                CmExpect(Get<int>(pouch, "_slotIndex") == 0 && CmStockE(CmBiscuit) == e8 + 1, "out in the freed slot 0, one consumed");
                pouch.SendCustomEvent("_PutAway");
            }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispIdle, 4, () => "put away"));
            s.Add(CmDo(() =>
            {
                CmReleasePuppetSlots(); CmRemovePuppets();
                CmNote("simulated: slot heaps giverId = 3 puppets (Reserved, outMs 0) -> [出す] refused SlotsFull with no stock loss; slot 0 _Release -> [出す] works; put away; slots released and puppets removed");
            }));
            s.Add(CmUntil(() => Enumerable.Range(0, 3).All(i => CmGiver(i) < 0), 3, () => "slots not free after the simulation"));

            // ---- TR9 ----
            s = CmCase("TR9-all-dogs-busy", false, 60);
            int e9 = 0;
            s.Add(CmDo(CmToGate));
            s.Add(CmWait(1));
            for (int d = 0; d < 6; d++)
            {
                int dog = d;
                s.Add(CmDo(() => CmAttach(dog, dog < 3 ? 0 : 1)));
                s.Add(CmWait(0.5));
            }
            s.Add(CmUntil(() => Enumerable.Range(0, 6).All(CmMine), 6, () => "not all 6 dogs leashed (walkers " + string.Join(",", Get<int[]>(manager, "walkers")) + ")"));
            s.Add(CmWait(0.5));
            s.Add(CmDo(() =>
            {
                e9 = CmStockE(CmBiscuit);
                pouch.SendCustomEvent("_Row" + biscuit);
                CmExpect(CmReason() == KD_TreatBox.GateDogsBusy && CmState() == KD_TreatBox.DispIdle && CmStockE(CmBiscuit) == e9, "all dogs leashed: reason DogsBusy (" + CmReason() + "), nothing taken");
                CmDetachAll();
            }));
            s.Add(CmUntil(CmNoneMine, 6, () => "detach all"));
            s.Add(CmDo(() => CmNote("6 dogs on the leash -> [出す] refused DogsBusy (leash), no stock loss; all detached")));

            // ---- TR10 ----
            for (int d = 0; d < 6; d++)
            {
                int dog = d;
                s = CmCase("TR10-dog" + dog, false, 90);
                var g = new CmGive { dog = dog, product = CmBiscuit };
                s.Add(CmDo(CmDetachAll));
                s.Add(CmUntil(CmNoneMine, 6, () => "detach before the round"));
                s.Add(CmDo(() => CmNearDog(dog)));
                s.Add(CmWait(0.5));
                int onHand = 0;
                for (int o = 0; o < 6; o++)
                {
                    if (o == dog) continue;
                    int other = o, h = onHand < 3 ? 0 : 1; onHand++;
                    s.Add(CmDo(() => CmAttach(other, h)));
                    s.Add(CmWait(0.5));
                }
                s.Add(CmUntil(() => Enumerable.Range(0, 6).All(i => i == dog ? !CmMine(i) : CmMine(i)), 6, () => "the 5 other dogs not leashed"));
                s.Add(CmDo(() => CmNote("the other 5 dogs placed at the feet and leashed through the menu (placement just before attach = the walk harnesses' only injection); leashed dogs are not eligible (HillsideDogBusy), so dog " + dog + " is the only candidate")));
                s.Add(CmWait(1));
                CmGiveSteps(s, g, () => biscuit, true);
                s.Add(CmDo(CmDetachAll));
                s.Add(CmUntil(CmNoneMine, 6, () => "detach after the round"));
            }

            // ---- TR11 ----
            s = CmCase("TR11-saved", false, 20);
            s.Add(CmWait(1));
            s.Add(CmDo(() =>
            {
                var f = CmFile();
                CmExpect(CmFileTotalsMatch(), "Earned / Spent saved: " + CmFileSummary());
                foreach (var k in new[] { CmBiscuit, CmJerky })
                    CmExpect(CmFileInt(f, CmKey("StockB", k)) == CmStockB(k) && CmFileIntOr0(f, CmKey("StockE", k)) == CmStockE(k), k + " StockB/StockE saved (" + CmStockB(k) + "/" + CmStockE(k) + ")");
                CmNote("file = heap: spent " + CmSpent() + ", biscuit " + CmStockB(CmBiscuit) + "/" + CmStockE(CmBiscuit) + ", jerky " + CmStockB(CmJerky) + "/" + CmStockE(CmJerky));
                CmFreezeStay(false);
            }));
        }

        /// <summary>出す → 吸着・在庫 -1 → (もう一度押して AlreadyOut) → あげる → 犬が掴む → 食べ始め → 食べ終わり (枠が空く) → お礼 → Idle。
        /// <paramref name="mustBeDog"/> なら g.dog 以外の犬が掴んだら失敗。</summary>
        static void CmGiveSteps(List<Func<double, bool>> s, CmGive g, Func<int> row, bool mustBeDog)
        {
            s.Add(CmDo(() => { g.stockE0 = CmStockE(g.product); pouch.SendCustomEvent("_Row" + row()); }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispOut, 4, () => "not out (state " + CmState() + ", reason " + CmReason() + ")"));
            s.Add(CmDo(() =>
            {
                g.slot = Get<int>(pouch, "_slotIndex"); g.outMs = CmOutMs(g.slot); g.stockE1 = CmStockE(g.product);
                CmExpect(g.stockE1 == g.stockE0 + 1, "one consumed at dispense");
                CmExpect(CmGiver(g.slot) == CmMe() && g.outMs != 0, "slot " + g.slot + " Out, giver = me");
                CmExpect(!CmPanelOpen(), "the menu closed on success");
                var body = CmBody(g.slot);
                CmExpect(Get<bool>(body, "_attaching"), "the treat is attached to the hand (ClientSim right-hand tracking)");
                float d = Vector3.Distance(body.transform.position, Networking.LocalPlayer.GetPosition());
                CmExpect(d < 2f, "the treat is at the hand (" + d.ToString("F2") + " m from the player)");
                pouch.SendCustomEvent("_Row" + row());
                CmExpect(CmReason() == KD_TreatBox.GateAlreadyOut && CmStockE(g.product) == g.stockE1, "second [出す]: AlreadyOut (" + CmReason() + "), nothing taken");
            }));
            s.Add(CmUntil(() => CmFileInt(CmFile(), CmKey("StockE", g.product)) == g.stockE1, 3, () => "StockE not saved at dispense"));
            s.Add(CmWait(0.3));   // 板を閉じたフレームの入力を捨てる (TreatInputBlocked) より後
            s.Add(CmDo(() => CmBody(g.slot).SendCustomEvent("_onPickupUseDown")));
            s.Add(CmWait(0.1));
            s.Add(CmDo(() =>
            {
                var body = CmBody(g.slot);
                body.SendCustomEvent("_onPickupUseUp");
                CmExpect(!Get<bool>(body, "_attaching"), "released from the hand (use down -> up = give)");
            }));
            s.Add(CmUntil(() => (g.eater = CmClaimer(g.slot, g.outMs)) >= 0, 15, () => "no dog claimed the treat (dogSlot " + string.Join(",", Get<int[]>(cmBox, "dogSlot")) + ")"));
            s.Add(CmDo(() => { if (mustBeDog) CmExpect(g.eater == g.dog, "dog " + g.dog + " (the only free dog) claimed it, got dog " + g.eater); }));
            s.Add(CmUntil(() => Get<int[]>(cmBox, "dogEatMs")[g.eater] != 0, 40, () => "dog " + g.eater + " did not start eating (dist " + Vector3.Distance(roots[g.eater].position, CmBody(g.slot).transform.position).ToString("F2") + " m)"));
            s.Add(CmDo(() => g.eatAt = EditorApplication.timeSinceStartup));
            s.Add(CmUntil(() => CmGiver(g.slot) < 0, 8, () => "slot not freed after eating"));
            s.Add(CmDo(() => g.freeAt = EditorApplication.timeSinceStartup));
            s.Add(CmUntil(() => Get<float>(wander[g.eater], "_thanksUntil") > Time.time && Get<int>(wander[g.eater], "_thanksPlayerId") == CmMe(), 2, () => "dog " + g.eater + " did not thank the giver"));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispIdle, 3, () => "dispense state not back to Idle"));
            s.Add(CmDo(() =>
            {
                double eat = g.freeAt - g.eatAt; float want = Get<float>(cmBox, "eatDuration");
                CmExpect(CmStockE(g.product) == g.stockE1, "eating took nothing more (no double consume)");
                CmExpect(eat >= want - 0.2 && eat <= want + 1.2, "eat time " + eat.ToString("F2") + " s (eatDuration " + want + ")");
                CmNote("dog " + g.eater + (g.dog >= 0 ? " (target " + g.dog + ")" : " (nearest)") + ": out in slot " + g.slot + ", given, ate " + eat.ToString("F2") + " s, thanked me; stock -1 once");
            }));
        }

        /// <summary>枠 (slot, 世代 outMs) を掴んでいる犬 (-1 = いない)。KD_TreatBox.DogOfSlot と同じ判定を同期配列から。</summary>
        static int CmClaimer(int slot, int outMs)
        {
            var ds = Get<int[]>(cmBox, "dogSlot"); var dm = Get<int[]>(cmBox, "dogSlotOutMs");
            for (int d = 0; d < ds.Length; d++) if (ds[d] == slot && dm[d] == outMs) return d;
            return -1;
        }

        static int CmNearestDog()
        {
            var p = Networking.LocalPlayer.GetPosition(); int best = 0; float bd = float.MaxValue;
            for (int d = 0; d < 6; d++) { float x = TvFlat(roots[d].position, p); if (x < bd) { bd = x; best = d; } }
            return best;
        }

        /// <summary>犬 d の 1.6 m 手前 (NavMesh の上) に、犬の方を向いて立つ。</summary>
        static void CmNearDog(int d)
        {
            var dogPos = roots[d].position; var away = Networking.LocalPlayer.GetPosition() - dogPos; away.y = 0;
            if (away.sqrMagnitude < 0.01f) away = roots[d].forward;
            var p = CmGround(dogPos + away.normalized * 1.6f);
            CmTeleport(p, dogPos - p);
        }

        static float CmBodyHomeGap(int slot)
        {
            var body = CmBody(slot); object o;
            var parking = body.TryGetProgramVariable("parking", out o) ? o as Transform : null;
            if (parking == null) throw new InvalidOperationException("slot " + slot + " body has no parking (KD_TreatHome) wired");
            return Vector3.Distance(body.transform.position, parking.position);
        }
        static bool CmBodyHome(int slot) { return CmBodyHomeGap(slot) <= Mathf.Max(Get<float>(CmBody(slot), "homeRadius"), 0.25f); }
    }
}
