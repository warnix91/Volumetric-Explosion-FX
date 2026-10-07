Shader "VolumetricExplosionFX/Trail"
{
    Properties
    {
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _Glow ("Incandescent trail (additive emission)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            sampler3D _NoiseTex;
            float4 _VefxSun, _VefxAmbient, _VefxSky, _VefxFire0, _VefxFire1, _VefxFireRange;
            float _Glow;
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.color=v.color; o.uv=v.uv; return o;
            }
            float3 Fire(float4 F,float range,float3 world)
            {
                float3 d=F.xyz-world; float r2=dot(d,d)+0.25;
                return float3(1,0.55,0.22)*F.w*0.6/(1+r2/max(range*range*0.12,1));
            }
            float4 frag(v2f i):SV_Target
            {
                float4 n=tex3D(_NoiseTex,i.world*0.045);
                float4 m=tex3D(_NoiseTex,i.world*0.013+float3(0.3,0.1,0.7));
                float across=abs(i.uv.y*2-1);
                if(_Glow>0.5)
                {
                    float along=saturate(i.uv.x), w=lerp(0.4,1,along), x=across/w;
                    if(x>=1) discard;
                    float core=exp(-x*x*10), halo=exp(-x*x*2.5)*0.5*(1-x*x);
                    float torn=lerp(1,saturate(0.3+1.2*n.g+0.6*(m.r-0.5)),along*along);
                    float glow=(core+halo)*torn*i.color.a;
                    if(glow<0.002) discard;
                    float3 hot=lerp(i.color.rgb,float3(1,0.97,0.92),core*0.55*(1-along));
                    return float4(hot*glow*0.9,0);
                }
                float edge=saturate((1-across)*1.35-(n.r-0.5)*1.2-(m.a-0.5)*0.7);
                float alpha=edge*edge*(3-2*edge)*saturate(0.35+0.9*n.g+0.5*(m.r-0.5))*i.color.a*0.8;
                if(alpha<0.002) discard;
                float3 V=normalize(i.world-_WorldSpaceCameraPos);
                float forward=pow(saturate(dot(V,normalize(_VefxSun.xyz+float3(0,1e-5,0)))),5);
                float3 sun=float3(1,0.96,0.9)*_VefxSun.w*(0.3+0.45*n.b+0.15*m.g+0.9*forward);
                float3 light=sun+_VefxSky.rgb*0.75+_VefxAmbient.rgb*0.5+Fire(_VefxFire0,_VefxFireRange.x,i.world)+Fire(_VefxFire1,_VefxFireRange.y,i.world);
                float3 color=i.color.rgb*light;
                return float4(color*alpha,alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
