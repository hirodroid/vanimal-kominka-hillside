using UdonSharp;
using VRC.SDKBase;

namespace KominkaHillside
{
    // The washing permission belongs to KH_WashStationCall._CallerId(), the Wash button requester.
    // Joint strokes do not register additional consent participants (user decision 2026-10-14).
    public partial class KH_DogWash
    {
        public KH_HalloweenParadeDog paradeDog;
        [UdonSynced] private int paradeCleanRequest;
        private bool _paradeDidClean;
        public bool _ParadeCleaned(int id) { return paradeCleanRequest == id && _paradeDidClean; }
        private bool ParadeStrokeAllowed(int player)
        {
            // Consent and reservation never interrupt an ongoing wash. Only the committed end blocks strokes.
            return paradeDog == null || (!paradeDog.Committed && !paradeDog.Isolated);
        }
        public bool _CleanForParade(int id)
        {
            if (!_ready || !Networking.IsOwner(gameObject) || paradeDog == null || !paradeDog.Committed
                || paradeDog.parade == null || paradeDog.parade.RequestId != id) return false;
            if (paradeCleanRequest == id) return true;
            bool washing = _WashHold() || paradeDog.parade._WasWashing(dogIndex);
            if (washCalls != null) for (int i = 0; i < washCalls.Length; i++)
                if (washCalls[i] != null && washCalls[i]._HeldDog() == dogIndex) washing = true;
            _paradeDidClean = washing;
            if (washing)
            {
                _cellsDirty = true;
                for (int c = 0; c < CellCount; c++) { _dirt[c] = 0f; _foam[c] = 0f; _wet[c] = 0f; }
                cleanArmed = false; ClearArm(); ClearBits(quietBits); riverBlocksEdge = false;
                // Reset epoch rejects old packets. Never award a wash-completion reward.
                BumpEpoch();
            }
            _ReleaseHold(); paradeCleanRequest = id;
            _syncDue = true; _nextSync = UnityEngine.Time.time; Wake();
            return true;
        }
    }
}
