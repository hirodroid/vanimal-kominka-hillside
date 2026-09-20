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
                if (_discRecallAt != null && Time.time - _discRecallAt[row] < recallCooldown) return Cooldown;
                return Ok;
            }
            if (balls == null || row < 0 || row >= balls.Length || balls[row] == null || fetchers == null || row >= fetchers.Length || fetchers[row] == null) return Unwired;
            if (_pendingRow >= 0) return Pending;
            if (balls[row].IsHeldNow) return Held;
            if (activities != null && activities.RaceBall == row) return DogBusy;
            if (!fetchers[row].IsIdle) return DogBusy;
            if (_recallAt != null && Time.time - _recallAt[row] < recallCooldown) return Cooldown;
            return Ok;
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
