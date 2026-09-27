// IMP-WLD-001 段階 2: ワープの暗転 (設計書 docs/plans/2026-09-27-map-and-travel.md §D.1)。
//
// ナイトモードの覆い (KH_NightOverlay) と同じ「頂点 xy をそのままクリップ座標にする全画面の板」だが、
// **乗算ではなく alpha で黒へ補間する** (Blend SrcAlpha OneMinusSrcAlpha)。_Alpha 1 のとき下に何があっても黒になるので、
// ナイトモードの乗算板 (queue 4999) やライブの粒 (5000) と重なっても黒い。queue は 5001 で、それらより後に描く。
// GrabPass は使わないので Android / iOS でも描ける。ミラー用カメラでは描かない (主視点の板が鏡の面も覆う)。
Shader "KominkaHillside/KH_TravelFade"
{
    Properties
    {
        _Alpha ("Alpha", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+2001" "IgnoreProjector"="True" }   // 5001
        ZTest Always
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _Alpha;
            float _VRChatMirrorMode;   // VRChat shader global (ワールド側では宣言するだけ)

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = float4(v.vertex.xy, 0, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (_VRChatMirrorMode > 0.5) discard;
                return fixed4(0, 0, 0, saturate(_Alpha));
            }
            ENDCG
        }
    }
    Fallback Off
}
