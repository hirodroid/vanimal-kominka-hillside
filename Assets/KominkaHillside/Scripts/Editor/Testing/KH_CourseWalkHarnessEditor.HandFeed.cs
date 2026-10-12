using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using KD_TreatBox = KominkaDogRun.KD_TreatBox;

namespace KominkaHillside.Editor
{
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/97e - Walk Harness Hand Feed (HF1 only, fresh profile)")]
        public static void HandFeedHarness() { BeginCommerce(CmSetHandFeed); }

        static Action hfRestore;
        static int hfDog;
        static bool hfHigh, hfAway;

        static void HfRestoreTracking()
        {
            if (hfRestore != null) { hfRestore(); hfRestore = null; }
            EditorApplication.playModeStateChanged -= HfPlayState;
        }
        static void HfPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) HfRestoreTracking();
        }

        static Transform HfMouth() { return Get<Transform>(Get<VRC.Udon.UdonBehaviour>(wander[hfDog], "fetchController"), "mouthAnchor"); }

        static void HfMockTracking()
        {
            // Input boundary only. The actual Udon attachment/offer/claim/receipt state is never injected.
            var original = VRCPlayerApi._GetTrackingData;
            if (original == null) throw new InvalidOperationException("ClientSim tracking not ready");
            VRCPlayerApi._GetTrackingData = (player, type) =>
            {
                if (player.isLocal && type == VRCPlayerApi.TrackingDataType.RightHand)
                {
                    Vector3 food = HfMouth().position + (hfAway ? Vector3.up * 2f : hfHigh ? Vector3.up * .65f : Vector3.forward * .05f);
                    return new VRCPlayerApi.TrackingData(food - Vector3.forward * .06f, Quaternion.identity);
                }
                return original(player, type);
            };
            hfRestore = () => VRCPlayerApi._GetTrackingData = original;
            EditorApplication.playModeStateChanged += HfPlayState;
        }

        static bool HfVisible(int slot)
        {
            return Get<GameObject[]>(CmBody(slot), "meshNodes").Any(g => g != null && g.activeSelf);
        }

        static void MakeHandFeedCases()
        {
            cmReport.notCovered.Add("Single Owner ClientSim, automatic hand attachment only. Real Pickup.IsHeld/Drop, VR left/right, network order, late join and Owner transfer NOT TESTED.");
            cmReport.notCovered.Add("No visual judgement of chewing or gaze; no complete floor/leash regression. These remain PCVR/device and later targeted cases.");
            var s = CmCase("HF1-high-hand-to-mouth-and-recovery", true, 80);
            int slot = -1, gen = 0, stock = 0;
            float highAt = 0, eatAt = 0;
            Vector3 receivedAt = Vector3.zero;
            s.Add(CmUntil(CmLoaded, 20, () => "ledger not loaded"));
            s.Add(CmUntil(() => Get<bool>(cmCatalog, "_resolved") && CmSeeded(CmBiscuit), 5, () => "catalog not ready"));
            s.Add(CmDo(() =>
            {
                CmFreezeStay(true); CmToGate(); TcPlaceInFront(0, 1.2f);
                hfDog = 0; hfHigh = true; hfAway = false; HfMockTracking();
                CmNote("SIMULATED: dog 0 initially placed on gate NavMesh; local RightHand tracking supplied at mouth +0.65 m, then mouth +0.05 m. No injection into treat/claim/eat state.");
                CmMenu("_Tab0"); stock = CmStockE(CmBiscuit);
                pouch.SendCustomEvent("_Row" + CmTreatRow(CmBiscuit));
            }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispOut, 5, () => "dispense state " + CmState() + " reason " + CmReason()));
            s.Add(CmDo(() => { slot = Get<int>(pouch, "_slotIndex"); gen = CmOutMs(slot); }));
            s.Add(CmUntil(() => CmClaimer(slot, gen) >= 0, 12, () => "no eligible dog claimed the offer"));
            s.Add(CmDo(() => { hfDog = CmClaimer(slot, gen); highAt = Time.time; }));
            s.Add(CmUntil(() =>
            {
                CmExpect(Get<int[]>(cmBox, "dogEatMs")[hfDog] == 0, "high hand must not start eating");
                CmExpect(HfVisible(slot), "high offer still visible");
                return Time.time - highAt >= 1.2f;
            }, 5, () => "high offer observation incomplete"));
            s.Add(CmDo(() =>
            {
                CmExpect(Get<bool>(CmBody(slot), "_attaching") && Get<bool>(cmSlots[slot], "handOffered"), "automatic attachment publishes hand offer");
                CmExpect(CmStockE(CmBiscuit) == stock + 1, "dispense consumes once");
                hfHigh = false;
            }));
            s.Add(CmUntil(() => Get<int[]>(cmBox, "dogEatMs")[hfDog] != 0, 10,
                () => "no bite; mouth gap " + Vector3.Distance(HfMouth().position, CmBody(slot).transform.position)));
            s.Add(CmDo(() =>
            {
                eatAt = Time.time; receivedAt = Get<Vector3[]>(cmBox, "dogBitePos")[hfDog];
                CmExpect(Get<bool[]>(cmBox, "dogHandFed")[hfDog], "bite marked as hand feeding");
                hfAway = true;
            }));
            s.Add(CmUntil(() => !HfVisible(slot) && !Get<bool>(CmBody(slot), "_attaching"), 1, () => "received treat did not disappear/detach"));
            s.Add(CmDo(() =>
            {
                CmExpect(!CmBody(slot).GetComponent<VRC_Pickup>().pickupable, "received treat not pickupable");
                CmExpect(CmBody(slot).GetComponent<Rigidbody>().isKinematic, "received body kinematic");
            }));
            s.Add(CmUntil(() =>
            {
                CmExpect(!HfVisible(slot), "received treat never reappears");
                CmExpect(!Get<bool>(wander[hfDog], "_eatPose"), "hand feeding never uses floor pose");
                if (CmGiver(slot) < 0) return true;
                CmExpect(Get<Vector3[]>(cmBox, "dogBitePos")[hfDog] == receivedAt, "bite point does not follow the moved hand");
                return false;
            }, 6, () => "slot not recovered"));
            s.Add(CmDo(() =>
            {
                float elapsed = Time.time - eatAt;
                CmExpect(elapsed >= 2.3f && elapsed < 4f, "eat duration and recovery " + elapsed.ToString("F3"));
                CmExpect(CmStockE(CmBiscuit) == stock + 1, "no second inventory consume");
                HfRestoreTracking();
                CmNote("dog " + hfDog + ", slot " + slot + ": high hand refused for 1.2 s, then accepted without Use/drop; receipt hidden, detached, kinematic; recovery " + elapsed.ToString("F3") + " game s, one inventory consume");
            }));
            s.Add(CmUntil(() => CmBodyHome(slot) && CmState() == KD_TreatBox.DispIdle, 5, () => "not home/Idle"));
        }

        // ================================================================ IMP-DOG-042 Course/97f (HF2 only)
        // Run like 97e: bash scripts/kh-harness-run.sh walk "Course/97f - Walk Harness Called Hand Feed (HF2 only, fresh profile)" --reason .. --consult ..
        [MenuItem("Tools/KominkaHillside/Course/97f - Walk Harness Called Hand Feed (HF2 only, fresh profile)")]
        public static void CalledHandFeedHarness() { BeginCommerce(CmSetCalledHandFeed); }

        const int Hf2Dog = 0, Hf2Pose = 2;   // the called dog and its pose (おすわり: a base pose, no purchase)
        static VRC.Udon.UdonBehaviour hf2Seat;   // that dog's KD_SleepSlot; the call made here is released by HfReleaseCall (also CmRecover)

        static VRC.Udon.UdonBehaviour HfSeat(int dog)
        {
            foreach (var u in UnityEngine.Object.FindObjectsOfType<VRC.Udon.UdonBehaviour>())
                if (u.programSource != null && u.programSource.name == "KD_SleepSlot" && Get<int>(u, "dogIndex") == dog) return u;
            throw new InvalidOperationException("KD_SleepSlot of dog " + dog + " missing");
        }

        static void HfReleaseCall()
        {
            if (hf2Seat != null && Get<int>(hf2Seat, "sleeperId") == CmMe()) hf2Seat.SendCustomEvent("_Release");
        }

        /// <summary>The called dog stays put in the same call and pose (root within 3 cm / 2 degrees, same caller / call / pose, frozen).</summary>
        static void HfExpectInPlace(Vector3 at, Quaternion rot, int calledMs, string when)
        {
            Vector3 moved = roots[Hf2Dog].position - at; moved.y = 0f;
            CmExpect(moved.magnitude < .03f && Quaternion.Angle(roots[Hf2Dog].rotation, rot) < 2f,
                when + ": the called dog stays on its spot (moved " + moved.magnitude.ToString("F3") + " m, turned " + Quaternion.Angle(roots[Hf2Dog].rotation, rot).ToString("F1") + " deg)");
            CmExpect(Get<int>(hf2Seat, "sleeperId") == CmMe() && Get<int>(hf2Seat, "calledMs") == calledMs && Get<int>(hf2Seat, "poseId") == Hf2Pose,
                when + ": same call and pose (sleeper " + Get<int>(hf2Seat, "sleeperId") + ", pose " + Get<int>(hf2Seat, "poseId") + ")");
            CmExpect(Get<bool>(wander[Hf2Dog], "_sleepFrozen"), when + ": still frozen at its spot");
            CmExpect(!Get<bool>(wander[Hf2Dog], "_eatPose") && !Get<bool>(wander[Hf2Dog], "_look"), when + ": no floor eat pose / LookAt");
        }

        static void MakeCalledHandFeedCases()
        {
            cmReport.notCovered.Add("Another player's hand at the called dog's mouth: not simulated (the local ClientSim player owns every slot, so a puppet's offer cannot be 'held'). Covered by Menu/6e (KD_TreatBox.CalledFeedAllowWith) and B86.");
            cmReport.notCovered.Add("Single Owner ClientSim, automatic hand attachment, one pose (おすわり) on the fan spot. ふせ / lying poses / placed spot / release, pose change, leave or Owner change mid-eat: Menu/6e and PCVR / 2-person rows (A0-63..65, B86..87).");
            cmReport.notCovered.Add("No visual judgement of the gaze, the tail or the pose; chin rest / paw yield (KD_CallPose) is a VR hand path not driven here.");
            var s = CmCase("HF2-called-dog-stays-and-eats", true, 150);
            int slot = -1, gen = 0, stock = 0, bites = 0, calledMs = 0, firstClaimer = -1;
            float highAt = 0, eatAt = 0;
            Vector3 at = Vector3.zero; Quaternion rot = Quaternion.identity;
            s.Add(CmUntil(CmLoaded, 20, () => "ledger not loaded"));
            s.Add(CmUntil(() => Get<bool>(cmCatalog, "_resolved") && CmSeeded(CmBiscuit), 5, () => "catalog not ready"));
            s.Add(CmDo(() =>
            {
                CmFreezeStay(true); CmToGate();
                var me = Networking.LocalPlayer;
                Vector3 fwd = me.GetRotation() * Vector3.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                // the other dogs well behind me: one of them may claim the treat when it comes out and run over (the claim must move
                // to the called dog at its mouth); 14 m+ keeps them from reaching the hand during the 1.5 s of the high hand
                for (int d = 0; d < 6; d++)
                {
                    if (d == Hf2Dog) continue;
                    var p = CmGround(me.GetPosition() - fwd * (14f + d * .8f) + side * (d - 3) * 1.2f, 6f);
                    roots[d].SetPositionAndRotation(p, Quaternion.LookRotation(fwd));
                    var sync = roots[d].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                }
                TcPlaceInFront(Hf2Dog, 1.4f);
                hfDog = Hf2Dog; hfHigh = true; hfAway = false;
                hf2Seat = HfSeat(Hf2Dog);
                // the product path of a call (as Water.cs W4-call): KD_SleepSlot._TryCall with a base pose; I am the slot Owner
                hf2Seat.SetProgramVariable("requestedPose", Hf2Pose);
                hf2Seat.SendCustomEvent("_TryCall");
                CmNote("SIMULATED: dog " + Hf2Dog + " placed 1.4 m in front, the other five 14-18 m behind me (initial placement only); call through KD_SleepSlot._TryCall pose " + Hf2Pose + "; local RightHand tracking at the mouth +0.65 m, then +0.05 m, then +2 m. No injection into the call / treat / claim / eat state.");
            }));
            s.Add(CmUntil(() => Get<int>(hf2Seat, "sleeperId") == CmMe(), 5, () => "call not committed (sleeper " + Get<int>(hf2Seat, "sleeperId") + ")"));
            s.Add(CmUntil(() => Get<bool>(wander[Hf2Dog], "_sleepFrozen") && Get<bool>(wander[Hf2Dog], "_wantSleep"), 25,
                () => "the called dog did not arrive and pose (frozen " + Get<bool>(wander[Hf2Dog], "_sleepFrozen") + ")"));
            s.Add(CmWait(1.0));   // the pose settles; the spot is final from here (frozen)
            s.Add(CmDo(() =>
            {
                at = roots[Hf2Dog].position; rot = roots[Hf2Dog].rotation; calledMs = Get<int>(hf2Seat, "calledMs");
                bites = Get<int>(wander[Hf2Dog], "_ctBites");
                HfMockTracking();
                CmMenu("_Tab0"); stock = CmStockE(CmBiscuit);
                pouch.SendCustomEvent("_Row" + CmTreatRow(CmBiscuit));
            }));
            s.Add(CmUntil(() => CmState() == KD_TreatBox.DispOut, 5, () => "dispense state " + CmState() + " reason " + CmReason()));
            s.Add(CmDo(() => { slot = Get<int>(pouch, "_slotIndex"); gen = CmOutMs(slot); highAt = Time.time; }));
            // far (0.65 m above the mouth): the called dog never claims, never eats, never moves
            s.Add(CmUntil(() =>
            {
                HfExpectInPlace(at, rot, calledMs, "high hand");
                int k = CmClaimer(slot, gen);
                CmExpect(k != Hf2Dog, "the called dog claimed a treat 0.65 m above its mouth");
                CmExpect(Get<int[]>(cmBox, "dogEatMs")[Hf2Dog] == 0, "high hand must not start eating");
                if (k >= 0) firstClaimer = k;
                return Time.time - highAt >= 1.5f;
            }, 5, () => "high offer observation incomplete"));
            s.Add(CmDo(() => { hfHigh = false; }));
            s.Add(CmUntil(() =>
            {
                HfExpectInPlace(at, rot, calledMs, "offer at the mouth");
                return Get<int[]>(cmBox, "dogEatMs")[Hf2Dog] != 0;
            }, 6, () => "no bite at the mouth (a free dog that reached the food first and ate it = placement flake, not 042); first claimer "
                + firstClaimer + ", claimer " + CmClaimer(slot, gen) + ", eatMs " + string.Join(",", Get<int[]>(cmBox, "dogEatMs")) + ", mouth gap " + Vector3.Distance(HfMouth().position, CmBody(slot).transform.position).ToString("F3")));
            s.Add(CmDo(() =>
            {
                eatAt = Time.time;
                CmExpect(CmClaimer(slot, gen) == Hf2Dog && Get<bool[]>(cmBox, "dogHandFed")[Hf2Dog], "the called dog holds the hand-fed receipt");
                CmExpect(CmStockE(CmBiscuit) == stock + 1, "dispense consumes once");
                hfAway = true;
                CmNote(firstClaimer >= 0 ? "free dog " + firstClaimer + " had claimed the treat while the hand was high; the claim moved to the called dog at its mouth"
                    : "no other dog claimed the treat while the hand was high");
            }));
            s.Add(CmUntil(() => !HfVisible(slot) && !Get<bool>(CmBody(slot), "_attaching"), 1, () => "received treat did not disappear/detach"));
            s.Add(CmUntil(() =>
            {
                HfExpectInPlace(at, rot, calledMs, "eating");
                CmExpect(!HfVisible(slot), "received treat never reappears");
                return CmGiver(slot) < 0;
            }, 6, () => "slot not recovered"));
            s.Add(CmDo(() =>
            {
                float elapsed = Time.time - eatAt;
                CmExpect(elapsed >= 2.3f && elapsed < 4f, "eat duration and recovery " + elapsed.ToString("F3"));
                CmExpect(CmStockE(CmBiscuit) == stock + 1, "no second inventory consume");
                CmExpect(Get<int>(wander[Hf2Dog], "_ctBites") == bites + 1, "the called-dog branch saw exactly one bite (" + (Get<int>(wander[Hf2Dog], "_ctBites") - bites) + ")");
                CmNote("recovery " + elapsed.ToString("F3") + " game s after the bite, one inventory consume");
            }));
            // back to the same wait and pose: the thank-you look ends, the beat ends, still called, frozen, posed, unmoved
            s.Add(CmUntil(() =>
            {
                HfExpectInPlace(at, rot, calledMs, "thanks");
                return !Get<bool>(wander[Hf2Dog], "_ctActive") && !Get<bool>(wander[Hf2Dog], "_treatBusy");
            }, 6, () => "the treat beat did not end"));
            s.Add(CmDo(() => { highAt = Time.time; }));
            s.Add(CmUntil(() =>
            {
                HfExpectInPlace(at, rot, calledMs, "after the treat");
                CmExpect(Get<bool>(wander[Hf2Dog], "_wantSleep"), "the pose is held (CallReady / CallPose) after the treat");
                return Time.time - highAt >= 1.5f;
            }, 4, () => "after-treat observation incomplete"));
            s.Add(CmDo(() =>
            {
                HfRestoreTracking();
                CmNote("dog " + Hf2Dog + " called in pose " + Hf2Pose + ": high hand ignored 1.5 s, bite at the mouth, slot recovered, back to the same call / pose / spot for 1.5 s; then released");
                HfReleaseCall();
            }));
            s.Add(CmUntil(() => Get<int>(hf2Seat, "sleeperId") < 0 && CmBodyHome(slot) && CmState() == KD_TreatBox.DispIdle, 5, () => "call not released / not home / not Idle"));
        }
    }
}
