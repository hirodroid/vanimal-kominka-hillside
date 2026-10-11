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
    }
}
