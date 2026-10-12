using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-020 step 3 (DES-DOG-004 B-1) and RC-14 (破壊監査 2026-09-28, FUNC-04 BUG-02) used to keep a wading dog off the leash:
    // ReqAttach refused a dog _InWater (the synced waterPose or the depth under its synced root) and asked it out, the menu did the
    // same (KD_TreatPouch WalkGateWater, "水遊び中 — 上がったらつなげます"), and a connection that got through anyway was vetoed by
    // the dog Owner, which asked for its release here (ReqWaterRelease).
    // 2026-10-11 の決定 (DES-DOG-010) で上書き (IMP-DOG-053): a dog in the water is leashed and follows its holder there
    // (KD_DogWanderController.HillsideLeashRiver). The refusals and ReqWaterRelease (the sender and the receiver) are retired. What
    // stays here: _InWater itself (KH_PhotoWalkWatcher's "no walk photo in the water"), and ReqWaterRelease's one lasting rule, the
    // roaming home of a dog let go in the river, now applied by every release (Release -> _ReleaseHome; DES-DOG-010 decision 8).
    public partial class KH_LeashManager
    {
        public bool _InWater(int dog)
        {
            if (fetchers == null || dog < 0 || dog >= fetchers.Length || fetchers[dog] == null) return false;
            return fetchers[dog].WaterPose != 0 || fetchers[dog]._WetAtRoot();
        }

        /// <summary>IMP-DOG-053 #10 (DES-DOG-010 decision 8): where a dog let go at p roams from (Release, this Owner; also called by
        /// Course/131 in Edit mode). In the river band or wet (_InWater: the synced pose / the depth under the synced root) = the return
        /// point of the ok bank slice nearest p (own side first, within twice the bank reach: always on the land and on the NavMesh,
        /// checked by Course/91; static data every client holds), else the home it had (ReqWaterRelease's old rule). Elsewhere p, as
        /// before. The dog itself walks out by the water play's strand rule (the nearest usable bank).</summary>
        public Vector3 _ReleaseHome(int dog, Vector3 p)
        {
            if (fetchers == null || dog < 0 || dog >= fetchers.Length || fetchers[dog] == null) return p;
            KH_RiverData rd = fetchers[dog].riverData;
            if (rd == null || !rd._FetchReady()) return _InWater(dog) ? releasedHomes[dog] : p;
            if (!rd._InBand(p, KH_RiverData.BandMargin) && !_InWater(dog)) return p;
            int side = p.x >= rd._CenterX(p.z) ? 1 : -1;
            float reach = 2f * rd._BankReach();
            int code = rd._NearestSlice(p, side, reach, -1);
            if (code < 0) code = rd._NearestSlice(p, -side, reach, -1);
            return code >= 0 ? rd._SliceReturn(code) : releasedHomes[dog];
        }
    }
}
