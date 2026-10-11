// IMP-DOG-052: POSITION is a repeated body anchor, skinned by Unity. UV1 holds an unskinned unit sphere.
// No geometry shader, additional RT, transparency layers, or per-lobe CPU update.
Shader "KominkaHillside/PC Foam"
{
    Properties
    {
        _Mask ("Existing wash mask", 2D) = "black" {}
        _FoamColor ("Foam colour", Color) = (0.95,0.96,0.98,1)
        _FoamShadow ("Valley tint", Color) = (0.66,0.73,0.86,1)
        _GrowthStart ("Growth start", Range(0,1)) = 0.18
        _GrowthFull ("Full growth", Range(0,1)) = 0.75
        _NormalScale ("Height relative to width", Range(0.3,1)) = 1
        _Inset ("Embed radius fraction", Range(0,0.5)) = 0
        [HideInInspector] _Diagnostic ("Diagnostic unlit", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+11" "RenderType"="Opaque" "IgnoreProjector"="True" }
        Pass
        {
            Name "FOAM"
            Tags { "LightMode"="ForwardBase" }
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            sampler2D _Mask;
            half4 _FoamColor, _FoamShadow;
            float _GrowthStart, _GrowthFull, _Diagnostic, _NormalScale, _Inset;
            float _PartFoamLimit[16];
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 sphere : TEXCOORD1;
                float4 supportA : TEXCOORD2;
                float4 supportB : TEXCOORD3;
                float4 config : TEXCOORD4; // part, seed, fur offset metres, reserved
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 colour : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 surfaceNormal : TEXCOORD2;
                float growth : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float f = tex2Dlod(_Mask,float4(v.uv,0,0)).g;
                f = min(f,tex2Dlod(_Mask,float4(v.supportA.xy,0,0)).g);
                f = min(f,tex2Dlod(_Mask,float4(v.supportA.zw,0,0)).g);
                f = min(f,tex2Dlod(_Mask,float4(v.supportB.xy,0,0)).g);
                f = min(f,tex2Dlod(_Mask,float4(v.supportB.zw,0,0)).g);
                float limit = _PartFoamLimit[clamp((int)(v.config.x+0.5),0,15)];
                f = min(f,limit > 0 ? limit : 1);
                o.growth = smoothstep(_GrowthStart,max(_GrowthStart+0.001,_GrowthFull),f);
                // Inverse-transpose with row vector; zero-length blended normals give zero offset, never NaN.
                float3 surface = mul(v.normal,(float3x3)unity_WorldToObject);
                surface *= rsqrt(max(dot(surface,surface),1e-12));
                float radius = v.sphere.w*o.growth;
                float k = clamp(_NormalScale,0.3,1.0);
                float radial = dot(v.sphere.xyz,surface);
                float3 centre = mul(unity_ObjectToWorld,v.vertex).xyz + surface*(v.config.z-_Inset*radius);
                float3 wp = centre + (v.sphere.xyz+(k-1)*radial*surface)*radius;
                float3 shapedNormal = normalize(v.sphere.xyz+(1/k-1)*radial*surface);
                o.pos = mul(UNITY_MATRIX_VP,float4(wp,1));
                o.normal = shapedNormal;
                o.surfaceNormal = surface;
                half3 ambient = max(half3(0,0,0),ShadeSH9(float4(shapedNormal,1)));
                half3 light = ambient + _LightColor0.rgb;
                light *= min(1.0,1.0/max(max(light.r,light.g),max(light.b,0.0001)));
                light = lerp(light,half3(1,1,1),saturate(_Diagnostic));
                o.colour = _FoamColor.rgb*light*lerp(0.95,1.0,frac(v.config.y));
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(i.growth-0.015);
                float3 n = normalize(i.normal);
                float3 lightDir = _WorldSpaceLightPos0.xyz;
                lightDir *= rsqrt(max(dot(lightDir,lightDir),1e-8));
                half shading = 0.76+0.24*saturate(dot(n,lightDir)*0.5+0.5);
                half root = saturate(dot(n,i.surfaceNormal)*0.5+0.5);
                fixed4 c = fixed4(i.colour*lerp(_FoamShadow.rgb,half3(1,1,1),0.48+0.52*root)*shading,1);
                UNITY_APPLY_FOG(i.fogCoord,c);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
