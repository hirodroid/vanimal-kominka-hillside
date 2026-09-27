using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    // IMP-DOG-020 step 3 (DES-DOG-004 B-1): a wading dog is not leashed. The arbiter (this Owner) reads the synced
    // waterPose, because WaterPlay itself is local to the dog's Owner. The menu shows "水遊び中 — 上がったらつなげます" and
    // asks the dog to come out (KD_TreatPouch.Water). A clip attached by hand reaches ReqAttach only, so the refusal there
    // also asks the dog to come out, turning to that player (_AskWaterExitFor). Attaching again after landing is accepted.
    public partial class KH_LeashManager
    {
        public bool _InWater(int dog)
        {
            return fetchers != null && dog >= 0 && dog < fetchers.Length && fetchers[dog] != null && fetchers[dog].WaterPose != 0;
        }
    }
}
