Shader "KominkaHillside/Parade Lantern Glow"
{
 Properties { _MainTex("Atlas",2D)="white"{} _Color("Glow",Color)=(1,.7,.3,1) _Gain("Gain",Float)=.65 _GoldMask("Gold pattern only",Float)=0 _Halo("Radial halo",Float)=0 _AccentMode("Hat band / Bat contour",Float)=0 }
 SubShader {
  Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
  ZWrite Off ZTest LEqual Cull Off Blend One One Offset -1,-1
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float localY:TEXCOORD1; float3 normal:TEXCOORD2; float3 view:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
   sampler2D _MainTex; float4 _Color; float _Gain,_GoldMask,_Halo,_AccentMode;
   v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.pos=UnityObjectToClipPos(v.vertex);
    if(_Halo>.5) { float3 center=mul(UNITY_MATRIX_MV,float4(0,0,0,1)).xyz; float2 size=float2(length(unity_ObjectToWorld._m00_m10_m20),length(unity_ObjectToWorld._m01_m11_m21)); o.pos=mul(UNITY_MATRIX_P,float4(center+float3(v.vertex.xy*size,0),1)); }
    o.uv=v.uv;o.localY=v.vertex.y;o.normal=UnityObjectToWorldNormal(v.normal);o.view=WorldSpaceViewDir(v.vertex); return o; }
   half4 frag(v2f i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half4 c=tex2D(_MainTex,i.uv); float mask=1;
    if(_GoldMask>.5)mask=smoothstep(.40,.72,c.r)*smoothstep(.22,.55,c.g)*(1-smoothstep(.6,.95,c.b/max(c.g,.01)));
    if(_Halo>.5){ float r=length(i.uv*2-1); mask=pow(saturate(1-r),3);c=1; }
    if(_AccentMode>.5 && _AccentMode<1.5){mask=smoothstep(.09,.11,i.localY)*(1-smoothstep(.18,.21,i.localY));c=1;}
    if(_AccentMode>1.5){mask=.12+.88*pow(1-abs(dot(normalize(i.normal),normalize(i.view))),2);c=1;}
    return half4(c.rgb*_Color.rgb*_Gain*mask*c.a,0); }
   ENDCG
  }
 }
 Fallback Off
}
