Shader "KominkaDogRun/Pumpkin Costume"
{
    Properties
    {
        _MainTex ("Base palette", 2D) = "white" {}
        _EmissionMap ("Emission palette", 2D) = "black" {}
        _EmissionGain ("Live emission", Range(0,3)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        LOD 200
        ZWrite On
        Cull Back
        CGPROGRAM
        #pragma surface surf Standard
        #pragma target 3.0
        sampler2D _MainTex, _EmissionMap;
        half _EmissionGain, _Glossiness;
        struct Input { float2 uv_MainTex; float2 uv_EmissionMap; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb;
            o.Emission = tex2D(_EmissionMap, IN.uv_EmissionMap).rgb * _EmissionGain;
            o.Metallic = 0;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
