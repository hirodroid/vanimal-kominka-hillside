Shader "KominkaDogRun/Dog Preview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination", Float) = 0
        [Toggle] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        // Resolve the front surface before blending so internal mouth/eye geometry
        // cannot make the sleeping silhouette look like an X-ray.
        Pass
        {
            ColorMask 0
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i):SV_Target { return 0; }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float shade:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex);
                o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.shade=.58+.42*saturate(dot(normalize(v.normal),normalize(float3(-.4,.8,-.6))));
                return o;
            }
            fixed4 frag(v2f i):SV_Target { fixed4 c=tex2D(_MainTex,i.uv)*_Color; c.rgb*=i.shade; return c; }
            ENDCG
        }
    }
}
