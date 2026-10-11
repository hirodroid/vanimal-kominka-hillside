// IMP-DOG-052 v3: one connected skin surface. No bubble primitives or granular noise.
Shader "KominkaHillside/PC Foam"
{
    Properties
    {
        _Mask ("Existing wash mask", 2D) = "black" {}
        _FoamColor ("Foam colour", Color) = (0.95,0.96,0.98,1)
        _HillScale ("Broad rise amount", Range(0,1)) = 1
    }
    CGINCLUDE

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            sampler2D _Mask;
            half4 _FoamColor;
            float _HillScale;
            float _PartFoamLimit[16];
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT; // skinned maximum-rise normal, not a texture tangent
                float2 uv : TEXCOORD0;
                float4 coat : TEXCOORD1; // canonical seam UV, hill height metres, coat offset metres
                float2 part : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                half3 ambient : TEXCOORD2;
                float limit : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // All duplicates at a permitted seam use the SAME sample for displacement.
                // Fragment coverage still uses the original UV, so rinsing never expands to another island.
                float f = tex2Dlod(_Mask,float4(v.coat.xy,0,0)).g;
                float limit=_PartFoamLimit[clamp((int)(v.part.x+.5),0,15)];o.limit=limit>0?saturate(limit):1;f=min(f,o.limit);
                float growth = smoothstep(.12,.75,f) * _HillScale;
                float3 n = UnityObjectToWorldNormal(v.normal);
                float3 shaped = UnityObjectToWorldNormal(v.tangent.xyz);
                float3 wp = mul(unity_ObjectToWorld,v.vertex).xyz + n * (v.coat.w + v.coat.z * growth);
                o.pos = mul(UNITY_MATRIX_VP,float4(wp,1));
                o.uv = v.uv;
                o.normal = normalize(lerp(n,shaped,growth));
                o.ambient = max(half3(0,0,0),ShadeSH9(float4(o.normal,1)));
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            fixed4 Foam(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 mask = tex2D(_Mask,i.uv);
                half alpha = smoothstep(.15,.55,min(mask.g,i.limit));
                clip(alpha-.02);
                half3 n = normalize(i.normal);
                half3 l = normalize(_WorldSpaceLightPos0.xyz+float3(0,1e-5,0));
                half3 lighting = i.ambient+_LightColor0.rgb;
                lighting *= min(1.0,1.0/max(max(lighting.r,lighting.g),max(lighting.b,.0001)));
                half shade = .65+.35*saturate(dot(n,l)*.5+.5);
                half3 colour = _FoamColor.rgb*lighting*shade*lerp(half3(1,1,1),half3(.86,.80,.72),saturate(mask.r)*.5);
                fixed4 c = fixed4(colour,alpha);
                UNITY_APPLY_FOG(i.fogCoord,c);
                return c;
            }
    ENDCG
    SubShader
    {
        Tags { "Queue"="Transparent+11" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "FOAM_SOLID"
            Tags { "LightMode"="ForwardBase" }
            Cull Back ZWrite On ZTest LEqual
            Blend Off
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma fragment frag
            fixed4 frag(v2f i):SV_Target { fixed4 c=Foam(i); clip(c.a-.98); c.a=1; return c; }
            ENDCG
        }
        Pass
        {
            Name "FOAM_EDGE"
            Tags { "LightMode"="ForwardBase" }
            Cull Back ZWrite Off ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma fragment frag
            fixed4 frag(v2f i):SV_Target { fixed4 c=Foam(i); clip(.98-c.a);  return c; }
            ENDCG
        }
    }
    Fallback Off
}
