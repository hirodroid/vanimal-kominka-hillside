Shader "KominkaHillside/PC Foam Runoff"
{
    Properties { _FoamColor ("Foam colour",Color)=(0.95,0.96,0.98,1) }
    SubShader
    {
        Tags { "Queue"="Transparent+11" "RenderType"="Opaque" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            ZWrite On
            Cull Back
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
            half4 _FoamColor;
            struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 pos:SV_POSITION;half3 colour:TEXCOORD0;UNITY_FOG_COORDS(1) UNITY_VERTEX_OUTPUT_STEREO};
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex);
                float3 n=UnityObjectToWorldNormal(v.normal);
                half3 light=max(half3(0,0,0),ShadeSH9(float4(n,1)))+_LightColor0.rgb;
                light*=min(1.0,1.0/max(max(light.r,light.g),max(light.b,.0001)));
                o.colour=_FoamColor.rgb*light*(.72+.28*saturate(n.y*.5+.5));UNITY_TRANSFER_FOG(o,o.pos);return o;
            }
            fixed4 frag(v2f i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);fixed4 c=fixed4(i.colour,1);UNITY_APPLY_FOG(i.fogCoord,c);return c;}
            ENDCG
        }
    }
    Fallback Off
}
