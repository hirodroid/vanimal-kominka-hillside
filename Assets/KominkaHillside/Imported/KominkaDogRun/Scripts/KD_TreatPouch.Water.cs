using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // IMP-DOG-020 step 3 (DES-DOG-004 B-1 "始め方の案内"): the hand menu's one-liners for the water-play spot.
    //   - The first time in this stay that the player's own detach lands within 12 m of the spot's entry, the detach result
    //     line says "ここで放すと水遊びします。上がったらまたつなげます" instead of the usual "放しました" (once per stay).
    //   - Leashing or calling a toy for a dog that is wading is refused on the menu ("水遊び中 — 上がったら…") and asks the
    //     dog to come out (KD_DogFetchController._AskWaterExit). Acceptance is after landing (the arbiters refuse too).
    // A detach by hand (taking the clip off without the menu) does not show the first line.
    public partial class KD_TreatPouch
    {
        public KominkaHillside.KH_WaterSpot waterSpot;
        public const int WalkGateWater = 10;
        private bool _waterNoteShown;

        private bool WaterBusy(int dog)
        {
            return manager != null && manager._InWater(dog);
        }

        private void WaterAsk(int dog)
        {
            if (manager == null || manager.fetchers == null || dog < 0 || dog >= manager.fetchers.Length || manager.fetchers[dog] == null) return;
            manager.fetchers[dog]._AskWaterExit();
        }

        private string WaterReleaseKey(int dog, string fallback)
        {
            if (_waterNoteShown || waterSpot == null || manager == null || manager.dogRoots == null
                || dog < 0 || dog >= manager.dogRoots.Length || manager.dogRoots[dog] == null) return fallback;
            if (!waterSpot._NearEntry(manager.dogRoots[dog].position)) return fallback;
            _waterNoteShown = true;
            return "course.water.release";
        }
    }
}
