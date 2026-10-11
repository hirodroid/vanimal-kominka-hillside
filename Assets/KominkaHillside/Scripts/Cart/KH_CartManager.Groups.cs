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

        private bool EnsureRoute()
        {
            if (route == null) return false;
            if (_loopDist == null)
            {
                _loopDist = route.loopDistances;
                _L = route.loopLength;
                _destNodes = route.destinationNodes;
                _nodeCount = route.count;
                _loopFwd = route.loopForwardDestination;
                _loopRev = route.loopReverseDestination;
            }
            return _loopDist != null && _nodeCount > 1 && _loopDist.Length >= _nodeCount && _L > 0f && route.closedLoop;
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
            ResetCart(c);
            int freed = now == 0 ? 1 : now;   // 0 は「猶予なし」の印
            cartFreedMs[c] = freed;
            if (!ValidGroup(g) || groupState[g] == GroupFree)
            {
                Debug.Log("[KH_Cart] leave cart=" + c + " group=" + g + " player=" + pid + " reason=" + reason + " (no group)");
                return;
            }
            // 残りの順を詰める (順序維持)
            for (int k = 0; k < MaxCarts; k++)
                if (cartState[k] != CartIdle && cartGroup[k] == g && cartOrder[k] > order) cartOrder[k]--;
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
            Debug.Log("[KH_Cart] free group=" + g);
        }

        private int CountMembers(int g)
        {
            int n = 0;
            for (int c = 0; c < MaxCarts; c++) if (cartState[c] != CartIdle && cartGroup[c] == g) n++;
            return n;
        }

        private int MemberAt(int g, int order)
        {
            for (int c = 0; c < MaxCarts; c++) if (cartState[c] != CartIdle && cartGroup[c] == g && cartOrder[c] == order) return c;
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
            float s = speed * t - gap;   // 新しい先頭の、古い錨からの進み
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
            if (speed * Elapsed(g, now) < Remaining(g) - arriveSlack) return false;
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
                    if (cartRider[c] != 0 || cartGroup[c] != -1) { ResetCart(c); changed = true; }
                    continue;
                }
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
                if (!ValidCart(groupMaster[g]) || cartGroup[groupMaster[g]] != g || cartState[groupMaster[g]] == CartIdle)
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
            int node = DestNode(groupDest[g]);
            if (node < 0) return 0f;
            float a = groupAnchorDist[g], b = _loopDist[node];
            float raw = Wrap(groupDir[g] > 0 ? b - a : a - b);
            return raw > _L * .75f ? 0f : raw;
        }

        /// <summary>カート c の、錨からの進み (進行方向の符号なし距離)。目的地走行は残りで止める (clamp)。</summary>
        private float TravelOf(int c, int now)
        {
            int g = cartGroup[c];
            if (!ValidGroup(g)) return 0f;
            float s = speed * Elapsed(g, now) - cartOrder[c] * gap;
            if (IsDestRun(g)) { float r = Remaining(g); if (s > r) s = r; }
            return s;
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
