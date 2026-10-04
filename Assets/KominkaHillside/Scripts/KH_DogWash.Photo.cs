using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-WLD-011 (2026-10-04 user decision, plan A): the wash milestones for the auto photos. Local only: nothing here is synced and
    // nothing is sent. Only the client whose own strokes wash this dog takes these photos (its own album); a client that only watches
    // never starts a session. Two players washing one dog each run their own session on their own client.
    //
    // Session: starts at this client's first counted stroke on this dog (Stroke(counts)); a foam stroke (sponge / bottle / hand) after
    // the "done" photo, or any own stroke after washShotSessionGap without one, starts a new wash (another dog has its own KH_DogWash).
    // Milestones, each at most once per session (photoTaken bits):
    //   foam  : avg foam over the valid cells >= washShotFoamAvg (checked at every EvaluateState), own stroke within washShotRecent
    //   rinse : the own shower has run washShotRinseSeconds of contact while the dog's avg foam >= washShotRinseFoam (there is foam to
    //           rinse; wetting a dog before soaping does not count), checked in the own shower stroke
    //   done  : the clean edge (synced cleanSerial: the Owner's EvaluateState / a non-Owner's OnDeserialization), or - when the wash
    //           never armed the clean edge (cleanArmed false: too little foam / dirt) - the shake edge (shakeSerial) after a foam or
    //           rinse photo with avg foam < cleanFoamThreshold; own stroke within washShotRecent
    // The photo itself: voiceDog.photoDirector (this dog's KD_DogWanderController, wired by Photo/1 like the frisbee hook) ->
    // KD_PhotoDirector._WashShoot. Unwired = nothing happens. A refusal (photos OFF, camera busy) is retried after 1 s for foam / rinse;
    // a refused "done" edge is not retried (one edge).
    public partial class KH_DogWash
    {
        [Header("Photos (IMP-WLD-011, prototype values)")]
        [Tooltip("泡の写真: 有効セルの泡の平均がこれ以上 (WashShakeFoamMax 0.15 = 犬が「泡を流した」とみなす境と同じ値)")]
        public float washShotFoamAvg = 0.15f;
        [Tooltip("すすぎの写真: 自分のシャワーがこの秒数 (接触の合計) 当たったら")]
        public float washShotRinseSeconds = 2f;
        [Tooltip("すすぎの秒数は、犬の泡の平均がこれ以上の間だけ数える (泡を付ける前の濡らしでは撮らない)")]
        public float washShotRinseFoam = 0.05f;
        [Tooltip("泡・洗い上がりの写真は、自分の最後のストロークからこの秒数以内だけ (洗っている人が犬のそばにいる)")]
        public float washShotRecent = 10f;
        [Tooltip("自分のストロークがこの秒数無ければ、次のストロークから別の洗い (数え直し)")]
        public float washShotSessionGap = 60f;

        const int PhotoFoam = 1, PhotoRinse = 2, PhotoDone = 4;

        // ---- local state (harness reads) ----
        private bool _photoActive;           // a session of this client's washing
        private int _photoTaken;             // PhotoFoam | PhotoRinse | PhotoDone of this session
        private float _photoLastLocal = -1e9f;
        private float _photoShowerSec;
        private float _photoNextTry;
        private int _photoCleanSeen;
        private int _photoShakeSeen;
        private int _photoAsked;             // photos the director accepted (all sessions)
        private string _photoLastReason = "";

        /// <summary>The own counted stroke (Stroke, counts = true): the session and the rinse milestone.</summary>
        void PhotoStroke(int tool, float sec)
        {
            float now = Time.time;
            bool foamTool = tool == ToolSponge || tool == ToolBottle || tool == ToolHand;
            if (!_photoActive || now - _photoLastLocal > washShotSessionGap || (foamTool && (_photoTaken & PhotoDone) != 0))
            {
                _photoActive = true;
                _photoTaken = 0;
                _photoShowerSec = 0f;
                _photoNextTry = 0f;
                _photoCleanSeen = cleanSerial;   // an edge from before this wash is not this wash's "done"
                _photoShakeSeen = shakeSerial;
            }
            _photoLastLocal = now;
            if (tool != ToolShower || (_photoTaken & (PhotoRinse | PhotoDone)) != 0) return;
            if (_avgFoam >= washShotRinseFoam) _photoShowerSec += sec;
            if (_photoShowerSec >= washShotRinseSeconds && now >= _photoNextTry) PhotoAsk(PhotoRinse, KominkaDogRun.KD_PhotoDirector.WashShotRinse);
        }

        /// <summary>Every client: EvaluateState (every evaluation), OnDeserialization (non-Owner) and _RequestShake (Owner). Records the
        /// clean / shake edges and, in this client's own session, asks for the foam and done photos. Local only (`_`).</summary>
        public void _PhotoEdges()
        {
            bool clean = cleanSerial != _photoCleanSeen;
            bool shake = shakeSerial != _photoShakeSeen;
            _photoCleanSeen = cleanSerial;
            _photoShakeSeen = shakeSerial;
            if (!_photoActive) return;
            float now = Time.time;
            if (now - _photoLastLocal > washShotSessionGap) { _photoActive = false; return; }
            if (now - _photoLastLocal > washShotRecent) return;
            if ((_photoTaken & PhotoFoam) == 0 && (_photoTaken & PhotoDone) == 0 && _avgFoam >= washShotFoamAvg && now >= _photoNextTry)
                PhotoAsk(PhotoFoam, KominkaDogRun.KD_PhotoDirector.WashShotFoam);
            if ((_photoTaken & PhotoDone) != 0) return;
            bool done = clean || (shake && !cleanArmed && (_photoTaken & (PhotoFoam | PhotoRinse)) != 0 && _avgFoam < cleanFoamThreshold);
            if (done) PhotoAsk(PhotoDone, KominkaDogRun.KD_PhotoDirector.WashShotDone);
        }

        void PhotoAsk(int bit, int milestone)
        {
            KominkaDogRun.KD_PhotoDirector d = voiceDog != null ? voiceDog.photoDirector : null;
            if (d == null) { _photoTaken |= bit; _photoLastReason = "no photo director (Photo/1 not run)"; return; }
            d.washDog = dogIndex;
            d.washMilestone = milestone;
            bool ok = d._WashShoot();
            if (ok) _photoAsked++;
            if (ok || bit == PhotoDone)
            {
                // "done" is one edge: taken whether or not the director could shoot it (the next wash counts again)
                _photoTaken |= bit;
                _photoLastReason = ok ? "" : "done refused (not retried)";
                return;
            }
            _photoNextTry = Time.time + 1f;
            _photoLastReason = "refused";
        }
    }
}
