// BUG-CAM-003: 目の前に大きく出す写真 (KD_PhotoViewer) と、その後ろの黒い背景。
//
// **常に最前面に描く** (ZTest Always / Queue Overlay)。頭に追従する板なので、
// 壁や柱に埋まったときに欠けてはいけない — 欠けると持ち帰った 1 枚に壁が入る。
// 吸着シェーダー (KD_PhotoPrint) と違い、頂点は普通に射影する。
// iOS で黒くなったのは近接面へ直接置く方式のほうで、普通の射影の板 (ギャラリーの板)
// は同じ端末で正常に写っていた (2026-09-04 実測)。
Shader "KominkaDogRun/KD_PhotoViewer"
{
    Properties
    {
        _MainTex ("Photo", 2D) = "white" {}
        _Color ("Tint (背景は黒にする)", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // アルファは 1 固定 (KD_PhotoPrint と同じ理由 — 合成側にアルファを見せない)
                return fixed4(tex2D(_MainTex, i.uv).rgb * _Color.rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}
