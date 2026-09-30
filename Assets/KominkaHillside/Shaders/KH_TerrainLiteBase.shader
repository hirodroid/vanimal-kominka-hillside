// IMP-ENV-010: distant-tile pass of KominkaHillside/Terrain Lite (beyond basemapDistance).
// Samples the generated base map (_MainTex) with the same Lambert + SH + fog lighting as the splat pass.
// Instancing code mirrors SplatmapVert in Unity 2022.3 TerrainSplatmapCommon.cginc (kept in sync with KH_TerrainLite.shader).
Shader "Hidden/KominkaHillside/Terrain Lite Base"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "grey" {}
        _Color ("Main Color", Color) = (1,1,1,1)
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLES)
        #undef TERRAIN_USE_SEPARATE_VERTEX_SAMPLER
    #else
        #define TERRAIN_USE_SEPARATE_VERTEX_SAMPLER
    #endif
    #if defined(UNITY_INSTANCING_ENABLED) && !defined(SHADER_API_D3D11_9X)
        #define KH_TERRAIN_INSTANCED
        #if defined(TERRAIN_USE_SEPARATE_VERTEX_SAMPLER)
            UNITY_DECLARE_TEX2D(_TerrainHeightmapTexture);
            UNITY_DECLARE_TEX2D(_TerrainNormalmapTexture);
            SamplerState vertex_linear_clamp_sampler;
            #define KH_TERRAIN_SAMPLE(tex, uv) tex.SampleLevel(vertex_linear_clamp_sampler, uv, 0)
        #else
            sampler2D _TerrainHeightmapTexture;
            sampler2D _TerrainNormalmapTexture;
            #define KH_TERRAIN_SAMPLE(tex, uv) tex2Dlod(tex, float4(uv, 0, 0))
        #endif
        float4 _TerrainHeightmapRecipSize;
        float4 _TerrainHeightmapScale;
    #endif
    UNITY_INSTANCING_BUFFER_START(Terrain)
        UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)
    UNITY_INSTANCING_BUFFER_END(Terrain)

    struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

    void KH_TerrainVertex(inout appdata v, out float2 normalUV)
    {
        normalUV = 0;
    #ifdef KH_TERRAIN_INSTANCED
        float2 patchVertex = v.vertex.xy;
        float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
        float4 uvscale = instanceData.z * _TerrainHeightmapRecipSize;
        float4 uvoffset = instanceData.xyxy * uvscale;
        uvoffset.xy += 0.5f * _TerrainHeightmapRecipSize.xy;
        float2 sampleCoords = patchVertex.xy * uvscale.xy + uvoffset.xy;
        float hm = UnpackHeightmap(KH_TERRAIN_SAMPLE(_TerrainHeightmapTexture, sampleCoords));
        v.vertex.xz = (patchVertex.xy + instanceData.xy) * _TerrainHeightmapScale.xz * instanceData.z;
        v.vertex.y = hm * _TerrainHeightmapScale.y;
        v.vertex.w = 1.0f;
        v.uv = patchVertex.xy * uvscale.zw + uvoffset.zw;
        v.normal = KH_TERRAIN_SAMPLE(_TerrainNormalmapTexture, sampleCoords).xyz * 2 - 1;
        normalUV = sampleCoords;
    #endif
    }

    half3 KH_TerrainNormal(half3 interpolated, float2 normalUV)
    {
    #ifdef KH_TERRAIN_INSTANCED
        return normalize(KH_TERRAIN_SAMPLE(_TerrainNormalmapTexture, normalUV).xyz * 2 - 1);
    #else
        return normalize(interpolated);
    #endif
    }
    ENDCG
    SubShader
    {
        Tags { "Queue"="Geometry-100" "RenderType"="Opaque" "TerrainCompatible"="True" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap forwardadd
            #include "Lighting.cginc"
            sampler2D _MainTex;
            half4 _Color;
            struct v2f { float4 pos:SV_POSITION; float4 tc:TEXCOORD0; half3 normal:TEXCOORD1; half3 ambient:TEXCOORD2; UNITY_FOG_COORDS(3) UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float2 normalUV;
                KH_TerrainVertex(v, normalUV);
                o.pos=UnityObjectToClipPos(v.vertex);
                o.tc=float4(v.uv,normalUV); // base map spans the whole tile (0..1), no tiling
                o.normal=v.normal;
                o.ambient=ShadeSH9(half4(normalize(v.normal),1));
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 n=KH_TerrainNormal(i.normal,i.tc.zw);
                half3 light=i.ambient+_LightColor0.rgb*saturate(dot(n,_WorldSpaceLightPos0.xyz));
                fixed4 col=fixed4(tex2D(_MainTex,i.tc.xy).rgb*_Color.rgb*light,1);
                UNITY_APPLY_FOG(i.fogCoord,col);
                return col;
            }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap forwardadd
            struct v2f { V2F_SHADOW_CASTER; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float2 normalUV;
                KH_TerrainVertex(v, normalUV);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }
            float4 frag(v2f i):SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    Fallback Off
}
