using UnityEngine;

namespace KominkaDogRun
{
    // IMP-DOG-020 stage 1 (DES-DOG-004 C-1): the chase and the carry back are straight moves with no path check, and the
    // water surface has no collider (SnapToGround lands on the riverbed). A step into the river band is refused and
    // the Fetch gives up (reason=river-band). The ball itself is brought back to the bank by KH_ToyRecall (C-7/8).
    //
    // The single place for later exceptions: the in-water Fetch states of DES-DOG-004 stage 2 (WaterChase / WaterReturn /
    // WaterExit) will be allowed here inside the water-play spot. Owner-only; no synced variable.
    public partial class KD_DogFetchController
    {
        public KominkaHillside.KH_RiverData riverData;
        const float RiverProbe = 0.1f;

        bool _BlockedByRiver(Vector3 from, Vector3 next)
        {
            if (riverData == null) return false;
            return riverData._BlocksStep(from, next, KominkaHillside.KH_RiverData.BandMargin);
        }

        void GiveUpAtRiver()
        {
            Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState
                + " reason=river-band at=(" + dogRoot.position.x.ToString("F2") + "," + dogRoot.position.y.ToString("F2") + "," + dogRoot.position.z.ToString("F2") + ")");
            GiveUp();
        }
    }
}
