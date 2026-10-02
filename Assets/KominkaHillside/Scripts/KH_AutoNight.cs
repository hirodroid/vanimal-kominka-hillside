using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-ENV-005 (recommended plan R, user decision 2026-10-02): the instance's shared night timeline for the Auto mode.
    //
    // One synced int: _epochMs = the server time (ms) at which the shared clock was started, i.e. when the start was written:
    // ~StartDelaySeconds after the instance's first player joined, or later if a successor Owner had to start it. Everything
    // else is derived on every client from that value and the server clock (vrc-networking §6: each client computes only
    // when the output is a pure function of synced input): the darkness, the progress and who is in Auto are never synced.
    // Same shape as KH_LiveNight (the live clock -> liveDarkness).
    //
    //   writer      the Owner only (default Owner = instance master; SetOwner is never called), once, while _epochMs is 0,
    //               and only through _TryStart and its gate (StartAllowed). _epochMs == 0 means both "never started" and
    //               "not received yet", so a 0 may be trusted only when this client can know it is real:
    //                 (a) _firstPlayer: Owner at its own OnPlayerJoined with no other player in the instance (a late joiner is
    //                     never the Owner of a scene object while others are present, and is never alone), or
    //                 (b) _received: this object's state has arrived (OnDeserialization), or
    //                 (c) Networking.IsNetworkSettled: the initial state of the instance has arrived for this client
    //                     (exposed to Udon in SDK 3.10.5: VRCSDKBaseNetworking.__get_IsNetworkSettled__SystemBoolean), or
    //                 (d) fallback: the gate has been closed for FallbackSeconds (120 s) while Owner with 0.
    //               Otherwise _TryStart retries every RetrySeconds (one pending retry at most). Every entry - the join-time
    //               delay (StartDelaySeconds after the local OnPlayerJoined; not in Start: a Start serialization never
    //               reaches late joiners), the handover delay (HandoverDelaySeconds after OnOwnershipTransferred) and the
    //               retries - goes through the same gate, so a join-time event still pending when the ownership arrives
    //               cannot write before the state is known.
    //   handover    old Owner wrote X and left; the new Owner has not received X yet: the gate is closed (not first, not
    //               received, not settled), so it waits. X arrives -> _epochMs = X (non-zero) -> every later _TryStart returns
    //               without writing: the received value wins. A local write can only exist if the gate opened, and then the
    //               0 was real (nobody had written) - except the 120 s fallback, which accepts a reset only when the state
    //               never arrived at all (e.g. the old Owner left before its serialization reached anyone); the clock then
    //               still starts, and it never stays unstarted forever.
    //   late joiner shows 0 (day) until the value arrives, then KH_NightMode fades to the current darkness (AutoJumpFade).
    //   time base   Networking.GetServerTimeInMilliseconds (shared by all clients). The difference is an int subtraction
    //               (correct up to ~24.8 days, same as KD_TreatBox.EarlierMs); a large negative difference (the int wrapped)
    //               is read as "long past", i.e. night. Known limit: the server ms counter itself wraps every 2^32 ms
    //               (~49.7 days), so an instance alive for ~49.7 days would read the difference as small again (day).
    //
    // Shape (prototype values, tuned on device): day for daySeconds (0), then smoothstep 0 -> nightDarkness over
    // duskSeconds, then it stays at nightDarkness (no cycle). 0.8 instead of 1 keeps the path, dogs and menu readable.
    //
    // Per frame: nothing. A delayed event every tickSeconds (1 s) recomputes the value and writes
    // KH_NightMode.autoDarkness only when it changed (constant before the dusk and after the night is reached).
    // The dusk moves at most ~0.0007 per second, so a 1 s step is invisible. OnDeserialization recomputes at once.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_AutoNight : UdonSharpBehaviour
    {
        [Header("Wiring (Tools/KominkaHillside/Night/1 Setup writes this)")]
        public KH_NightMode nightMode;
        [Header("Timeline (code is the source: Night/1 Setup writes the Default* constants every run; a harness may shorten them at runtime)")]
        public float daySeconds = DefaultDaySeconds;       // stays day (0) this long after the first player arrived
        public float duskSeconds = DefaultDuskSeconds;     // then darkens over this long (smoothstep)
        public float nightDarkness = DefaultNightDarkness; // and stays here (0..1)
        public float tickSeconds = DefaultTickSeconds;     // recompute interval

        public const float DefaultDaySeconds = 600f;      // 10 min (user decision 2026-10-02, prototype value)
        public const float DefaultDuskSeconds = 1800f;    // 30 min (same)
        public const float DefaultNightDarkness = 0.8f;   // same
        public const float DefaultTickSeconds = 1f;
        public const float StartDelaySeconds = 2f;
        public const float HandoverDelaySeconds = 5f;
        public const float RetrySeconds = 5f;       // gate closed: check again this often
        public const float FallbackSeconds = 120f;  // gate closed this long while Owner with 0: start anyway (see the header)
        /// <summary>A difference below this (ms) is an int wrap after ~24.8 days, not clock jitter: read as night.</summary>
        public const int WrapGuardMs = -600000;

        [UdonSynced] private int _epochMs;   // 0 = not started yet
        private float _written = -1f;        // last value written to nightMode.autoDarkness (-1 = never)
        private bool _looping;
        // Start gate (see the header). All local-only, never serialized; the harness writes them to simulate the orders.
        private bool _firstPlayer;           // (a) Owner at its own join, alone in the instance
        private bool _received;              // (b) OnDeserialization has run on this client
        private float _blockedSince = -1f;   // Time.time when the gate first refused (-1 = not waiting)
        private bool _retryPending;
        private bool _debugUnsettled;        // harness only: treat IsNetworkSettled as false (private, never in the scene)

        // net: callable-ok: pure query (has the shared clock started on this client)
        public bool Started { get { return _epochMs != 0; } }

        void Start()
        {
            if (nightMode == null) Debug.LogError("[KH_AutoNight] nightMode is not wired; run Tools/KominkaHillside/Night/1 Setup.");
            if (!_looping) { _looping = true; SendCustomEventDelayedSeconds(nameof(_Tick), Mathf.Max(0.1f, tickSeconds)); }
            Recompute();
        }

        /// <summary>The 1 Hz loop. Local only (underscore); started once in Start.</summary>
        public void _Tick()
        {
            Recompute();
            SendCustomEventDelayedSeconds(nameof(_Tick), Mathf.Max(0.1f, tickSeconds));
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            if (Networking.IsOwner(gameObject) && VRCPlayerApi.GetPlayerCount() <= 1) _firstPlayer = true;
            SendCustomEventDelayedSeconds(nameof(_TryStart), StartDelaySeconds);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // Delayed events are not handed over with the ownership (vrc-networking §3): the new Owner schedules its own check.
            if (Utilities.IsValid(player) && player.isLocal) SendCustomEventDelayedSeconds(nameof(_TryStart), HandoverDelaySeconds);
        }

        public override void OnDeserialization() { _received = true; Recompute(); }

        /// <summary>Owner only, once: start the shared clock if nobody has and this client can trust its 0 (StartAllowed).
        /// Never rewrites a set value. The only write path. Local only (underscore).</summary>
        public void _TryStart()
        {
            if (_epochMs != 0 || !Networking.IsOwner(gameObject)) { _blockedSince = -1f; return; }   // started, or someone else's job
            if (!StartAllowed())
            {
                if (_blockedSince < 0f) _blockedSince = Time.time;
                if (!_retryPending) { _retryPending = true; SendCustomEventDelayedSeconds(nameof(_RetryStart), RetrySeconds); }
                return;
            }
            int now = Networking.GetServerTimeInMilliseconds();
            _epochMs = now == 0 ? 1 : now;   // 0 means "not started"
            _blockedSince = -1f;
            RequestSerialization();
            Recompute();
        }

        /// <summary>The single pending retry of a refused _TryStart. Local only (underscore).</summary>
        public void _RetryStart() { _retryPending = false; _TryStart(); }

        private bool StartAllowed()
        {
            if (_firstPlayer || _received) return true;
            if (Networking.IsNetworkSettled && !_debugUnsettled) return true;
            return _blockedSince >= 0f && Time.time - _blockedSince >= FallbackSeconds;
        }

        // net: callable-ok: pure query: the shared Auto darkness for this moment (0..1)
        public float _AutoDarkness() { return Compute(); }

        // net: callable-ok: pure query: seconds since the shared start (-1 = not started)
        public float _ElapsedSeconds()
        {
            if (_epochMs == 0) return -1f;
            int ms = Networking.GetServerTimeInMilliseconds() - _epochMs;
            if (ms < 0) return ms < WrapGuardMs ? 1e9f : 0f;
            return ms * 0.001f;
        }

        private void Recompute()
        {
            float d = Compute();
            if (d == _written) return;
            _written = d;
            if (nightMode != null) nightMode.autoDarkness = d;
        }

        private float Compute()
        {
            float t = _ElapsedSeconds();
            if (t < 0f) return 0f;   // not started (late joiner before the value arrives, or the first seconds of the instance)
            float day = Mathf.Max(0f, daySeconds);
            if (t <= day) return 0f;
            float u = duskSeconds > 0.01f ? Mathf.Clamp01((t - day) / duskSeconds) : 1f;
            u = u * u * (3f - 2f * u);
            return Mathf.Clamp01(nightDarkness) * u;
        }
    }
}
