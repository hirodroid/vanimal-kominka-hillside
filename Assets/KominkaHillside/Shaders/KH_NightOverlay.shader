// IMP-ENV-002: ナイトモードの乗算オーバーレイ。古民家ドッグランの KD_NightOverlay.shader (IMP-PRG-004) をコピーし、
// ミラー用カメラでは描かない 1 点だけ足した派生物 (docs/plans/2026-09-21-sky-and-night-mode.md §1)。
//
// **画面いっぱいの板を頂点シェーダで作る**: メッシュの頂点 xy (-1〜1) をそのままクリップ座標にするので、
// どのカメラから描いても画面全体に 1 回だけ掛かる。メッシュ (KH_FullscreenQuad) は Setup が bounds を巨大にして作る
// = どのカメラでもカリングされない。位置・回転・スケールは見た目に効かない。
//
// **GrabPass を使わない**ので Android / iOS でも描ける (RBS SleepKit の NightMode は GrabPass なので Quest では描かれない)。
//
// **暗くする範囲**: renderQueue 4999 で描くので、ライブの粒 (5000) 以外の全部 (世界・犬・アバター・UI) が暗くなる。
//
// **ミラーでは描かない** (Codex D-1 2026-09-21): ミラー用カメラで 1 回、主視点でミラー面にもう 1 回で 2 回暗くなる。
// VRChat が全カメラに配るシェーダーグローバル _VRChatMirrorMode (0 = 通常 / 1 = VR ミラー / 2 = Desktop ミラー) が
// 0 以外なら覆いを捨てる。鏡の中は主視点の覆いで周囲と同じ 1 回になる。写真カメラ (_VRChatCameraMode) は
// 写真そのものを夜にしたいので描く (手持ちカメラのプレビュー画面だけ主視点でもう 1 回濃くなる。既知)。
Shader "KominkaHillside/KH_NightOverlay"
{
    Properties
    {
        _Darkness ("Darkness", Range(0, 1)) = 0
        _Tint ("Night Tint (at Darkness 1)", Color) = (0.22, 0.26, 0.42, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1999" "IgnoreProjector"="True" }   // 4999
        ZTest Always
        ZWrite Off
        Cull Off
        Blend DstColor Zero   // out = src * dst (乗算)
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _Darkness;
            fixed4 _Tint;
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
                // 頂点 xy をクリップ座標にそのまま使う = 画面全体。z は ZTest Always なので何でもよい
                o.pos = float4(v.vertex.xy, 0, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ミラー用カメラでは描かない (二重減光の回避)
                if (_VRChatMirrorMode > 0.5) discard;
                // Darkness 0 = 白 (変化なし)。1 = _Tint で乗算
                fixed3 mul = lerp(fixed3(1, 1, 1), _Tint.rgb, saturate(_Darkness));
                return fixed4(mul, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
