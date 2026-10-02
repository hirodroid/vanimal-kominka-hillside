// Wash mask (RGBA = dirt, foam, wet, 1) written with VRCGraphics.Blit(prev, next, material, pass). Never drawn on a
// dog. Contract: docs/plans/2026-09-30-dog-wash-prototype.md §4 (names, passes, spaces).
//   Pass 0 Brush   : capsule brush in bind-pose mesh space (position map RGB), weighted _Delta, clamped 0..1
//   Pass 1 Restore : whole mask from the 16x16 _CellTex (late join / lost RenderTexture), dirt gets its pattern
//   Pass 2 Decay   : subtract _Decay everywhere (drying)
//   Pass 3 Runoff  : foam next to rinsed texels (wet >= 0.5, foam < 0.1; 8 neighbours at +-_RunoffStep texels) loses
//                    _RunoffAmount x rinsed / 8; dirt follows the foam's ratio, wet unchanged (2026-10-02)
// Every pass writes all channels with Blend Off (double buffer), so one channel may depend on another.
//
// Arrays are NOT in the Properties block (Unity cannot declare them there): set them with Material.SetFloatArray.
//   _AllowPart[16]  : per part ID, 1 = the brush may paint this part (hit part and its neighbours)
//   _BoneToPart[64] : bone index (position map A - 1) -> part ID
// Unset arrays read 0: every bone maps to part 0 and part 0 is blocked, so the brush does nothing until wired.
//
// Look: the brush rim is broken by 3D noise (no visible circle), and the amount applied inside is uneven ("clumps"),
// so a slow shower pass leaves a band whose edge is irregular like the prior DogWash erase mask, but the noise lives
// in mesh space so it follows the body and matches across UV seams.
Shader "KominkaHillside/Wash Mask"
{
    Properties
    {
        _MainTex ("Previous mask (Blit source)", 2D) = "black" {}
        _PosMap ("Position map (RGB mesh pos, A bone+1, read as a point)", 2D) = "black" {}
        _BrushA ("Brush capsule end A (mesh space)", Vector) = (0,0,0,0)
        _BrushB ("Brush capsule end B (mesh space)", Vector) = (0,0,0,0)
        _BrushRadius ("Brush radius (mesh units)", Float) = 0.056
        _BrushSoft ("Brush edge softness (fraction of radius)", Range(0,1)) = 0.35
        _Delta ("Delta (dirt, foam, wet) already x seconds", Vector) = (0,0,0,0)
        _RinseFoamBoost ("Dirt removal x (1 + foam x this)", Float) = 2.2
        _FoamRinseGate ("Foam rinse gate (1 = dirt follows foam)", Range(0,1)) = 1
        _RinseReach ("Foam removal outer edge (x noisy radius)", Float) = 1.2
        _RunoffStep ("Runoff neighbour distance (texels)", Float) = 2
        _RunoffAmount ("Runoff foam removed when all 8 neighbours are rinsed", Float) = 0
        _EdgeNoise ("Edge noise strength (fraction of radius)", Range(0,1)) = 0.35
        _EdgeNoiseScale ("Edge noise frequency (1 / mesh unit)", Float) = 30
        _FoamGrowEase ("Foam growth slows near full (0 = linear)", Range(0,0.95)) = 0.7
        _CellTex ("Cells 16x16 (dirt, foam, wet), bilinear", 2D) = "black" {}
        _Decay ("Decay (dirt, foam, wet) to subtract", Vector) = (0,0,0,0)
        _DirtHeightFade ("Dirt height fade (ground z, full z, mid z, top z; mesh units)", Vector) = (0,0,0,0)
        _DirtHeightWeight ("Dirt height weight (mid weight, top weight, enabled, -)", Vector) = (0.35,0.05,0,0)
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "KH_WashCommon.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;   // set by Blit (the mask's texel size)
    sampler2D _PosMap;
    float4 _PosMap_TexelSize;
    sampler2D _CellTex;
    float4 _BrushA, _BrushB;
    float _BrushRadius, _BrushSoft;
    float4 _Delta;
    float _RinseFoamBoost, _FoamRinseGate, _RinseReach;
    float _RunoffStep, _RunoffAmount;
    float _EdgeNoise, _EdgeNoiseScale, _FoamGrowEase;
    float4 _Decay;
    float4 _DirtHeightFade, _DirtHeightWeight;

    // IMP-DOG-029 (decision 2026-09-30): dirt builds from the feet up. The same curve as KH_DogWash.DirtHeightWeight on
    // the bind-pose mesh z (Z-up): 1 up to full, 1 -> mid weight up to mid, -> top weight up to top, then flat.
    float DirtHeightWeight(float z)
    {
        float full = _DirtHeightFade.y, mid = max(_DirtHeightFade.z, full + 1e-4), top = max(_DirtHeightFade.w, mid + 1e-4);
        if (z <= full) return 1.0;
        if (z <= mid) return lerp(1.0, _DirtHeightWeight.x, (z - full) / (mid - full));
        return lerp(_DirtHeightWeight.x, _DirtHeightWeight.y, saturate((z - mid) / (top - mid)));
    }
    float _AllowPart[16];
    float _BoneToPart[64];

    // One texel of the position map, read at its centre (A must not blend between bones).
    float4 ReadPos(float2 uv)
    {
        return tex2Dlod(_PosMap, float4(KHWash_SnapUV(uv, _PosMap_TexelSize), 0, 0));
    }

    // 1 when the bone's part may be painted, 0 when blocked or the texel is outside the islands (bone < 0).
    float AllowAt(int bone)
    {
        if (bone < 0) return 0.0;
        int part = clamp((int)(_BoneToPart[bone] + 0.5), 0, 15);
        return _AllowPart[part] > 0.5 ? 1.0 : 0.0;
    }

    float4 FragBrush(v2f_img i) : SV_Target
    {
        float4 prev = tex2D(_MainTex, i.uv);
        float4 pm = ReadPos(i.uv);
        int bone = KHWash_BoneIndex(pm.a);
        if (bone < 0) return prev;                                     // outside the UV islands: keep
        // Part gate, softened over the 4 neighbouring texels: the part border (max-weight bone per texel) is a texel
        // staircase in UV, and a hard 0/1 gate printed that staircase as a saw-tooth foam edge. Only the weight is
        // scaled; the amount formulas below are unchanged. Neighbours outside the islands count as blocked.
        // A blocked texel is never painted (no leak into a non-neighbour part); the ramp lies on the allowed side.
        if (AllowAt(bone) < 0.5) return prev;
        float allowSoft = 2.0;
        allowSoft += AllowAt(KHWash_BoneIndex(ReadPos(i.uv + float2(_PosMap_TexelSize.x, 0)).a));
        allowSoft += AllowAt(KHWash_BoneIndex(ReadPos(i.uv - float2(_PosMap_TexelSize.x, 0)).a));
        allowSoft += AllowAt(KHWash_BoneIndex(ReadPos(i.uv + float2(0, _PosMap_TexelSize.y)).a));
        allowSoft += AllowAt(KHWash_BoneIndex(ReadPos(i.uv - float2(0, _PosMap_TexelSize.y)).a));
        allowSoft /= 6.0;

        float3 p = pm.rgb;
        float3 ab = _BrushB.xyz - _BrushA.xyz;
        float t = saturate(dot(p - _BrushA.xyz, ab) / max(dot(ab, ab), 1e-12));
        float d = length(p - (_BrushA.xyz + ab * t));
        float r = max(_BrushRadius, 1e-6);
        float reach = max(_RinseReach, 1.0);                           // wOut's outer edge (x rr); the shower sets rinseReach
        if (d > r * (1.0 + _EdgeNoise) * reach + 1e-6) return prev;   // cheap reject before the noise

        // Rim: two octaves, both directions (in and out), so the circle never shows.
        float3 q = p * _EdgeNoiseScale;
        float rim = KHWash_VNoise3(q) * 0.62 + KHWash_VNoise3(q * 2.37 + 17.3) * 0.38;
        rim = KHWash_Contrast(rim, 1.8) * 2.0 - 1.0;                   // -1..1
        float rr = r * (1.0 + _EdgeNoise * rim);
        float inner = rr * (1.0 - max(_BrushSoft, 0.01));
        float w = 1.0 - smoothstep(inner, rr, d);
        // Clumps: the amount is uneven inside, so foam grows and melts in lumps rather than as a flat disc.
        float clump = KHWash_Contrast(KHWash_VNoise3(q * 3.1 + 41.7), 1.6);
        float clumpMul = 1.0 + (clump * 2.0 - 1.0) * 0.5 * saturate(_EdgeNoise * 2.0);
        // Foam removal uses a wider, softer falloff (reaching (_RinseReach - 1) past the rim: 1.2 for the towel, rinseReach
        // for the shower, 2026-10-02), so the foam left at the band edge thins out gradually and the shell draws it as small
        // sparse bubbles instead of a cut-out edge, and a rinse takes the foam around the band along with it.
        float wOut = 1.0 - smoothstep(rr * (1.0 - min(1.0, max(_BrushSoft, 0.01) * 1.8)), rr * reach, d);
        w *= clumpMul * allowSoft;
        wOut *= clumpMul * allowSoft;

        float dirt = prev.r, foam = prev.g, wet = prev.b;

        // Foam: growth slows as it fills (scrubbing the same place keeps building; one pass never tops out).
        float dF = _Delta.y * (_Delta.y < 0 ? wOut : w);
        float ease = dF > 0 ? (1.0 - _FoamGrowEase * foam) : 1.0;
        float foamNew = saturate(foam + dF * ease);

        // Dirt: removal is faster under foam (_RinseFoamBoost).
        float dD = _Delta.x * w;
        // Only a dirt increase (the Owner's dirt event; tools never add dirt) is weighted by height, when enabled.
        if (_Delta.x > 0 && _DirtHeightWeight.z > 0.5) dD *= DirtHeightWeight(p.z);
        float boost = dD < 0 ? (1.0 + foam * _RinseFoamBoost) : 1.0;
        float dirtNormal = saturate(dirt + dD * boost);
        // Gate: while rinsing (dirt and foam both going down), dirt under foam falls in the same ratio as the foam,
        // so where the foam is gone the dirt is gone too and the band edges of both layers line up.
        float rinsing = (_Delta.x < 0 && _Delta.y < 0) ? 1.0 : 0.0;
        float gate = saturate(_FoamRinseGate) * rinsing * smoothstep(0.03, 0.2, foam);
        float dirtGated = dirt * (foamNew / max(foam, 1e-4));
        float dirtNew = saturate(lerp(dirtNormal, min(dirtGated, dirt), gate));

        float wetNew = saturate(wet + _Delta.z * w);
        return float4(dirtNew, foamNew, wetNew, 1);
    }

    float4 FragRestore(v2f_img i) : SV_Target
    {
        float4 cell = tex2D(_CellTex, i.uv);                           // bilinear on purpose (soft cell edges)
        float4 pm = ReadPos(i.uv);
        float dirt = cell.r;
        if (pm.a > 0.5)
        {
            // Mud pattern in mesh space; mean ~1 so the cell average is kept.
            float n = KHWash_Contrast(KHWash_VNoise3(pm.rgb * (_EdgeNoiseScale * 0.5) + 5.9), 1.6);
            dirt = cell.r * (0.5 + n);
        }
        return float4(saturate(dirt), saturate(cell.g), saturate(cell.b), 1);
    }

    float4 FragDecay(v2f_img i) : SV_Target
    {
        float4 prev = tex2D(_MainTex, i.uv);
        return float4(saturate(prev.rgb - _Decay.xyz), 1);
    }

    // 1 when the texel at uv is inside the islands and rinsed (wet >= 0.5 and foam < 0.1). Offsets are whole texels, so
    // the mask is read at a texel centre.
    float RinsedAt(float2 uv)
    {
        if (KHWash_BoneIndex(ReadPos(uv).a) < 0) return 0.0;          // outside the UV islands: does not count
        float4 m = tex2Dlod(_MainTex, float4(uv, 0, 0));
        return (m.b >= 0.5 && m.g < 0.1) ? 1.0 : 0.0;
    }

    // Leftover foam next to a rinsed area flows away by itself (2026-10-02). Same rule as KH_DogWash.RunoffCells.
    float4 FragRunoff(v2f_img i) : SV_Target
    {
        float4 prev = tex2D(_MainTex, i.uv);
        if (prev.g <= 0.0) return prev;
        if (KHWash_BoneIndex(ReadPos(i.uv).a) < 0) return prev;       // outside the UV islands: keep
        float2 o = _MainTex_TexelSize.xy * max(_RunoffStep, 1.0);
        float n = 0.0;
        n += RinsedAt(i.uv + float2( o.x, 0));
        n += RinsedAt(i.uv + float2(-o.x, 0));
        n += RinsedAt(i.uv + float2(0,  o.y));
        n += RinsedAt(i.uv + float2(0, -o.y));
        n += RinsedAt(i.uv + float2( o.x,  o.y));
        n += RinsedAt(i.uv + float2(-o.x,  o.y));
        n += RinsedAt(i.uv + float2( o.x, -o.y));
        n += RinsedAt(i.uv + float2(-o.x, -o.y));
        if (n < 0.5) return prev;                                      // no rinsed neighbour: nothing changes (dirt too)
        float foamNew = max(0.0, prev.g - _RunoffAmount * n / 8.0);
        if (foamNew >= prev.g) return prev;                            // no foam change (amount 0): dirt must not move either
        float dirtNew = min(prev.r, prev.r * foamNew / max(prev.g, 1e-4));   // the rinse gate's ratio, never more dirt
        return float4(dirtNew, foamNew, prev.b, 1);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "PreviewType"="Plane" "IgnoreProjector"="True" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        Pass
        {
            Name "BRUSH"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragBrush
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Name "RESTORE"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragRestore
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Name "DECAY"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragDecay
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Name "RUNOFF"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragRunoff
            #pragma target 3.0
            ENDCG
        }
    }
    FallBack Off
}
