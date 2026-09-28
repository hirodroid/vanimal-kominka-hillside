using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using UnityEngine.AI;

namespace KominkaHillside
{
    // "Call the ball to me", ported from KominkaDogRun KD_TreatPouch.Toy (IMP-FET-011): one ball at a time,
    // never taken from a hand, from a dog chasing it on land (Fetch 1-3) or from a mouth (4 / 7 / 8, a Drop before the release);
    // IMP-DOG-021 (DES-DOG-004 E-4): a dog after it in the water (6) or a Drop that has let it go gives it up. No bulk reset (game-design §4).
    // The ball is placed in front of the head on the ground found by a downward ray, since the hillside slopes.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_ToyRecall : UdonSharpBehaviour
    {
        public KominkaDogRun.KD_BallController[] balls;
        public KominkaDogRun.KD_DogFetchController[] fetchers;
        public KominkaDogRun.KD_GroupActivities activities;
        // Second toy kind (IMP-DOG-005): one disc per dog, wired only where the dog can catch it (null = "not yet").
        public KH_Frisbee[] discs;
        public KH_FrisbeeCatch[] discCatchers;
        // IMP-DOG-019 (2026-09-26 night): one toy OUT per dog. The other one waits in the park under the plaza
        // (KH_Dogs/KH_ToyPark, Walking/3 Build). "Parked" is only a position, carried by VRCObjectSync to everyone
        // and to late joiners; no synced flag, no SetActive. Balls start in the park, discs on the plaza.
        public Transform[] parkBall;
        public Transform[] parkDisc;
        const float ParkedRadius = 1f;      // within this of its park spot = already parked (stage 2 skipped)
        const float StageTimeout = 4f;      // per stage (ownership wait)
        const float MovedTolerance = 0.1f;  // the other toy moved more than this since the ask = someone else is using it
        public const int KindBall = 0, KindDisc = 1;
        public LayerMask groundMask = (1 << 0) | (1 << 11);
        public float recallDistance = 1.2f;
        public float recallCooldown = 2f;
        public const int Ok = 0, Held = 1, DogBusy = 2, Cooldown = 3, Unwired = 4, Pending = 5;
        // IMP-DOG-020: the dog is wading; called again after it lands (DES-DOG-004 B-1). IMP-DOG-021 (E-4): only while the ball is
        // in its mouth in the water (Fetch 7 / 8); a ball the dog is not holding is called even when the dog is in the water.
        public const int InWater = 6;
        // IMP-DOG-022 (DES-DOG-005 stage 1) -> DES-DOG-005 F (IMP-DOG-023 M4, 2026-09-28): the disc is taken out only where the dog
        // could start its play at the local player's feet (KH_FrisbeeCatch._TakeoutOkAt: not in the river band / the water-play spot,
        // ground within reach below, no steeper than runMaxSlopeDeg). The plaza circle + discTakeoutMargin it replaces is gone.
        public const int OutOfArea = 7;
        // F: the menu asks the gate for all six rows on every redraw (KD_TreatPouch.Toy RedrawHillsideToy), so the feet are judged
        // at most once per TakeoutCacheSeconds and the answer is shared by the rows (<= 2 rays/s). The six catchers carry the same
        // river / spot / slope values (Course/92), so the row that happens to ask first decides for all.
        const float TakeoutCacheSeconds = 0.5f;
        private float _takeoutAt = -10f;
        private bool _takeoutOk = true;
        // Harness read-out (Course/94 F15-gate): the disc gate of row _hsGateRow without pressing the menu. Nothing calls it at runtime.
        private int _hsGateRow = -1;
        private int _hsGate = -99;
        private float[] _recallAt;
        private float[] _discRecallAt;
        private int _pendingRow = -1;
        private int _pendingKind = KindBall;
        private int _lastKind = KindBall;
        private float _requestAt;
        private float _writtenAt = -1f;
        private int _lastResult = -1;   // 0 placed, 1 failed (busy), 2 timeout; -1 none
        private int _lastRow = -1;
        // 0 idle, 1 = bring the asked toy to the player (the original machine), 2 = park the same dog's other toy.
        // _pendingRow stays set through both stages, so the gate answers Pending until the pair is settled.
        private int _stage;
        private float _parkAskedAt;
        private Vector3 _otherAtAsk;        // where the same dog's other toy was when the menu was pressed
        private bool _otherAtAskValid;
        // IMP-DOG-019 (2026-09-26): one toy per dog at a time. Local pickupable veto, derived from synced state.
        const float ExclusiveInterval = 0.25f;
        private float _nextExclusiveAt;
        private VRC_Pickup[] _ballPickups;
        private VRC_Pickup[] _discPickups;
        private bool[] _ballVetoed;
        private bool[] _discVetoed;
        // IMP-DOG-020 stage 1 (DES-DOG-004 C-7/8): a ball in the river band is brought back to the bank by its Owner.
        public KH_RiverData riverData;
        // IMP-DOG-020 step 6 (DES-DOG-004 C-8): a ball in the water-play spot floats instead of going back to the bank.
        public KH_WaterSpot waterSpot;
        private bool[] _floating;
        const float RiverCheckInterval = 0.25f; // the ball Owner looks this often
        const float RiverStrandSeconds = 10f;   // inside the band this long without sinking = stranded
        private float _nextRiverAt;
        private float[] _riverSince;
        private float[] _riverLastReturn;
        private int[] _riverRepeat;
        private float[] _riverSettleUntil;      // after a bank return: keep the ball asleep until this time
        const float RiverSettleSeconds = 1.25f; // 5 looks: the teleport and the first ground contact can wake it
        const int RiverBankTries = 6;           // bank candidates at +2, +3.5 ... +9.5 m beyond the band edge
        const float RiverSlopeAccept = -0.10f;  // RiverBankSpot: steepest accepted fall toward the water (normal · outward)
        const float RiverSlopeTie = 0.01f;
        const float RiverBankShiftZ = 3f;       // RiverBankSpot also tries the same distances 3 m up and down the river      // a farther candidate must beat the nearer one by this much
        private float _riverSpotScore;
        const float RiverRepeatWindow = 20f;    // returned again within this = it came back; third time = park

        public int _LastResult() { return _lastResult; }
        public int _LastRow() { return _lastRow; }
        public int _LastKind() { return _lastKind; }

        public int _Gate(int row) { return _GateToy(row, KindBall); }

        public int _GateToy(int row, int kind)
        {
            if (kind == KindDisc)
            {
                if (discs == null || row < 0 || row >= discs.Length || discs[row] == null || discCatchers == null || row >= discCatchers.Length || discCatchers[row] == null) return Unwired;
                if (_pendingRow >= 0) return Pending;
                if (!LocalTakeoutOk(discCatchers[row])) return OutOfArea;   // DES-DOG-005 F (M4): the player's feet, not the plaza circle
                if (discs[row].IsHeldNow) return Held;
                // DES-DOG-005 D-5 (M3): the disc in the mouth in the water (12 / 13) is refused with the water one-liner (the premise of
                // IMP-DOG-021:47 no longer holds). A dog swimming after it (11) does not block the call: the disc comes, the dog loses
                // its target (KH_FrisbeeCatch.Water: target-lost) and walks out. Everything else engaged is "the dog is playing".
                int cs = discCatchers[row].State;
                if (cs == KH_FrisbeeCatch.StateWaterCarry || cs == KH_FrisbeeCatch.StateWaterExit) return InWater;
                if (cs != KH_FrisbeeCatch.StateWaterChase && discCatchers[row].IsEngaged) return DogBusy;
                // IMP-DOG-021 (DES-DOG-004 E-4): a dog in the water no longer blocks its disc; a disc that is not engaged is not in
                // any mouth, so it is called as usual and the dog stays in the water.
                if (BallInUse(row)) return DogBusy;   // IMP-DOG-019: the same dog's ball is out (menu text = "dog is playing")
                if (_discRecallAt != null && Time.time - _discRecallAt[row] < recallCooldown) return Cooldown;
                return Ok;
            }
            if (balls == null || row < 0 || row >= balls.Length || balls[row] == null || fetchers == null || row >= fetchers.Length || fetchers[row] == null) return Unwired;
            if (_pendingRow >= 0) return Pending;
            if (balls[row].IsHeldNow) return Held;
            if (activities != null && activities.RaceBall == row) return DogBusy;
            if (!fetchers[row].IsIdle)
            {
                // IMP-DOG-021 (DES-DOG-004 E-4): only a ball in a mouth is refused. In the water (7 / 8) with the old one-liner
                // ("水遊び中 — ..."), elsewhere as before. A dog swimming / wading after it (6, not in the mouth yet) loses its target
                // and walks out (KD_DogFetchController.HillsideWater: target-lost). The land chase (1-3) keeps "dog is playing".
                KominkaDogRun.KD_DogFetchController f = fetchers[row];
                if (f._BallInMouth()) return f.IsWaterFetching || f.WaterPose != 0 ? InWater : DogBusy;
                // Codex 2026-09-28 中: a Drop that has let the ball go (5 after the release; IsCarrying minus the mouth) holds
                // nothing either: called as an idle dog's ball would be
                if (f.DebugState != KominkaDogRun.KD_DogFetchController.StateWaterChase && !f.IsCarrying) return DogBusy;
            }
            // E-4: an idle dog in the water (playing, not after this ball) no longer refuses: the ball comes, the dog stays in.
            if (DiscInUse(row)) return DogBusy;   // IMP-DOG-019: the same dog's disc is out
            if (_recallAt != null && Time.time - _recallAt[row] < recallCooldown) return Cooldown;
            return Ok;
        }

        // DES-DOG-005 F (M4): can the dog start its play at the local player's feet? One ray per TakeoutCacheSeconds, shared by the
        // six rows. No local player (Editor without ClientSim) counts as allowed. Local read only: nothing is synced, the menu just
        // refuses to call the disc.
        bool LocalTakeoutOk(KH_FrisbeeCatch c)
        {
            if (Time.time - _takeoutAt < TakeoutCacheSeconds) return _takeoutOk;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return true;
            _takeoutAt = Time.time;
            _takeoutOk = c._TakeoutOkAt(lp.GetPosition());
            return _takeoutOk;
        }

        // Harness only (Course/94 F15-gate): the disc gate of _hsGateRow into _hsGate. '_' = not callable over the network.
        public void _HsGateDisc() { _hsGate = _GateToy(_hsGateRow, KindDisc); }

        // ---- IMP-DOG-019: ball and disc of one dog are mutually exclusive ----
        // "In use" is derived on every client from state that is already synced (no new synced variable):
        //   ball: held || that dog's Fetch not Idle || it is the gathering's race ball
        //   disc: held || flying || catcher engaged
        // IsFlying is true on the thrower's client only. Elsewhere an accepted throw shows up through the catcher's
        // synced state a moment later; a throw the catcher refused (outside the play area, dog busy...) never does,
        // so there the ball stays usable during that flight. Accepted as is: the dog is not going for that disc.
        // Unwired rows count as "not in use" so a missing toy never locks the other one.
        bool BallInUse(int i)
        {
            if (balls == null || i < 0 || i >= balls.Length || balls[i] == null) return false;
            if (balls[i].IsHeldNow) return true;
            if (fetchers != null && i < fetchers.Length && fetchers[i] != null && !fetchers[i].IsIdle) return true;
            return activities != null && activities.RaceBall == i;
        }

        bool DiscInUse(int i)
        {
            if (discs == null || i < 0 || i >= discs.Length || discs[i] == null) return false;
            if (discs[i].IsHeldNow || discs[i].IsFlying) return true;
            return discCatchers != null && i < discCatchers.Length && discCatchers[i] != null && discCatchers[i].IsEngaged;
        }

        void CachePickups()
        {
            int nb = balls == null ? 0 : balls.Length, nd = discs == null ? 0 : discs.Length;
            _ballPickups = new VRC_Pickup[nb]; _ballVetoed = new bool[nb];
            _discPickups = new VRC_Pickup[nd]; _discVetoed = new bool[nd];
            for (int i = 0; i < nb; i++) if (balls[i] != null) _ballPickups[i] = (VRC_Pickup)balls[i].GetComponent(typeof(VRC_Pickup));
            for (int i = 0; i < nd; i++) if (discs[i] != null) _discPickups[i] = (VRC_Pickup)discs[i].GetComponent(typeof(VRC_Pickup));
        }

        // pickupable is a local property with an owner already: KD_DogFetchController.ApplyState (ball) and
        // KH_FrisbeeCatch.ApplyState (disc) write it on their state changes / deserializations. This veto only ever
        // writes false while the other toy is in use, re-asserting every tick (a write from those controllers can
        // reopen it for up to one tick). Once the other toy is free the record stays until that controller is back
        // at rest, and only then true is written (Codex review 2026-09-26: clearing it early left a ball closed after
        // a race rebound the Fetch to another ball). The ball's "Drop: the thrower may take it from the mouth" true
        // is not mirrored (no public Drop getter), so if a veto overlapped that Drop the ball opens at Idle instead.
        // A held toy is never touched (no Drop, no pickupable=false under a hand).
        void ApplyExclusive()
        {
            if (_ballPickups == null) CachePickups();
            int n = Mathf.Min(_ballPickups.Length, _discPickups.Length);
            for (int i = 0; i < n; i++)
            {
                bool ballUse = BallInUse(i), discUse = DiscInUse(i);
                VRC_Pickup bp = _ballPickups[i];
                if (bp != null)
                {
                    if (discUse && !balls[i].IsHeldNow) { if (bp.pickupable) bp.pickupable = false; _ballVetoed[i] = true; }
                    else if (_ballVetoed[i])
                    {
                        // KD_DogFetchController: Idle -> true (a race ball belongs to the racer's controller: wait)
                        if (fetchers != null && i < fetchers.Length && fetchers[i] != null && fetchers[i].IsIdle
                            && (activities == null || activities.RaceBall != i)) { bp.pickupable = true; _ballVetoed[i] = false; }
                    }
                }
                VRC_Pickup dp = _discPickups[i];
                if (dp != null)
                {
                    if (ballUse && !discs[i].IsHeldNow) { if (dp.pickupable) dp.pickupable = false; _discVetoed[i] = true; }
                    else if (_discVetoed[i])
                    {
                        KH_FrisbeeCatch c = (discCatchers != null && i < discCatchers.Length) ? discCatchers[i] : null;
                        // KH_FrisbeeCatch.ApplyState's rule, copied; no catcher (prototype) = nobody else writes it
                        int st = c == null ? KH_FrisbeeCatch.StateIdle : c.State;
                        if (st == KH_FrisbeeCatch.StateIdle || st == KH_FrisbeeCatch.StateReady || st == KH_FrisbeeCatch.StateDrop
                            || st == KH_FrisbeeCatch.StateChase || st == KH_FrisbeeCatch.StateRetrieve || st == KH_FrisbeeCatch.StateWaterChase) { dp.pickupable = true; _discVetoed[i] = false; }
                    }
                }
            }
        }

        GameObject ToyObject(int row, int kind) { return kind == KindDisc ? discs[row].gameObject : balls[row].gameObject; }

        // Rejections are not recorded here (the caller already knows the gate); results refer to the pending row only.
        public void _Recall(int row) { _RecallToy(row, KindBall); }

        public void _RecallToy(int row, int kind)
        {
            if (_GateToy(row, kind) != Ok) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (_recallAt == null) _recallAt = new float[balls.Length];
            if (_discRecallAt == null) _discRecallAt = new float[discs == null ? 6 : discs.Length];
            _pendingRow = row; _pendingKind = kind; _stage = 1; _requestAt = Time.time; _writtenAt = -1f; _lastRow = row; _lastKind = kind; _lastResult = -1;
            int otherKind = kind == KindDisc ? KindBall : KindDisc;
            _otherAtAskValid = ToyWired(row, otherKind);
            if (_otherAtAskValid) _otherAtAsk = ToyObject(row, otherKind).transform.position;
            GameObject go = ToyObject(row, kind);
            if (!Networking.IsOwner(go)) Networking.SetOwner(lp, go); // net: setowner-ok: toy only; position waits for ownership
        }

        private void Update()
        {
            if (Time.time >= _nextExclusiveAt) { _nextExclusiveAt = Time.time + ExclusiveInterval; ApplyExclusive(); }
            if (riverData != null && Time.time >= _nextRiverAt) { _nextRiverAt = Time.time + RiverCheckInterval; CheckRiverBalls(); }
            if (_pendingRow < 0) return;
            int row = _pendingRow; int kind = _pendingKind;
            if (_stage == 2) { StepPark(row, kind); return; }
            GameObject go = ToyObject(row, kind);
            bool owner = Networking.IsOwner(go);
            if (_writtenAt < 0f && owner)
            {
                _pendingRow = -1; // let the gate see no pending request
                if (_GateToy(row, kind) == Ok) { _pendingRow = row; Place(row, kind); }
                else { _lastRow = row; _lastResult = 1; _stage = 0; }
            }
            else if (_writtenAt >= 0f && (!owner || Time.time - _writtenAt >= 1f))
            {
                _lastRow = row; _lastResult = owner ? 0 : 2;
                if (owner) BeginPark(row, kind); else { _pendingRow = -1; _stage = 0; }
            }
            else if (Time.time - _requestAt >= StageTimeout) { _pendingRow = -1; _stage = 0; _lastRow = row; _lastResult = 2; }
        }

        // ---- IMP-DOG-019 stage 2: the other toy of the same dog goes to the park ----
        // Runs only after stage 1 placed the asked toy (_lastResult = 0 is already out, so nothing here can turn
        // "brought" into a failure). Gives up without taking anything when the other toy is in use (held, flying,
        // chased, race ball) at the start or again once ownership arrives, when its row/park spot is unwired, or
        // after StageTimeout without ownership. A give-up only means both toys stay visible; the exclusive veto still
        // keeps the pair to one in use.
        // "Moved since the ask" is the guard for what the in-use flags cannot see on this client (Codex review
        // 2026-09-26 night): a disc another player threw and the dog refused is IsFlying on the thrower only, and a
        // second player who brought the other toy out at the same moment is not Pending here. Both show up as the
        // other toy's synced position moving after our menu press, so stage 2 leaves it alone (both stay visible,
        // never zero out). A disc that already lay still when the menu was pressed is not in use and is parked.
        void BeginPark(int row, int kind)
        {
            int other = kind == KindDisc ? KindBall : KindDisc;
            Transform spot = ParkSpot(row, other);
            if (spot == null || !ToyWired(row, other) || OtherInUse(row, other) || OtherMoved(row, other)
                || InPark(row, other) || InPark(row, kind)) { EndPark(); return; }
            _stage = 2; _parkAskedAt = Time.time;
            GameObject go = ToyObject(row, other);
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { EndPark(); return; }
            if (!Networking.IsOwner(go)) Networking.SetOwner(lp, go); // net: setowner-ok: the same dog's other toy only, never while in use; position waits for ownership
        }

        void StepPark(int row, int kind)
        {
            int other = kind == KindDisc ? KindBall : KindDisc;
            GameObject go = ToyObject(row, other);
            if (Networking.IsOwner(go))
            {
                if (!OtherInUse(row, other) && !OtherMoved(row, other) && !InPark(row, kind)) Park(row, other);   // picked up / moved / ours parked by someone meanwhile: leave it
                EndPark();
            }
            else if (Time.time - _parkAskedAt >= StageTimeout) EndPark();
        }

        void EndPark() { _pendingRow = -1; _stage = 0; }

        bool ToyWired(int row, int kind)
        {
            if (kind == KindDisc) return discs != null && row >= 0 && row < discs.Length && discs[row] != null;
            return balls != null && row >= 0 && row < balls.Length && balls[row] != null;
        }

        // Near its own park spot. For the other toy: already parked, skip. For the asked toy: someone else parked it
        // (their stage 2 of the same dog, the opposite way) - then parking ours too would leave the dog with none.
        bool InPark(int row, int kind)
        {
            Transform spot = ParkSpot(row, kind);
            return spot != null && ToyWired(row, kind) && Vector3.Distance(ToyObject(row, kind).transform.position, spot.position) <= ParkedRadius;
        }

        bool OtherInUse(int row, int kind) { return kind == KindDisc ? DiscInUse(row) : BallInUse(row); }

        bool OtherMoved(int row, int kind)
        {
            if (!_otherAtAskValid) return true;
            return Vector3.Distance(ToyObject(row, kind).transform.position, _otherAtAsk) > MovedTolerance;
        }

        Transform ParkSpot(int row, int kind)
        {
            Transform[] a = kind == KindDisc ? parkDisc : parkBall;
            if (a == null || row < 0 || row >= a.Length) return null;
            return a[row];
        }

        // ---- IMP-DOG-020 stage 1: balls that fell into the river (DES-DOG-004 C-7 / C-8) ----
        // Only the ball's Owner acts (the thrower, since a Pickup moves ownership), so nothing new is synced: the move is a
        // teleport carried by VRCObjectSync. A ball in the band (centre ± (half width + 1 m)) goes back at once when it
        // has sunk (centre below the water surface; the surface has no collider, so a real ball always sinks), or after
        // RiverStrandSeconds when it lies in the band above the water. Destination: the bank on the Owner's side
        // (the NavMesh point 2..9.5 m beyond the band edge sloping least toward the water, RiverBankSpot); failing that, or on the
        // third return within 20 s, the park (IMP-DOG-019).
        // Parking never brings the same dog's disc out, so "one toy out per dog" still holds (at most one).
        // Held balls and balls in a dog's mouth (Return / Drop) are left alone. Stage 2 (floating balls) and the
        // water-play spot exception belong here later.
        void CheckRiverBalls()
        {
            if (balls == null) return;
            if (_riverSince == null || _riverSince.Length != balls.Length)
            {
                _riverSince = new float[balls.Length];
                _riverLastReturn = new float[balls.Length];
                _riverSettleUntil = new float[balls.Length];
                _floating = new bool[balls.Length];
                _riverRepeat = new int[balls.Length];
                for (int k = 0; k < _riverSince.Length; k++) { _riverSince[k] = -1f; _riverLastReturn[k] = -1000f; }
            }
            for (int i = 0; i < balls.Length; i++)
            {
                KominkaDogRun.KD_BallController b = balls[i];
                if (b == null) continue;
                GameObject go = b.gameObject;
                // In the mouth now (RC-09, 破壊監査 2026-09-28 FUNC-05 BUG-04): Drop after the release is not a hold, so the
                // physics below applies from the release on instead of waiting for Idle (IsCarrying covers the whole Drop)
                bool carried = fetchers != null && i < fetchers.Length && fetchers[i] != null && fetchers[i]._BallInMouth();
                // Held or carried = a new play: forget earlier returns, so only a ball that rolls back by itself counts
                // toward the park (Codex 2026-09-27 U-中: three quick throws into the river must not park the third)
                // (a throw starts that dog's Fetch, which also catches a grab-and-throw shorter than one 0.25 s look)
                bool fetching = fetchers != null && i < fetchers.Length && fetchers[i] != null && !fetchers[i].IsIdle;
                if (b.IsHeldNow || fetching) { _riverRepeat[i] = 0; _riverLastReturn[i] = -1000f; }
                if (b.IsHeldNow || carried) _riverSettleUntil[i] = 0f;   // a dog merely chasing does not touch it
                if (b.IsHeldNow && _floating[i] && Networking.IsOwner(go)) StopFloat(i, go);   // picked up: normal physics
                if (!Networking.IsOwner(go) || b.IsHeldNow || carried) { _riverSince[i] = -1f; continue; }
                // Codex review 2026-09-28 高 (RC-17 / RC-09): a dog that lost the ball while carrying gives up without touching its
                // physics (only the Owner may write them), so the ball's Owner - this client - unfreezes a kinematic ball that no
                // dog has in its mouth (a race ball is its winner's). Then the rules below float it or give its gravity back.
                if (UnfreezeIfFree(i, go)) continue;
                if (Time.time < _riverSettleUntil[i]) SettleBall(go);
                Vector3 p = go.transform.position;
                // In the spot: float (every new Owner does the same from the position, so an Owner change keeps it afloat, S4).
                // IMP-DOG-021 (E-2): anywhere along the river as well (KH_RiverData._FloatsAt: in the water, outside the bridge
                // zones, a usable bank within reach). The bridge zones and the stretches without a bank keep the bank return below.
                bool inSpot = waterSpot != null && waterSpot._FloatsAt(p);
                if (inSpot || riverData._FloatsAt(p)) { FloatBall(i, go, p, inSpot); _riverSince[i] = -1f; continue; }
                // Left the spot water (or released on land): gravity back. Judged from the physical state too, not only the local
                // history: a ball another Owner floated and a dog then carried out has _floating false here (Codex 2026-09-27 D-高)
                if (_floating[i] || GravityOff(go)) StopFloat(i, go);
                if (!riverData._InBand(p, KH_RiverData.BandMargin)) { _riverSince[i] = -1f; continue; }
                if (_riverSince[i] < 0f) _riverSince[i] = Time.time;
                bool sunk = p.y < riverData._SurfaceY(p.z);
                if (!sunk && Time.time - _riverSince[i] < RiverStrandSeconds) continue;
                _riverSince[i] = -1f;
                ReturnFromRiver(i, sunk);
            }
        }

        // Bank destination on the given side. The bank slopes down into the water, and a sphere rolls on any slope, so a
        // point just outside the band sends the ball straight back in (measured 2026-09-27 W5: returned 1 m outside the band,
        // rolled back, sank and was returned again every ~2 s). Candidates: NavMesh points 2 .. 9.5 m beyond the band edge in
        // 1.5 m steps, each also 3 m up and down the river (local search for a flatter spot). Score = ground normal (horizontal part = downhill) · the band's outward normal in x/z. The edge is
        // side * (x - centreX(z)) = halfWidth(z) + 1, so the outward normal is (side, -(side * dcx/dz + dhw/dz)); this also holds
        // where the river runs diagonally (Codex 2026-09-27 D-中).
        // Calibration (generating formula, z 140-160, 2026-09-27): on the bank slope (+2 / +3.5 m) the score is -0.15 .. -0.34
        // (9-20 deg toward the water); past the crest (+5 m and beyond) it is -0.022 .. +0.023 at 1.3-1.7 deg, i.e. only the
        // water's own fall along z. The first threshold (-0.005) rejected the crest on the -x side at z 140 and parked the ball
        // on its first return (run 20260927-041222). Accept score >= RiverSlopeAccept (-0.10, about 5.7 deg toward the water)
        // and take the best score, nearer first on ties. None accepted = y -10000 (no out parameter): the caller parks.
        // The chosen score is left in _riverSpotScore for the log.
        Vector3 RiverBankSpot(Vector3 from, float side)
        {
            Vector3 best = new Vector3(0f, -10000f, 0f);
            float bestScore = -2f;
            for (int c = 0; c < RiverBankTries * 3; c++)
            {
                int k = c / 3;
                float dz = (c % 3 == 0) ? 0f : (c % 3 == 1 ? -RiverBankShiftZ : RiverBankShiftZ);   // local search along the bank
                Vector3 bank = riverData._BankPoint(from + new Vector3(0f, 0f, dz), side, KH_RiverData.BandMargin + KH_RiverData.BankExtra + k * 1.5f);
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(bank, out hit, 2f, NavMesh.AllAreas)) continue;
                Vector3 p = hit.position;
                if (riverData._InBand(p, KH_RiverData.BandMargin)) continue;
                RaycastHit g;
                if (!Physics.Raycast(p + Vector3.up, Vector3.down, out g, 3f, groundMask, QueryTriggerInteraction.Ignore)) continue;
                float dcx = riverData._CenterX(p.z + 0.5f) - riverData._CenterX(p.z - 0.5f);
                float dhw = riverData._HalfWidth(p.z + 0.5f) - riverData._HalfWidth(p.z - 0.5f);
                Vector3 outward = new Vector3(side, 0f, -(side * dcx + dhw));
                outward = outward.normalized;
                float score = g.normal.x * outward.x + g.normal.z * outward.z;
                if (score < RiverSlopeAccept) continue;
                if (score > bestScore + RiverSlopeTie) { bestScore = score; best = g.point; }
            }
            _riverSpotScore = bestScore;
            return best;
        }

