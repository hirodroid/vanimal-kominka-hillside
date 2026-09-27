Shader "KominkaHillside/River Lite"
{
    Properties
    {
        _DeepColor ("Deep water", Color) = (0.19,0.36,0.35,1)
        _ShallowColor ("Shallow water", Color) = (0.38,0.52,0.43,1)
        _RippleColor ("Flow highlights", Color) = (0.67,0.73,0.61,1)
        _FlowTex ("Linear flow mask", 2D) = "gray" {}
        _FlowSpeed ("Downstream speed", Range(0,1)) = 0.16
        _RippleStrength ("Flow strength", Range(0,0.4)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        LOD 100
        Pass
        {
            Name "WATER"
            ZWrite On
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _FlowTex;
            half4 _DeepColor, _ShallowColor, _RippleColor;
            half _FlowSpeed, _RippleStrength;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; half2 bank:TEXCOORD1; UNITY_FOG_COORDS(2) UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex);
                // UV.y increases upstream: positive sampling offset makes the pattern travel downstream.
                o.uv=v.uv+float2(0,_Time.y*_FlowSpeed);
                o.bank=v.color.rg;
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half flow=tex2D(_FlowTex,i.uv).r;
                half3 baseColor=lerp(_DeepColor.rgb,_ShallowColor.rgb,i.bank.r);
                fixed4 col=fixed4(lerp(baseColor,_RippleColor.rgb,flow*_RippleStrength)*(1-.22*i.bank.g),1);
                UNITY_APPLY_FOG(i.fogCoord,col);
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
