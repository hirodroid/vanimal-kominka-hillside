// Shared helpers for KominkaHillside/Wash Mask and KominkaHillside/Wash Shell (IMP-DOG-025).
// Contract: docs/plans/2026-09-30-dog-wash-prototype.md §4 / §5.
// Noise is a pure function of the bind-pose mesh position (position map RGB), so every client draws the same
// shapes for the same strokes and the pattern sticks to the body (it does not swim when the dog moves).
#ifndef KH_WASH_COMMON_INCLUDED
#define KH_WASH_COMMON_INCLUDED

// Sine-free hash (Dave Hoskins). Stable across GPUs; inputs must be highp (float).
float KHWash_Hash13(float3 p3)
{
    p3 = frac(p3 * 0.1031);
    p3 += dot(p3, p3.zyx + 31.32);
    return frac((p3.x + p3.y) * p3.z);
}

// 3D value noise, 0..1 (8 hashes).
float KHWash_VNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = KHWash_Hash13(i);
    float b = KHWash_Hash13(i + float3(1, 0, 0));
    float c = KHWash_Hash13(i + float3(0, 1, 0));
    float d = KHWash_Hash13(i + float3(1, 1, 0));
    float e = KHWash_Hash13(i + float3(0, 0, 1));
    float g = KHWash_Hash13(i + float3(1, 0, 1));
    float h = KHWash_Hash13(i + float3(0, 1, 1));
    float k = KHWash_Hash13(i + float3(1, 1, 1));
    float x0 = lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
    float x1 = lerp(lerp(e, g, f.x), lerp(h, k, f.x), f.y);
    return lerp(x0, x1, f.z);
}

// Sine-free 3-channel hash (Dave Hoskins).
float3 KHWash_Hash33(float3 p3)
{
    p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yxx) * p3.zyx);
}

// Round bubbles on a jittered 3D grid, 8 cells (2x2x2 around p). Each cell holds one bubble with its own radius and
// an "appear" id 0..1. A bubble exists when id < cov (fuzzy), so low coverage gives sparse bubbles and high coverage a
// packed cluster. Two parameter sets share the same cells (x = set A, y = set B); each returns the max of the bubbles'
// paraboloid caps (1 at a bubble's centre, 0 at its rim). The max over all 8 cells (not only the nearest) keeps the
// outlines round instead of cut along Voronoi edges. Jitter is kept inside 0.25..0.75 of a cell and radii should stay
// <= ~0.55 so a bubble from an unsearched cell rarely reaches p.
//   covA / covB : coverage 0..1      rA / rB : max radius in cell units (the actual radius is 40..100 % of it)
float2 KHWash_Bubbles2(float3 p, float covA, float rA, float covB, float rB)
{
    float3 b = floor(p - 0.5);
    float2 best = 0;
    [unroll] for (int k = 0; k < 8; k++)
    {
        float3 c = b + float3(k & 1, (k >> 1) & 1, (k >> 2) & 1);
        float3 h = KHWash_Hash33(c);
        float3 dv = p - (c + 0.25 + 0.5 * h);
        float d2 = dot(dv, dv);
        float id = frac((h.x + h.y + h.z) * 13.7);
        float s = frac(h.x * 7.31 + h.z * 3.17);
        float size = 0.4 + 0.6 * s * (2.0 - s);                     // mixed sizes, more big than small
        float rA2 = rA * size; rA2 *= rA2;
        float rB2 = rB * size; rB2 *= rB2;
        float capA = saturate(1.0 - d2 / max(rA2, 1e-6)) * saturate((covA - id) * 7.0);
        float capB = saturate(1.0 - d2 / max(rB2, 1e-6)) * saturate((covB - id) * 7.0);
        best = max(best, float2(capA, capB));
    }
    return best;
}

// Value noise piles up near 0.5; stretch it so thresholds bite (result 0..1).
float KHWash_Contrast(float n, float k)
{
    return saturate((n - 0.5) * k + 0.5);
}

// UV of the centre of the texel that contains uv. Sampling there reads exactly one texel even with bilinear
// filtering, which is how the position map's A (bone index + 1) is read "as a point".
float2 KHWash_SnapUV(float2 uv, float4 texelSize)
{
    return (floor(uv * texelSize.zw) + 0.5) * texelSize.xy;
}

// Position map A -> bone index (0..63), or -1 when the texel is invalid (A == 0).
int KHWash_BoneIndex(float a)
{
    if (a < 0.5) return -1;
    return clamp((int)(a + 0.5) - 1, 0, 63);
}

#endif
