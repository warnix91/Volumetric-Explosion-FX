Shader "VolumetricExplosionFX/HeroFlare"
{
    Properties
    {
        _Flare ("Size, intensity, height, warmth", Vector) = (0.5,1,0,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            float4 _Flare;
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o;
                float scale=length(unity_ObjectToWorld._m00_m10_m20);
                float size=_Flare.x*scale;
                float3 center=mul(unity_ObjectToWorld,float4(0,_Flare.z,0,1)).xyz;
                float3 view=mul(UNITY_MATRIX_V,float4(center,1)).xyz;
                view.xy+=vertex.xy*size;
                view.z+=min(size*1.3,max(-view.z-0.5,0));
                o.vertex=mul(UNITY_MATRIX_P,float4(view,1));
                o.uv=vertex.xy;
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float r2=dot(i.uv,i.uv);
                float core=exp(-r2*40), halo=exp(-sqrt(r2)*5)*saturate(1-r2);
                float3 warm=lerp(float3(1,0.62,0.3),float3(1,0.95,0.88),saturate(_Flare.w));
                return float4(warm*(core*1.1+halo*0.35)*_Flare.y,0);
            }
            ENDCG
        }
    }
    Fallback Off
}
