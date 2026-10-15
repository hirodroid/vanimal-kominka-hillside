// Dedicated to KH Halloween pumpkin meshes. The inner lamp must be occluded by the skin.
Shader "KominkaHillside/Halloween Story Prop"
{
    Properties
    {
        _MainTex ("Palette", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Body ("Body brightness", Float) = 1
        _Emission ("Warm core emission", Float) = 0
        _SurfaceEmission ("Unlit surface emission", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Cull Back
        ZWrite On
        ZTest LEqual
        Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                half3 worldNormal : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Body, _Emission, _SurfaceEmission;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 col = tex2D(_MainTex,i.uv) * _Color * i.color;
                half3 unlit = col.rgb;
                // Stable 4x4 Bayer coverage: the same threshold for every surface at a pixel.
                // Preserve opaque coverage while supporting authored alpha.
                float2 p = fmod(floor(i.pos.xy),4.0);
                float4 row = p.y < 1.0 ? float4(0,8,2,10)
                    : p.y < 2.0 ? float4(12,4,14,6)
                    : p.y < 3.0 ? float4(3,11,1,9) : float4(15,7,13,5);
                float value = p.x < 1.0 ? row.x : p.x < 2.0 ? row.y : p.x < 3.0 ? row.z : row.w;
                clip(col.a - (value + 0.5) / 16.0);
                // Fixed soft sky direction: readable volume without realtime lights/shadows.
                half diffuse = saturate(dot(normalize(i.worldNormal), half3(-0.3499,0.7797,0.5198)));
                col.rgb *= 0.55 + 0.45 * diffuse;
                col.rgb = col.rgb * (_Body + _Emission * smoothstep(.42,.85,min(col.r,col.g)));
                col.rgb += unlit * _SurfaceEmission;
                col.a = 1;
                UNITY_APPLY_FOG(i.fogCoord,col);
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
