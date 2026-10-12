using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M1: 隊列の順・マスターの引き継ぎ・到着・錨の打ち直し (Owner) と、距離の計算 (全員)。
    ///
    /// 状態の表 (cartState × groupState。それ以外の組は OwnerTick の見張りが Idle / Free へ戻す):
    /// | groupState \ cartState | Idle (0) | Riding (1) | Ending (2) |
    /// |---|---|---|---|
    /// | Free (0)    | 待機 (駐車・不可視) | 不整合 → 解放 | 不整合 → 解放 |
    /// | Running (1) | このグループ外 | 走行中。合流を受ける (受付 ON のとき) | 不整合 → 解放 |
    /// | Ending (2)  | このグループ外 | (到着の瞬間に Ending へ揃える) | 目的地で停止へ。各自が着いたら自分で降りる。合流は拒否 |
    /// 遷移: Start → (Riding, Running) / Join → 末尾に Riding / 到着 (先頭が目的地距離 - arriveSlack) → 全員 Ending, Ending /
    /// Leave・退出・見張り → Idle、残りの順を詰め、先頭が抜けたら錨を新しい先頭へ、マスターなら order 0 へ引き継ぎ、0 人で Free。</summary>
    public partial class KH_CartManager
    {
        // 経路の写し (KH_WalkRoute の配列は Build が書く静的データ。同じ配列オブジェクトを持つだけ)
        private float[] _loopDist;
        private int[] _destNodes;
        private float _L;
        private int _nodeCount, _loopFwd = 2, _loopRev = 3;

        private bool _useTrack, _routeCached;

        /// <summary>距離の基準を決める。M1.1: カート専用の軌道 (trackPositions / trackDistances / trackLength。Setup が書く) が揃っていれば
        /// それを使い (_L = trackLength)、無ければ従来の route の周回 (_L = loopLength)。行き先の値 (周回 2/3) は route から読む。</summary>
        private bool EnsureRoute()
        {
            if (!_routeCached)
            {
                _routeCached = true;
                _useTrack = trackPositions != null && trackDistances != null && trackPositions.Length > 1
                    && trackDistances.Length == trackPositions.Length && trackLength > 0f;
                if (route != null)
                {
                    _loopDist = route.loopDistances;
                    _destNodes = route.destinationNodes;
                    _nodeCount = route.count;
                    _loopFwd = route.loopForwardDestination;
                    _loopRev = route.loopReverseDestination;
                }
                _L = _useTrack ? trackLength : (route != null ? route.loopLength : 0f);
                Debug.Log("[KH_Cart] distance basis=" + (_useTrack ? "track nodes=" + trackPositions.Length : "route") + " length=" + _L);
            }
            if (_useTrack) return true;
            return route != null && _loopDist != null && _nodeCount > 1 && _loopDist.Length >= _nodeCount && _L > 0f && route.closedLoop;
        }

        /// <summary>乗り場 spot の開始距離。軌道なら spotTrackDistance[spot]、無ければ spotNodes のノードの loopDistances。無効なら -1。</summary>
        private float SpotStartDistance(int spot)
        {
            if (spot < 0 || spot >= SpotCount || !EnsureRoute()) return -1f;
            if (_useTrack)
            {
                if (spotTrackDistance == null || spot >= spotTrackDistance.Length) return -1f;
                float s = spotTrackDistance[spot];
                return s >= 0f ? s : -1f;
            }
            int node = SpotNode(spot);
            return node >= 0 ? _loopDist[node] : -1f;
        }

        /// <summary>行き先の到着距離。周回 (2/3) と無効は -1。軌道なら destTrackDistance[dest]、無ければ destinationNodes のノードの loopDistances。</summary>
        private float DestDistance(int destination)
        {
            if (destination == _loopFwd || destination == _loopRev || !EnsureRoute()) return -1f;
            if (_useTrack)
            {
                if (destTrackDistance == null || destination < 0 || destination >= destTrackDistance.Length) return -1f;
                float s = destTrackDistance[destination];
                return s >= 0f ? s : -1f;
            }
            int node = DestNode(destination);
            return node >= 0 ? _loopDist[node] : -1f;
        }

        // ============================================================
        // 軌道 (M1.1)。Probe が Reflection で呼ぶので名前・引数 (float d 1 つ) を変えない。配列が無ければ route の同名機能へ
        // ============================================================

        /// <summary>距離 d の点 (軌道の区間 i = trackPositions[i] → [(i+1) % n] の直線補間)。</summary>
        public Vector3 TrackPoint(float d)
        {
            if (!EnsureRoute()) return transform.position;
            if (!_useTrack) return route._PointAtDistance(d);
            int n = trackPositions.Length;
            float x = Wrap(d);
            int i = TrackSegment(x);
            int j = (i + 1) % n;
            float a = trackDistances[i];
            float b = j == 0 ? trackLength : trackDistances[j];
            float t = b - a > 0.0001f ? Mathf.Clamp01((x - a) / (b - a)) : 0f;
            return Vector3.Lerp(trackPositions[i], trackPositions[j], t);
        }

        /// <summary>距離 d を含む区間の水平単位ベクトル (距離が増える向き)。</summary>
        public Vector3 TrackDirection(float d)
        {
            if (!EnsureRoute()) return Vector3.forward;
            if (!_useTrack) return route._DirectionAtDistance(d);
            int n = trackPositions.Length;
            int i = TrackSegment(Wrap(d));
            Vector3 v = trackPositions[(i + 1) % n] - trackPositions[i]; v.y = 0f;
            return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
        }

        /// <summary>距離 d を含む区間で横ずれしてよいか (trackDetour[i]。配列が無ければ true)。</summary>
        public bool TrackDetour(float d)
        {
            if (!EnsureRoute()) return true;
            if (!_useTrack) return route._DetourAtDistance(d);
            if (trackDetour == null) return true;
            int i = TrackSegment(Wrap(d));
            return i >= trackDetour.Length || trackDetour[i];
        }

        /// <summary>距離 d が追加の横ずれ禁止範囲 [noLateralFrom[k], noLateralTo[k]] に入るか (周回をまたぐ範囲・負の始点も可)。</summary>
        public bool TrackNoLateral(float d)
        {
            // 範囲は軌道の距離。route へ戻っているとき (軌道配列なし) は距離の基準が違うので適用しない (Codex M1.1 レビュー (2))
            if (noLateralFrom == null || noLateralTo == null || !EnsureRoute() || !_useTrack) return false;
            float x = Wrap(d);
            for (int k = 0; k < noLateralFrom.Length && k < noLateralTo.Length; k++)
            {
                float len = noLateralTo[k] - noLateralFrom[k];
                if (len <= 0f) continue;
                if (Wrap(x - noLateralFrom[k]) <= len) return true;
            }
            return false;
        }

        // trackDistances[i] <= x を満たす最大の i (二分探索。trackDistances[0] = 0、昇順)
        private int TrackSegment(float x)
        {
            int lo = 0, hi = trackDistances.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (trackDistances[mid] <= x) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        // ============================================================
        // Owner: 解放・詰め・引き継ぎ
        // ============================================================

        /// <summary>カートを Idle に戻してグループから外す (Commit は呼び元)。</summary>
        private void RemoveCart(int c, string reason)
        {
            int g = cartGroup[c], order = cartOrder[c], pid = cartRider[c];
            bool wasLead = order == 0;
            int now = Networking.GetServerTimeInMilliseconds();
            // レビュー #2: 先頭が到着距離に達していれば、外して錨を打ち直す前に Ending を確定する (Owner 自身が先頭のとき
            // LocalTick の到着降車が OwnerTick より先に走り、錨が 1 間隔後ろへ移って Ending が遅れていた)
            if (ValidGroup(g)) SettleArrival(g, now);
            // M3: 途中で降りたら降りた場所に残す (Parked)。目的地への到着とグループが無い不整合は Idle (プールへ戻す)
            bool canPark = ValidGroup(g) && groupState[g] != GroupFree && EnsureRoute();
            // 2026-10-12 追加要望: 目的地に着いて降りたカートは残さずプール (Idle) へ戻す (駐車枠も作らない)。途中の停止・ジャンプ・ワープ・リスポーン・退出は Parked
            bool arrived = canPark && IsDestRun(g) && TravelOf(c, now) >= Remaining(g) - arriveSlack;
            if (arrived) { canPark = false; Debug.Log("[KH_Cart] arrive-release cart=" + c + " group=" + g + " player=" + pid); }
            int parkDir = canPark && groupDir[g] < 0 ? -1 : 1;
            float stopAt = canPark ? TargetDistance(c, now) : 0f;
            ResetCart(c);
            // Codex M3 (2): 駐車枠 (狭い区間は出口 + 3 m まで進む、他の Parked と 1.6 m 未満なら後ろへずらす)。ResetCart の後に決める (自分を数えない)
            float parkAt = canPark ? ParkSlotDistance(stopAt, parkDir, c) : 0f;
            int freed = now == 0 ? 1 : now;   // 0 は「猶予なし」の印
            cartFreedMs[c] = freed;
            if (canPark)
            {
                cartState[c] = CartParked;
                cartParkDist[c] = parkAt;
                cartParkedMs[c] = freed;
                cartParkDir[c] = parkDir;
                Debug.Log("[KH_Cart] park cart=" + c + " at=" + parkAt + " stop=" + stopAt + " dir=" + parkDir + " reason=" + reason);
            }
            if (!ValidGroup(g) || groupState[g] == GroupFree)
            {
                Debug.Log("[KH_Cart] leave cart=" + c + " group=" + g + " player=" + pid + " reason=" + reason + " (no group)");
                return;
            }
            // 残りの順を詰める (順序維持)
            for (int k = 0; k < MaxCarts; k++)
                if (IsRidingState(cartState[k]) && cartGroup[k] == g && cartOrder[k] > order) cartOrder[k]--;
            int count = CountMembers(g);
            groupCount[g] = count;
            Debug.Log("[KH_Cart] leave cart=" + c + " group=" + g + " player=" + pid + " order=" + order + " count=" + count + " reason=" + reason);
            if (count <= 0) { FreeGroup(g); return; }
            if (wasLead) ReanchorForNewLead(g);
            if (groupMaster[g] == c)
            {
                int next = MemberAt(g, 0);
                groupMaster[g] = next;   // groupDest / groupDir / groupAccepting は維持
                Debug.Log("[KH_Cart] handover group=" + g + " master cart " + c + "->" + next
                    + " player " + pid + "->" + (ValidCart(next) ? cartRider[next] : 0));
            }
        }

        private void ResetCart(int c)
        {
            cartState[c] = CartIdle;
            cartRider[c] = 0;
            cartGroup[c] = -1;
            cartOrder[c] = 0;
            cartSpot[c] = 0;
            cartSeatSteps[c] = 0;   // M1.1: 降車で座席を既定へ戻す
            cartYawSteps[c] = 0;
            cartFwdSteps[c] = 0;
            cartParkDist[c] = 0f;   // M3 (Parked にするときは呼び元が書き直す)
            cartParkedMs[c] = 0;
            cartParkDir[c] = 0;
        }

        private void FreeGroup(int g)
        {
            groupState[g] = GroupFree;
            groupMaster[g] = -1;
            groupAccepting[g] = false;
            groupCount[g] = 0;
            groupDir[g] = 0;
            groupDest[g] = 0;
            groupAnchorDist[g] = 0f;
            groupAnchorMs[g] = 0;
            groupLane[g] = 0;            // M2: 交通の状態も解く
            groupHoldDist[g] = -1f;
            groupHoldSinceMs[g] = 0;
            groupHoldBanMs[g] = 0;
            Debug.Log("[KH_Cart] free group=" + g);
        }

        private int CountMembers(int g)
        {
            int n = 0;
            for (int c = 0; c < MaxCarts; c++) if (IsRidingState(cartState[c]) && cartGroup[c] == g) n++;
            return n;
        }

        private int MemberAt(int g, int order)
        {
            for (int c = 0; c < MaxCarts; c++) if (IsRidingState(cartState[c]) && cartGroup[c] == g && cartOrder[c] == order) return c;
            return -1;
        }

        /// <summary>先頭が抜けた: 錨を新しい先頭 (元 order 1) の現在距離へ打ち直す。後続は前へ詰めない (同じ位置のまま)。
        /// 出発の猶予中 (now &lt; anchorMs) は時刻を保ち、距離だけ 1 間隔ぶん戻す (猶予を縮めない)。</summary>
        private void ReanchorForNewLead(int g)
        {
            if (!EnsureRoute()) return;
            int now = Networking.GetServerTimeInMilliseconds();
            int dir = groupDir[g];
            float t = Elapsed(g, now);
            if (t <= 0f)
            {
                groupAnchorDist[g] = Wrap(groupAnchorDist[g] - dir * gap);
                return;
            }
            float s = LeadTravel(g, now) - gap;   // 新しい先頭の、古い錨からの進み (M2: 交通の待機で止まっていればその分)
            if (IsDestRun(g)) { float r = Remaining(g); if (s > r) s = r; }
            groupAnchorDist[g] = Wrap(groupAnchorDist[g] + dir * s);
            groupAnchorMs[g] = now;
        }

        // ============================================================
        // Owner: 到着と見張り (0.25 秒ごと)
        // ============================================================

        private void OwnerTick()
        {
            if (!EnsureRoute()) return;
            bool changed = false;
            int now = Networking.GetServerTimeInMilliseconds();
            for (int g = 0; g < MaxGroups; g++)
                if (SettleArrival(g, now)) changed = true;
            if (TrafficTick(now)) changed = true;   // M2: すれ違い (一列化・狭い区間の先入優先・待機と解除。KH_CartManager.Traffic.cs)
            if (Time.time >= _nextMaintenance)
            {
                _nextMaintenance = Time.time + 1f;
                if (Maintain()) changed = true;
            }
            if (changed) Commit();
        }

        /// <summary>到着の確定 (共通関数。OwnerTick・RemoveCart・ReceiveJoin から)。目的地走行の Running グループで、先頭の進みが
        /// 残り - arriveSlack に達していれば groupState = Ending、乗員を Ending にする。書いたら true (Commit は呼び元)。</summary>
        private bool SettleArrival(int g, int now)
        {
            if (!EnsureRoute() || groupState[g] != GroupRunning || !IsDestRun(g)) return false;
            if (LeadTravel(g, now) < Remaining(g) - arriveSlack) return false;
            groupState[g] = GroupEnding;   // 以後の合流は拒否
            for (int c = 0; c < MaxCarts; c++) if (cartState[c] == CartRiding && cartGroup[c] == g) cartState[c] = CartEnding;
            Debug.Log("[KH_Cart] arrive group=" + g + " dest=" + groupDest[g] + " count=" + groupCount[g]);
            return true;
        }

        /// <summary>退出した乗員 (Owner 交代と重なった OnPlayerLeft の取りこぼし)・不整合な組を解放する。KH_LeashManager.Update の見張りと同じ役割。</summary>
        private bool Maintain()
        {
            bool changed = false;
            for (int c = 0; c < MaxCarts; c++)
            {
                if (cartState[c] == CartIdle)
                {
                    if (cartRider[c] != 0 || cartGroup[c] != -1 || cartSeatSteps[c] != 0 || cartYawSteps[c] != 0 || cartFwdSteps[c] != 0) { ResetCart(c); changed = true; }
                    continue;
                }
                if (cartState[c] == CartParked)
                {
                    // M3: 置いてあるカートは乗員もグループも持たない (退出・不整合の判定の対象外)
                    if (cartRider[c] != 0 || cartGroup[c] != -1 || cartOrder[c] != 0) { cartRider[c] = 0; cartGroup[c] = -1; cartOrder[c] = 0; changed = true; }
                    continue;
                }
                if (!IsRidingState(cartState[c])) { ResetCart(c); changed = true; continue; }   // 未知の値
                int g = cartGroup[c];
                bool gone = !Utilities.IsValid(VRCPlayerApi.GetPlayerById(cartRider[c]));
                bool orphan = !ValidGroup(g) || groupState[g] == GroupFree || (cartState[c] == CartEnding) != (groupState[g] == GroupEnding);
                if (gone || orphan) { RemoveCart(c, gone ? "gone" : "orphan"); changed = true; }
            }
            for (int g = 0; g < MaxGroups; g++)
            {
                if (groupState[g] == GroupFree) continue;
                int n = CountMembers(g);
                if (n == 0) { FreeGroup(g); changed = true; continue; }
                if (groupCount[g] != n) { groupCount[g] = n; changed = true; }
                if (!ValidCart(groupMaster[g]) || cartGroup[groupMaster[g]] != g || !IsRidingState(cartState[groupMaster[g]]))
                {
                    groupMaster[g] = MemberAt(g, 0);
                    Debug.Log("[KH_Cart] handover group=" + g + " master cart ->" + groupMaster[g] + " (repair)");
                    changed = true;
                }
            }
            return changed;
        }

        // ============================================================
        // 距離 (全員が同じ式で計算する)
        // ============================================================

        private bool IsDestRun(int g) { int d = groupDest[g]; return d != _loopFwd && d != _loopRev; }

        /// <summary>錨からの経過秒 (出発の猶予中は 0)。サーバー時刻は int の差を取ってから秒へ (桁あふれは int の差で吸収)。</summary>
        private float Elapsed(int g, int now)
        {
            int dt = now - groupAnchorMs[g];
            return dt > 0 ? dt * 0.001f : 0f;
        }

        /// <summary>錨 (先頭の距離) から目的地距離まで、進行方向に測った残り。行き過ぎ (浮動小数で 1 周近くに化けた値) は 0。</summary>
        private float Remaining(int g)
        {
            float b = DestDistance(groupDest[g]);
            if (b < 0f) return 0f;
            float a = groupAnchorDist[g];
            float raw = Wrap(groupDir[g] > 0 ? b - a : a - b);
            return raw > _L * .75f ? 0f : raw;
        }

        /// <summary>M2: 錨から交通の待機点 (groupHoldDist) までの、進行方向に測った距離。待機していなければ大きな値。行き過ぎは 0。</summary>
        private float HoldRemaining(int g)
        {
            float h = groupHoldDist[g];
            if (h < 0f) return 1e9f;
            float raw = Wrap(groupDir[g] > 0 ? h - groupAnchorDist[g] : groupAnchorDist[g] - h);
            return raw > _L * .75f ? 0f : raw;
        }

        /// <summary>先頭の、錨からの進み。待機点で止める (M2)。目的地の clamp は TravelOf / Remaining 側。</summary>
        private float LeadTravel(int g, int now)
        {
            float s = speed * Elapsed(g, now);
            float h = HoldRemaining(g);
            return s > h ? h : s;
        }

        /// <summary>カート c の、錨からの進み (進行方向の符号なし距離)。先頭は待機点で止まり、後続は間隔を保って止まる (M2)。
        /// 目的地走行は残りで止める (clamp。こちらは各車が目的地まで進む)。</summary>
        private float TravelOf(int c, int now)
        {
            int g = cartGroup[c];
            if (!ValidGroup(g)) return 0f;
            float s = LeadTravel(g, now) - cartOrder[c] * gap;
            if (IsDestRun(g)) { float r = Remaining(g); if (s > r) s = r; }
            return s;
        }

        /// <summary>グループ g の先頭の今の経路距離。</summary>
        private float LeadDistance(int g, int now)
        {
            float s = LeadTravel(g, now);
            if (IsDestRun(g)) { float r = Remaining(g); if (s > r) s = r; }
            return Wrap(groupAnchorDist[g] + groupDir[g] * s);
        }

        /// <summary>カート c の目標の経路距離 [0, L)。d = anchor + dir * (speed * t - order * gap)、目的地は越えない。</summary>
        private float TargetDistance(int c, int now)
        {
            int g = cartGroup[c];
            if (!ValidGroup(g)) return 0f;
            return Wrap(groupAnchorDist[g] + groupDir[g] * TravelOf(c, now));
        }

        private float Wrap(float x)
        {
            if (_L <= 0f) return x;
            float r = Mathf.Repeat(x, _L);
            return r >= _L || r < 0f ? 0f : r;
        }

        /// <summary>(-L/2, L/2] へ畳んだ差。</summary>
        private float WrapSigned(float x)
        {
            float r = Wrap(x);
            return r > _L * .5f ? r - _L : r;
        }
    }
}
