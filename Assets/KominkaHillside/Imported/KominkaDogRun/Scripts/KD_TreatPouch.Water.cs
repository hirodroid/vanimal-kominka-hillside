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
        private bool _waterWentDone;   // IMP-DOG-034 C1-misc: every dog's "went" note shown (once per stay, never re-armed)

        private void PollWaterWent()
        {
            // IMP-DOG-034 C1-misc: once every dog's note has been shown the loop can no longer show anything (it only updates
            // _waterTouched / _waterPrevPose, which are read solely for dogs whose note is still unshown), so stop polling.
            if (_waterWentDone) return;
            // IMP-DOG-036 S5: the clock first (own field), the manager's references only when a poll is due. A null reference returns
            // without arming the clock, as before, so the result is the same.
            float now = Time.time;
            if (now < _nextWentPoll) return;
            if (manager == null || manager.walkers == null || manager.fetchers == null) return;
            _nextWentPoll = now + 0.25f;
            // IMP-DOG-036 S5: why this loop rarely ends: it finishes only once all n dogs' notes were shown, and a note needs that
            // player to have had that dog and that dog to go into the water. It cannot stop earlier without a notice: any dog can
            // become this player's at any moment (manager.walkers arrives by deserialization; nothing tells the pouch), and the note
            // on the poll where it becomes theirs compares with the pose seen on the poll before. So it keeps its 4 Hz, and each poll
            // reads the walkers array once and compares ids here instead of calling manager._IsMine / _IsLeashed per dog (same
            // tests: _IsLeashed = walkers[i] >= 0, _IsMine = a valid local player and walkers[i] == its playerId).
            int[] walkers = manager.walkers;
            KD_DogFetchController[] fetchers = manager.fetchers;
            VRCPlayerApi local = Networking.LocalPlayer;
            int me = Utilities.IsValid(local) ? local.playerId : -1;
            int n = walkers.Length;
            if (_waterTouched == null || _waterTouched.Length != n) { _waterTouched = new bool[n]; _waterWentShown = new bool[n]; _waterPrevPose = new int[n]; }
            int shown = 0;
            for (int i = 0; i < n; i++)
            {
                int w = walkers[i];
                if (w >= 0 && me >= 0 && w == me) _waterTouched[i] = true;
                else if (w >= 0) _waterTouched[i] = false;   // someone else has it now
                KD_DogFetchController f = i < fetchers.Length ? fetchers[i] : null;
                int pose = f != null ? f.WaterPose : 0;
                if (pose != 0 && _waterPrevPose[i] == 0 && _waterTouched[i] && !_waterWentShown[i])
                {
                    _waterWentShown[i] = true;
                    HillsideNote(TabWalk, "course.water.went", HillsideDogName(i));
                    Redraw();
                }
                _waterPrevPose[i] = pose;
                if (_waterWentShown[i]) shown++;
            }
            if (n > 0 && shown == n) _waterWentDone = true;   // IMP-DOG-034 C1-misc
        }

        private void PollWaterRelease()
        {
            PollWaterWent();
            // IMP-DOG-036 S5: own flag and clock before the references (a null returns without arming the clock, as before), and the
            // walkers array read once per poll instead of _IsMine / _IsLeashed per dog (same tests, see PollWaterWent).
            if (_waterNoteShown) return;
            float now = Time.time;
            if (now < _nextWaterPoll) return;
            if (waterSpot == null || manager == null || manager.walkers == null || manager.dogRoots == null) return;
            _nextWaterPoll = now + 0.25f;
            int[] walkers = manager.walkers;
            Transform[] roots = manager.dogRoots;
            VRCPlayerApi local = Networking.LocalPlayer;
            int me = Utilities.IsValid(local) ? local.playerId : -1;
            int n = walkers.Length;
            if (_waterMine == null || _waterMine.Length != n)
            {
                _waterMine = new bool[n];
                for (int i = 0; i < n; i++) _waterMine[i] = walkers[i] >= 0 && me >= 0 && walkers[i] == me;
                return;
            }
            for (int i = 0; i < n; i++)
            {
                int w = walkers[i];
                bool mine = w >= 0 && me >= 0 && w == me;
                if (_waterMine[i] && !mine && w < 0 && i < roots.Length && roots[i] != null
                    && waterSpot._NearEntry(roots[i].position))
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
