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
    /// - 位置・向き・横ずれ可否は TrackPoint / TrackDirection / TrackDetour / TrackNoLateral (M1.1 の軌道。無ければ route)。
    /// - 接地 (M1.1): GroundAt (道の高さ + 4 m から 10 m、道の高さから 0.35 m 超の当たりは下から撃ち直し、最も近い当たりを [-1.0, +1.5] に収める)。
    ///   自分のカートは前後 ±0.42 m の 2 点を毎フレーム、他は中央 1 点を 3 フレームに 1 回。ピッチは前後の高さの差 (他は道の勾配)。
    ///   当たらなければ前の目標の高さ。高さとピッチは SmoothDamp、ロールは常に 0。
    /// - ヨーは d + dir * lookAhead の点へ。車輪は表示距離の差で _Spin。
    /// - Idle のカートは ParkDelay の後、ローカルプレイヤーが座っていなければ parking へ移して Renderer を消す (SetActive は使わない)。</summary>
    public partial class KH_CartManager
    {
        private bool[] _shown, _parked, _snap;
        private float[] _parkAt, _err, _dLast, _y, _vy, _pitch, _vp, _lat, _yT, _placedAt;
        private int[] _seg, _groundFrame;   // 前回の軌道の区間 (近傍からたどる) と、前回の接地のフレーム
        private Transform[] _cartT;
        private bool _gHit;   // GroundAt の結果 (当たりがあったか)

        const float CatchUpSpeed = 0.6f;    // m/s: 目標の跳びを追う速さ
        const float LateralSpeed = 0.4f;    // m/s: 横ずれの切り替え
        const float LateralLookAhead = 2.0f; // m: 寄せ切るまでに進む距離 (lateral / LateralSpeed × speed = 0.6 / 0.4 × 1.3 ≈ 1.95)
        // M1.1 接地 (契約 Runtime 1)
        const float GroundRayUp = 4f, GroundRayLength = 10f, GroundAccept = 0.35f, GroundBelow = 1.0f, GroundAbove = 1.5f;
        const int GroundRetries = 3;
        const float PitchHalf = 0.42f;       // 前後の当たりの間隔の半分 (m)

        private void EnsureLocals()
        {
            if (_shown != null) return;
            _shown = new bool[MaxCarts]; _parked = new bool[MaxCarts]; _snap = new bool[MaxCarts];
            _parkAt = new float[MaxCarts]; _err = new float[MaxCarts]; _dLast = new float[MaxCarts];
            _y = new float[MaxCarts]; _vy = new float[MaxCarts]; _pitch = new float[MaxCarts]; _vp = new float[MaxCarts]; _lat = new float[MaxCarts];
            _yT = new float[MaxCarts];
            _placedAt = new float[MaxCarts];
            _seg = new int[MaxCarts]; _groundFrame = new int[MaxCarts];
            for (int k = 0; k < MaxCarts; k++) { _seg[k] = -1; _groundFrame[k] = -1000; }
            _cartT = new Transform[MaxCarts];
            for (int c = 0; carts != null && c < carts.Length && c < MaxCarts; c++)
                if (carts[c] != null) _cartT[c] = carts[c].transform;
        }

        private void MotionTick()
        {
            if (carts == null || !EnsureRoute()) return;
            int now = Networking.GetServerTimeInMilliseconds();
            float dt = Time.deltaTime;
            float t = Time.time;
            for (int c = 0; c < MaxCarts && c < carts.Length; c++)
            {
                if (!_shown[c])
                {
                    if (!_parked[c] && t >= _parkAt[c] && _seatedCart != c && _cartT[c] != null) ParkCart(c);
                    continue;   // 駐車中・不可視のカートは何もしない
                }
                if (_cartT[c] == null) continue;
                // 位置・回転 (距離の前進・横ずれ・yaw) は全カート毎フレーム (Codex M1.2 (1): 間引くと 30 fps で約 9 cm の段付きになる)。
                // 間引くのは他人のカートの接地レイ (前回から 3 フレーム以上で 1 点、間は前回の高さ・ピッチの目標) だけ。区間は前回の区間から 1 歩たどるだけ
                float step = _placedAt[c] > 0f ? t - _placedAt[c] : dt;
                _placedAt[c] = t;
                PlaceCart(c, now, step);
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

            // 2026-10-12 (コスト削減): 軌道では区間の探索を 1 カート 1 回の二分探索 (前回の区間から近傍をたどる) にし、先読み点・横ずれの先読みは
            // その区間から隣へたどるだけ。route へ戻っているときは従来どおり KH_WalkRoute の関数
            Vector3 p, ahead, segDir;
            bool detour;
            float d2 = d + dir * LateralLookAhead;
            if (_useTrack)
            {
                int i = TrackSegNear(_seg[c], d);
                _seg[c] = i;
                p = SegPoint(i, d);
                float xa = Wrap(d + dir * lookAhead);
                ahead = SegPoint(TrackSegNear(i, xa), xa);
                float x2 = Wrap(d2);
                int i2 = TrackSegNear(i, x2);
                detour = SegDetour(i) && SegDetour(i2) && !TrackNoLateral(d) && !TrackNoLateral(x2);
                segDir = trackPositions[(i + 1) % trackPositions.Length] - trackPositions[i];
                segDir.y = 0f;
                segDir = segDir.sqrMagnitude < 0.0001f ? Vector3.forward : segDir.normalized;
            }
            else
            {
                p = route._PointAtDistance(d);
                ahead = route._PointAtDistance(d + dir * lookAhead);
                detour = route._DetourAtDistance(d) && route._DetourAtDistance(d2);
                segDir = Vector3.zero;
            }
            Vector3 fwd = ahead - p;
            float rise = fwd.y;
            fwd.y = 0f;
            float run = fwd.magnitude;
            if (run < 0.01f) { fwd = (_useTrack ? segDir : route._DirectionAtDistance(d)) * (float)dir; run = 0f; }
            fwd = fwd.normalized;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);   // Cross(up, fwd)
            float slope = run > 0.01f ? rise / run : 0f;      // 道の勾配 (前後の当たりの基準と、他人のカートのピッチ)

            float side = cartOrder[c] % 2 == 0 ? 1f : -1f;
            // レビュー #5: 橋の手前 LateralLookAhead から中央へ寄せ始める。M1.1: ベンチ・門枠などの追加範囲 (TrackNoLateral) も同じ扱い
            float latTarget = detour ? side * lateral : 0f;
            float lat = snap ? latTarget : Mathf.MoveTowards(_lat[c], latTarget, LateralSpeed * dt);
            _lat[c] = lat;
            Vector3 pos = p + right * lat;

            // 接地 (M1.1)。自分のカートは毎フレーム前後 2 点、他は 3 フレームに 1 回中央 1 点 (出現時は必ず)。ピッチは高さの差 (法線は使わない)
            float yTarget = _yT[c];
            float pitchTarget;
            bool mine = c == _myCart || c == _seatedCart;
            if (mine)
            {
                float yF = GroundAt(pos + fwd * PitchHalf, p.y + slope * PitchHalf);
                bool okF = _gHit;
                float yR = GroundAt(pos - fwd * PitchHalf, p.y - slope * PitchHalf);
                bool okR = _gHit;
                if (!okF) yF = okR ? yR + 2f * PitchHalf * slope : p.y + slope * PitchHalf;
                if (!okR) yR = okF ? yF - 2f * PitchHalf * slope : p.y - slope * PitchHalf;
                if (okF || okR || snap) yTarget = (yF + yR) * 0.5f;
                pitchTarget = Mathf.Atan2(yR - yF, 2f * PitchHalf) * Mathf.Rad2Deg;   // 前が高い → 負 (機首上げ)
            }
            else
            {
                // 前回の当たりから 3 フレーム以上たっていれば 1 点 (3 フレームに 1 回。間のフレームは前回の高さの目標へ SmoothDamp)
                if (snap || Time.frameCount - _groundFrame[c] >= 3)
                {
                    _groundFrame[c] = Time.frameCount;
                    float y = GroundAt(pos, p.y);
                    if (_gHit || snap) yTarget = y;
                }
                pitchTarget = -Mathf.Atan(slope) * Mathf.Rad2Deg;
            }
            pitchTarget = Mathf.Clamp(pitchTarget, -pitchLimit, pitchLimit);
            _yT[c] = yTarget;
            if (snap)
            {
                _y[c] = yTarget; _vy[c] = 0f; _pitch[c] = pitchTarget; _vp[c] = 0f;
            }
            else
            {
                // vrc-udonsharp §1.4: 配列要素を ref に渡さない。ローカルで受けて書き戻す
                float vy = _vy[c];
                // 他人のカートは 2 フレームおきに来るので、経過 dt を明示して平滑化の速さを揃える
                float y = Mathf.SmoothDamp(_y[c], yTarget, ref vy, smoothTime, Mathf.Infinity, dt);
                _y[c] = y; _vy[c] = vy;
                float vp = _vp[c];
                float pitch = Mathf.SmoothDamp(_pitch[c], pitchTarget, ref vp, smoothTime, Mathf.Infinity, dt);
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

        // ---- 軌道の区間 (PlaceCart 専用。公開の TrackPoint などは Probe 用に毎回二分探索する) ----

        /// <summary>x を含む区間を、start の区間から前後 4 区間までたどって探す。見つからなければ二分探索。</summary>
        private int TrackSegNear(int start, float x)
        {
            int n = trackDistances.Length;
            if (start < 0 || start >= n) return TrackSegment(Wrap(x));
            int i = start;
            for (int k = 0; k < 5; k++)
            {
                float a = trackDistances[i];
                float b = i + 1 < n ? trackDistances[i + 1] : trackLength;
                if (x >= a && x < b) return i;
                // 向きは周回の符号付きの差で決める (0 跨ぎ: 末尾の区間から先頭へ、先頭から末尾へ 1 歩で行ける)
                i = WrapSigned(x - a) > 0f ? (i + 1) % n : (i + n - 1) % n;
            }
            return TrackSegment(Wrap(x));
        }

        private Vector3 SegPoint(int i, float x)
        {
            int n = trackPositions.Length;
            int j = (i + 1) % n;
            float a = trackDistances[i];
            float b = j == 0 ? trackLength : trackDistances[j];
            float t = b - a > 0.0001f ? Mathf.Clamp01((x - a) / (b - a)) : 0f;
            return Vector3.Lerp(trackPositions[i], trackPositions[j], t);
        }

        private bool SegDetour(int i) { return trackDetour == null || i >= trackDetour.Length || trackDetour[i]; }

        /// <summary>(xz, 道の高さ py) の地面の高さ。py + 4 から下へ 10 m。当たりが道の高さから 0.35 m より離れていれば、その 2 cm 下から撃ち直す
        /// (最大 3 回。ベンチ・看板・犬・停止ボタンの上を抜ける)。採用は py に最も近い当たりで、[py - 1.0, py + 1.5] に収める。
        /// _gHit = 何かに当たったか。契約の「TerrainCollider は撃ち直さない」は、Udon に TerrainCollider 型が公開されていない
        /// (VRC.Udon.Wrapper.dll に無い。2026-10-12 確認) ので判定せず、距離の条件だけで撃ち直す (Terrain は下からは当たらないので結果は同じ)。</summary>
        private float GroundAt(Vector3 at, float py)
        {
            _gHit = false;
            Vector3 o = new Vector3(at.x, py + GroundRayUp, at.z);
            float left = GroundRayLength;
            float best = py, bestErr = 1e9f;
            RaycastHit hit;
            for (int k = 0; k <= GroundRetries; k++)
            {
                if (left <= 0f || !Physics.Raycast(o, Vector3.down, out hit, left, KH_LeashManager.TravelGroundMask, QueryTriggerInteraction.Ignore)) break;
                float hy = hit.point.y;
                float e = Mathf.Abs(hy - py);
                if (e < bestErr) { bestErr = e; best = hy; _gHit = true; }
                if (e <= GroundAccept) break;
                Vector3 next = hit.point + Vector3.down * 0.02f;
                left -= o.y - next.y;
                o = next;
            }
            return Mathf.Clamp(best, py - GroundBelow, py + GroundAbove);
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