        void ReturnFromRiver(int i, bool sunk)
        {
            GameObject go = balls[i].gameObject;
            Vector3 from = go.transform.position;
            VRCPlayerApi lp = Networking.LocalPlayer;
            float centre = riverData._CenterX(from.z);
            float sideX = Utilities.IsValid(lp) ? lp.GetPosition().x : from.x;
            float side = sideX >= centre ? 1f : -1f;
            // A ball that keeps coming back (returned again within RiverRepeatWindow) goes to the park on the third time.
            if (Time.time - _riverLastReturn[i] < RiverRepeatWindow) _riverRepeat[i]++; else _riverRepeat[i] = 0;
            _riverLastReturn[i] = Time.time;
            Vector3 spot = _riverRepeat[i] >= 2 ? new Vector3(0f, -10000f, 0f) : RiverBankSpot(from, side);
            bool found = spot.y > -9999f;
            if (found)
            {
                VRCObjectSync sync = go.GetComponent<VRCObjectSync>();
                if (sync != null) sync.SetKinematic(false);
                float radius = go.transform.localScale.x * 0.5f;
                go.transform.position = spot + Vector3.up * (radius + 0.005f);   // resting on the ground: no drop to wake it
                if (sync != null) sync.FlagDiscontinuity();
                // A rigid sphere has no rolling resistance: on the crest's gentle fall (-0.03) it rolled back into the water
                // in about a minute (run 20260927-042802). Put it to sleep after the teleport; gravity alone does not wake a
                // sleeping body (contact, force or a pickup does). The teleport and the first contact can wake it, so the
                // settle pass keeps re-sleeping it for RiverSettleSeconds (cleared by a hold or a Fetch).
                SettleBall(go);
                _riverSettleUntil[i] = Time.time + RiverSettleSeconds;
            }
            else Park(i, KindBall);
            Debug.Log("[KH_ToyRecall] river-return ball=" + i + " reason=" + (sunk ? "sunk" : "stranded") + " to=" + (found ? "bank" : "park")
                + " side=" + (side > 0f ? "+x" : "-x") + " repeat=" + _riverRepeat[i] + " slope=" + (found ? _riverSpotScore.ToString("F3") : "none") + " from=" + RiverV3(from) + " at=" + RiverV3(go.transform.position));
        }

