// Wash shell: drawn by a second SkinnedMeshRenderer that shares the dog body's mesh and bones. Reads the dog's wash
// mask (R dirt, G foam, B wet) and draws, top to bottom, foam > dirt > wet (darkening + weak gloss) over the fur.
// Contract: docs/plans/2026-09-30-dog-wash-prototype.md §5. One pass, no geometry shader, no GrabPass, no shadow
// caster (FallBack Off) and no shadow sampling. Single-pass stereo instancing supported.
//
// Extrusion is in metres along the WORLD normal (the dog renderers have lossyScale 50..100, so object-space offsets
// would be off by that factor): _BaseOffset + foam x _FoamThickness x lump.
// Noise coordinates are the bind-pose mesh position from the position map (RGB), so grains stick to the body.
//
// Arrays (Material.SetFloatArray, not in Properties):
//   _BoneToPart[64]    : bone index (position map A - 1) -> part ID
//   _PartFoamLimit[16] : max foam shown per part (head from 0.35). A value <= 0 means "no limit" (= 1), so an unset
//                        array does not hide all foam.
//
// Blend is premultiplied (One OneMinusSrcAlpha) so the wet layer can darken the fur and add a highlight in one pass.
Shader "KominkaHillside/Wash Shell"
{
    Properties
    {
        _Mask ("Wash mask (R dirt, G foam, B wet)", 2D) = "black" {}
        _PosMap ("Position map (RGB mesh pos, A bone+1)", 2D) = "black" {}
        _BaseOffset ("Offset to fur surface (m, world)", Float) = 0.008
        _FoamThickness ("Extra offset at foam 1 (m, world)", Float) = 0.025
        _FoamColor ("Foam colour", Color) = (0.95,0.96,0.98,1)
        _DirtColor ("Dirt colour (A = max opacity)", Color) = (0.30,0.21,0.13,0.9)
        _WetDarken ("Wet darkening at wet 1", Range(0,1)) = 0.4
        _WetGloss ("Wet gloss", Range(0,2)) = 0.35
        _DirtPattern ("Dirt pattern (R, multiplies the procedural mud)", 2D) = "white" {}
        _FoamScale ("Foam grain frequency (1 / mesh unit)", Float) = 60
        _FoamSoft ("Foam grain edge softness", Range(0.01,0.5)) = 0.12
        _FoamBump ("Foam grain relief (lighting)", Range(0,3)) = 1
        _FoamRim ("Foam rim brightening", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        LOD 100
        Pass
        {
            Name "WASH_SHELL"
            Tags { "LightMode"="ForwardBase" }
            ZTest LEqual
            ZWrite Off
            Cull Back
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "KH_WashCommon.cginc"

            sampler2D _Mask;
            sampler2D _PosMap;
            float4 _PosMap_TexelSize;
            sampler2D _DirtPattern;
            float4 _DirtPattern_ST;
            float _BaseOffset, _FoamThickness;
            half4 _FoamColor, _DirtColor;
            half _WetDarken, _WetGloss;
            float _FoamScale;
            half _FoamSoft, _FoamBump, _FoamRim;
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

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float4 m = tex2Dlod(_Mask, float4(v.uv, 0, 0));
                float4 pm = tex2Dlod(_PosMap, float4(KHWash_SnapUV(v.uv, _PosMap_TexelSize), 0, 0));
                float limit = PartLimit(pm.a);
                float foam = min(saturate(m.g), limit);
                float3 bindPos = pm.a > 0.5 ? pm.rgb : v.vertex.xyz;

                float3 wn = UnityObjectToWorldNormal(v.normal);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                // Lumpy foam: the push varies over the body so the silhouette bulges in tufts.
                float lump = KHWash_VNoise3(bindPos * (_FoamScale * 0.4) + 3.1);
                wp += wn * (_BaseOffset + foam * _FoamThickness * (0.55 + 0.9 * lump));

                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
                o.uv = v.uv;
                o.bindPos = bindPos;
                o.worldPos = wp;
                o.worldNormal = wn;
                o.ambient = max(half3(0, 0, 0), ShadeSH9(float4(wn, 1.0)));
                o.limit = limit;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float4 m = tex2D(_Mask, i.uv);
                half dirt = saturate(m.r);
                half foam = min(saturate(m.g), i.limit);
                half wet = saturate(m.b);

                // --- grain noise (2 octaves, faded out when a pixel covers several grains: no shimmer) ---
                float3 p = i.bindPos * _FoamScale;
                float fw = length(fwidth(p));
                float g1 = saturate(1.6 - fw * 0.8);
                float g2 = saturate(1.6 - fw * 1.9);
                float n1 = KHWash_VNoise3(p);
                float n2 = KHWash_VNoise3(p * 2.37 + 19.1);
                float N = saturate(0.5 + ((n1 - 0.5) * 0.62 * g1 + (n2 - 0.5) * 0.38 * g2) * 1.8);
                // Mud blotches (1 octave, coarser).
                float dn = KHWash_Contrast(KHWash_VNoise3(i.bindPos * (_FoamScale * 0.3) + 7.3), 1.6);

                // --- foam coverage: few grains at low foam, grains merge into a thick coat as it grows ---
                float th = lerp(0.9, -0.05, saturate(foam * 1.1));
                float soft = max(_FoamSoft, 0.01);
                float af = smoothstep(th - soft, th + soft, N) * saturate(foam * 6.0);
                float h = saturate((N - th) / 0.45);                     // 0 at a grain's edge, 1 at its top

                // --- grain relief from screen derivatives (surface gradient; computed before any clip) ---
                float3 n = normalize(i.worldNormal);
                float hw = h * (_FoamThickness * 0.35 + 0.004) * _FoamBump;
                float3 dpdx = ddx(i.worldPos), dpdy = ddy(i.worldPos);
                float dhx = ddx(hw), dhy = ddy(hw);
                float3 r1 = cross(dpdy, n), r2 = cross(n, dpdx);
                float det = dot(dpdx, r1);
                float3 grad = (det < 0 ? -1.0 : 1.0) * (dhx * r1 + dhy * r2);
                float3 nbRaw = abs(det) * n - grad;
                float3 nb = dot(nbRaw, nbRaw) > 1e-20 ? normalize(nbRaw) : n;

                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                float3 L = _WorldSpaceLightPos0.xyz;
                L *= rsqrt(max(dot(L, L), 1e-6));                        // no directional light -> L = 0
                half3 lightCol = _LightColor0.rgb;
                half ndv = saturate(dot(n, V));

                // --- foam (lit by the scene: ambient/SH + main light; no emission, so night mode darkens it) ---
                half3 foamBase = lerp(_FoamColor.rgb, _DirtColor.rgb * 1.6, dirt * 0.3); // dirty suds
                half ndlF = saturate(dot(nb, L) * 0.7 + 0.3);                // wrapped: soft, translucent
                half3 ambF = i.ambient * (0.82 + 0.18 * nb.y);                // tops brighter
                half ao = lerp(0.62, 1.0, h);                                 // valleys between grains darker
                half rim = pow(1.0 - ndv, 3.0) * _FoamRim;
                half3 cf = foamBase * ((ambF + lightCol * ndlF) * ao + rim * (i.ambient + lightCol * 0.5));
                af *= lerp(0.55, 1.0, saturate(ndv * 3.0));                   // soft edge at the silhouette

                // --- dirt (mud on the fur, blotchy) ---
                half pat = tex2D(_DirtPattern, TRANSFORM_TEX(i.uv, _DirtPattern)).r;
                float thd = lerp(1.0, -0.1, saturate(dirt * 1.15));
                float ad = smoothstep(thd - 0.18, thd + 0.18, dn * 0.7 + N * 0.3)
                         * saturate(dirt * 5.0) * pat * _DirtColor.a;
                half ndlD = saturate(dot(n, L));
                half3 cd = _DirtColor.rgb * (0.7 + 0.6 * dn) * lerp(1.0, 0.65, wet)
                         * (i.ambient + lightCol * ndlD);

                // --- wet (darken what is below + weak gloss) ---
                float aw = wet * _WetDarken;
                float3 H = normalize(L + V + float3(0, 1e-4, 0));
                half spec = pow(saturate(dot(n, H)), 40.0) * (ndlD > 0 ? 1.0 : 0.0);
                half3 cw = (spec * lightCol + pow(1.0 - ndv, 4.0) * 0.3 * i.ambient) * _WetGloss * wet;

                // --- composite, premultiplied: wet < dirt < foam ---
                half3 c = cw;
                float a = aw;
                c = cd * ad + c * (1.0 - ad);
                a = ad + a * (1.0 - ad);
                c = cf * af + c * (1.0 - af);
                a = af + a * (1.0 - af);

                clip(a + dot(c, half3(1, 1, 1)) - 0.004);
                fixed4 col = fixed4(c, saturate(a));
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, unity_FogColor * col.a);
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
