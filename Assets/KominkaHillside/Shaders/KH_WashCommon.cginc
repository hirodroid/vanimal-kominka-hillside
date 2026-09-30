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
