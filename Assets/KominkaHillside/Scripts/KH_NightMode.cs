using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace KominkaHillside
{
    // IMP-ENV-002: local night mode driven from the hand menu. Same recipe as KominkaDogRun's live night
    // (KD_LiveNight): a full-screen multiplicative quad (KH_NightOverlay, queue 4999, no GrabPass so it also draws on
    // Quest / iOS) plus the sky material's _Exposure / _Tint. No RBS SleepKit. Nothing is synced here: this behaviour
    // only draws what this player should see.
    //
    // Menu rebuild stage 0-b (docs/plans/2026-09-23-menu-rebuild.md D-5): the hand menu's settings slider (KD_MenuSettings)
    // sets the manual darkness 0..1 through _SetDarkness; the value is saved to this player's PlayerData (SaveKey) and
    // restored on rejoin (applies to you only).
    //
    // IMP-ENV-005 (recommended plan R, user decision 2026-10-02): each player chooses Auto or Manual.
    //   Auto   = the instance's shared timeline. KH_AutoNight (child object, Manual sync, one synced int) derives it from the
    //            synced start time and writes autoDarkness (0..1) about once a second. Every Auto player sees the same value.
    //   Manual = the slider (onDarkness while ON, 0 while OFF). Only this player; never touches the shared clock.
    //   The mode is per player, saved as ModeKey (absent = Auto). The manual value keeps its old key (SaveKey) and is never
    //   overwritten by the Auto value. Touching the slider while in Auto switches this player to Manual at that value.
    //   Mode switches, OFF <-> ON, the restore and a jump of the Auto value (a late joiner receiving the clock) fade the
    //   shown value over fadeSeconds; a slider drag while Manual and ON is still immediate.
    //
    // Materials are never written on the shared assets: the overlay uses Renderer.material (an instance) and the sky
    // is instanced from a hidden SkyHolder renderer, then assigned to RenderSettings.skybox in Start. Leaving Play
    // mid-night therefore leaves KH_Skybox.mat untouched.
    //
    // The live (DES-LIV-001, KH_LiveNight) writes liveDarkness (0..1); the displayed darkness is the max of the shown
    // Auto / Manual value and that value ("take the stronger, never multiply"). When the live ends it returns to the
    // Auto / Manual value, not to 0. Design and values: docs/plans/2026-09-21-sky-and-night-mode.md §7.
    //
    // Per frame: Update is a few float compares; the materials are written only when the darkness changes (during a fade,
    // and in Auto at most once a second while the dusk runs).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_NightMode : UdonSharpBehaviour
    {
        [Header("Wiring (Tools/KominkaHillside/Night/1 Setup writes these)")]
        public Renderer overlayRenderer;     // full-screen quad with KH_NightOverlay.mat; starts disabled
        public Renderer skyHolder;           // never drawn (enabled=false); its .material is the sky instance
        [Header("Values (code is the source: Setup writes the Default* constants every run)")]
        public float onDarkness = DefaultOnDarkness;   // manual darkness while ON, 0..1
        public float fadeSeconds = DefaultFadeSeconds; // <= 0 switches instantly
        [Header("Live input (KH_LiveNight writes 0..1 and returns it to 0 when the live ends)")]
        public float liveDarkness;
        [Header("Parade input (local playback; never changes saved Auto/Manual settings)")]
        public float paradeDarkness;
        [Header("Auto input (KH_AutoNight writes the shared timeline's darkness 0..1)")]
        public float autoDarkness;

        public const float DefaultOnDarkness = 1f;
        public const float DefaultFadeSeconds = 3f;
        /// <summary>In Auto, a change of the target larger than this in one step is faded instead of applied (late join, harness jump).
        /// The dusk itself moves about 0.0007 per second, far below this.</summary>
        public const float AutoJumpFade = 0.05f;
        public const int OverlayQueue = 4999;           // above UI (4000) and the world, below live particles (5000)
        // Overlay multiplier at darkness 1 (KH_NightOverlay _Tint; same as KD_NightOverlay). Setup writes it to the material every run.
        public const float OverlayTintR = 0.22f, OverlayTintG = 0.26f, OverlayTintB = 0.42f;
        // Sky at day / night. Same values as KD_LiveNight: the cubemap shader is texCUBE * _Tint * _Exposure, so the
        // baked sun sinks with the exposure and reads as a moon. UdonSharp cannot hold static Color, hence components.
        public const float DaySkyExposure = 1f;
        public const float NightSkyExposure = 0.15f;
        public const float DayTintR = 0.5f, DayTintG = 0.5f, DayTintB = 0.5f;
        public const float NightTintR = 0.30f, NightTintG = 0.34f, NightTintB = 0.50f;
        public const float SkyTintA = 0.5f;

        private bool _on;                    // manual target state (OFF = 0)
        private bool _auto = true;           // IMP-ENV-005: this player's mode. Initial (and key absent) = Auto
        private float _shown;                // shown Auto / Manual darkness before the live max (smoothed on switches)
        private bool _fading;
        private float _fadeFrom, _fadeElapsed;
        private float _applied = -1f;        // last darkness written to the materials (-1 = never)
        private Material _overlayMat, _skyMat;
        private bool _ready;
        // PlayerData (local player only): restored in OnPlayerRestored; a choice made before the restore wins and is saved then.
        // The manual value and the mode are tracked separately, so choosing only the mode early never overwrites the saved manual value.
        public const string SaveKey = "KH_NightMode.dark";   // manual darkness 0..1 (since IMP-UI-001; unchanged)
        public const string ModeKey = "KH_NightMode.mode";   // IMP-ENV-005: ModeAuto / ModeManual (absent = Auto)
        public const int ModeAuto = 0, ModeManual = 1;
        private bool _restored;
        private bool _chosenDark, _chosenMode;

        // net: callable-ok: pure query for the menu (manual ON)
        public bool IsOn { get { return _on; } }
        // net: callable-ok: pure query for the menu (true = Auto)
        public bool IsAuto { get { return _auto; } }
        // net: callable-ok: pure query. max(Auto / Manual, live) so a live never multiplies on top of a night viewer.
        public float Darkness { get { return Mathf.Max(Mathf.Clamp01(_shown), Mathf.Max(Mathf.Clamp01(liveDarkness), Mathf.Clamp01(paradeDarkness))); } }

        /// <summary>Manual OFF / ON (kept from IMP-ENV-002; no current caller). Switches to Manual like the slider does.</summary>
        public void _Toggle() { _SetDarkness(_on ? 0f : Mathf.Clamp01(onDarkness)); }

        /// <summary>Settings slider (0 = OFF .. 1 = darkest). Always this player's Manual value: in Auto the first touch switches
        /// to Manual (and saves the mode). A change while Manual and ON is immediate; OFF &lt;-&gt; ON and the switch fade over
        /// fadeSeconds. Saved to PlayerData once restored. Local only (underscore prefix).</summary>
        public void _SetDarkness(float dark)
        {
            dark = Mathf.Clamp01(dark);
            bool wasAuto = _auto, wasOn = _on;
            _chosenDark = true;
            SetTarget(dark);
            if (wasAuto) { _auto = false; _chosenMode = true; }
            if (wasAuto || wasOn != _on) StartFade();
            if (!_restored) return;
            SaveDark();
            if (wasAuto) SaveMode();
        }

        /// <summary>[オート]: follow the shared timeline (fades to its current value; the shared clock is not touched). Local only.</summary>
        public void _UseAuto() { SetMode(true); }
        /// <summary>[マニュアル]: back to this player's saved / last manual value (fades). Local only.</summary>
        public void _UseManual() { SetMode(false); }

        private void SetMode(bool auto)
        {
            _chosenMode = true;
            if (_auto == auto) return;
            _auto = auto;
            StartFade();
            if (_restored) SaveMode();
        }

        // net: callable-ok: pure query for the settings slider: what the slider shows (Auto = the shared value, Manual = the target; OFF = 0)
        public float _Darkness() { return BaseTarget(); }
        // net: callable-ok: pure query: this player's manual value (the value saved under SaveKey; OFF = 0)
        public float _ManualDarkness() { return _on ? Mathf.Clamp01(onDarkness) : 0f; }

        private float BaseTarget() { return _auto ? Mathf.Clamp01(autoDarkness) : _ManualDarkness(); }

        private void SetTarget(float dark)
        {
            // Keep the last non-zero level as onDarkness so switching back ON returns to it.
            if (dark > 0.005f) { onDarkness = dark; _on = true; }
            else _on = false;
        }

        private void StartFade()
        {
            _fadeFrom = _shown; _fadeElapsed = 0f; _fading = true;
        }

        private void SaveDark()
        {
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            PlayerData.SetFloat(SaveKey, _ManualDarkness());
        }

        private void SaveMode()
        {
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            PlayerData.SetInt(ModeKey, _auto ? ModeAuto : ModeManual);
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            _restored = true;
            if (_chosenDark) SaveDark();   // chosen before the restore: the player's choice wins
            else
            {
                float saved = 0f;
                if (PlayerData.TryGetFloat(player, SaveKey, out saved)) SetTarget(Mathf.Clamp01(saved));
            }
            if (_chosenMode) SaveMode();
            else
            {
                int mode = ModeAuto;
                if (PlayerData.TryGetInt(player, ModeKey, out mode)) _auto = mode != ModeManual;
            }
            StartFade();   // the restored mode / value arrives without a pop
        }

        void Start()
        {
            if (overlayRenderer == null) { Debug.LogError("[KH_NightMode] overlayRenderer is not wired; run Tools/KominkaHillside/Night/1 Setup."); return; }
            if (skyHolder == null || skyHolder.sharedMaterial == null) { Debug.LogError("[KH_NightMode] skyHolder is not wired; run Tools/KominkaHillside/Night/1 Setup."); return; }
            _overlayMat = overlayRenderer.material;   // instance (vrc-udonsharp §1: .material is the only copy in Udon)
            _overlayMat.renderQueue = OverlayQueue;
            overlayRenderer.enabled = false;
            _skyMat = skyHolder.material;
            skyHolder.enabled = false;
            RenderSettings.skybox = _skyMat;
            _ready = true;
            Apply(Darkness);
        }

        void Update()
        {
            if (!_ready) return;
            float target = BaseTarget();
            if (_fading)
            {
                _fadeElapsed += Time.deltaTime;
                float k = fadeSeconds > 0f ? Mathf.Clamp01(_fadeElapsed / fadeSeconds) : 1f;
                _shown = Mathf.Lerp(_fadeFrom, target, k);   // the target may move (Auto); the lerp still lands on it at k = 1
                if (k >= 1f) _fading = false;
            }
            else if (_shown != target)
            {
                if (_auto && Mathf.Abs(target - _shown) > AutoJumpFade) StartFade();   // late joiner receiving the clock: no pop
                else _shown = target;
            }
            float d = Darkness;
            if (d == _applied) return;   // idle: nothing to write
            Apply(d);
        }

        private void Apply(float d)
        {
            _applied = d;
            _skyMat.SetFloat("_Exposure", Mathf.Lerp(DaySkyExposure, NightSkyExposure, d));
            _skyMat.SetColor("_Tint", new Color(
                Mathf.Lerp(DayTintR, NightTintR, d),
                Mathf.Lerp(DayTintG, NightTintG, d),
                Mathf.Lerp(DayTintB, NightTintB, d),
                SkyTintA));
            bool on = d > 0.001f;
            if (on) _overlayMat.SetFloat("_Darkness", d);
            if (overlayRenderer.enabled != on) overlayRenderer.enabled = on;
        }
    }
}
