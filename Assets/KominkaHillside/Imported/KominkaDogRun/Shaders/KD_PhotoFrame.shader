Shader "KominkaDogRun/KD_PhotoFrame"
{
    Properties
    {
        _LandscapeTex("Landscape", 2D) = "black" {}
        _PortraitTex("Portrait", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue"="Overlay+100" "RenderType"="Transparent" "DisableBatching"="True" "IgnoreProjector"="True" }
        Cull Off ZTest Always ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _LandscapeTex, _PortraitTex;
            float _VRChatCameraMode, _VRChatMirrorMode;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                float2 xy = v.uv * 2 - 1;
                xy.y *= _ProjectionParams.x;
                o.pos = float4(xy, UNITY_NEAR_CLIP_VALUE, 1);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                clip(_VRChatCameraMode - .5);
                clip(.5 - _VRChatMirrorMode);
                return _ScreenParams.x >= _ScreenParams.y ? tex2D(_LandscapeTex, i.uv) : tex2D(_PortraitTex, i.uv);
            }
            ENDCG
        }
    }
}
