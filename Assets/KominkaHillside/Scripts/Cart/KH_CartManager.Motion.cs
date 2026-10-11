using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M1: 距離 → 姿勢 (全クライアント、毎フレーム)。
    ///
    /// - 目標距離は KH_CartManager.Groups.cs の TargetDistance (同期済みの錨の純関数)。錨の打ち直しや隊列の詰めで目標が跳んだ分は
    ///   _err に入れて CatchUpSpeed で 0 へ戻す (乗っている人を 3 m 瞬間移動させない。SnapJump を超える跳びは表示も跳ぶ)。
    /// - 横ずれ: 進行方向の右手へ side * lateral (order 偶数 +1 / 奇数 -1)。linkAllowsDetour = false の区間 (橋) は 0。
    ///   今の点か LateralLookAhead 先の点のどちらかが橋なら 0 を目標にし、LateralSpeed で寄せる (入口で一列になり終わる)。
    /// - 接地: (x, 道の高さ + 2, z) から下へ 6 m (KH_LeashManager.TravelGroundMask)。自分の子 (Default 層の停止ボタン等) に当たったら
    ///   その下から撃ち直す。当たらなければ前フレームの高さ。高さとピッチは SmoothDamp、ロールは常に 0。
    /// - ヨーは d + dir * lookAhead の点へ。車輪は表示距離の差で _Spin。
    /// - Idle のカートは ParkDelay の後、ローカルプレイヤーが座っていなければ parking へ移して Renderer を消す (SetActive は使わない)。</summary>
    public partial class KH_CartManager
    {
        private bool[] _shown, _parked, _snap;
        private float[] _parkAt, _err, _dLast, _y, _vy, _pitch, _vp, _lat;
        private Transform[] _cartT;

        const float CatchUpSpeed = 0.6f;    // m/s: 目標の跳びを追う速さ
        const float LateralSpeed = 0.4f;    // m/s: 横ずれの切り替え
        const float LateralLookAhead = 2.0f; // m: 寄せ切るまでに進む距離 (lateral / LateralSpeed × speed = 0.6 / 0.4 × 1.3 ≈ 1.95)
        const float RayUp = KH_LeashManager.TravelRayUp, RayLength = KH_LeashManager.TravelRayLength;

        private void EnsureLocals()
        {
            if (_shown != null) return;
            _shown = new bool[MaxCarts]; _parked = new bool[MaxCarts]; _snap = new bool[MaxCarts];
            _parkAt = new float[MaxCarts]; _err = new float[MaxCarts]; _dLast = new float[MaxCarts];
            _y = new float[MaxCarts]; _vy = new float[MaxCarts]; _pitch = new float[MaxCarts]; _vp = new float[MaxCarts]; _lat = new float[MaxCarts];
            _cartT = new Transform[MaxCarts];
            for (int c = 0; carts != null && c < carts.Length && c < MaxCarts; c++)
                if (carts[c] != null) _cartT[c] = carts[c].transform;
        }

        private void MotionTick()
        {
            if (carts == null || !EnsureRoute()) return;
            int now = Networking.GetServerTimeInMilliseconds();
            float dt = Time.deltaTime;
            for (int c = 0; c < MaxCarts && c < carts.Length; c++)
            {
                if (_cartT[c] == null) continue;
                if (_shown[c]) PlaceCart(c, now, dt);
                else if (!_parked[c] && Time.time >= _parkAt[c] && _seatedCart != c) ParkCart(c);
            }
        }

        /// <summary>カート c を今の目標へ置く。_snap[c] なら平滑化なしで合わせる (出現・着座の直前)。</summary>
        private void PlaceCart(int c, int now, float dt)
        {
            if (_cartT == null || _cartT[c] == null || !EnsureRoute()) return;
            int g = cartGroup[c];
            if (!ValidGroup(g)) return;
            int dir = groupDir[g] >= 0 ? 1 : -1;
            bool snap = _snap[c] || dt <= 0f;

            float target = TargetDistance(c, now);
            float err = snap ? 0f : Mathf.MoveTowards(_err[c], 0f, CatchUpSpeed * dt);
            _err[c] = err;
            float d = Wrap(target - err);

            Vector3 p = route._PointAtDistance(d);
            Vector3 fwd = route._PointAtDistance(d + dir * lookAhead) - p;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = route._DirectionAtDistance(d) * (float)dir;
            fwd = fwd.normalized;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);   // Cross(up, fwd)

            float side = cartOrder[c] % 2 == 0 ? 1f : -1f;
            // レビュー #5: 橋の手前 LateralLookAhead から中央へ寄せ始める (入口で一列になっているように)。出口は今の点が抜けてから戻す
            bool detour = route._DetourAtDistance(d) && route._DetourAtDistance(d + dir * LateralLookAhead);
            float latTarget = detour ? side * lateral : 0f;
            float lat = snap ? latTarget : Mathf.MoveTowards(_lat[c], latTarget, LateralSpeed * dt);
            _lat[c] = lat;
            Vector3 pos = p + right * lat;

            // 接地
            RaycastHit hit;
            Vector3 origin = new Vector3(pos.x, p.y + RayUp, pos.z);
            bool ok = Physics.Raycast(origin, Vector3.down, out hit, RayLength, KH_LeashManager.TravelGroundMask, QueryTriggerInteraction.Ignore);
            if (ok && hit.collider != null && hit.collider.transform.IsChildOf(_cartT[c]))
            {
                Vector3 below = hit.point + Vector3.down * 0.02f;
                float rest = RayLength - (origin.y - below.y);
                ok = rest > 0f && Physics.Raycast(below, Vector3.down, out hit, rest, KH_LeashManager.TravelGroundMask, QueryTriggerInteraction.Ignore);
            }
            float yTarget = ok ? hit.point.y : (snap ? p.y : _y[c]);
            float pitchTarget = _pitch[c];
            if (ok) pitchTarget = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(hit.normal, fwd), hit.normal.y) * Mathf.Rad2Deg, -pitchLimit, pitchLimit);
            else if (snap) pitchTarget = 0f;
            if (snap)
            {
                _y[c] = yTarget; _vy[c] = 0f; _pitch[c] = pitchTarget; _vp[c] = 0f;
            }
            else
            {
                // vrc-udonsharp §1.4: 配列要素を ref に渡さない。ローカルで受けて書き戻す
                float vy = _vy[c];
                float y = Mathf.SmoothDamp(_y[c], yTarget, ref vy, smoothTime);
                _y[c] = y; _vy[c] = vy;
                float vp = _vp[c];
                float pitch = Mathf.SmoothDamp(_pitch[c], pitchTarget, ref vp, smoothTime);
                _pitch[c] = pitch; _vp[c] = vp;
            }
            pos.y = _y[c];
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            _cartT[c].SetPositionAndRotation(pos, Quaternion.Euler(_pitch[c], yaw, 0f));

            if (!snap)
            {
                float moved = Mathf.Abs(WrapSigned(d - _dLast[c]));
                if (moved > 0.0001f && moved < 5f) carts[c]._Spin(moved);
            }
            _dLast[c] = d;
            _snap[c] = false;
        }

        private void ParkCart(int c)
        {
            if (carts == null || c >= carts.Length || carts[c] == null) return;
            EnsureLocals();
            _parked[c] = true;
            carts[c]._SetVisible(false);
            if (parking != null && _cartT[c] != null)
                _cartT[c].SetPositionAndRotation(parking.position + parking.right * (c * 3f), parking.rotation);
        }
    }
}
