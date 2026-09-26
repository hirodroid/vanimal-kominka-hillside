using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaHillside
{
    // "Call the ball to me", ported from KominkaDogRun KD_TreatPouch.Toy (IMP-FET-011): one ball at a time,
    // never taken from a hand or from a dog that is chasing/carrying it. No bulk reset (game-design §4).
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
        public const int KindBall = 0, KindDisc = 1;
        public LayerMask groundMask = (1 << 0) | (1 << 11);
        public float recallDistance = 1.2f;
        public float recallCooldown = 2f;
        public const int Ok = 0, Held = 1, DogBusy = 2, Cooldown = 3, Unwired = 4, Pending = 5;
        private float[] _recallAt;
        private float[] _discRecallAt;
        private int _pendingRow = -1;
        private int _pendingKind = KindBall;
        private int _lastKind = KindBall;
        private float _requestAt;
        private float _writtenAt = -1f;
        private int _lastResult = -1;   // 0 placed, 1 failed (busy), 2 timeout; -1 none
        private int _lastRow = -1;
        // IMP-DOG-019 (2026-09-26): one toy per dog at a time. Local pickupable veto, derived from synced state.
        const float ExclusiveInterval = 0.25f;
        private float _nextExclusiveAt;
        private VRC_Pickup[] _ballPickups;
        private VRC_Pickup[] _discPickups;
        private bool[] _ballVetoed;
        private bool[] _discVetoed;

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
                if (discs[row].IsHeldNow) return Held;
                if (discCatchers[row].IsEngaged) return DogBusy;
                if (BallInUse(row)) return DogBusy;   // IMP-DOG-019: the same dog's ball is out (menu text = "dog is playing")
                if (_discRecallAt != null && Time.time - _discRecallAt[row] < recallCooldown) return Cooldown;
                return Ok;
            }
            if (balls == null || row < 0 || row >= balls.Length || balls[row] == null || fetchers == null || row >= fetchers.Length || fetchers[row] == null) return Unwired;
            if (_pendingRow >= 0) return Pending;
            if (balls[row].IsHeldNow) return Held;
            if (activities != null && activities.RaceBall == row) return DogBusy;
            if (!fetchers[row].IsIdle) return DogBusy;
            if (DiscInUse(row)) return DogBusy;   // IMP-DOG-019: the same dog's disc is out
            if (_recallAt != null && Time.time - _recallAt[row] < recallCooldown) return Cooldown;
            return Ok;
        }

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
                            || st == KH_FrisbeeCatch.StateChase || st == KH_FrisbeeCatch.StateRetrieve) { dp.pickupable = true; _discVetoed[i] = false; }
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
            _pendingRow = row; _pendingKind = kind; _requestAt = Time.time; _writtenAt = -1f; _lastRow = row; _lastKind = kind; _lastResult = -1;
            GameObject go = ToyObject(row, kind);
            if (!Networking.IsOwner(go)) Networking.SetOwner(lp, go); // net: setowner-ok: toy only; position waits for ownership
        }

        private void Update()
        {
            if (Time.time >= _nextExclusiveAt) { _nextExclusiveAt = Time.time + ExclusiveInterval; ApplyExclusive(); }
            if (_pendingRow < 0) return;
            int row = _pendingRow; int kind = _pendingKind;
            GameObject go = ToyObject(row, kind);
            bool owner = Networking.IsOwner(go);
            if (_writtenAt < 0f && owner)
            {
                _pendingRow = -1; // let the gate see no pending request
                if (_GateToy(row, kind) == Ok) { _pendingRow = row; Place(row, kind); }
                else { _lastRow = row; _lastResult = 1; }
            }
            else if (_writtenAt >= 0f && (!owner || Time.time - _writtenAt >= 1f)) { _pendingRow = -1; _lastRow = row; _lastResult = owner ? 0 : 2; }
            else if (Time.time - _requestAt >= 4f) { _pendingRow = -1; _lastRow = row; _lastResult = 2; }
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
