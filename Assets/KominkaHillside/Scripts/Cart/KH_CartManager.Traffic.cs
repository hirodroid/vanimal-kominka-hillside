using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M2: 交通 (対向グループのすれ違いと待機)。Owner が OwnerTick (4 Hz) で判定して同期配列に書き、全員が Motion で反映する。
    ///
    /// - 一列化 (groupLane): 先頭から approach 先まで (と自分の隊列の横) に対向グループの車があれば 1 (右寄せ laneOffset)。
    ///   狭い区間 (narrowFrom..narrowTo) の中と手前 8 m は 2 (中央)。対向が自分の最後尾から 10 m 後ろへ離れたら 0 (千鳥) に戻す。同方向は対象外。
    /// - 狭い区間の先入優先 (groupHoldDist): <see cref="TrafficDecide"/> が 1 を返した区間の手前の待機点 (<see cref="TrafficHoldDistance"/>) で
    ///   先頭が止まる (後続は間隔を保って止まる)。相手の最後尾が区間を出て 2 m 進む・相手が接近をやめる・holdTimeoutSec で解除し、
    ///   錨を今の先頭の距離・今の時刻へ打ち直す。タイムアウトの後 HoldBanMs (同期の groupHoldBanMs) は同じグループを待たせない (詰まり防止)。
    ///   自分の車が区間内なら待たない (区間内で待つのは乗り場の出発保留だけ)。相互待ちは出口に近い方を解く (安全弁)。
    /// - 待機は交通上の自動停止で、所属・目的地・マスター・受付は変えない。
    /// - 軌道 (_useTrack) が無い・狭い区間の配列が無いときは交通なし (従来動作)。残っていた lane / hold は解く。
    /// ログ: `traffic lane g=.. lane=..` / `traffic hold g=.. at=.. because=..` / `traffic release g=..` / `traffic timeout g=..`。</summary>
    public partial class KH_CartManager
    {
        public const int TrafficGo = 0, TrafficHold = 1, TrafficInside = 2;
        public const int LaneStagger = 0, LaneRight = 1, LaneCenter = 2;
        const float LaneNarrowLead = 8f;   // 狭い区間の手前この距離から中央へ
        const float LaneRelease = 10f;     // 対向が最後尾からこの距離後ろへ離れたら千鳥へ戻す
        const float ZoneExitMargin = 2f;   // 相手の最後尾が区間を出てこの距離進んだら解除
        const int HoldBanMs = 20000;       // タイムアウト後に同じグループを待たせない時間 (同期の groupHoldBanMs。Codex M2 (5))

        // ============================================================
        // 判定 (副作用なし。Probe が Reflection で呼ぶ)
        // ============================================================

        /// <summary>1 つの狭い区間 [zoneFrom, zoneTo] (軌道距離) について、自分のグループが進むか待つか。先頭の距離と向き (±1)・台数・グループ番号だけで決める。
        /// 戻り値: 0 = 進む、1 = 区間の手前で待つ、2 = 自分が区間内なので進む。
        /// 順: 同じ向き・同じグループ → 0 / 自分の車が区間内 → 2 / 自分の入口が背後か approach より遠い → 0 /
        /// 相手の車が [zoneFrom − 2, zoneTo + 2] の中 → 1 / 相手の入口が背後か approach より遠い → 0 / 入口に近い方が先 (同距離はグループ番号の小さい方)。</summary>
        public int TrafficDecide(float myLead, int myDir, int myCount, int myGroup, float otherLead, int otherDir, int otherCount, int otherGroup, float zoneFrom, float zoneTo)
        {
            if (!EnsureRoute()) return TrafficGo;
            int md = myDir >= 0 ? 1 : -1, od = otherDir >= 0 ? 1 : -1;
            if (md == od || myGroup == otherGroup) return TrafficGo;
            if (GroupInZone(myLead, md, myCount, zoneFrom, zoneTo, 0f)) return TrafficInside;
            float mine = EntryAhead(myLead, md, zoneFrom, zoneTo);
            if (mine < 0f || mine > approach) return TrafficGo;
            if (GroupInZone(otherLead, od, otherCount, zoneFrom, zoneTo, ZoneExitMargin)) return TrafficHold;
            float theirs = EntryAhead(otherLead, od, zoneFrom, zoneTo);
            if (theirs < 0f || theirs > approach) return TrafficGo;
            if (theirs < mine - 0.01f) return TrafficHold;
            if (theirs > mine + 0.01f) return TrafficGo;
            return otherGroup < myGroup ? TrafficHold : TrafficGo;
        }

        /// <summary>区間 zone の手前の待機距離 (dir +1 は waitBefore、−1 は waitAfter。無ければ入口から 3 m + gap×4 手前)。
        /// 先頭がもう待機点を越えていれば先頭の距離 (その場で止まる)。zone が範囲外なら −1。副作用なし。</summary>
        public float TrafficHoldDistance(float myLead, int myDir, int zone)
        {
            if (!EnsureRoute() || narrowFrom == null || narrowTo == null || zone < 0 || zone >= narrowFrom.Length || zone >= narrowTo.Length) return -1f;
            int d = myDir >= 0 ? 1 : -1;
            float[] waits = d > 0 ? waitBefore : waitAfter;
            float w = waits != null && zone < waits.Length ? waits[zone] : -1f;
            if (w < 0f) w = Wrap((d > 0 ? narrowFrom[zone] : narrowTo[zone]) - d * (3f + gap * 4f));
            float ahead = Wrap(d * (w - myLead));
            if (ahead > _L * .5f) return Wrap(myLead);   // 待機点はもう背後
            return w;
        }

        /// <summary>先頭 lead・向き dir・台数 count の隊列のどれかの車が [from − margin, to + margin] に入っているか。
        /// 区間長は周回距離へ正規化してから余裕を足す (0 を跨ぐ区間 [L−5, 5] も長さ 10。Codex M2 (4))。</summary>
        private bool GroupInZone(float lead, int dir, int count, float from, float to, float margin)
        {
            float a = from - margin, len = Wrap(to - from) + 2f * margin;
            if (len <= 0f) return false;
            for (int j = 0; j < count; j++)
                if (Wrap(lead - dir * j * gap - a) <= len) return true;
            return false;
        }

        /// <summary>先頭から区間の入口までの距離 (進行方向)。入口が背後 (半周より遠い) なら −1。</summary>
        private float EntryAhead(float lead, int dir, float from, float to)
        {
            float e = Wrap(dir * ((dir > 0 ? from : to) - lead));
            return e > _L * .5f ? -1f : e;
        }

        // ============================================================
        // Owner: 4 Hz の判定
        // ============================================================

        private bool TrafficTick(int now)
        {
            bool ready = _useTrack && narrowFrom != null && narrowTo != null;
            bool changed = false;
            for (int g = 0; g < MaxGroups; g++)
            {
                if (groupState[g] != GroupRunning)
                {
                    // 到着停止・解放後は交通の状態を残さない (到着中に待機点が残ると到着距離へ進めない)
                    if (groupState[g] == GroupEnding && groupHoldDist[g] >= 0f) { ReleaseHold(g, now, "ending"); changed = true; }
                    continue;
                }
                if (!ready)
                {
                    if (groupLane[g] != LaneStagger) { groupLane[g] = LaneStagger; changed = true; }
                    if (groupHoldDist[g] >= 0f) { ReleaseHold(g, now, "no-track"); changed = true; }
                    continue;
                }
                float lead = LeadDistance(g, now);
                int dir = groupDir[g] >= 0 ? 1 : -1;
                int count = CountMembers(g);
                if (TrafficLane(g, lead, dir, count, now)) changed = true;
                if (TrafficHoldStep(g, lead, dir, count, now)) changed = true;
            }
            return changed;
        }

        private bool TrafficLane(int g, float lead, int dir, int count, int now)
        {
            int want = LaneStagger;
            float myLen = (count - 1) * gap;
            for (int k = 0; k < narrowFrom.Length && k < narrowTo.Length; k++)
            {
                if (GroupInZone(lead, dir, count, narrowFrom[k], narrowTo[k], 0f)) { want = LaneCenter; break; }
                float e = EntryAhead(lead, dir, narrowFrom[k], narrowTo[k]);
                if (e >= 0f && e <= LaneNarrowLead) { want = LaneCenter; break; }
            }
            if (want == LaneStagger)
            {
                bool keep = groupLane[g] != LaneStagger;
                float back = keep ? myLen + LaneRelease : myLen;
                float front = keep ? approach + LaneRelease : approach;
                for (int h = 0; h < MaxGroups && want == LaneStagger; h++)
                {
                    if (h == g || groupState[h] == GroupFree || (groupDir[h] >= 0 ? 1 : -1) == dir) continue;
                    float hl = LeadDistance(h, now);
                    int hd = groupDir[h] >= 0 ? 1 : -1;
                    int hc = CountMembers(h);
                    for (int j = 0; j < hc; j++)
                    {
                        float rel = WrapSigned(dir * (hl - hd * j * gap - lead));
                        if (rel <= front && rel >= -back) { want = LaneRight; break; }
                    }
                }
            }
            if (want == groupLane[g]) return false;
            groupLane[g] = want;
            Debug.Log("[KH_Cart] traffic lane g=" + g + " lane=" + want + " lead=" + lead);
            return true;
        }

        private bool TrafficHoldStep(int g, float lead, int dir, int count, int now)
        {
            bool holding = groupHoldDist[g] >= 0f;
            if (holding)
            {
                int waited = now - groupHoldSinceMs[g];
                if (waited > (int)(holdTimeoutSec * 1000f))
                {
                    ReleaseHold(g, now, "timeout");
                    Debug.Log("[KH_Cart] traffic timeout g=" + g + " waitedMs=" + waited);
                    int ban = now + HoldBanMs;
                    groupHoldBanMs[g] = ban == 0 ? 1 : ban;
                    return true;
                }
            }
            // Codex M2 (2)/(3): 自分の車が狭い区間の中なら待たない (TrafficInside が優先)。区間の中で待っているのは乗り場での出発保留だけ
            int inside = ZoneOf(lead, dir, count);
            if (inside >= 0)
            {
                if (!holding) return false;
                if (StartBlocked(g, inside, dir, now)) return false;   // 出発保留: 対向が区間内か接近中の間は待ち続ける
                ReleaseHold(g, now, "start-clear");
                return true;
            }

            int wantZone = -1, because = -1;
            float best = 1e9f;
            for (int k = 0; k < narrowFrom.Length && k < narrowTo.Length; k++)
            {
                for (int h = 0; h < MaxGroups; h++)
                {
                    if (h == g || groupState[h] == GroupFree) continue;
                    float hl = LeadDistance(h, now);
                    int hc = CountMembers(h);
                    int hd = groupDir[h] >= 0 ? 1 : -1;
                    // 乗り場で出発保留中 (区間内で待機中) の相手には道を譲らない (相手はこちらが抜けるのを待っている)
                    if (groupHoldDist[h] >= 0f && GroupInZone(hl, hd, hc, narrowFrom[k], narrowTo[k], 0f)) continue;
                    int r = TrafficDecide(lead, dir, count, g, hl, groupDir[h], hc, h, narrowFrom[k], narrowTo[k]);
                    if (r != TrafficHold) continue;
                    float e = EntryAhead(lead, dir, narrowFrom[k], narrowTo[k]);
                    if (e >= 0f && e < best) { best = e; wantZone = k; because = h; }
                }
            }
            if (holding)
            {
                if (wantZone < 0) { ReleaseHold(g, now, "clear"); return true; }
                // 安全弁 (Codex M2 (2)): 相手もこちらを待っている (相互待ち) なら、その区間の出口に近い方を解く (同距離は番号の小さい方)
                if (groupHoldDist[because] >= 0f)
                {
                    float myExit = ExitAhead(lead, dir, narrowFrom[wantZone], narrowTo[wantZone]);
                    int bd = groupDir[because] >= 0 ? 1 : -1;
                    float theirExit = ExitAhead(LeadDistance(because, now), bd, narrowFrom[wantZone], narrowTo[wantZone]);
                    if (myExit < theirExit - 0.01f || (Mathf.Abs(myExit - theirExit) <= 0.01f && g < because))
                    {
                        ReleaseHold(g, now, "mutual");
                        return true;
                    }
                }
                return false;   // 待ち続ける (待機点は最初に決めたまま)
            }
            if (wantZone < 0 || InHoldBan(g, now)) return false;
            // Codex M2 (1): 待機点を入れる前に、各車の位置を保ったまま錨を今の先頭距離・今の時刻へ打ち直す (出発時の錨から測ると
            // 長く走った後に残距離が周長の 75% を超えて 0 になり、先頭が出発位置へ跳んでいた)
            if (Elapsed(g, now) > 0f) { groupAnchorDist[g] = lead; groupAnchorMs[g] = now; }
            float at = TrafficHoldDistance(lead, dir, wantZone);
            if (at < 0f) return true;
            groupHoldDist[g] = at;
            groupHoldSinceMs[g] = now;
            Debug.Log("[KH_Cart] traffic hold g=" + g + " at=" + at + " zone=" + wantZone + " because=" + because + " lead=" + lead);
            return true;
        }

        /// <summary>隊列のどれかの車が入っている狭い区間の番号 (無ければ −1)。</summary>
        private int ZoneOf(float lead, int dir, int count)
        {
            if (narrowFrom == null || narrowTo == null) return -1;
            for (int k = 0; k < narrowFrom.Length && k < narrowTo.Length; k++)
                if (GroupInZone(lead, dir, count, narrowFrom[k], narrowTo[k], 0f)) return k;
            return -1;
        }

        /// <summary>区間 k の出口までの距離 (進行方向)。出口が背後なら 0。</summary>
        private float ExitAhead(float lead, int dir, float from, float to)
        {
            float e = Wrap(dir * ((dir > 0 ? to : from) - lead));
            return e > _L * .5f ? 0f : e;
        }

        /// <summary>乗り場の出発保留を続けるか: 対向グループ (自分も出発保留中のものは除く) が区間 k の中 (±2 m) に居るか、
        /// 待たずに (保留していない) 区間へ approach 以内まで近づいている。</summary>
        private bool StartBlocked(int g, int k, int dir, int now)
        {
            if (narrowFrom == null || narrowTo == null || k < 0 || k >= narrowFrom.Length || k >= narrowTo.Length) return false;
            for (int h = 0; h < MaxGroups; h++)
            {
                if (h == g || groupState[h] == GroupFree) continue;
                int hd = groupDir[h] >= 0 ? 1 : -1;
                if (hd == dir) continue;
                float hl = LeadDistance(h, now);
                int hc = CountMembers(h);
                bool hHold = groupHoldDist[h] >= 0f;
                if (hHold && GroupInZone(hl, hd, hc, narrowFrom[k], narrowTo[k], 0f)) continue;   // 相手も区間内で出発保留中
                if (GroupInZone(hl, hd, hc, narrowFrom[k], narrowTo[k], ZoneExitMargin)) return true;
                float e = EntryAhead(hl, hd, narrowFrom[k], narrowTo[k]);
                if (!hHold && e >= 0f && e <= approach) return true;
            }
            return false;
        }

        /// <summary>Codex M2 (3): 乗り場 (開始距離 start) が狭い区間の中で、対向が区間内か接近中なら、出発を保留する (groupHoldDist = 開始距離)。
        /// ReceiveStart が新しいグループの状態を書いた直後に呼ぶ。保留の解除は TrafficHoldStep (start-clear)。</summary>
        private void TrafficStartGate(int g, float start, int dir, int now)
        {
            if (!_useTrack || narrowFrom == null || narrowTo == null) return;
            int k = ZoneOf(start, dir, 1);
            if (k < 0 || !StartBlocked(g, k, dir, now)) return;
            groupHoldDist[g] = Wrap(start);
            groupHoldSinceMs[g] = now;
            Debug.Log("[KH_Cart] traffic hold g=" + g + " at=" + groupHoldDist[g] + " zone=" + k + " because=start");
        }

        private bool InHoldBan(int g, int now)
        {
            int until = groupHoldBanMs[g];
            return until != 0 && now - until < 0;
        }

        /// <summary>待機を解いて、錨を今の先頭の距離・今の時刻へ打ち直す (止まっていた位置から動き出す。出発の猶予中なら時刻は保つ)。</summary>
        private void ReleaseHold(int g, int now, string why)
        {
            float lead = LeadDistance(g, now);
            bool moving = Elapsed(g, now) > 0f;
            groupHoldDist[g] = -1f;
            groupHoldSinceMs[g] = 0;
            if (moving) { groupAnchorDist[g] = lead; groupAnchorMs[g] = now; }
            Debug.Log("[KH_Cart] traffic release g=" + g + " why=" + why + " at=" + lead);
        }
    }
}
