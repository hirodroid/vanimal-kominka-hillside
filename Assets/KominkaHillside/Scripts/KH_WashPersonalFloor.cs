using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-ENV-011 (docs/plans/2026-10-01-wash-call-and-height.md §3-4): my own invisible floor at a wash station. Each
    // client moves its own copy only, so it lifts only the local player (remote players are drawn where their own client
    // puts them). Nothing is synced and there is no network event: the only entry points are the local _Adjust / _Up / _Down.
    //
    // The floor is a frame of 4 boxes (5 x 5 m around the duckboard, a hole over the duckboard and the shelf, so the dog,
    // the tools and the board stay outside it), 0.1 m thick, on a layer the players collide with but the dogs' ground rays
    // (0|11), the frisbee and the wash rays (2) do not see (the builder picks it; Wash/12 checks the dogs' masks).
    // 0 = off (the colliders are disabled). 0.1 m steps up to maxHeight, moved at speed m/s. 8 m away -> back to 0.
    //
    // Lifting the player (coordinator's decision 2026-10-01): the floor rises slowly under the player (the controller may
    // ride it by itself); no TeleportTo while it moves. When an upward move ends at its target (repeated presses only move
    // the target: one move, one end), a local player inside the floor's area who stood on it when the move began (feet
    // within catchBelow of that top) and is now more than sinkTolerance below the top is put on the top ONCE with TeleportTo
    // (horizontal position and facing kept, lerpOnRemote). Going down: never (gravity). Outside the area: nothing (walk on).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WashPersonalFloor : UdonSharpBehaviour
    {
        [Tooltip("床の箱 4 つの親。ローカルの y = baseY + 高さ (上面)")]
        public Transform floor;
        public Collider[] floorColliders;
        [Tooltip("高さ 0 の上面 (この Transform のローカル y。すのこの下の地面)")]
        public float baseY;
        [Tooltip("床の外形 (ローカル xz、中心からの半分)")]
        public Vector2 outerHalf = new Vector2(2.5f, 2.5f);
        [Tooltip("穴 (すのこと棚。ローカル xz の最小・最大)")]
        public Vector2 holeMin = new Vector2(-0.75f, -0.45f);
        public Vector2 holeMax = new Vector2(0.75f, 1.5f);
        public float maxHeight = 1.2f;
        public float step = 0.1f;
        [Tooltip("動かす速さ (m/s)")]
        public float speed = 0.5f;
        [Tooltip("洗い場 (この Transform) からこの距離 (m、水平) より離れたら 0 へ戻す")]
        public float leaveMeters = 8f;
        [Tooltip("上げ終えた時、上面よりこれ以上低ければ 1 回だけ上面へ (m)")]
        public float sinkTolerance = 0.06f;
        [Tooltip("上げ始めた時の上面からこれ以内の下にいた時だけ (床に乗っていた人) (m)")]
        public float catchBelow = 0.35f;

        private int _steps;
        private float _height;
        private bool _on;
        private bool _moving;
        private float _moveFromHeight;
        private float _moveStartTop;
        private int _rescues;          // harness read-out: TeleportTo at the end of a rise

        void Start() { Apply(); SetColliders(false); }

        /// <summary>Local (the board's 自分 ▲▼): ±steps of `step` m, 0..maxHeight.</summary>
        public void _Adjust(int steps)
        {
            int max = Mathf.RoundToInt(maxHeight / Mathf.Max(0.01f, step));
            _steps = Mathf.Clamp(_steps + steps, 0, max);
        }

        public void _Up() { _Adjust(1); }
        public void _Down() { _Adjust(-1); }

        /// <summary>The height the floor is moving to (m).</summary>
        public float _TargetHeight() { return _steps * step; }
        /// <summary>The height now (m).</summary>
        public float _Height() { return _height; }

        void Update()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            Vector3 feet = lp.GetPosition();
            if (_steps > 0)
            {
                Vector3 d = feet - transform.position; d.y = 0f;
                if (d.magnitude > leaveMeters) _steps = 0;
            }
            float target = _steps * step;
            if (!_on && _height <= 0f && target <= 0f) return;   // idle: nothing to do

            bool arrived = false;
            if (_height != target)
            {
                if (!_moving) { _moving = true; _moveFromHeight = _height; _moveStartTop = transform.position.y + baseY + _height; }
                _height = Mathf.MoveTowards(_height, target, speed * Time.deltaTime);
                Apply();
                if (_height == target) { _moving = false; arrived = true; }
            }
            bool want = _height > 0.0005f;
            if (want != _on) SetColliders(want);
            if (!arrived || !_on || !(_height > _moveFromHeight)) return;   // only the end of an upward move

            // ---- once, at the end of the rise: a player who stood on it and was left below goes on top ----
            float top = transform.position.y + baseY + _height;
            Vector3 l = transform.InverseTransformPoint(feet);
            bool over = Mathf.Abs(l.x) <= outerHalf.x && Mathf.Abs(l.z) <= outerHalf.y
                && !(l.x > holeMin.x && l.x < holeMax.x && l.z > holeMin.y && l.z < holeMax.y);
            if (over && feet.y < top - sinkTolerance && feet.y > _moveStartTop - catchBelow)
            {
                lp.TeleportTo(new Vector3(feet.x, top + 0.02f, feet.z), lp.GetRotation(), VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint, true);
                _rescues++;
            }
        }

        void Apply()
        {
            if (floor == null) return;
            Vector3 p = floor.localPosition;
            p.y = baseY + _height;
            floor.localPosition = p;
        }

        void SetColliders(bool on)
        {
            _on = on;
            if (floorColliders == null) return;
            for (int i = 0; i < floorColliders.Length; i++) if (floorColliders[i] != null) floorColliders[i].enabled = on;
        }
    }
}
