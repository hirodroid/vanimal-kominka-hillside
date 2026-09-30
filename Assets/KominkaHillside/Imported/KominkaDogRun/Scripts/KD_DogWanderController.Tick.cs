using UnityEngine;

namespace KominkaDogRun
{
    // IMP-DOG-034 stage 1: thin the idle dog's Update when the frame rate is low.
    //
    // Policy (user decision 2026-09-30): lightness over realtime below 50 fps; unchanged at >= 50 fps. The divider is
    // decided here from this client's Time.smoothDeltaTime (no cross-behaviour reads, every script decides for itself):
    //   <= 20 ms (>= 50 fps) -> 1 (every frame, bit-identical to before), <= 28.57 ms (>= 35 fps) -> 2, slower -> 3.
    // Going back to a finer divider needs 1 ms more than the threshold (hysteresis), so a client sitting at ~50 fps does
    // not flip between 1 and 2 every few frames. The six dogs are phased by dogIndex so they do not tick on the same frame.
    public partial class KD_DogWanderController
    {
        const float TickDt1 = 0.020f;     // <= this: divider 1 (>= 50 fps)
        const float TickDt2 = 0.02857f;   // <= this: divider 2 (>= 35 fps); slower: 3
        const float TickHyst = 0.001f;    // back to a finer divider only this far below the threshold

        [Tooltip("0 = fps から自動 (既定)。1〜3 で固定 (Editor の計測・ハーネス用。本番は 0 のまま)")]
        public int tickDividerOverride = 0;
        private int _tickDiv = 1;
        private float _dt;                // this tick's delta time: Time.deltaTime + the skipped frames' time
        private float _dtCarry;           // time of the frames skipped since the last tick
        private bool _tickedThisFrame = true;   // LateUpdate: did Update run its body this frame
        private bool _inTick;             // inside UpdateTick (Dt() returns _dt only there)
        private bool _candyTicked;        // last tick's UpdateCandyPerformance result (TickBusy)

        /// <summary>Skip the whole Update on off-phase frames while the dog is idle (IMP-DOG-034).
        ///
        /// Only when the divider is above 1 (under 50 fps) and the dog is idle (TickBusy false) does a frame get skipped: its
        /// delta time is carried and the next tick moves / turns / accelerates with the sum, so speeds are unchanged and only
        /// the steps get coarser. The per-frame flags lowered at the top of UpdateTick keep the last tick's conclusion on a
        /// skipped frame, and LateUpdate reads them (it also skips). Busy states are never skipped because Wander is tied to
        /// Fetch, KH_FrisbeeCatch, KH_DogWash and the water play through Time.frameCount handshakes ("seen this frame" /
        /// "seen last frame": _waterSeenFrame, _washLastFrame, _waterFetchFrame, ...), and the gaze / tail injections into
        /// KD_DogPetController are valid for one frame only; skipping any of those would read as preempted / flickering.
        /// So busy = the old per-frame behaviour.</summary>
        void Update()
        {
            int div = TickDivider();
            if (div > 1 && ((Time.frameCount + dogIndex) % div) != 0 && !TickBusy())
            {
                _dtCarry += Time.deltaTime;
                _tickedThisFrame = false;
                return;
            }
            _dt = Time.deltaTime + _dtCarry;
            _dtCarry = 0f;
            _tickedThisFrame = true;
            _inTick = true;
            UpdateTick();
            _inTick = false;
        }

        /// <summary>The delta time for motion and integration. Inside this dog's own tick it is _dt (the skipped frames
        /// included); when another behaviour calls in (Fetch's _WaterFetchStep, KD_DogPack, the disc's water legs...) it
        /// is the plain Time.deltaTime, exactly as before.</summary>
        float Dt()
        {
            return _inTick ? _dt : Time.deltaTime;
        }

        int TickDivider()
        {
            if (tickDividerOverride > 0) return tickDividerOverride > 3 ? 3 : tickDividerOverride;
            float sdt = Time.smoothDeltaTime;
            int d = _tickDiv;
            if (d <= 1)
            {
                if (sdt > TickDt1) d = sdt > TickDt2 ? 3 : 2;
            }
            else if (d == 2)
            {
                if (sdt > TickDt2) d = 3;
                else if (sdt < TickDt1 - TickHyst) d = 1;
            }
            else if (sdt < TickDt2 - TickHyst)
            {
                d = sdt < TickDt1 - TickHyst ? 1 : 2;
            }
            _tickDiv = d;
            return d;
        }

        /// <summary>Is the dog doing anything that must run every frame? Read on off-phase frames only, before UpdateTick
        /// lowers its per-frame flags, so the own-field checks see the last tick's conclusion. When in doubt a state counts
        /// as busy. Own fields first; the cross-behaviour reads (Fetch, disc) last.</summary>
        bool TickBusy()
        {
            // Leash (and a leash vetoed in the water). _wasLeashed is the last tick's view of KH_LeashManager; the travel
            // ticket only applies to a leashed dog, so it is covered here too.
            if (_wasLeashed || _waterLeashVeto) return true;
            // Water play (Owner) and the swim height / nose-up pitch / hips bob still easing back after the water.
            if (waterState != WaterOff || _waterSwimming || _swimYValid || _swimPitchNow != 0f || _hipsWrote) return true;
            // Being washed (the wash branches raise it; _washLastFrame handshake).
            if (_washActive) return true;
            // Candy performance / mount / exit hold, and a pending petting resume.
            if (candyPerformanceActive || _candyTicked || _candyPetResumePending) return true;
            // Branches that inject the one-frame gaze / tail into the pet controller, or bind the dog to others.
            if (_greetingActive || _nudging || _treatBusy || _activityBusy) return true;
            int b = _selectedBehavior;
            if (b == BehaviorTreat || b == BehaviorNudge || b == BehaviorGreeting || b == BehaviorActivity
                || b == BehaviorPlay || b == BehaviorWater) return true;
            // Rest: walking to the water bowl looks at it, drinking wags the tail. Lying down is idle.
            if (b == BehaviorRest && (_restPhase == RestPhaseToWater || _restPhase == RestPhaseDrink)) return true;
            // Called to sleep beside someone: walking there looks at the caller. Once frozen (asleep) it is idle.
            if (b == BehaviorSleep && !_sleepFrozen) return true;
            // A companion stretch writes a running CallTime every frame (LateUpdate)
            if (_writtenCallTime != 0f) return true;
            // Fetch / disc (cross-behaviour reads). No Fetch at all keeps the old per-frame path.
            if (fetchController == null) return true;
            if (!fetchController.IsIdle || fetchController.IsWaterFetching || fetchController.WaterPose != 0) return true;
            if (FrisbeeActive) return true;
            return false;
        }
    }
}
