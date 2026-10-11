using UdonSharp;
using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M3 (Codex M3 レビュー (2)): 駐車枠。Parked のカートが通過車 (千鳥 右 0.6 m・車幅 0.82 m) や橋の中央走行と重ならず、
    /// 連続して停めたカート同士も重ならない位置。どちらも副作用なし (Probe が Reflection で呼ぶ)。
    /// - 距離 <see cref="ParkSlotDistance"/> (Owner が RemoveCart で決めて cartParkDist に書く): 狭い区間 (narrowFrom..narrowTo) の中なら
    ///   進行方向側の端 + 3 m まで進む (乗員はそこで降りる)。同じ向きの他の Parked と 1.6 m 未満なら後ろへ 1.6 m ずつずらす (最大 8 回)。
    /// - 横 <see cref="ParkSlotLateral"/> (各クライアントが同じ式で計算。NavMesh は全員同じ): class 0 は右 1.6 m、class 1 は 1.2 m、
    ///   候補点が NavMesh から 0.5 m 以内でなければ 0.3 m ずつ内側へ (下限 1.0 m)。</summary>
    public partial class KH_CartManager
    {
        public const float ParkLatWide = 1.6f, ParkLatMid = 1.2f, ParkLatMin = 1.0f, ParkLatStep = 0.3f, ParkSampleRadius = 0.5f;
        public const float ParkSpacing = 1.6f, ParkNarrowExit = 3f;
        public const int ParkShiftMax = 8;

        /// <summary>駐車距離。wantDist = 止まった距離、dir = 進行方向 (±1)、self = 自分のカート番号 (他の Parked との間隔の判定から外す。−1 で全部見る)。</summary>
        public float ParkSlotDistance(float wantDist, int dir, int self)
        {
            if (!EnsureRoute()) return wantDist;
            int d1 = dir < 0 ? -1 : 1;
            float d = Wrap(wantDist);
            // 狭い区間の中なら出口 + 3 m まで進む (連なる区間は続けて抜ける。最大 3 回)
            if (_useTrack && narrowFrom != null && narrowTo != null)
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    int k = ZoneAt(d);
                    if (k < 0) break;
                    d = Wrap((d1 > 0 ? narrowTo[k] : narrowFrom[k]) + d1 * ParkNarrowExit);
                }
            }
            // 同じ向き (= 道の同じ側) の他の Parked と近ければ後ろへずらす
            for (int n = 0; n < ParkShiftMax; n++)
            {
                bool clash = false;
                for (int c = 0; c < MaxCarts && !clash; c++)
                {
                    if (c == self || cartState[c] != CartParked) continue;
                    int cd = cartParkDir[c] < 0 ? -1 : 1;
                    if (cd != d1) continue;
                    if (Mathf.Abs(WrapSigned(cartParkDist[c] - d)) < ParkSpacing) clash = true;
                }
                if (!clash) break;
                d = Wrap(d - d1 * ParkSpacing);
            }
            return d;
        }

        /// <summary>駐車距離 dist・向き dir での右への横位置 (m)。</summary>
        public float ParkSlotLateral(float dist, int dir)
        {
            if (!EnsureRoute()) return ParkLatMin;
            int d1 = dir < 0 ? -1 : 1;
            float x = Wrap(dist);
            float wide = ParkLatWide;
            if (_useTrack)
            {
                int i = TrackSegment(x);
                int cls = trackClass != null && i < trackClass.Length ? trackClass[i] : 0;
                if (cls == 1) wide = ParkLatMid;
                else if (cls >= 2) wide = ParkLatMin;
            }
            else if (!route._DetourAtDistance(x)) wide = ParkLatMin;
            Vector3 p = TrackPoint(x);
            Vector3 f = TrackDirection(x) * (float)d1;
            Vector3 right = new Vector3(f.z, 0f, -f.x);
            NavMeshHit hit;
            for (float lat = wide; lat >= ParkLatMin - 0.001f; lat -= ParkLatStep)
                if (NavMesh.SamplePosition(p + right * lat, out hit, ParkSampleRadius, NavMesh.AllAreas)) return lat;
            return ParkLatMin;
        }

        /// <summary>距離 x を含む狭い区間の番号 (無ければ −1。0 を跨ぐ区間も可)。</summary>
        private int ZoneAt(float x)
        {
            if (narrowFrom == null || narrowTo == null) return -1;
            for (int k = 0; k < narrowFrom.Length && k < narrowTo.Length; k++)
                if (Wrap(x - narrowFrom[k]) <= Wrap(narrowTo[k] - narrowFrom[k])) return k;
            return -1;
        }
    }
}
