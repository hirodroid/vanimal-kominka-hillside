// IMP-DOG-052 v3: retain dirt/wet BASE, replace granular foam with a continuous lather film.
Shader "KominkaHillside/Wash Shell PC Thin"
{
    Properties
    {
        _Mask ("Wash mask (R dirt, G foam, B wet)", 2D) = "black" {}
        _PosMap ("Position map (RGB mesh pos, A bone+1)", 2D) = "black" {}
        _BaseOffset ("Offset to fur surface (m, world)", Float) = 0.005
        _FoamThickness ("Foam height of the top layer (m, world)", Float) = 0.05
        _FoamColor ("Foam colour", Color) = (0.95,0.96,0.98,1)
        _FoamShadow ("Foam valley tint (multiplier)", Color) = (0.66,0.73,0.86,1)
        _FurBrightness ("Fur brightness (0.5 brown fur, 1 white fur)", Range(0,1)) = 0.5
        _DirtColor ("Dirt colour (A = max opacity)", Color) = (0.15,0.10,0.065,0.88)
        _WetDarken ("Wet darkening at wet 1", Range(0,1)) = 0.25
        _WetGloss ("Wet gloss", Range(0,2)) = 0.6
        _WetTint ("Wet cool tint (sheen colour)", Color) = (0.45,0.6,0.85,1)
        _DirtPattern ("Dirt pattern (R, multiplies the procedural mud)", 2D) = "white" {}
        _FurFlowAxis ("Fur flow axis (bind-pose mesh space, dirt streaks)", Vector) = (0,0,1,0)
        _FoamScale ("Foam frequency (1 / mesh unit; mounds x0.65, grains x2.6)", Float) = 60
        _FoamSoft ("Foam cut edge softness", Range(0.01,0.5)) = 0.12
        _FoamBump ("(unused since rev 3)", Range(0,3)) = 0.6
        _FoamRim ("Foam rim brightening", Range(0,1)) = 0.3
        _CoatOffset ("Continuous coat offset (world metres)", Float) = 0.015
        _FurCoatFloor ("Non-head long-fur clearance (world metres)", Float) = 0
        _HeadMin ("Head bounds minimum (bind space)", Vector) = (0,0,0,0)
        _HeadInvSize ("Head bounds inverse size", Vector) = (1,1,1,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        UsePass "KominkaHillside/Wash Shell/BASE"
        Pass
        {
            Name "LATHER"
            Tags { "LightMode"="ForwardBase" }
            Cull Back ZTest LEqual ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "KH_WashCommon.cginc"
            sampler2D _Mask, _PosMap;
            float4 _PosMap_TexelSize, _HeadMin, _HeadInvSize;
            float _BaseOffset, _CoatOffset, _FoamThickness, _FurCoatFloor;
            half4 _FoamColor;
            float _BoneToPart[64], _PartFoamLimit[16];
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1;
                half3 ambient:TEXCOORD2; UNITY_FOG_COORDS(3) UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 pm=tex2Dlod(_PosMap,float4(KHWash_SnapUV(v.uv,_PosMap_TexelSize),0,0));
                int bone=KHWash_BoneIndex(pm.a);
                int part=bone<0?0:clamp((int)(_BoneToPart[bone]+.5),0,15);
                float3 n=UnityObjectToWorldNormal(v.normal);
                float offset=part==7?_BaseOffset:part==8?_BaseOffset+_FoamThickness*.6:part<=2?_CoatOffset:_BaseOffset+.009;
                if(part!=7)offset=max(offset,_FurCoatFloor);
                float3 wp=mul(unity_ObjectToWorld,v.vertex).xyz+n*offset;
                o.pos=mul(UNITY_MATRIX_VP,float4(wp,1));o.uv=v.uv;o.normal=n;
                o.ambient=max(half3(0,0,0),ShadeSH9(float4(n,1)));UNITY_TRANSFER_FOG(o,o.pos);return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 pm=tex2D(_PosMap,KHWash_SnapUV(i.uv,_PosMap_TexelSize));
                int bone=KHWash_BoneIndex(pm.a);int part=bone<0?0:clamp((int)(_BoneToPart[bone]+.5),0,15);
                float limit=_PartFoamLimit[part];limit=limit>0?saturate(limit):1;
                half4 mask=tex2D(_Mask,i.uv);
                half foam=min(mask.g,limit);
                half alpha=smoothstep(.015,.24,foam);
                if(part==7)
                {
                    // Keep muzzle/eyes (front/lower head) and ear tips clear; a broad soft crown remains.
                    float3 q=(pm.xyz-_HeadMin.xyz)*_HeadInvSize.xyz;
                    float crown=smoothstep(.22,.58,q.y)*smoothstep(.40,.67,q.z)*(1-smoothstep(.83,1,q.z));
                    alpha*=crown*.72;
                }
                clip(alpha-.005);
                half3 n=normalize(i.normal),l=normalize(_WorldSpaceLightPos0.xyz+float3(0,1e-5,0));
                half3 lighting=i.ambient+_LightColor0.rgb;
                lighting*=min(1.0,1.0/max(max(lighting.r,lighting.g),max(lighting.b,.0001)));
                half3 colour=_FoamColor.rgb*lighting*(.65+.35*saturate(dot(n,l)*.5+.5));
                colour*=lerp(half3(1,1,1),half3(.86,.80,.72),saturate(mask.r)*.5);
                fixed4 c=fixed4(colour,alpha);UNITY_APPLY_FOG(i.fogCoord,c);return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
