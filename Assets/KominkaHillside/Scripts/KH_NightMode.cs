using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace KominkaHillside
{
    // IMP-ENV-002: local night mode (OFF / ON) driven from the hand menu. Same recipe as KominkaDogRun's live night
    // (KD_LiveNight): a full-screen multiplicative quad (KH_NightOverlay, queue 4999, no GrabPass so it also draws on
    // Quest / iOS) plus the sky material's _Exposure / _Tint. No RBS SleepKit. Nothing is synced: each player
    // chooses for their own eyes.
    //
    // Menu rebuild stage 0-b (docs/plans/2026-09-23-menu-rebuild.md D-5): the hand menu's settings slider (KD_MenuSettings)
    // sets the darkness 0..1 through _SetDarkness; the value is saved to this player's PlayerData (SaveKey) and restored on
    // rejoin (applies to you only). onDarkness is the target while ON; switching ON/OFF still fades over fadeSeconds.
    //
    // Materials are never written on the shared assets: the overlay uses Renderer.material (an instance) and the sky
    // is instanced from a hidden SkyHolder renderer, then assigned to RenderSettings.skybox in Start. Leaving Play
    // mid-night therefore leaves KH_Skybox.mat untouched.
    //
    // A future live (DES-LIV-001) writes liveDarkness (0..1); the displayed darkness is the max of the manual fade and
    // that value ("take the stronger, never multiply"), and the live is responsible for writing 0 when it ends.
    // Design and values: docs/plans/2026-09-21-sky-and-night-mode.md.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_NightMode : UdonSharpBehaviour
    {
        [Header("Wiring (Tools/KominkaHillside/Night/1 Setup writes these)")]
        public Renderer overlayRenderer;     // full-screen quad with KH_NightOverlay.mat; starts disabled
        public Renderer skyHolder;           // never drawn (enabled=false); its .material is the sky instance
        [Header("Values (code is the source: Setup writes the Default* constants every run)")]
        public float onDarkness = DefaultOnDarkness;   // darkness while ON, 0..1
        public float fadeSeconds = DefaultFadeSeconds; // <= 0 switches instantly
        [Header("Live input (a future live writes 0..1 and resets to 0 when it ends)")]
        public float liveDarkness;

        public const float DefaultOnDarkness = 1f;
        public const float DefaultFadeSeconds = 3f;
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

        private bool _on;                    // target state (what the menu shows)
        private float _phase;                // manual fade 0..1
        private float _applied = -1f;        // last darkness written to the materials (-1 = never)
        private Material _overlayMat, _skyMat;
        private bool _ready;
        // PlayerData (local player only): restored in OnPlayerRestored; a choice made before the restore wins and is saved then.
        public const string SaveKey = "KH_NightMode.dark";
        private bool _restored;
        private bool _chosen;

        // net: callable-ok: pure query for the menu
        public bool IsOn { get { return _on; } }
        // net: callable-ok: pure query. max(manual, live) so a live never multiplies on top of a night-mode viewer.
        public float Darkness { get { return Mathf.Max(Mathf.Clamp01(_phase) * Mathf.Clamp01(onDarkness), Mathf.Clamp01(liveDarkness)); } }

        public void _Toggle() { _on = !_on; }

        /// <summary>Settings slider (0 = OFF .. 1 = darkest). A change while ON is immediate; OFF <-> ON fades over fadeSeconds.
        /// Saved to PlayerData once restored. Local only (underscore prefix).</summary>
        public void _SetDarkness(float dark)
        {
            dark = Mathf.Clamp01(dark);
            _chosen = true;
            SetTarget(dark);
            if (_restored) Save();
        }

        // net: callable-ok: pure query for the settings slider (target darkness 0..1; OFF = 0)
        public float _Darkness() { return _on ? Mathf.Clamp01(onDarkness) : 0f; }

        private void SetTarget(float dark)
        {
            // Keep the last non-zero level as onDarkness so switching OFF fades out from where it was.
            if (dark > 0.005f) { onDarkness = dark; _on = true; }
            else _on = false;
        }

        private void Save()
        {
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            PlayerData.SetFloat(SaveKey, _Darkness());
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            _restored = true;
            if (_chosen) { Save(); return; }   // chosen before the restore: the player's choice wins
            float saved = 0f;
            if (PlayerData.TryGetFloat(player, SaveKey, out saved)) SetTarget(Mathf.Clamp01(saved));
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
            float target = _on ? 1f : 0f;
            if (_phase != target)
            {
                if (fadeSeconds <= 0f) _phase = target;
                else _phase = Mathf.MoveTowards(_phase, target, Time.deltaTime / fadeSeconds);
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
