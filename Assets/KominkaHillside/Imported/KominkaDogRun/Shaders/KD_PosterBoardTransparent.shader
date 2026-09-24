Shader "KominkaDogRun/Mobile/Poster Board Transparent"
{
    Properties
    {
        _MainTex ("Board texture (RGB and mask R)", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _AlphaMaskScale ("Mask scale", Float) = -1
        _AlphaMaskValue ("Mask offset", Float) = 1.1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;
            half _AlphaMaskScale, _AlphaMaskValue;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 sample = tex2D(_MainTex, i.uv);
                half4 color = half4(sample.rgb * _Color.rgb,
                    saturate(sample.r * _AlphaMaskScale + _AlphaMaskValue));
                // lilToon alpha-mask mode 1 replaces alpha; it does not multiply texture/color alpha.
                UNITY_APPLY_FOG(i.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
