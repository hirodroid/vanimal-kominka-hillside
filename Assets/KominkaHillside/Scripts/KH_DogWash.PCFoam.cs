using UnityEngine;

namespace KominkaHillside
{
    public partial class KH_DogWash
    {
#if !UNITY_ANDROID && !UNITY_IOS
        [Header("PC foam (IMP-DOG-052; omitted from mobile builds)")]
        public SkinnedMeshRenderer pcFoamRenderer;
        public ParticleSystem pcFoamRunoff;
        public Material pcFoamThinMaterial;
        public Material pcFoamFallbackShellMaterial;
        public int pcFoamVersion;

        private Material _pcFoamMat;
        private bool _pcFoamReady;
        private float _pcFoamAcc;
        private float _pcFoamLastContact = -1000f;
        private float _pcFoamNextEmit;
        private Vector3 _pcFoamLastPosition;
        private bool _pcFoamHasPosition;

        // Must precede the existing shellRenderer.material getter: that getter fixes the runtime material cache.
        void PCFoamPrepare()
        {
            _pcFoamReady = pcFoamVersion == 1 && shellRenderer != null && pcFoamRenderer != null
                && pcFoamRenderer.sharedMesh != null && pcFoamRenderer.sharedMaterial != null
                && pcFoamRunoff != null && pcFoamThinMaterial != null && pcFoamFallbackShellMaterial != null;
            if (pcFoamRenderer != null) pcFoamRenderer.enabled = false;
            if (pcFoamRunoff != null) pcFoamRunoff.Clear();
            if (_pcFoamReady)
            {
                shellRenderer.sharedMaterial = pcFoamThinMaterial;
                _pcFoamMat = pcFoamRenderer.material;
            }
            else if (shellRenderer != null && pcFoamFallbackShellMaterial != null)
            {
                shellRenderer.sharedMaterial = pcFoamFallbackShellMaterial;
                Debug.LogWarning("[PC Foam] Missing/stale setup; retaining the original wash shell.");
            }
        }

        void PCFoamInitialize(float[] limits)
        {
            if (!_pcFoamReady) return;
            _pcFoamMat.SetFloatArray(_idPartFoamLimit, limits);
            _pcFoamMat.SetTexture(_idMask, _cur);
        }

        void PCFoamBind()
        {
            if (_pcFoamReady) _pcFoamMat.SetTexture(_idMask, _cur);
        }

        void PCFoamTouch(int tool)
        {
            if (!_pcFoamReady || (tool != ToolBottle && tool != ToolHand && tool != ToolSponge)) return;
            pcFoamRenderer.enabled = true;
        }

        void PCFoamEvaluate(float foamSum)
        {
            if (!_pcFoamReady) return;
            if (foamSum > 0f) pcFoamRenderer.enabled = true;
        }

        void PCFoamTime(float now)
        {
            if (!_pcFoamReady) return;
            // Coarse zero cells do not prove a clear mask. Disable only after Restore or the final clearing Blit.
            // Reuse the existing half-second evaluation; particles live <= 0.6 s, so a teleport leaves no long trail.
            if (dogRoot != null)
            {
                Vector3 position = dogRoot.position;
                if (_pcFoamHasPosition && (position - _pcFoamLastPosition).sqrMagnitude > 9f) PCFoamClearParticles();
                _pcFoamLastPosition = position;
                _pcFoamHasPosition = true;
            }
        }

        void PCFoamReset()
        {
            PCFoamClearParticles();
            // The old mask may still be visible until Restore runs; do not claim it cleared before the Blit.
        }

        void PCFoamRestored(bool hasFoam)
        {
            if (!_pcFoamReady) return;
            pcFoamRenderer.enabled = hasFoam;
            // Replay is visual only. DrawBrush wakes it again for a newer local/replayed lather stroke, without Emit.
        }

        void PCFoamClearParticles()
        {
            if (pcFoamRunoff != null) pcFoamRunoff.Clear();
            _pcFoamAcc = 0f;
            _pcFoamLastContact = -1000f;
            _pcFoamNextEmit = 0f;
        }

        void PCFoamContact(int tool, int part, Vector3 world, float sec, float foamHere)
        {
            if (!_pcFoamReady || tool != ToolShower || foamHere <= 0.2f || sec <= 0f) return;
            float now = Time.time;
            if (now - _pcFoamLastContact > 0.2f) _pcFoamAcc = 0f;
            _pcFoamLastContact = now;
            _pcFoamAcc += Mathf.Min(sec,0.25f) * 12f * Mathf.Clamp01(foamHere);
            int requested = Mathf.FloorToInt(_pcFoamAcc);
            if (requested <= 0) return;
            _pcFoamAcc -= requested; // Drop denied integer requests; never bank a delayed burst.
            if (now < _pcFoamNextEmit || pcFoamRunoff.particleCount >= 16) return;
            _pcFoamNextEmit = now + 1f / 12f;
            Vector3 centre = world;
            if (partColliders != null && partOfCollider != null)
            {
                float nearest = 1e20f;
                for (int i = 0; i < partColliders.Length && i < partOfCollider.Length; i++)
                {
                    if (partOfCollider[i] != part || partColliders[i] == null) continue;
                    Vector3 c = partColliders[i].bounds.center;
                    float d = (world - c).sqrMagnitude;
                    if (d < nearest) { nearest = d; centre = c; }
                }
            }
            Vector3 outward = world - centre;
            float length = outward.magnitude;
            outward = length > 0.0001f ? outward / length : Vector3.zero;
            // Deterministic variation avoids disturbing the global random stream used by dog behaviour.
            float seed = Mathf.Repeat(now * 7.13f + dogIndex * 0.618f,1f);
            float size = Mathf.Lerp(0.008f,0.025f,seed);
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
            emit.position = world + outward * (size * 0.6f);
            emit.velocity = outward * 0.08f + Vector3.down * Mathf.Lerp(0.07f,0.18f,seed);
            emit.startSize = size;
            emit.startLifetime = Mathf.Lerp(0.25f,0.6f,1f-seed);
            emit.applyShapeToPosition = false;
            pcFoamRunoff.Emit(emit,1);
        }
#endif
    }
}