        void SettleBall(GameObject go)
        {
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) return;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
        }

        // Floating (C-8): gravity off (synced by VRCObjectSync) and a velocity that holds y = water - radius x floatSink and drifts
        // downstream (-z) at floatDrift until the region's downstream end. Not kinematic, so the idle Fetch's "release a kinematic
        // ball" safety net does not fight it, and a dog's carry (Fetch 7, kinematic) simply overrides it. A pickup restores gravity
        // (KD_BallController.OnPickup), a ball outside the spot gets it back here.
        // RC-09 (破壊監査 2026-09-28, FUNC-03 BUG-01 / FUNC-05 BUG-02 / FUNC-06 BUG-02): the gravity is reconciled with the physical
        // state on every look, not only when the local history says "not floating yet". A pickup shorter than one look (0.25 s)
        // turns gravity on without this client ever seeing the hold, and an Owner that floated the ball, lost it to someone
        // who picked it up, and got it back keeps _floating true: both left a floating ball with gravity on. Held balls and
        // balls in a mouth never reach here (CheckRiverBalls skips them), so a person's or a dog's physics is not overridden.
        // IMP-DOG-021 (E-2): outside the spot the drift follows the river's centre line (the lateral offset is kept, pulled back
        // inside half width - 0.3 m where the water narrows) and stops 1 m before a bridge zone / a stretch without a usable bank
        // (KH_RiverData._DriftStops), so a ball never drifts into the places where it would be sent to the bank. In the spot the
        // old rule stays: it drifts to the spot's downstream end and stops there (a ball left in the spot stays by "川辺").
        void FloatBall(int i, GameObject go, Vector3 p, bool inSpot)
        {
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) return;
            VRCObjectSync sync = go.GetComponent<VRCObjectSync>();
            if (!_floating[i]) { _floating[i] = true; rb.angularVelocity = Vector3.zero; }
            if (rb.useGravity && sync != null) sync.SetGravity(false);
            float radius = go.transform.localScale.x * 0.5f;
            float sink = waterSpot != null ? waterSpot.floatSink : KH_WaterSpot.DefaultFloatSink;
            float drift = waterSpot != null ? waterSpot.floatDrift : KH_WaterSpot.DefaultFloatDrift;
            float wantY = riverData._SurfaceY(p.z) - radius * sink;
            // DES-DOG-005 D-2 (M2): the drift rule moved to KH_RiverData._DriftVelocity unchanged (the disc floats with it too)
            Vector3 h = riverData._DriftVelocity(p, drift, inSpot, inSpot ? waterSpot._DownstreamZ() : 0f);
            rb.velocity = new Vector3(h.x, Mathf.Clamp((wantY - p.y) * 4f, -1f, 1f), h.z);
        }

        /// <summary>Owner, not held: true = the ball was frozen with no dog carrying it and has just been unfrozen (looked at again
        /// on the next pass, when the physics has settled).</summary>
        bool UnfreezeIfFree(int i, GameObject go)
        {
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null || !rb.isKinematic) return false;
            if (activities != null && activities.BallClaimed(i, -1)) return false;
            if (fetchers != null)
                for (int k = 0; k < fetchers.Length; k++)
                    if (fetchers[k] != null && fetchers[k]._Carries(go.transform)) return false;
            VRCObjectSync sync = go.GetComponent<VRCObjectSync>();
            if (sync == null) return false;
            sync.SetKinematic(false);
            Debug.Log("[KH_ToyRecall] unfrozen ball=" + i + " (kinematic, no dog carrying it)");
            return true;
        }

        bool GravityOff(GameObject go)
        {
            Rigidbody rb = go.GetComponent<Rigidbody>();
            return rb != null && !rb.isKinematic && !rb.useGravity;
        }

        void StopFloat(int i, GameObject go)
        {
            _floating[i] = false;
            VRCObjectSync sync = go.GetComponent<VRCObjectSync>();
            if (sync != null) sync.SetGravity(true);
        }

        string RiverV3(Vector3 v) { return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")"; }

        // Owner only (StepPark checked). A teleport: FlagDiscontinuity so other clients do not see it slide
        // through the ground (KD_TreatController._RespawnHome, measured 2026-08-24).
        void Park(int row, int kind)
        {
            GameObject go = ToyObject(row, kind);
            Transform spot = ParkSpot(row, kind);
            if (spot == null) return;
            if (kind == KindDisc) discs[row]._Recall();   // ends a flight / tells the dog, and unfreezes
            VRCObjectSync sync = go.GetComponent<VRCObjectSync>();
            if (sync != null) sync.SetKinematic(false);
            go.transform.position = spot.position;
            go.transform.rotation = Quaternion.identity;
            if (sync != null) sync.FlagDiscontinuity();
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        private void Place(int row, int kind)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            GameObject go = ToyObject(row, kind);
            // A disc may still be flying: end the flight and tell the dog (review D3) before moving it.
            if (kind == KindDisc) discs[row]._Recall();
            // Unfreeze before moving: a kinematic ball left by an interrupted Return would hang in the air.
            VRCObjectSync sync = go.GetComponent<VRCObjectSync>();
            if (sync != null) sync.SetKinematic(false);
            // IMP-DOG-021 (E-4): a ball called out of the water was floating with gravity off; it lands by the player at once
            // instead of hanging until the next river look (0.25 s)
            if (kind == KindBall)
            {
                if (sync != null) sync.SetGravity(true);
                if (_floating != null && row < _floating.Length) _floating[row] = false;
            }
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 fwd = head.rotation * Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) { fwd = lp.GetRotation() * Vector3.forward; fwd.y = 0f; }
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            float scale = Mathf.Clamp(lp.GetAvatarEyeHeightAsMeters() / 1.6f, 0.5f, 2f);
            Vector3 spot = lp.GetPosition() + fwd * (recallDistance * scale);
            float radius = go.transform.localScale.x * 0.5f;
            RaycastHit hit;
            float y = Physics.Raycast(spot + Vector3.up * 1.5f, Vector3.down, out hit, 4f, groundMask, QueryTriggerInteraction.Ignore) ? hit.point.y : lp.GetPosition().y;
            go.transform.position = new Vector3(spot.x, y + radius + 0.05f, spot.z);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            if (kind == KindDisc) _discRecallAt[row] = Time.time; else _recallAt[row] = Time.time;
            _writtenAt = Time.time;
        }
    }
}
