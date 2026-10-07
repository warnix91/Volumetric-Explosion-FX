Shader "VolumetricExplosionFX/Scorch"
{
    Properties
    {
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _Scorch ("Darkness, ember heat, seed, dust (airless)", Vector) = (0.7,0,13,0)
    }
    SubShader
    {
        Tags { "Queue"="Geometry+450" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Offset -1, -1
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            sampler3D _NoiseTex;
            float4 _Scorch;
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION,float2 uv:TEXCOORD0)
            {
                v2f o; o.pos=UnityObjectToClipPos(vertex); o.uv=uv; return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float2 c=i.uv*2-1;
                float r=length(c);
                float a=atan2(c.y,c.x)/6.2831853+0.5;
                float s=_Scorch.z;
                float2 ring=float2(cos(a*6.2831853),sin(a*6.2831853));
                float4 edgeNoise=tex3Dlod(_NoiseTex,float4(ring*0.35+s*0.071,s*0.013,0));
                float4 streak=tex3Dlod(_NoiseTex,float4(ring*1.6+s*0.033,r*0.15+s*0.017,0));
                float4 mottle=tex3Dlod(_NoiseTex,float4(c*1.3+s*0.051,s*0.029,0));
                float edge=0.5+0.45*edgeNoise.r;
                float body=1-smoothstep(edge*0.3,edge,r);
                float rays=smoothstep(0.55,0.85,streak.g)*(1-smoothstep(edge*0.6,edge*1.1,r))*0.5;
                float soot=saturate(max(body,rays)*smoothstep(0.1,0.6,mottle.r+0.3*(1-r)));
                float dark=soot*_Scorch.x;
                float specks=smoothstep(0.72,0.9,mottle.b)*body;
                float heat=_Scorch.y*specks;
                float3 glow=lerp(float3(0.9,0.15,0.02),float3(1,0.6,0.2),heat)*heat*2.2;
                float3 tint=lerp(float3(0.035,0.03,0.025),float3(0.16,0.155,0.15),saturate(_Scorch.w));
                return float4(tint*dark+glow,dark);
            }
            ENDCG
        }
    }
    Fallback Off
}
