using UdonSharp;
using UnityEngine;
using KominkaDogRun;

namespace KominkaHillside
{
    // DES-LIV-001: the bridge from the shared live clock to the local night. Every frame it derives the live's darkness
    // from KD_LiveSystem's synced clock (Elapsed / CurrentDuration / CurrentDarkness / CurrentLiveId) and writes it to
    // KH_NightMode.liveDarkness. KH_NightMode is the only writer of the sky and the overlay and takes max(manual, live),
    // so a live never multiplies on top of a night-mode viewer (KD_LiveNight is not used on the hillside: it would add a
    // second overlay and write the sky itself).
    //
    // Pure function of the synced clock, nothing synced here (0 synced variables): a late joiner starts from the darkness
    // of that moment, and the end of the live brings it back to 0 without any event.
    //
    // Shape (docs/plans/2026-09-26-hillside-live-wiring.md D2 (b) / D3), t = seconds since the live started:
    //   t < NightIn (the 3 s before the Timeline)      0
    //   song time s = t - NightIn, 0 <= s < duration    linear 0 -> 1 over rampSeconds[liveId] (the peak of the song), then 1
    //   after the song                                  the value at the end fades to 0 over NightOut (3 s)
    // multiplied by the show's cap (KD_LiveSystem.CurrentDarkness). useKominkaFade = true switches to KominkaDogRun's
    // shape (3 s fade-in from t = 0, then the cap) for comparison. A liveId without a ramp entry also uses that shape.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_LiveNight : UdonSharpBehaviour
    {
        [Header("Wiring (Tools/KominkaHillside/Menu/26a Setup Live writes these)")]
        public KD_LiveSystem live;
        public KH_NightMode nightMode;
        [Header("Values (code is the source: 26a writes them every run)")]
        [Tooltip("Seconds from the head of the song until the darkness reaches the cap, per liveId (index = liveId). <= 0 or missing = KominkaDogRun's 3 s fade-in")]
        public float[] rampSeconds;
        [Tooltip("true = KominkaDogRun's shape (3 s fade-in, then the cap) for comparison. 26a writes DefaultUseKominkaFade")]
        public bool useKominkaFade;

        /// <summary>KH_HillsideDusk 002 (124.0 s, 60 BPM): the night arrives at 80 s, where the harmony opens and the leaves turn
        /// into stars (the peak section is 72-104 s). Coordinator decision 2026-09-26.</summary>
        public const float DefaultRampSeconds = 80f;   // 001 was 88 s
        public const bool DefaultUseKominkaFade = false;

        private float _written = -1f;   // last value written to nightMode.liveDarkness (-1 = never)

        void Update()
        {
            if (nightMode == null) return;
            float d = LiveDarkness();
            if (d == _written) return;   // idle: nothing to write
            _written = d;
            nightMode.liveDarkness = d;
        }

        /// <summary>Pure query (no side effects) for local readers and debugging. Underscore = not callable over the network.</summary>
        public float _LiveDarkness() { return LiveDarkness(); }

        private float LiveDarkness()
        {
            if (live == null) return 0f;
            int id = live.CurrentLiveId;
            if (id < 0) return 0f;
            return Phase(id, live.Elapsed, live.CurrentDuration) * Mathf.Clamp01(live.CurrentDarkness);
        }

        /// <summary>0..1 from the synced clock only (reads no local state besides the configured constants).</summary>
        private float Phase(int id, float t, float duration)
        {
            if (t < 0f) return 0f;
            float nightIn = KD_LiveSystem.NightInSeconds;
            float end = nightIn + duration;
            float ramp = RampOf(id);
            if (t < end) return RampAt(t, nightIn, ramp);
            float atEnd = RampAt(end, nightIn, ramp);
            float outSec = KD_LiveSystem.NightOutSeconds;
            float o = outSec > 0.01f ? (t - end) / outSec : 1f;
            return o >= 1f ? 0f : atEnd * (1f - o);
        }

        /// <summary>Darkness while the live runs (before its end). ramp &lt;= 0 = KominkaDogRun's fade-in over NightIn from t = 0.</summary>
        private float RampAt(float t, float nightIn, float ramp)
        {
            if (ramp <= 0f) return nightIn > 0.01f ? Mathf.Clamp01(t / nightIn) : 1f;
            float s = t - nightIn;   // song time
            if (s <= 0f) return 0f;
            return Mathf.Clamp01(s / ramp);
        }

        private float RampOf(int id)
        {
            if (useKominkaFade) return 0f;
            if (rampSeconds == null || id < 0 || id >= rampSeconds.Length) return 0f;
            return rampSeconds[id];
        }
    }
}
