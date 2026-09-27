using UnityEngine;

namespace KominkaDogRun
{
    // IMP-DOG-020 stage 1 (DES-DOG-004 C-1): straight moves (free roaming, play, greeting, treat, nudge, rest, the
    // gathering's "no complete path, walk straight" fallback...) must not walk into the river. They all go through
    // StepToward / Coast, which ask _BlockedByRiver for every step and stop short of the band instead.
    //
    // NavMesh moves (LeashGo / GatherStep) are left as they were: they mark their StepToward with _navStep, since the
    // baked NavMesh already excludes the riverbed (bank ground above the exclusion's top can remain walkable inside the
    // band, and a leash walk must not freeze there). The gathering re-plans its NavMesh path every 0.5 s in GatherStep,
    // so a dog that walked straight to the bank waits there and takes the bridge as soon as a complete path appears.
    //
    // The single place for later exceptions: the water-play spot (KH_WaterSpot, IMP-DOG-020 step 2) will be allowed here
    // for a dog in WaterPlay. Owner-only locomotion; no synced variable.
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_RiverData riverData;
        private bool _navStep;              // true only while LeashGo / GatherStep call StepToward
        const float RiverProbe = 0.1f;      // look this far ahead even when the step itself is tiny

        bool _BlockedByRiver(Vector3 from, Vector3 next)
        {
            if (riverData == null || _navStep) return false;
            // (step 2) if (waterSpot != null && _waterPlay && waterSpot._Contains(next)) return false;
            return riverData._BlocksStep(from, next, KominkaHillside.KH_RiverData.BandMargin);
        }
    }
}
