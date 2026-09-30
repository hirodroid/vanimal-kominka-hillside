// IMP-ENV-010: Android/iOS terrain template swapped in by KH_MobileTerrainBuildEditor (PC keeps Nature/Terrain/Standard).
// 4-layer splat, per-pixel Lambert (one directional light) + SH ambient + fog. No specular, reflection or normal maps,
// so the "glossy" path of the Standard terrain (smoothness from splat alpha) cannot occur.
// Supports both drawInstanced terrains (vertex code mirrors SplatmapVert in Unity 2022.3 TerrainSplatmapCommon.cginc)
// and classic mesh terrains. No AddPassShader: a 5th+ layer would be silently ignored (tiles have 4).
Shader "KominkaHillside/Terrain Lite"
{
    Properties
    {
        [HideInInspector] _Control ("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] _Splat3 ("Layer 3 (A)", 2D) = "grey" {}
        [HideInInspector] _Splat2 ("Layer 2 (B)", 2D) = "grey" {}
        [HideInInspector] _Splat1 ("Layer 1 (G)", 2D) = "grey" {}
        [HideInInspector] _Splat0 ("Layer 0 (R)", 2D) = "grey" {}
        // Used by the base map pass / generator only.
        [HideInInspector] _MainTex ("BaseMap (RGB)", 2D) = "grey" {}
        [HideInInspector] _Color ("Main Color", Color) = (1,1,1,1)
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    // --- Terrain instancing (copied from Unity 2022.3 TerrainSplatmapCommon.cginc, SplatmapVert) ---
    #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLES)
        #undef TERRAIN_USE_SEPARATE_VERTEX_SAMPLER // GL cannot separate samplers from textures
    #else
        #define TERRAIN_USE_SEPARATE_VERTEX_SAMPLER
    #endif
    #if defined(UNITY_INSTANCING_ENABLED) && !defined(SHADER_API_D3D11_9X)
        #define KH_TERRAIN_INSTANCED
        #if defined(TERRAIN_USE_SEPARATE_VERTEX_SAMPLER)
            // Some drivers misbehave when anisotropic samplers are used in the VS; Unity uses an inline linear clamp sampler.
            UNITY_DECLARE_TEX2D(_TerrainHeightmapTexture);
            UNITY_DECLARE_TEX2D(_TerrainNormalmapTexture);
            SamplerState vertex_linear_clamp_sampler;
            #define KH_TERRAIN_SAMPLE(tex, uv) tex.SampleLevel(vertex_linear_clamp_sampler, uv, 0)
        #else
            sampler2D _TerrainHeightmapTexture;
            sampler2D _TerrainNormalmapTexture;
            #define KH_TERRAIN_SAMPLE(tex, uv) tex2Dlod(tex, float4(uv, 0, 0))
        #endif
        float4 _TerrainHeightmapRecipSize; // (1/w, 1/h, 1/(w-1), 1/(h-1))
        float4 _TerrainHeightmapScale;     // (hmScale.x, hmScale.y / kMaxHeight, hmScale.z, 0)
    #endif
    UNITY_INSTANCING_BUFFER_START(Terrain)
        UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData) // (xBase, yBase, skipScale, ~)
    UNITY_INSTANCING_BUFFER_END(Terrain)

    struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

    // Instanced patches carry only the grid position; rebuild height, uv and normal from the terrain textures.
    // Call after UNITY_SETUP_INSTANCE_ID. normalUV is the per-pixel normal lookup (instanced only).
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

    // Terrain ignores rotation and scale, so object-space normals are world-space normals.
    half3 KH_TerrainNormal(half3 interpolated, float2 normalUV)
    {
    #ifdef KH_TERRAIN_INSTANCED
        // Per-pixel like Standard's TERRAIN_INSTANCED_PERPIXEL_NORMAL: lighting does not pop with patch LOD.
        return normalize(KH_TERRAIN_SAMPLE(_TerrainNormalmapTexture, normalUV).xyz * 2 - 1);
    #else
        return normalize(interpolated);
    #endif
    }
    ENDCG
    SubShader
    {
        Tags { "Queue"="Geometry-100" "RenderType"="Opaque" "TerrainCompatible"="True" "SplatCount"="4" }
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
            sampler2D _Control;
            float4 _Control_TexelSize;
            sampler2D _Splat0, _Splat1, _Splat2, _Splat3;
            float4 _Splat0_ST, _Splat1_ST, _Splat2_ST, _Splat3_ST;
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
                o.tc=float4(v.uv,normalUV);
                o.normal=v.normal;
                o.ambient=ShadeSH9(half4(normalize(v.normal),1));
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Tile edges on control-map pixel centers, then normalize weights (SplatmapMix). No clip: first pass, and mobile skips it too.
                float2 splatUV=(i.tc.xy*(_Control_TexelSize.zw-1.0f)+0.5f)*_Control_TexelSize.xy;
                half4 control=tex2D(_Control,splatUV);
                control/=(dot(control,half4(1,1,1,1))+1e-3f);
                half3 albedo=control.r*tex2D(_Splat0,TRANSFORM_TEX(i.tc.xy,_Splat0)).rgb
                    +control.g*tex2D(_Splat1,TRANSFORM_TEX(i.tc.xy,_Splat1)).rgb
                    +control.b*tex2D(_Splat2,TRANSFORM_TEX(i.tc.xy,_Splat2)).rgb
                    +control.a*tex2D(_Splat3,TRANSFORM_TEX(i.tc.xy,_Splat3)).rgb;
                half3 n=KH_TerrainNormal(i.normal,i.tc.zw);
                half3 light=i.ambient+_LightColor0.rgb*saturate(dot(n,_WorldSpaceLightPos0.xyz));
                fixed4 col=fixed4(albedo*light,1);
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
                KH_TerrainVertex(v, normalUV); // instanced patches must be placed here too, or depth/shadows land at the origin
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }
            float4 frag(v2f i):SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    Dependency "BaseMapShader" = "Hidden/KominkaHillside/Terrain Lite Base"
    // Built-in single-target generator (writes _MainTex only); Standard-BaseGen would also render an unused _MetallicTex.
    Dependency "BaseMapGenShader" = "Hidden/TerrainEngine/Splatmap/Diffuse-BaseGen"
    Fallback Off
}
