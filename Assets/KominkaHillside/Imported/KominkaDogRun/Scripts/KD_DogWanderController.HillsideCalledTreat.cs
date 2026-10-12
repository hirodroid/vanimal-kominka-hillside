using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // IMP-DOG-042 (2026-10-12): a called dog, arrived and holding its pose, eats a treat its caller offers at its mouth without leaving
    // its spot. Plan: docs/plans/2026-10-03-treat-call.md "2026-10-12 IMP-DOG-042".
    //
    //   Who gets the slot: KD_TreatBox (master), CalledFeedAllowWith - only the caller's hand-held slot within HandFeedReach of this
    //   dog's mouth (kept up to CalledFeedKeep); never a far / high / ground / other player's treat; that slot is taken away from a dog
    //   that has not started eating. The eat start, bite point, receipt, recovery and the one inventory consume are IMP-DOG-041's.
    //   Here (every client, synced state only): UpdateSleep keeps the eat edges every decision of a called dog (CalledTreatEdges) and,
    //   once the dog is frozen in its pose, UpdateCalledTreat reports the bite (only the master writes) and shows the beat: the head
    //   looks at the food / the bite point and the tail wags, then the thank-you look at the caller's head. The root never moves or
    //   turns (no Coast-turn, FaceTowards, _look or _eatPose): the pose (Sleep / CallReady / CallPose, written in LateUpdate) stays as
    //   it was, so "back to the same pose" is the held gaze ending (HeldGazeEnd) and the head easing back.
    //   Lying poses (ごろん 0 / うずくまる 3 / へそ天 4 = IsAsleep, eyes closed) and the stretch take no treat (CalledFeedReady); a treat
    //   already being eaten when the pose changes finishes in the synced state and nothing is shown (the pet controller shows no gaze on
    //   a sleeping dog). KD_CallPose drops the chin rest / paw while CalledTreatActive (the hand at the mouth is not a chin rest).
    //
    // Sync: nothing new. The call is KD_SleepSlot's synced state; the claim / eat start / hand-fed / bite point are KD_TreatBox's.
    // Exits: released mid-eat -> UpdateSleep returns false and UpdateTreat's 041 hold / thanks finish it on the same _treatEating edge
    // (TreatEatEdge); a pose change -> the beat goes on in the data; the caller leaves -> KD_TreatSlot returns the slot and the thanks
    // drops (player invalid); an Owner change -> derived state only, nothing to carry over.
    public partial class KD_DogWanderController
    {
        private bool _ctActive;   // the last decision of this called dog ran the treat beat (claim mine / eating / thanks)
        private int _ctBites;     // harness read-out: eat starts seen while called (the TreatEatEdge rising edge in CalledTreatEdges)

        /// <summary>IMP-DOG-042: called, arrived and holding an awake pose (ふせ 1 / おすわり 2), not stretching. KD_TreatBox's assignment
        /// (master) reads it: the only state in which a called dog may be given a treat (and then only at its mouth, CalledFeedAllowWith).</summary>
        public bool CalledFeedReady // net: callable-ok: read-only
        {
            get
            {
                if (!IsCompanionReady) return false;
                int pose = CompanionPose;
                if (pose != 1 && pose != 2) return false;
                if (!PlacedFeedSettled()) return false;   // review 2026-10-12: not while still lifting / turning onto a placed spot
                return !IsStretching;
            }
        }

        /// <summary>KD_SleepSlot.Watch の場所指定の到着と同じ 3D 0.08 m (Watch はリテラル .08f)。</summary>
        const float PlacedFeedSettle = 0.08f;

        /// <summary>IMP-DOG-042 (review 2026-10-12, Codex 中): on a placed spot (場所を決めて呼ぶ) IsCompanionReady is raised before the
        /// lift (UpdatePlacedSleep sets _wantSleep, then the Owner moves the root up toward the knee / chest), so a dog still rising
        /// or turning must not take a treat. Settled = within PlacedFeedSettle of the synced spot in 3D (Watch's arrival value), facing
        /// the synced yaw within SleepFaceToleranceDeg, and on the Owner the lift itself finished (_placedSettling, exact). Non-Owners use
        /// the synced root (VRCObjectSync) with the same tolerance. The fan spot (no placed spot): arrival is the freeze, always settled.</summary>
        bool PlacedFeedSettled()
        {
            if (!_placedSleepActive) return true;
            KD_SleepSlot slot = sleepBoard == null ? null : sleepBoard.SlotOf(dogIndex);
            if (slot == null || !slot.HasSpot || dogRoot == null) return false;   // between a placed call and its release: not settled
            if (_dogOwner && _placedSettling) return false;
            if ((slot.Spot - dogRoot.position).sqrMagnitude > PlacedFeedSettle * PlacedFeedSettle) return false;
            Vector3 face = Quaternion.Euler(0f, slot.SpotYaw, 0f) * Vector3.forward;
            return Vector3.Angle(dogRoot.forward, face) <= SleepFaceToleranceDeg;
        }

        /// <summary>IMP-DOG-042: the called-dog treat beat ran at the last decision. KD_CallPose holds the chin rest / paw meanwhile.</summary>
        public bool CalledTreatActive { get { return _sleepCalled && _ctActive; } } // net: callable-ok: read-only

        /// <summary>Every decision of a called dog (UpdateSleep, walking there included): keep the eat edges fresh so that a release
        /// hands UpdateTreat a consistent _treatEating / _thanksUntil. A thank-you look owed to someone else ends (the call wins).</summary>
        void CalledTreatEdges(int sleeper)
        {
            _ctActive = false;
            if (treatBox == null || dogIndex < 0) return;
            bool was = _treatEating;
            TreatEatEdge();
            if (!was && _treatEating) _ctBites++;
            if (Time.time < _thanksUntil && _thanksPlayerId != sleeper) _thanksUntil = -1f;
        }

        /// <summary>Frozen in the pose (both the fan and the placed spot): the treat beat, in UpdateTreat's order (thanks, the hand-fed
        /// hold, the offer at the mouth). Ground food and a treat out of reach are not followed: nothing moves.</summary>
        void UpdateCalledTreat(int sleeper)
        {
            if (treatBox == null || dogIndex < 0) return;
            bool awake = CalledFeedReady;
            if (Time.time < _thanksUntil)
            {
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(_thanksPlayerId);
                if (!Utilities.IsValid(p)) { _thanksUntil = -1f; return; }
                _treatBusy = true; _ctActive = true;
                if (awake) CalledTreatGaze(p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position, 1f);
                return;
            }
            KD_TreatSlot slot = treatBox.LiveSlotOf(dogIndex);
            if (slot == null || slot.GiverId() != sleeper) return;
            _treatBusy = true; _ctActive = true;
            // 041's eat beat (and the hold until the slot is returned): look at the synced bite point, wag. No floor Pickup pose
            if (treatBox.HandFedOf(dogIndex))
            {
                if (awake) CalledTreatGaze(treatBox.BitePositionOf(dogIndex), 1f);
                return;
            }
            // dropped / put on the ground / lying pose: never walk; the master takes the claim away (CalledFeedAllowWith)
            if (!awake || !slot.BodyHeld()) return;
            Vector3 food = slot.BodyPosition();
            Transform mouth = fetchController != null ? fetchController.mouthAnchor : null;
            bool reach = mouth != null && treatBox.HandFeedReachWith(mouth.position, food);
            if (reach && !_treatEating) treatBox._ReportArrived(dogIndex);   // only the master writes the eat start
            CalledTreatGaze(food, reach ? 1f : 0.8f);
        }

        /// <summary>The gaze + tail, held until the next decision (the frozen dog's legs frames send nothing; HeldGazeEnd clears it on the
        /// first decision that does not renew it).</summary>
        void CalledTreatGaze(Vector3 worldPos, float urge)
        {
            if (petController == null) return;
            _standGaze = true;
            petController._SetGazeFor(worldPos, 0f, LeashGazeHold);
            petController._SetUrgeFor(urge, LeashGazeHold);
        }
    }
}
