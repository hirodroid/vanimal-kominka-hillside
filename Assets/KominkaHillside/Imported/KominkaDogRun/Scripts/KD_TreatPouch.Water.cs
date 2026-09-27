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

        // IMP-DOG-020 (Codex 2026-09-27 U-中): the new way in (a free dog within 25 m goes by itself) needs its own hint. A dog this
        // player last had (on their leash; cleared once someone else leashes it) that starts a water play (waterPose 0 -> wet)
        // gets "(name) が水遊びに行きました。上がったらまたつなげます" once per dog per stay. Local only.
        private bool[] _waterTouched, _waterWentShown;
        private int[] _waterPrevPose;
        private float _nextWentPoll;

        private void PollWaterWent()
        {
            if (manager == null || manager.walkers == null || manager.fetchers == null || Time.time < _nextWentPoll) return;
            _nextWentPoll = Time.time + 0.25f;
            int n = manager.walkers.Length;
            if (_waterTouched == null || _waterTouched.Length != n) { _waterTouched = new bool[n]; _waterWentShown = new bool[n]; _waterPrevPose = new int[n]; }
            for (int i = 0; i < n; i++)
            {
                if (manager._IsMine(i)) _waterTouched[i] = true;
                else if (manager._IsLeashed(i)) _waterTouched[i] = false;   // someone else has it now
                int pose = i < manager.fetchers.Length && manager.fetchers[i] != null ? manager.fetchers[i].WaterPose : 0;
                if (pose != 0 && _waterPrevPose[i] == 0 && _waterTouched[i] && !_waterWentShown[i])
                {
                    _waterWentShown[i] = true;
                    HillsideNote(TabWalk, "course.water.went", HillsideDogName(i));
                    Redraw();
                }
                _waterPrevPose[i] = pose;
            }
        }

        private void PollWaterRelease()
        {
            PollWaterWent();
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
                    HillsideNote(TabWalk, "course.water.release2", "");   // 2026-09-27: "near the river" (the trigger no longer needs the release spot)
                    Redraw();
                }
                _waterMine[i] = mine;
            }
        }
    }
}
