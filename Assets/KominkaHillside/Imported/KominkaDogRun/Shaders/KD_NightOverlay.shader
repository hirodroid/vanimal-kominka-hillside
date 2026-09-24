// IMP-PRG-004 Phase 1: Live 中の夜の乗算オーバーレイ (2026-09-06 に描画順方式へ変更)。
//
// **画面いっぱいの板を頂点シェーダで作る** (Codex 指摘 2026-09-06: 頭の周りの箱だと、写真カメラや鏡の
// 仮想カメラが箱の外に出たとき、箱の投影部分しか暗くならず Cull Off で 2 面を二重に掛ける)。
// メッシュの頂点 xy (-1〜1) をそのままクリップ座標にするので、**どのカメラから描いても画面全体に 1 回だけ**掛かる。
// メッシュ (KD_FullscreenQuad) は Setup が bounds を巨大にして作る = どのカメラでもカリングされない。
// 位置・回転・スケールは見た目に効かない。
//
// **GrabPass を使わない**ので Android でも描ける (SleepKit の RBS/NightModev3 は GrabPass なので Quest で描かれない)。
//
// **暗くする範囲**: renderQueue 4999 で描くので、Live の粒 (5000) 以外の全部 (世界・犬・アバター・UI) が暗くなる。
// 2026-09-06 ユーザー決定: 層の制限 (アバターだけ明るく残す) は廃止。暗さの上限は作品ごと (KD_LiveSystem.stageDarkness)。
Shader "KominkaDogRun/KD_NightOverlay"
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
                // Darkness 0 = 白 (変化なし)。1 = _Tint で乗算
                fixed3 mul = lerp(fixed3(1, 1, 1), _Tint.rgb, saturate(_Darkness));
                return fixed4(mul, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
