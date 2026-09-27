Shader "KominkaHillside/Foliage Lite"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        _VertexTint ("Use vertex tint", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _MainTex;
            half4 _Color;
            half _Cutoff, _VertexTint;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; fixed4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; half3 light:COLOR; UNITY_FOG_COORDS(1) UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f,o);
                UNITY_TRANSFER_INSTANCE_ID(v,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;
                half3 n=UnityObjectToWorldNormal(v.normal);
                // Two-sided soft lighting; a single albedo sample, no normal/specular/reflection textures.
                half3 ambient=(ShadeSH9(half4(n,1))+ShadeSH9(half4(-n,1)))*.5;
                half3 direct=_LightColor0.rgb*(.3+.7*abs(dot(n,_WorldSpaceLightPos0.xyz)));
                o.light=max(ambient+direct,half3(.06,.06,.06))*_Color.rgb*lerp(half3(1,1,1),v.color.rgb,_VertexTint);
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                UNITY_APPLY_DITHER_CROSSFADE(i.pos.xy);
                fixed4 col=tex2D(_MainTex,i.uv);clip(col.a-_Cutoff);
                col.rgb*=i.light;col.a=1;
                UNITY_APPLY_FOG(i.fogCoord,col);
                return col;
            }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            half _Cutoff;
            struct v2f { V2F_SHADOW_CASTER; float2 uv:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f,o);
                UNITY_TRANSFER_INSTANCE_ID(v,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.uv=v.texcoord;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_APPLY_DITHER_CROSSFADE(i.pos.xy);
                clip(tex2D(_MainTex,i.uv).a-_Cutoff);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    FallBack Off
}
