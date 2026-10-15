using VRC.SDKBase;

namespace KominkaHillside
{
    public partial class KH_LeashManager
    {
        public KH_HalloweenParade parade;
        public void _AdoptParadeHomes()
        {
            if (parade == null || !parade.Committed || !Networking.IsOwner(gameObject)) return;
            for (int i = 0; i < walkers.Length; i++)
                if (!KH_HalloweenParade.DogInMask(parade.RecoveryFailedMask, i) && walkers[i] < 0 && dogRoots[i] != null)
                    releasedHomes[i] = _ReleaseHome(i, dogRoots[i].position);
            ApplyHomes(); RequestSerialization();
        }
        public void _EndForParade()
        {
            if (parade == null || !parade.Committed || !Networking.IsOwner(gameObject)) return;
            bool changed = false;
            for (int i = 0; i < walkers.Length; i++) if (walkers[i] >= 0) { Release(i); changed = true; }
            if (changed) RequestSerialization();
        }
    }
}
