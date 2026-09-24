Shader "KominkaDogRun/Pumpkin Palette"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Palette", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.15
        _Metallic ("Metallic", Range(0,1)) = 0
        _EmissionMap ("Emission palette", 2D) = "white" {}
        _EmissionColor ("Emission", Color) = (0.25,0.25,0.25,1)
    }
    SubShader
    {
        // The host darkening overlay precedes live effects. Opaque depth keeps painted faces readable.
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        LOD 200
        ZWrite On
        Cull Back
        CGPROGRAM
        #pragma surface surf Standard
        #pragma target 3.0
        sampler2D _MainTex;
        sampler2D _EmissionMap;
        fixed4 _Color;
        fixed4 _EmissionColor;
        half _Glossiness;
        half _Metallic;
        struct Input { float2 uv_MainTex; float2 uv_EmissionMap; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = tex2D(_EmissionMap, IN.uv_EmissionMap).rgb * _EmissionColor.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
