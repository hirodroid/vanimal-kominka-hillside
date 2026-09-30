// Wash mask (RGBA = dirt, foam, wet, 1) written with VRCGraphics.Blit(prev, next, material, pass). Never drawn on a
// dog. Contract: docs/plans/2026-09-30-dog-wash-prototype.md §4 (names, passes, spaces).
//   Pass 0 Brush   : capsule brush in bind-pose mesh space (position map RGB), weighted _Delta, clamped 0..1
//   Pass 1 Restore : whole mask from the 16x16 _CellTex (late join / lost RenderTexture), dirt gets its pattern
//   Pass 2 Decay   : subtract _Decay everywhere (drying)
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
        _EdgeNoise ("Edge noise strength (fraction of radius)", Range(0,1)) = 0.35
        _EdgeNoiseScale ("Edge noise frequency (1 / mesh unit)", Float) = 30
        _FoamGrowEase ("Foam growth slows near full (0 = linear)", Range(0,0.95)) = 0.7
        _CellTex ("Cells 16x16 (dirt, foam, wet), bilinear", 2D) = "black" {}
        _Decay ("Decay (dirt, foam, wet) to subtract", Vector) = (0,0,0,0)
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "KH_WashCommon.cginc"

    sampler2D _MainTex;
    sampler2D _PosMap;
    float4 _PosMap_TexelSize;
    sampler2D _CellTex;
    float4 _BrushA, _BrushB;
    float _BrushRadius, _BrushSoft;
    float4 _Delta;
    float _RinseFoamBoost, _FoamRinseGate;
    float _EdgeNoise, _EdgeNoiseScale, _FoamGrowEase;
    float4 _Decay;
    float _AllowPart[16];
    float _BoneToPart[64];

    // One texel of the position map, read at its centre (A must not blend between bones).
    float4 ReadPos(float2 uv)
    {
        return tex2Dlod(_PosMap, float4(KHWash_SnapUV(uv, _PosMap_TexelSize), 0, 0));
    }

    float4 FragBrush(v2f_img i) : SV_Target
    {
        float4 prev = tex2D(_MainTex, i.uv);
        float4 pm = ReadPos(i.uv);
        int bone = KHWash_BoneIndex(pm.a);
        if (bone < 0) return prev;                                     // outside the UV islands: keep
        int part = clamp((int)(_BoneToPart[bone] + 0.5), 0, 15);
        if (_AllowPart[part] < 0.5) return prev;                       // not the hit part or its neighbour

        float3 p = pm.rgb;
        float3 ab = _BrushB.xyz - _BrushA.xyz;
        float t = saturate(dot(p - _BrushA.xyz, ab) / max(dot(ab, ab), 1e-12));
        float d = length(p - (_BrushA.xyz + ab * t));
        float r = max(_BrushRadius, 1e-6);
        if (d > r * (1.0 + _EdgeNoise) + 1e-6) return prev;           // cheap reject before the noise

        // Rim: two octaves, both directions (in and out), so the circle never shows.
        float3 q = p * _EdgeNoiseScale;
        float rim = KHWash_VNoise3(q) * 0.62 + KHWash_VNoise3(q * 2.37 + 17.3) * 0.38;
        rim = KHWash_Contrast(rim, 1.8) * 2.0 - 1.0;                   // -1..1
        float rr = r * (1.0 + _EdgeNoise * rim);
        float inner = rr * (1.0 - max(_BrushSoft, 0.01));
        float w = 1.0 - smoothstep(inner, rr, d);
        // Clumps: the amount is uneven inside, so foam grows and melts in lumps rather than as a flat disc.
        float clump = KHWash_Contrast(KHWash_VNoise3(q * 3.1 + 41.7), 1.6);
        w *= 1.0 + (clump * 2.0 - 1.0) * 0.5 * saturate(_EdgeNoise * 2.0);

        float dirt = prev.r, foam = prev.g, wet = prev.b;

        // Foam: growth slows as it fills (scrubbing the same place keeps building; one pass never tops out).
        float dF = _Delta.y * w;
        float ease = dF > 0 ? (1.0 - _FoamGrowEase * foam) : 1.0;
        float foamNew = saturate(foam + dF * ease);

        // Dirt: removal is faster under foam (_RinseFoamBoost).
        float dD = _Delta.x * w;
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
    }
    FallBack Off
}
