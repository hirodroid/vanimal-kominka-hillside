using KominkaDogRun;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-007 W2: 散歩中の写真のきっかけ (自分の端末だけ)。docs/plans/2026-10-03-photo-expansion.md §0・§6。
    ///
    /// - 自分のリードに犬がつながっている間だけ、0.5 s ごとの遅延イベント (_WalkTick) で見る。Update は持たない。
    ///   つなぎ外しは KH_LeashManager が _OnLeashChanged で知らせる (ローカルのイベント。同期は増えない)。犬が 0 匹になったら止まる
    /// - きっかけ (優先の順):
    ///   1. 立ち寄り: 自分の犬が立ち寄り場所のポーズに入った (KD_DogWanderController.PoseSpotIndex の立ち上がり) → 1 s 後もそのままなら犬目線
    ///      (その場所の注視点 lookTargets を見る)。だめなら自撮り風
    ///   2. 写真の場所 (spotPositions、半径 spotRadii) に入った → 自撮り風 (spotHasBackdrop なら背景 → 自分の延長線から)。同じ場所は spotRepeat 秒に 1 回
    ///   3. 時間と距離: 前の散歩の写真から timeInterval 秒 かつ 歩いた距離 distanceInterval m → 犬目線と自撮り風を交互に
    /// - 散歩の写真どうしは minGap 秒あける。撮らないとき: 移動の暗転中と明けて fadeGrace 秒 (5 m 以上の飛びも同じ扱い)、寝る場所を選んでいる間、
    ///   自分の犬が洗い場・水の中、撮影 OFF・ボード無し
    /// - 写真は KD_PhotoDirector._WalkShoot が撮る (散歩の枠 12 枚)。すべてローカル (同期変数 0)
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_PhotoWalkWatcher : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaHillside/Photo/1)")]
        public KD_PhotoDirector director;
        public KD_PhotoSettings settings;
        public KH_PhotoAlbum album;
        public KH_LeashManager manager;
        [Tooltip("KD_DogPack の名簿の順 (= dogIndex)")]
        public KD_DogWanderController[] wanderControllers;
        public KD_SleepPlacement sleepPlacement;

        [Header("写真の場所 (Photo/1 が名前の付いた場所から書く)")]
        public string[] spotNames;
        public Vector3[] spotPositions;
        public float[] spotRadii;
        public bool[] spotHasBackdrop;
        public Vector3[] spotBackdrops;

        [Header("試作値 (§6)")]
        public float tickSeconds = 0.5f;
        public float stopoverDelay = 1f;
        public float timeInterval = 90f;
        public float distanceInterval = 40f;
        public float minGap = 20f;
        public float fadeGrace = 2f;
        public float spotRepeat = 300f;
        public float jumpMeters = 5f;
        [Tooltip("写真の場所に入ったとみなす高さの差 (m)")]
        public float spotHeight = 6f;

        // ---- 状態 (ハーネスが読む・書く) ----
        private int _mask;                 // 自分の犬 (bit i)
        private bool _ticking;             // _WalkTick が予約されている
        private float _lastWalkShotAt = -1000f;
        private float _timeRefAt;          // 時間のきっかけの起点 (つないだとき・散歩の写真を撮ったとき)
        private float _walked;             // その起点から歩いた水平距離 (m)
        private Vector3 _prevPos;
        private bool _hasPrev;
        private bool _fadeBusy;
        private float _fadeUntil;
        private bool _timeToggle;          // false = 次の時間のきっかけは犬目線から
        private int[] _prevPose;
        private int _stopDog = -1, _stopSpot = -1;
        private float _stopAt;
        private bool[] _inSpot;
        private float[] _spotShotAt;
        private int _spotPending = -1;
        private int _shots;
        private string _lastSkip = "";

        private void Start()
        {
            _OnLeashChanged();
        }

        /// <summary>KH_LeashManager から: 自分の犬の組が変わったかもしれない。数え直し、いれば見回りを始める。ローカル専用。</summary>
        public void _OnLeashChanged()
        {
            int before = _mask;
            Recount();
            if (_mask != 0 && before == 0)
            {
                // つないだ: 時間と距離はここから数える
                _timeRefAt = Time.time;
                _walked = 0f;
                _hasPrev = false;
            }
            if (_mask != 0 && !_ticking)
            {
                _ticking = true;
                SendCustomEventDelayedSeconds(nameof(_WalkTick), tickSeconds);
            }
        }

        /// <summary>見回り 1 回 (遅延イベント)。自分の犬がいなければ止まる。ローカル専用。</summary>
        public void _WalkTick()
        {
            _ticking = false;
            Recount();
            if (_mask == 0) { Clear(); return; }
            _ticking = true;
            SendCustomEventDelayedSeconds(nameof(_WalkTick), tickSeconds > 0.1f ? tickSeconds : 0.1f);
            Step();
        }

        private void Recount()
        {
            int m = 0;
            if (manager != null && wanderControllers != null)
                for (int i = 0; i < wanderControllers.Length && i < 30; i++) if (manager._IsMine(i)) m |= 1 << i;
            _mask = m;
        }

        private void Clear()
        {
            _stopDog = -1; _spotPending = -1; _hasPrev = false; _walked = 0f;
            if (_prevPose != null) for (int i = 0; i < _prevPose.Length; i++) _prevPose[i] = -1;
        }

        private void Step()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            float now = Time.time;
            int n = wanderControllers != null ? wanderControllers.Length : 0;
            if (_prevPose == null || _prevPose.Length != n) { _prevPose = new int[n]; for (int i = 0; i < n; i++) _prevPose[i] = -1; }
            int spots = spotPositions != null ? spotPositions.Length : 0;
            if (_inSpot == null || _inSpot.Length != spots) { _inSpot = new bool[spots]; _spotShotAt = new float[spots]; for (int k = 0; k < spots; k++) _spotShotAt[k] = -1000f; }

            // ---- 暗転と飛び (明けてから fadeGrace 秒は撮らない) ----
            KH_TravelFade fade = manager != null ? manager.travelFade : null;
            bool busy = fade != null && fade._Busy();
            if (busy) { _fadeBusy = true; _fadeUntil = now + 3600f; }
            else if (_fadeBusy) { _fadeBusy = false; _fadeUntil = now + fadeGrace; }
            Vector3 p = lp.GetPosition();
            if (_hasPrev)
            {
                Vector3 d = p - _prevPos; d.y = 0f;
                float m = d.magnitude;
                if (m > jumpMeters) { if (!_fadeBusy) _fadeUntil = Mathf.Max(_fadeUntil, now + fadeGrace); }
                else _walked += m;
            }
            _prevPos = p; _hasPrev = true;

            // ---- 立ち寄りのポーズの立ち上がり (自分の犬だけ) ----
            for (int i = 0; i < n; i++)
            {
                KD_DogWanderController w = wanderControllers[i];
                int idx = (_mask & (1 << i)) != 0 && w != null ? w.PoseSpotIndex : -1;
                if (idx >= 0 && _prevPose[i] != idx && _stopDog < 0) { _stopDog = i; _stopSpot = idx; _stopAt = now + stopoverDelay; }
                _prevPose[i] = idx;
            }
            if (_stopDog >= 0 && now > _stopAt + 5f) _stopDog = -1;   // 撮れないまま古くなった

            // ---- 写真の場所に入った ----
            for (int k = 0; k < spots; k++)
            {
                Vector3 d = p - spotPositions[k];
                float r = spotRadii != null && k < spotRadii.Length ? spotRadii[k] : 4f;
                bool inside = Mathf.Abs(d.y) <= spotHeight && new Vector2(d.x, d.z).magnitude <= r;
                if (inside && !_inSpot[k] && now - _spotShotAt[k] >= spotRepeat) _spotPending = k;
                _inSpot[k] = inside;
            }

            string why = Blocked(now);
            if (why.Length > 0) { _lastSkip = why; return; }
            if (now - _lastWalkShotAt < minGap) { _lastSkip = "min gap"; return; }

            // 1. 立ち寄り
            if (_stopDog >= 0 && now >= _stopAt)
            {
                int dog = _stopDog, spot = _stopSpot;
                _stopDog = -1;
                KD_DogWanderController w = wanderControllers[dog];
                if ((_mask & (1 << dog)) != 0 && w != null && w.PoseSpotIndex == spot && manager.stopovers != null)
                {
                    Take(dog, true, manager.stopovers._LookTarget(spot), true, false, Vector3.zero, now);
                    return;
                }
            }
            // 2. 写真の場所
            if (_spotPending >= 0)
            {
                int k = _spotPending;
                _spotPending = -1;
                bool back = spotHasBackdrop != null && k < spotHasBackdrop.Length && spotHasBackdrop[k];
                Vector3 b = back && spotBackdrops != null && k < spotBackdrops.Length ? spotBackdrops[k] : Vector3.zero;
                if (Take(-1, false, Vector3.zero, false, back, b, now)) _spotShotAt[k] = now;
                return;
            }
            // 3. 時間と距離
            if (now - _timeRefAt >= timeInterval && _walked >= distanceInterval)
            {
                if (Take(-1, !_timeToggle, Vector3.zero, false, false, Vector3.zero, now)) _timeToggle = !_timeToggle;
            }
        }

        private string Blocked(float now)
        {
            if (director == null || settings == null || album == null) return "not wired";
            if (!album.surfacesPlaced || !settings.CaptureAllowed) return "photos off";
            if (_fadeBusy || now < _fadeUntil) return "fade";
            if (sleepPlacement != null && sleepPlacement.Active) return "sleep placement";
            int n = wanderControllers != null ? wanderControllers.Length : 0;
            for (int i = 0; i < n; i++)
            {
                if ((_mask & (1 << i)) == 0) continue;
                KD_DogWanderController w = wanderControllers[i];
                if (w != null && w.WashHeld) return "wash";
                if (manager != null && manager._InWater(i)) return "water";
            }
            return "";
        }

        private bool Take(int dog, bool dogEyeFirst, Vector3 look, bool useLook, bool useBackdrop, Vector3 backdrop, float now)
        {
            director.walkDog = dog;
            director.walkOwnMask = _mask;
            director.walkDogEyeFirst = dogEyeFirst;
            director.walkUseLook = useLook;
            director.walkLook = look;
            director.walkUseBackdrop = useBackdrop;
            director.walkBackdrop = backdrop;
            int kind = director._WalkShoot();
            if (kind == KD_PhotoDirector.WalkShotNone) { _lastSkip = "director took nothing"; return false; }
            _lastWalkShotAt = now;
            _timeRefAt = now;
            _walked = 0f;
            _shots++;
            _lastSkip = "";
            return true;
        }
    }
}
