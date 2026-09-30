// Wash shell: drawn by a second SkinnedMeshRenderer that shares the dog body's mesh and bones. Reads the dog's wash
// mask (R dirt, G foam, B wet). Contract: docs/plans/2026-09-30-dog-wash-prototype.md §5. No geometry shader, no
// GrabPass, no shadow caster (FallBack Off), no shadow sampling. Single-pass stereo instancing supported.
//
// Passes, in draw order (rev 3, 2026-09-30):
//   BASE          dirt + wet at _BaseOffset. Premultiplied blend (One OneMinusSrcAlpha), ZWrite Off
//   FOAM0..FOAMn  foam as stacked shells (like fur shells). Layer i is pushed out by
//                 _BaseOffset + _FoamThickness x i/(n-1) along the WORLD normal (metres; the dog renderers have
//                 lossyScale 50..100, so object-space offsets would be off by that factor). The push is constant per
//                 layer, so no vertex-coarse edge lifts off the body.
//                 Fragment: height field h = foam x (0.55 + 0.45 x shape), shape = round mounds (cellular, ~4 cm) +
//                 small grains (~1 cm) in bind-pose mesh space; the layer is cut where h < (i + 0.5) / n. Much foam
//                 reaches the top layers (round heaps); at the edge only the bottom layer remains. Foam below 0.3 is
//                 only sparse small grains on the bottom layer (no film). Near-opaque, alpha only for the cut edge,
//                 ZWrite On so foam sorts against itself.
// Layer count: SubShader 1 = PC (d3d11 only; every pass has #pragma only_renderers d3d11, so the whole SubShader is
// unsupported elsewhere) with 4 foam layers. SubShader 2 = everything else (Android / iOS) with 2 foam layers.
// Fragment noise: foam layer 1 value noise (mask UV jitter) + 2 cellular calls (8 cells each); BASE 2 value noises
// + 1 cellular. The mask is read with a texel-scale UV jitter so its 256^2 staircase does not show on cut edges.
//
// Arrays (Material.SetFloatArray, not in Properties):
//   _BoneToPart[64]    : bone index (position map A - 1) -> part ID
//   _PartFoamLimit[16] : max foam per part (head from 0.35). A value <= 0 means "no limit" (= 1).
// Lighting: ambient/SH + main light colour (toon-like, mild direction term), no emission: follows the scene and the
// night overlay. _FoamBump is unused since rev 3 (kept so the builder's writes still bind).
Shader "KominkaHillside/Wash Shell"
{
    Properties
    {
        _Mask ("Wash mask (R dirt, G foam, B wet)", 2D) = "black" {}
        _PosMap ("Position map (RGB mesh pos, A bone+1)", 2D) = "black" {}
        _BaseOffset ("Offset to fur surface (m, world)", Float) = 0.005
        _FoamThickness ("Foam height of the top layer (m, world)", Float) = 0.05
        _FoamColor ("Foam colour", Color) = (0.95,0.96,0.98,1)
        _FoamShadow ("Foam valley tint (multiplier)", Color) = (0.66,0.73,0.86,1)
        _FurBrightness ("Fur brightness (0.5 brown fur, 1 white fur)", Range(0,1)) = 0.5
        _DirtColor ("Dirt colour (A = max opacity)", Color) = (0.15,0.10,0.065,0.88)
        _WetDarken ("Wet darkening at wet 1", Range(0,1)) = 0.25
        _WetGloss ("Wet gloss", Range(0,2)) = 0.6
        _WetTint ("Wet cool tint (sheen colour)", Color) = (0.45,0.6,0.85,1)
        _DirtPattern ("Dirt pattern (R, multiplies the procedural mud)", 2D) = "white" {}
        _FurFlowAxis ("Fur flow axis (bind-pose mesh space, dirt streaks)", Vector) = (0,0,1,0)
        _FoamScale ("Foam frequency (1 / mesh unit; mounds x0.65, grains x2.6)", Float) = 60
        _FoamSoft ("Foam cut edge softness", Range(0.01,0.5)) = 0.12
        _FoamBump ("(unused since rev 3)", Range(0,3)) = 0.6
        _FoamRim ("Foam rim brightening", Range(0,1)) = 0.3
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "UnityLightingCommon.cginc"
    #include "KH_WashCommon.cginc"

    sampler2D _Mask;
    float4 _Mask_TexelSize;
    sampler2D _PosMap;
    float4 _PosMap_TexelSize;
    sampler2D _DirtPattern;
    float4 _DirtPattern_ST;
    float _BaseOffset, _FoamThickness;
    half4 _FoamColor, _FoamShadow, _DirtColor, _WetTint;
    half _WetDarken, _WetGloss;
    float4 _FurFlowAxis;
    float _FoamScale;
    half _FoamSoft, _FoamRim, _FurBrightness;
    float _BoneToPart[64];
    float _PartFoamLimit[16];

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 bindPos : TEXCOORD1;    // bind-pose mesh position (noise space)
        float3 worldPos : TEXCOORD2;
        half3 worldNormal : TEXCOORD3;
        half3 ambient : TEXCOORD4;     // SH (light probes / ambient), per vertex
        half limit : TEXCOORD5;        // part foam limit
        UNITY_FOG_COORDS(6)
        UNITY_VERTEX_OUTPUT_STEREO
    };

    float PartLimit(float a)
    {
        int bone = KHWash_BoneIndex(a);
        if (bone < 0) return 1.0;
        int part = clamp((int)(_BoneToPart[bone] + 0.5), 0, 15);
        float lim = _PartFoamLimit[part];
        return lim > 0.0 ? saturate(lim) : 1.0;
    }

    // Toon-like light level: follows the scene brightness but not the facing, capped (foam 1.0 so it does not blow
    // out to flat white and the heap shading survives; dirt 1.2).
    half3 LightLevel(half3 ambient, half cap)
    {
        half3 l = ambient + _LightColor0.rgb;
        half m = max(max(l.r, l.g), max(l.b, 1e-4));
        return l * min(1.0, cap / m);
    }

    // Mask UV nudged by up to 1.5 texels in a direction that wanders smoothly over the body (bind-pose value noise,
    // ~1.5 cm): the 256^2 mask's texel staircase no longer prints as a saw-tooth on foam / dirt / wet cut edges.
    // Stays within the position map's seam dilation.
    float2 MaskJitterUV(float2 uv, float3 bindPos)
    {
        float n = KHWash_VNoise3(bindPos * (_FoamScale * 1.5) + 5.1);
        float ang = n * 12.566;
        return uv + float2(cos(ang), sin(ang)) * _Mask_TexelSize.xy * 1.5;
    }

    float3 MainLightDir()
    {
        float3 L = _WorldSpaceLightPos0.xyz;
        return L * rsqrt(max(dot(L, L), 1e-6));                          // no directional light -> 0
    }

    v2f VertShell(appdata v, float offset)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_OUTPUT(v2f, o);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

        float4 pm = tex2Dlod(_PosMap, float4(KHWash_SnapUV(v.uv, _PosMap_TexelSize), 0, 0));
        float3 wn = UnityObjectToWorldNormal(v.normal);
        float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz + wn * offset;

        o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
        o.uv = v.uv;
        o.bindPos = pm.a > 0.5 ? pm.rgb : v.vertex.xyz;
        o.worldPos = wp;
        o.worldNormal = wn;
        o.ambient = max(half3(0, 0, 0), ShadeSH9(float4(wn, 1.0)));
        o.limit = PartLimit(pm.a);
        UNITY_TRANSFER_FOG(o, o.pos);
        return o;
    }

    v2f VertBase(appdata v) { return VertShell(v, _BaseOffset); }
    v2f VertLayer(appdata v, float t) { return VertShell(v, _BaseOffset + _FoamThickness * t); }

    // ---------------- BASE: dirt + wet ----------------
    fixed4 FragBase(v2f i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        // Mud field: blotches streaked along the fur.
        float3 ax = normalize(_FurFlowAxis.xyz + float3(0, 0, 1e-5));
        float3 pd = i.bindPos * (_FoamScale * 0.35);
        float3 ps = pd - ax * dot(pd, ax) * 0.75;                        // squash along the flow -> streaks
        float blot = KHWash_Contrast(KHWash_VNoise3(pd + 7.3), 1.6);
        float streak = KHWash_VNoise3(ps * 4.0 + 23.9);

        // Mask read nudged by the streak noise (texel-scale wander, same idea as MaskJitterUV, no extra noise).
        float ang = streak * 12.566;
        float4 m = tex2D(_Mask, i.uv + float2(cos(ang), sin(ang)) * _Mask_TexelSize.xy * 1.5);
        half dirt = saturate(m.r);
        half wet = saturate(m.b);
        clip(max(dirt, wet) - 0.003);
        // Splatter: few, small, irregular (cells warped by the blot noise, cut by the streaks).
        // Mud clumps (rev 5): coarse irregular lumps on the blotches instead of dots.
        float3 pk = i.bindPos * (_FoamScale * 0.9) + (blot - 0.5) * 1.5;
        float lumpCap = KHWash_Bubbles2(pk, saturate(dirt * 1.3) * 0.5, 0.5, 0, 0).x;
        float aSpeck = smoothstep(0.2, 0.55, lumpCap - (streak - 0.5) * 0.5) * 0.9;

        float3 n = normalize(i.worldNormal);
        float3 V = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
        float3 L = MainLightDir();
        half3 lightCol = _LightColor0.rgb;
        half3 lit = LightLevel(i.ambient, 1.2);
        half ndv = saturate(dot(n, V));
        half sil = saturate(ndv * 3.0 + 0.1);                             // fade at the silhouette

        half pat = tex2D(_DirtPattern, TRANSFORM_TEX(i.uv, _DirtPattern)).r;
        float field = blot * 0.65 + streak * 0.35;
        float thd = lerp(1.05, 0.0, saturate(dirt * 1.2));
        float aMain = smoothstep(thd - 0.12, thd + 0.16, field) * (0.75 + 0.25 * blot);
        float ad = saturate(max(aMain, aSpeck * saturate(field * 1.5)) * (0.8 + 0.2 * aSpeck))
                 * saturate(dirt * 6.0) * pat * _DirtColor.a * sil;
        half ndlD = saturate(dot(n, L) * 0.5 + 0.5);
        // Dark mud: lumps darker (thicker), thin areas a bit lighter; wet mud darker still.
        half3 cd = _DirtColor.rgb * lerp(1.25, 0.7, saturate(blot * 0.6 + aSpeck * 0.5)) * lerp(1.0, 0.75, wet)
                 * lit * lerp(0.75, 1.0, ndlD);

        // Wet (rev 5): read as "wet" by gloss and a cool tint, only mildly darker, so a rinsed band = clean fur that
        // shines, clearly unlike the dark mud. Gloss = tight highlight + a broad soft sheen, broken by the streaks.
        float aw = wet * _WetDarken * sil;
        float3 H = normalize(L + V + float3(0, 1e-4, 0));
        half ndh = saturate(dot(n, H));
        half lightOn = dot(n, L) > 0 ? 1.0 : 0.0;
        half spec = (pow(ndh, 60.0) * 0.8 + pow(ndh, 10.0) * 0.12) * lightOn * (0.35 + 0.65 * streak);
        half fres = pow(1.0 - ndv, 3.0);
        half3 c = (spec * lightCol + (0.05 + 0.2 * fres) * _WetTint.rgb * lit) * _WetGloss * wet * sil;

        // Premultiplied: wet < dirt.
        c = cd * ad + c * (1.0 - ad);
        float a = ad + aw * (1.0 - ad);
        fixed4 col = fixed4(c, saturate(a));
        UNITY_APPLY_FOG_COLOR(i.fogCoord, col, unity_FogColor * col.a);
        return col;
    }

    // ---------------- FOAM layer (layer 0 = bottom, layers = count) ----------------
    fixed4 FragFoam(v2f i, float layer, float layers)
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        float fw = length(fwidth(i.bindPos)) * _FoamScale;             // before any clip (derivatives)
        float3 n = normalize(i.worldNormal);
        float3 V = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
        half ndv = saturate(dot(n, V));

        float4 m = tex2D(_Mask, MaskJitterUV(i.uv, i.bindPos));
        half dirt = saturate(m.r);
        half foam = min(saturate(m.g), i.limit);

        float t = layers > 1.5 ? layer / (layers - 1.0) : 1.0;
        float thr = (layer + 0.5) / layers;
        // Grazing view: the upper layers need more height, so their cuts do not stay as thin slivers / spikes on the
        // silhouette.
        float graze = saturate(1.0 - ndv * 2.5);
        thr += graze * graze * 0.45 * t;
        float thinPeak = 1.2 / layers;                                   // thin grains reach the bottom layer only
        // Upper layers fade in over a wide band around their cut (60 % of the layer spacing, in height units) and so
        // melt into the layer below instead of ending in a visible contour. The bottom layer keeps a short edge.
        float fadeW = layer < 0.5 ? max(_FoamSoft, 0.01) * 0.3 : 0.6 / layers;
        float thrLo = layer < 0.5 ? thr : thr - fadeW * 0.5;
        // h never exceeds foam (thick) or thinPeak (grains, bottom layer): skip the noise when nothing can show.
        float hMax = max(foam, layer < 0.5 ? thinPeak * step(0.01, foam) : 0.0);
        clip(hMax - thrLo);

        float3 p = i.bindPos * _FoamScale;
        // Mounds (~4 cm on the Shiba), some cells empty and sizes mixed so the heaps are uneven. Same for every layer
        // (the heap shape must line up through the stack).
        float2 bigC = KHWash_Bubbles2(p * 0.65, 0.85, 0.55, 0.0, 0.0);
        // Thin foam: fine bubbles, clustered where a mound would be (starting lather), not an even dot scatter.
        float covThin = smoothstep(0.0, 0.35, foam) * saturate(0.35 + bigC.x) * 0.8;
        // Grains get a per-layer phase shift, so the cut lines of successive layers are not concentric copies.
        float2 smC = KHWash_Bubbles2(p * 2.6 + 11.3 + layer * float3(3.7, 1.9, 5.3), 1.5, 0.5, covThin, 0.32);
        // Far away a pixel spans many cells: fade to the mean instead of shimmering.
        float gB = saturate(1.6 - fw * 0.52);
        float gS = saturate(1.6 - fw * 2.1);
        float big = lerp(0.4, bigC.x, gB);
        float grain = lerp(0.35, smC.x, gS);
        float thin = lerp(covThin * 0.25, smC.y, gS);

        float shape = saturate(big * 0.8 + grain * 0.3);
        float hFull = foam * (0.55 + 0.45 * shape) * smoothstep(0.2, 0.45, foam);
        float h = max(hFull, thin * thinPeak);

        float a = smoothstep(thrLo, thrLo + fadeW, h);
        // Top layers also thin out at grazing angles (on top of the raised threshold above).
        a *= lerp(1.0, saturate(ndv * 3.0), t * 0.6);
        clip(a - 0.02);

        // Colour from the CONTINUOUS height h (not the layer index, not the distance to this layer's cut), so there is
        // no brightness step between layers: low foam / valleys / heap flanks in _FoamShadow, crowns white. Fine thin
        // bubbles: white centre, tinted rim. No point highlights.
        // _FurBrightness (rev 6): 0.5 = brown fur (the look approved on the Shiba / Dachs, unchanged), 1.0 = white fur.
        // Towards 1: most of the heap turns white and crowns go brighter than the fur, valleys and the gaps between
        // grains get darker (neutral grey), and the silhouette gets a bright rounded rim, so foam still reads on white.
        half fb = saturate(_FurBrightness * 2.0 - 1.0);
        float occlBrown = saturate(0.05 + 0.55 * h + 0.4 * big + 0.15 * grain + 0.35 * thin * (1.0 - h));
        float occlWhite = saturate(0.1 + 0.5 * h + 0.65 * big + 0.2 * grain + 0.35 * thin * (1.0 - h));
        occlWhite = saturate((occlWhite - 0.5) * 1.6 + 0.62);            // steeper: narrow dark valleys, broad crowns
        float occl = lerp(occlBrown, occlWhite, fb);
        // fb = 0: _FoamShadow as is (brown fur look). fb = 1: a darker neutral grey derived here (no extra material value).
        half3 shadowCol = lerp(_FoamShadow.rgb, half3(0.70, 0.72, 0.77) * 0.75, fb);
        half3 shade = lerp(shadowCol, half3(1, 1, 1) * (1.0 + 0.3 * fb * big), occl);
        // Gaps between the small grains: outlines of the bubbles.
        shade *= 1.0 - 0.25 * fb * (1.0 - smoothstep(0.0, 0.4, grain)) * saturate(foam * 2.0);   // 0 when fb = 0

        float3 L = MainLightDir();
        half dirF = lerp(0.85, 1.0, saturate(dot(n, L) * 0.5 + 0.5)) * (0.92 + 0.08 * n.y);
        half rim = pow(1.0 - ndv, 3.0) * _FoamRim * 0.4 + pow(1.0 - ndv, 2.0) * 0.35 * fb * saturate(big + 0.3);
        half3 foamBase = _FoamColor.rgb * lerp(half3(1, 1, 1), half3(0.86, 0.8, 0.72), dirt * 0.5); // dirty suds
        half3 c = foamBase * LightLevel(i.ambient, 1.0 + 0.35 * fb) * (dirF * shade + rim);

        fixed4 col = fixed4(c, a);
        UNITY_APPLY_FOG(i.fogCoord, col);
        return col;
    }
    ENDCG

    // ================= PC: 4 foam layers =================
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        LOD 100
        Pass
        {
            Name "BASE"
            Tags { "LightMode"="ForwardBase" }
            ZTest LEqual ZWrite Off Cull Back
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex VertBase
            #pragma fragment FragBase
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            ENDCG
        }
        Pass
        {
            Name "FOAM0"
            Tags { "LightMode"="ForwardBase" }
            ZTest LEqual ZWrite On Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex V0
            #pragma fragment F0
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            v2f V0(appdata v) { return VertLayer(v, 0.0); }
            fixed4 F0(v2f i) : SV_Target { return FragFoam(i, 0.0, 4.0); }
            ENDCG
        }
        Pass
        {
            Name "FOAM1"
            Tags { "LightMode"="ForwardBase" }
            ZTest LEqual ZWrite On Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex V1
            #pragma fragment F1
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            v2f V1(appdata v) { return VertLayer(v, 1.0 / 3.0); }
            fixed4 F1(v2f i) : SV_Target { return FragFoam(i, 1.0, 4.0); }
            ENDCG
        }
        Pass
        {
            Name "FOAM2"
            Tags { "LightMode"="ForwardBase" }
            ZTest LEqual ZWrite On Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex V2
            #pragma fragment F2
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            v2f V2(appdata v) { return VertLayer(v, 2.0 / 3.0); }
            fixed4 F2(v2f i) : SV_Target { return FragFoam(i, 2.0, 4.0); }
            ENDCG
        }
        Pass
        {
            Name "FOAM3"
            Tags { "LightMode"="ForwardBase" }
            ZTest LEqual ZWrite On Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex V3
            #pragma fragment F3
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            v2f V3(appdata v) { return VertLayer(v, 1.0); }
            fixed4 F3(v2f i) : SV_Target { return FragFoam(i, 3.0, 4.0); }
            ENDCG
        }
    }

    // ================= Mobile (and anything not d3d11): 2 foam layers =================
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        LOD 100
        Pass
        {
            Name "BASE"
            Tags { "LightMode"="ForwardBase" }
            ZTest LEqual ZWrite Off Cull Back
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex VertBase
            #pragma fragment FragBase
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            ENDCG
        }
        Pass
        {
            Name "FOAM0"
            Tags { "LightMode"="ForwardBase" }
            ZTest LEqual ZWrite On Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex V0
            #pragma fragment F0
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            v2f V0(appdata v) { return VertLayer(v, 0.0); }
            fixed4 F0(v2f i) : SV_Target { return FragFoam(i, 0.0, 2.0); }
            ENDCG
        }
        Pass
        {
            Name "FOAM1"
            Tags { "LightMode"="ForwardBase" }
            ZTest LEqual ZWrite On Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex V1
            #pragma fragment F1
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            v2f V1(appdata v) { return VertLayer(v, 1.0); }
            fixed4 F1(v2f i) : SV_Target { return FragFoam(i, 1.0, 2.0); }
            ENDCG
        }
    }
    FallBack Off
}
