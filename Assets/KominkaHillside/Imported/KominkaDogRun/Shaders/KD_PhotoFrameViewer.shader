Shader "KominkaDogRun/KD_PhotoFrameViewer"
{
    Properties { _MainTex("Frame", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Overlay+101" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZTest Always ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _VRChatCameraMode, _VRChatMirrorMode;
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.texcoord;return o; }
            fixed4 frag(v2f i):SV_Target
            {
                // Viewer is phone-only. Camera path uses the fullscreen overlay exactly once.
                clip(.5 - _VRChatCameraMode); clip(.5 - _VRChatMirrorMode);
                return tex2D(_MainTex,i.uv);
            }
            ENDCG
        }
    }
}
