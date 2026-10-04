using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// 裏山改変 (IMP-WLD-007 W2): 散歩の写真の 2 種類。docs/plans/2026-10-03-photo-expansion.md §0 (この節が優先)。
    ///
    /// - **犬目線** = 犬が向いている方向の景色。頭アンカーの前 (口先より前) から、犬の向き (ルートの前方を水平にして 3° 上) か、
    ///   立ち寄り場所の注視点 (walkUseLook) を見る。前 2.5 m に地形・壁があるとき、1 m 先の下 1.5 m に地面が無いときは撮らない
    /// - **自撮り風** = 自分の頭から水平に 0.9 m・0.15 m 上から、頭と犬の中心を 0.35 : 0.65 で結んだ点を見る。顔の向き (見晴らしの場所では
    ///   背景 → 自分の延長線) を基準に 0 / ±35 / ±70° の 5 つを試し、自分の犬 1 匹以上が 6 m 以内で画面の内側 80 % に入り、
    ///   頭からカメラまで遮るものが無い最初の向きで撮る
    /// - どちらから試すかは呼ぶ側 (KH_PhotoWalkWatcher) が walkDogEyeFirst で決め、だめならもう一方、両方だめなら撮らない
    /// - 枠は散歩 (_hillsideGroupOverride = HillsideGroupWalk)。すべてローカル (同期なし)
    ///
    /// 呼び方: KH_PhotoWalkWatcher が walk* を書いてから _WalkShoot() (引数なし。ハーネスも同じ口を使う)。
    /// </summary>
    public partial class KD_PhotoDirector
    {
        public const int WalkShotNone = 0, WalkShotDogEye = 1, WalkShotSelfie = 2;

        [Header("裏山: 散歩の写真 — 犬目線 (IMP-WLD-007 W2。試作値。Photo/1 が書く)")]
        public float walkDogEyeFov = 70f;
        [Tooltip("水平にした犬の前方から上へ向ける角度 (度)")]
        public float walkDogEyePitch = 3f;
        [Tooltip("頭アンカーからの前出し = clamp(DogSize × scale, min, max)")]
        public float walkDogEyeAheadScale = 0.25f, walkDogEyeAheadMin = 0.08f, walkDogEyeAheadMax = 0.2f;
        public float walkDogEyeLift = 0.04f;
        [Tooltip("この距離より手前に地形・壁があれば撮らない (m)")]
        public float walkDogEyeClearAhead = 2.5f;
        [Tooltip("カメラの probe m 先から下へ、犬の頭の高さ + この長さの内に地面が無ければ撮らない (崖の縁・橋の外)")]
        public float walkDogEyeGroundProbe = 1f, walkDogEyeGroundBelow = 1.5f;

        [Header("裏山: 散歩の写真 — 自撮り風")]
        public float walkSelfieFov = 60f, walkSelfieReach = 0.9f, walkSelfieLift = 0.15f;
        [Tooltip("注視点 = 頭 + (犬の中心 − 頭) × この値")]
        public float walkSelfieLookToDogs = 0.35f;
        [Tooltip("犬がこの割合の内側 (画面の中央から) に入っていること")]
        public float walkSelfieInner = 0.8f;
        public float walkSelfieDogMax = 6f;

        // ---- 依頼 (KH_PhotoWalkWatcher が _WalkShoot の直前に書く。ローカル) ----
        [HideInInspector] public int walkDog = -1;          // 犬目線の犬 (-1 = 自分の犬のうちいちばん長く撮られていない犬)
        [HideInInspector] public int walkOwnMask;           // 自分の犬 (bit i = 犬 i)
        [HideInInspector] public bool walkDogEyeFirst = true;
        [HideInInspector] public bool walkUseLook;          // 犬目線の向きを walkLook (立ち寄り場所の注視点) にする
        [HideInInspector] public Vector3 walkLook;
        [HideInInspector] public bool walkUseBackdrop;      // 自撮り風の基準を「walkBackdrop → 自分」の延長線にする
        [HideInInspector] public Vector3 walkBackdrop;

        float _baseFov;        // 普段の FOV (Shoot が最初に読む。50)
        float _shotFov;        // > 0 の間、Shoot はこの FOV で撮る
        // ---- 最後の散歩の写真 (ハーネスが読む) ----
        int _walkLastKind;
        int _walkLastDog = -1;
        Vector3 _walkLastPos, _walkLastDir, _walkLastHead, _walkLastRootFwd;
        string _walkLastReason = "";

        /// <summary>散歩の写真を 1 枚 (KH_PhotoWalkWatcher)。戻り値 WalkShot*。撮れたかどうかは 1 フレーム後 (KD_PhotoCapture)。ローカル専用。</summary>
        public int _WalkShoot()
        {
            _walkLastKind = WalkShotNone;
            _walkLastReason = "";
            if (CapturePaused()) { _walkLastReason = "paused (OFF / no surfaces)"; return WalkShotNone; }
            if (photoCapture == null || photoCamera == null || !photoCapture.CanCapture) { _walkLastReason = "camera busy"; return WalkShotNone; }
            if (_n <= 0) { _walkLastReason = "not started"; return WalkShotNone; }
            int dog = walkDog >= 0 && ValidDog(walkDog) && !SleepingDog(walkDog) ? walkDog : PickWalkDog(walkOwnMask);
            int kind = WalkShotNone;
            if (walkDogEyeFirst)
            {
                if (dog >= 0 && WalkDogEye(dog)) kind = WalkShotDogEye;
                else if (WalkSelfie()) kind = WalkShotSelfie;
            }
            else
            {
                if (WalkSelfie()) kind = WalkShotSelfie;
                else if (dog >= 0 && WalkDogEye(dog)) kind = WalkShotDogEye;
            }
            _walkLastKind = kind;
            if (kind == WalkShotDogEye) { MarkShot(dog); _walkLastDog = dog; }
            if (kind == WalkShotSelfie)
                for (int i = 0; i < _n; i++) if ((walkOwnMask & (1 << i)) != 0) MarkShot(i);
            return kind;
        }

        /// <summary>自分の犬のうち、いちばん長く撮られていない犬 (公平性)。寝ている犬は外す。</summary>
        int PickWalkDog(int mask)
        {
            int best = -1;
            for (int i = 0; i < _n; i++)
            {
                if ((mask & (1 << i)) == 0 || !ValidDog(i) || SleepingDog(i)) continue;
                if (best >= 0 && _lastShotTime[i] >= _lastShotTime[best]) continue;
                best = i;
            }
            return best;
        }

        /// <summary>犬目線: 犬が向いている方向の景色 (§0)。</summary>
        bool WalkDogEye(int dog)
        {
            Transform root = dogRoots[dog];
            if (root == null) { _walkLastReason += "dog's-eye: no root; "; return false; }
            Vector3 head = DogHead(dog);
            Vector3 flat = walkUseLook ? walkLook - head : root.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) { flat = root.forward; flat.y = 0f; }
            if (flat.sqrMagnitude < 0.0001f) { _walkLastReason += "dog's-eye: no forward; "; return false; }
            flat.Normalize();
            float ahead = Mathf.Clamp(DogSize(dog) * walkDogEyeAheadScale, walkDogEyeAheadMin, walkDogEyeAheadMax);
            Vector3 pos = head + flat * ahead + Vector3.up * walkDogEyeLift;
            Vector3 dir;
            if (walkUseLook)
            {
                dir = walkLook - pos;
                if (dir.sqrMagnitude < 0.01f) dir = flat;
                dir.Normalize();
            }
            else
            {
                float r = walkDogEyePitch * Mathf.Deg2Rad;
                dir = flat * Mathf.Cos(r) + Vector3.up * Mathf.Sin(r);
            }
            RaycastHit hit;
            if (Physics.Raycast(pos, dir, out hit, walkDogEyeClearAhead, cameraBlockMask, QueryTriggerInteraction.Ignore))
            {
                _walkLastReason += "dog's-eye: blocked " + hit.distance.ToString("F1") + " m ahead; ";
                return false;
            }
            Vector3 probe = pos + flat * walkDogEyeGroundProbe;
            float above = Mathf.Max(0f, pos.y - root.position.y);
            if (!Physics.Raycast(probe, Vector3.down, out hit, walkDogEyeGroundBelow + above, cameraBlockMask, QueryTriggerInteraction.Ignore))
            {
                _walkLastReason += "dog's-eye: no ground ahead; ";
                return false;
            }
            Vector3 rf = root.forward; rf.y = 0f;
            _walkLastHead = head;
            _walkLastRootFwd = rf.sqrMagnitude > 0.0001f ? rf.normalized : flat;
            _logKind = "walk-dogeye"; _logDog = dog;   // IMP-WLD-011: 撮影の記録
            return ShootWalk(pos, pos + dir * 10f, walkDogEyeFov);
        }

        /// <summary>自撮り風 (§5.2 + §0)。犬が画面の内側に入る向きが無ければ撮らない (空の構図にしない)。</summary>
        bool WalkSelfie()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { _walkLastReason += "selfie: no player; "; return false; }
            VRCPlayerApi.TrackingData td = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 head = td.position;
            Vector3 baseDir = td.rotation * Vector3.forward;
            if (walkUseBackdrop) baseDir = head - walkBackdrop;
            baseDir.y = 0f;
            if (baseDir.sqrMagnitude < 0.0001f) baseDir = lp.GetRotation() * Vector3.forward;
            baseDir.y = 0f;
            if (baseDir.sqrMagnitude < 0.0001f) baseDir = Vector3.forward;
            baseDir.Normalize();

            Vector3 sum = Vector3.zero;
            int own = 0;
            for (int i = 0; i < _n; i++)
            {
                if ((walkOwnMask & (1 << i)) == 0 || !ValidDog(i) || SleepingDog(i)) continue;
                sum += DogSubject(i);
                own++;
            }
            if (own == 0) { _walkLastReason += "selfie: no own dog; "; return false; }
            Vector3 look = head + (sum / own - head) * walkSelfieLookToDogs;

            float tanV = Mathf.Tan(walkSelfieFov * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * (16f / 9f);   // the photo RT is 16:9
            float[] yaws = { 0f, -35f, 35f, -70f, 70f };
            for (int a = 0; a < yaws.Length; a++)
            {
                Vector3 d = Quaternion.AngleAxis(yaws[a], Vector3.up) * baseDir;
                Vector3 cam = head + d * walkSelfieReach + Vector3.up * walkSelfieLift;
                Vector3 dir = look - cam;
                if (dir.sqrMagnitude < 0.0001f) continue;
                Quaternion inv = Quaternion.Inverse(Quaternion.LookRotation(dir.normalized, Vector3.up));
                bool seen = false;
                for (int i = 0; i < _n && !seen; i++)
                {
                    if ((walkOwnMask & (1 << i)) == 0 || !ValidDog(i) || SleepingDog(i)) continue;
                    Vector3 v = DogSubject(i) - cam;
                    if (v.magnitude > walkSelfieDogMax) continue;
                    Vector3 l = inv * v;
                    if (l.z <= 0.05f) continue;
                    if (Mathf.Abs(l.x / l.z) <= tanH * walkSelfieInner && Mathf.Abs(l.y / l.z) <= tanV * walkSelfieInner) seen = true;
                }
                if (!seen) continue;
                Vector3 hc = cam - head;
                if (Physics.Raycast(head, hc.normalized, hc.magnitude, cameraBlockMask, QueryTriggerInteraction.Ignore)) continue;   // the arm would be inside a wall / slope
                _walkLastHead = head;
                _logKind = "walk-selfie"; _logDog = -1;   // IMP-WLD-011: 撮影の記録
                return ShootWalk(cam, look, walkSelfieFov);
            }
            _walkLastReason += "selfie: no own dog in view from 5 angles; ";
            return false;
        }

        bool ShootWalk(Vector3 pos, Vector3 look, float fov)
        {
            _shotFov = fov;
            _hillsideGroupOverride = HillsideGroupWalk;
            bool ok = Shoot(pos, look, GroupWide);
            _hillsideGroupOverride = -1;
            _shotFov = 0f;
            if (ok)
            {
                _walkLastPos = pos;
                Vector3 d = look - pos;
                _walkLastDir = d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
            }
            else _walkLastReason += "capture refused; ";
            return ok;
        }
    }
}
