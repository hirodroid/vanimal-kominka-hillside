using UnityEngine;

namespace KominkaDogRun
{
    public partial class KD_CandyDogPerformance
    {
        // Optional local appearance: empty on CandyStep and the source world.
        public Renderer[] costumeGlowRenderers = new Renderer[0];
        public float costumeGlowQuiet = .65f;
        public float costumeGlowPeak = 1.65f;
        private MaterialPropertyBlock _costumeGlowBlock;
        private float _costumeGlowValue = -1f;

        private void SetCostumeGlow(float gain)
        {
            if (costumeGlowRenderers == null || costumeGlowRenderers.Length == 0) return;
            if (_costumeGlowValue == gain || gain > 0 && Mathf.Abs(_costumeGlowValue - gain) < .005f) return;
            _costumeGlowValue = gain;
            if (_costumeGlowBlock == null) _costumeGlowBlock = new MaterialPropertyBlock();
            for (int i = 0; i < costumeGlowRenderers.Length; i++)
            {
                var target = costumeGlowRenderers[i];
                if (target == null) continue;
                _costumeGlowBlock.Clear();
                target.GetPropertyBlock(_costumeGlowBlock, 0);
                _costumeGlowBlock.SetFloat("_EmissionGain", gain);
                target.SetPropertyBlock(_costumeGlowBlock, 0);
            }
        }

        private void UpdateCostumeGlow(float t)
        {
            // Broad musical phrases, not per-note flashes. Zero outside playback.
            float first = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(22, 24, t))
                * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(39, 42, t)));
            float last = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(85, 88, t))
                * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(111, 115, t)));
            float fade = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 2))
                * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(117, 120, t)));
            SetCostumeGlow(Mathf.Lerp(costumeGlowQuiet, costumeGlowPeak, Mathf.Max(first, last)) * fade);
        }
    }
}
