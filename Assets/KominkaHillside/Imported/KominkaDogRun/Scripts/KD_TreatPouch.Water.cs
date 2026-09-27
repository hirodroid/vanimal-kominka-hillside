using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // IMP-DOG-020 step 3 (DES-DOG-004 B-1 "始め方の案内"): the hand menu's one-liners for the water-play spot.
    //   - The first time in this stay that one of the player's own dogs actually comes off the leash within 12 m of the spot's
    //     entry, the walk tab's result line says "ここで放すと水遊びします。上がったらまたつなげます" (once per stay). Observed from
    //     the synced walkers, so a menu detach and a hand detach (taking the clip off) both count, and the flag is used only
    //     when the note is really shown (Codex 2026-09-27 U-中 x2). It runs after PollWalkPending, so it replaces "放しました".
    //   - Leashing or calling a toy for a dog that is wading is refused on the menu ("水遊び中 — 上がったら…") and asks the
    //     dog to come out (KD_DogFetchController._AskWaterExit). Acceptance is after landing (the arbiters refuse too).
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

        private bool[] _waterMine;
        private float _nextWaterPoll;

        private void PollWaterRelease()
        {
            if (_waterNoteShown || waterSpot == null || manager == null || manager.walkers == null || manager.dogRoots == null) return;
            if (Time.time < _nextWaterPoll) return;
            _nextWaterPoll = Time.time + 0.25f;
            int n = manager.walkers.Length;
            if (_waterMine == null || _waterMine.Length != n)
            {
                _waterMine = new bool[n];
                for (int i = 0; i < n; i++) _waterMine[i] = manager._IsMine(i);
                return;
            }
            for (int i = 0; i < n; i++)
            {
                bool mine = manager._IsMine(i);
                if (_waterMine[i] && !mine && !manager._IsLeashed(i) && i < manager.dogRoots.Length && manager.dogRoots[i] != null
                    && waterSpot._NearEntry(manager.dogRoots[i].position))
                {
                    _waterNoteShown = true;
                    HillsideNote(TabWalk, "course.water.release", "");
                    Redraw();
                }
                _waterMine[i] = mine;
            }
        }
    }
}
