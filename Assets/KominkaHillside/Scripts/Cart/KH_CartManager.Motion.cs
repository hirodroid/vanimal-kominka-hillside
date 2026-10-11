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
        private bool[] _prevParked, _parkDone;   // M3: 前回の反映で Parked だったか / その場に寄せ終わったか
        private float[] _parkLat;                // 駐車枠の横 (ParkSlotLateral の結果。駐車ごとに 1 回)
        private bool[] _parkLatReady;
        private int[] _seenParkMs;               // 最後に反映した cartParkedMs / cartParkDist (Codex M3 (3): 中間なしの再駐車を見分ける)
        private float[] _seenParkDist;
        private int _deferExitCart = -1;         // 乗員を乗せたまま駐車枠まで進み、着いたら降ろすカート
        private float _deferExitUntil;
        const float ParkLateralSpeed = 1.0f;     // m/s: 駐車枠への横寄せ (乗員を乗せていることがあるので走行の千鳥より速く・短く)
        private float[] _parkAt, _err, _dLast, _y, _vy, _pitch, _vp, _lat, _yT, _placedAt;
        private int[] _seg, _groundFrame;   // 前回の軌道の区間 (近傍からたどる) と、前回の接地のフレーム
        private Transform[] _cartT;
        private bool _gHit;   // GroundAt の結果 (当たりがあったか)

        const float CatchUpSpeed = 0.6f;    // m/s: 目標の跳びを追う速さ
        const float LateralSpeed = 0.4f;    // m/s: 横ずれの切り替え
        const float LateralLookAhead = 2.0f; // m: 寄せ切るまでに進む距離 (lateral / LateralSpeed × speed = 0.6 / 0.4 × 1.3 ≈ 1.95)
        // M2 接地 v3 (契約 M2 Runtime 1)
        const float GroundRayUp = 1.5f, GroundRayLength = 4f, GroundRecoverUp = 4f, GroundRecoverLength = 10f;
        const float GroundObstacle = 0.45f, GroundRetryLength = 1.0f, GroundBelow = 1.0f, GroundAbove = 1.5f, GapLimit = 0.5f;
        const int GroundRetries = 2;
        private Collider _gCol;
        private float[] _gapSince, _gapLogAt;
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
            _gapSince = new float[MaxCarts]; _gapLogAt = new float[MaxCarts];
            for (int k = 0; k < MaxCarts; k++) _gapSince[k] = -1f;
            _prevParked = new bool[MaxCarts]; _parkDone = new bool[MaxCarts];
            _parkLat = new float[MaxCarts]; _parkLatReady = new bool[MaxCarts];
            _seenParkMs = new int[MaxCarts]; _seenParkDist = new float[MaxCarts];
            _cartT = new Transform[MaxCarts];
            for (int c = 0; carts != null && c < carts.Length && c < MaxCarts; c++)
                if (carts[c] != null) _cartT[c] = carts[c].transform;
        }

        /// <summary>M3 (Codex M3 レビュー後): その場に残った (Parked) カートを駐車枠へ置く。距離は cartParkDist、向きと右側は cartParkDir (同期) で決め、
        /// 横は ParkSlotLateral (class 0: 1.6 m / class 1: 1.2 m、NavMesh 外なら 0.3 m ずつ内側へ、下限 1.0 m。各クライアントが同じ式で計算)。
        /// 表示距離 (_dLast) は駐車距離へ speed で進む (狭い区間の出口 + 3 m まで乗員を乗せたまま進む)。乗員が座っていれば横にも寄せ切ってから
        /// 外側 (右) へ降ろす (_deferExitCart)。寄せ終われば以後は書かない。snap (初回・跳び) は即時配置。</summary>
        private void PlaceParked(int c, float dt)
        {
            if (_cartT == null || _cartT[c] == null || !EnsureRoute()) return;
            bool snap = _snap[c] || dt <= 0f;
            if (!snap && _parkDone[c]) return;
            int dir = cartParkDir[c] < 0 ? -1 : 1;
            float goal = cartParkDist[c];
            if (!_parkLatReady[c]) { _parkLat[c] = ParkSlotLateral(goal, dir); _parkLatReady[c] = true; }
            float d = snap ? goal : Wrap(_dLast[c] + Mathf.Clamp(WrapSigned(goal - _dLast[c]), -speed * dt, speed * dt));
            bool reached = Mathf.Abs(WrapSigned(goal - d)) < 0.01f;
            Vector3 p, ahead;
            if (_useTrack)
            {
                int i = TrackSegNear(_seg[c], d);
                _seg[c] = i;
                p = SegPoint(i, d);
                float xa = Wrap(d + dir * lookAhead);
                ahead = SegPoint(TrackSegNear(i, xa), xa);
            }
            else
            {
                p = route._PointAtDistance(d);
                ahead = route._PointAtDistance(d + dir * lookAhead);
            }
            Vector3 fwd = ahead - p;
            float rise = fwd.y;
            fwd.y = 0f;
            float run = fwd.magnitude;
            fwd = run > 0.01f ? fwd / run : (TrackDirection(d) * (float)dir);
            float slope = run > 0.01f ? rise / run : 0f;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            // 横は駐車距離に着いてから寄せる (走っている間は今の横位置のまま)
            float latTarget = reached ? _parkLat[c] : _lat[c];
            float lat = snap ? _parkLat[c] : Mathf.MoveTowards(_lat[c], latTarget, ParkLateralSpeed * dt);
            _lat[c] = lat;
            Vector3 pos = p + right * lat;
            float prevY = snap ? p.y : _y[c];
            float y = GroundAt(pos, p.y, prevY);
            if (!_gHit && !snap) y = _y[c];
            float pitch = Mathf.Clamp(-Mathf.Atan(slope) * Mathf.Rad2Deg, -pitchLimit, pitchLimit);
            if (snap) { _y[c] = y; _vy[c] = 0f; _pitch[c] = pitch; _vp[c] = 0f; }
            else
            {
                float vy = _vy[c];
                float yy = Mathf.SmoothDamp(_y[c], y, ref vy, groundSmoothTime, Mathf.Infinity, dt);
                _y[c] = yy; _vy[c] = vy;
                float vp = _vp[c];
                float pp = Mathf.SmoothDamp(_pitch[c], pitch, ref vp, smoothTime, Mathf.Infinity, dt);
                _pitch[c] = pp; _vp[c] = vp;
            }
            pos.y = _y[c];
            _cartT[c].SetPositionAndRotation(pos, Quaternion.Euler(_pitch[c], Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, 0f));
            _dLast[c] = d;
            _snap[c] = false;
            bool settled = reached && Mathf.Abs(lat - _parkLat[c]) < 0.001f;
            // 乗員を乗せたまま駐車枠へ着いた: 外側 (右) へ降ろす (Codex M3 (2)。降車位置は駐車枠の外側)
            if (settled && _deferExitCart == c)
            {
                _deferExitCart = -1;
                if (_seatedCart == c && carts != null && c < carts.Length && carts[c] != null)
                {
                    _unboardCart = c; _unboardAt = Time.time;
                    carts[c]._UnboardOuter();
                }
            }
            _parkDone[c] = settled && _deferExitCart != c && _seatedCart != c && Mathf.Abs(_y[c] - y) < 0.005f && Mathf.Abs(_pitch[c] - pitch) < 0.05f;
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
                if (cartState[c] == CartParked) PlaceParked(c, step);   // M3: その場に残ったカート (寄せ終われば何もしない)
                else PlaceCart(c, now, step);
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
            // M2: 横ずれは groupLane で決める (0 = 千鳥、1 = 右寄せ laneOffset (ベンチ等の追加範囲は 0)、2 = 中央)
            int lane = groupLane[g];
            float latTarget;
            if (lane == LaneRight) latTarget = (_useTrack && (TrackNoLateral(d) || TrackNoLateral(d2))) ? 0f : laneOffset;
            else if (lane == LaneCenter) latTarget = 0f;
            else latTarget = detour ? side * lateral : 0f;
            float lat = snap ? latTarget : Mathf.MoveTowards(_lat[c], latTarget, LateralSpeed * dt);
            _lat[c] = lat;
            Vector3 pos = p + right * lat;

            // 接地 (M2 v3)。自分のカートは毎フレーム前後 2 点、他は 3 フレームに 1 回中央 1 点 (出現時は必ず)。ピッチは高さの差 (法線は使わない)。
            // レイの原点は前回の高さ + 1.5 m (snap は道の高さ + 1.5 m)
            float yTarget = _yT[c];
            float pitchTarget;
            bool mine = c == _myCart || c == _seatedCart;
            float prevY = snap ? p.y : _y[c];
            bool rayed = mine;
            if (mine)
            {
                float yF = GroundAt(pos + fwd * PitchHalf, p.y + slope * PitchHalf, prevY + slope * PitchHalf);
                bool okF = _gHit;
                float yR = GroundAt(pos - fwd * PitchHalf, p.y - slope * PitchHalf, prevY - slope * PitchHalf);
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
                    float y = GroundAt(pos, p.y, prevY);
                    if (_gHit || snap) yTarget = y;
                    rayed = true;
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
                float y = Mathf.SmoothDamp(_y[c], yTarget, ref vy, groundSmoothTime, Mathf.Infinity, dt);   // M2: 0.08 s (坂での遅れを 2 cm 程度に)
                _y[c] = y; _vy[c] = vy;
                float vp = _vp[c];
                float pitch = Mathf.SmoothDamp(_pitch[c], pitchTarget, ref vp, smoothTime, Mathf.Infinity, dt);
                _pitch[c] = pitch; _vp[c] = vp;
            }
            if (rayed) GapWatch(c, _y[c], p.y, d, pos);   // Codex M2 (8): 平滑化した後の実際の高さで判定 (レイを撃ったフレームだけ)
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

        /// <summary>M2 接地 v3 (犬の SnapToGround に寄せる): (xz, 前回の高さ prevY + 1.5) から下へ 4 m、最初の当たりを採る。
        /// 当たりが prevY より 0.45 m 以上高い (ベンチ・犬・看板の上) ときは、その 2 cm 下から 1.0 m 撃ち直して当たれば低い方を採る (最大 2 回)。
        /// 何も当たらなければ道の高さ py + 4 から 10 m で撃ち直す (復帰用)。安全のため [py − 1.0, py + 1.5] に収める。
        /// _gHit = 何かに当たったか、_gCol = 採った当たりの Collider (診断ログ用)。Udon に TerrainCollider 型は無いので種類は見ない。</summary>
        private float GroundAt(Vector3 at, float py, float prevY)
        {
            _gHit = false; _gCol = null;
            RaycastHit hit;
            Vector3 o = new Vector3(at.x, prevY + GroundRayUp, at.z);
            bool ok = Physics.Raycast(o, Vector3.down, out hit, GroundRayLength, KH_LeashManager.TravelGroundMask, QueryTriggerInteraction.Ignore);
            if (!ok)
            {
                o = new Vector3(at.x, py + GroundRecoverUp, at.z);
                ok = Physics.Raycast(o, Vector3.down, out hit, GroundRecoverLength, KH_LeashManager.TravelGroundMask, QueryTriggerInteraction.Ignore);
                if (!ok) return Mathf.Clamp(prevY, py - GroundBelow, py + GroundAbove);
            }
            float y = hit.point.y;
            Collider col = hit.collider;
            for (int k = 0; k < GroundRetries && y > prevY + GroundObstacle; k++)
            {
                Vector3 below = hit.point + Vector3.down * 0.02f;
                if (!Physics.Raycast(below, Vector3.down, out hit, GroundRetryLength, KH_LeashManager.TravelGroundMask, QueryTriggerInteraction.Ignore)) break;
                if (hit.point.y < y) { y = hit.point.y; col = hit.collider; }
            }
            _gHit = true; _gCol = col;
            return Mathf.Clamp(y, py - GroundBelow, py + GroundAbove);
        }

        /// <summary>診断: |カートの高さ − 道の高さ| > 0.5 m が 1 秒以上続いたら、5 秒に 1 回 `ground gap` を出す (埋まり・浮きの場所を Play ログから特定する)。</summary>
        private void GapWatch(int c, float y, float py, float d, Vector3 at)
        {
            float gapM = y - py;
            if (Mathf.Abs(gapM) <= GapLimit) { _gapSince[c] = -1f; return; }
            float t = Time.time;
            if (_gapSince[c] < 0f) { _gapSince[c] = t; return; }
            if (t - _gapSince[c] < 1f || t < _gapLogAt[c]) return;
            _gapLogAt[c] = t + 5f;
            Debug.Log("[KH_Cart] ground gap=" + gapM.ToString("F2") + " d=" + d.ToString("F1") + " at=(" + at.x.ToString("F1") + "," + at.y.ToString("F1") + "," + at.z.ToString("F1") + ")"
                + " hit=" + (_gCol != null ? _gCol.gameObject.name : "-") + " cart=" + c);
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
