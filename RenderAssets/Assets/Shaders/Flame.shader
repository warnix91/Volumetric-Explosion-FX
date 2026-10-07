Shader "VolumetricExplosionFX/Flame"
{
    Properties
    {
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _Width ("Base radius over height", Float) = 0.3
        _Rise ("Rise of the turbulence (heights per second for a 1 m flame)", Float) = 2.6
        _Intensity ("Emission", Float) = 1.35
        _Opacity ("Occlusion of what lies behind", Float) = 0.9
        _Steps ("March steps", Float) = 18
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
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
            float _Width, _Rise, _Intensity, _Opacity, _Steps;
            float4 _VefxUp;
            struct appdata
            {
                float4 vertex:POSITION; float4 color:COLOR;
                float4 tc0:TEXCOORD0;   // particle position (the flame's base), size (its height)
                float4 tc1:TEXCOORD1;   // corner (0..1), age (0..1), stable random
            };
            struct v2f
            {
                float4 pos:SV_POSITION; float4 color:COLOR;
                float3 world:TEXCOORD0;  // point of the quad
                float4 base:TEXCOORD1;   // flame base, height
                float4 misc:TEXCOORD2;   // age, rise rate, seed, fade near the camera
            };
            v2f vert(appdata v)
            {
                v2f o;
                float3 base=mul(unity_ObjectToWorld,float4(v.tc0.xyz,1)).xyz;
                float h=max(v.tc0.w*length(unity_ObjectToWorld._m00_m10_m20),0.01);
                float3 up=normalize(_VefxUp.xyz+float3(0,1e-5,0));
                float3 centre=base+up*(0.5*h);
                float reach=sqrt(0.25*h*h+_Width*_Width*h*h)*1.25;
                float3 view=centre-_WorldSpaceCameraPos; float dist=max(length(view),1e-3); view/=dist;
                float push=min(reach,max(dist-reach*0.05-0.3,0));
                float grow=(dist-push)/sqrt(max(dist*dist-reach*reach,1e-4));
                float3 right=normalize(cross(UNITY_MATRIX_V[1].xyz,view)), lift=cross(view,right);
                float2 corner=(v.tc1.xy-0.5)*2;
                float3 world=centre-view*push+(right*corner.x+lift*corner.y)*reach*clamp(grow,0.05,4)*1.04;
                o.pos=mul(UNITY_MATRIX_VP,float4(world,1));
                o.world=world; o.color=v.color;
                o.base=float4(base,h);
                o.misc=float4(v.tc1.z,_Rise/sqrt(max(h,0.3)),v.tc1.w*61.7,saturate((dist-reach*1.05)/(reach*0.6+0.5)));
                return o;
            }
            float3 Ramp(float t)
            {
                float3 c=lerp(float3(0.42,0.035,0),float3(1,0.26,0.02),saturate(t*2.6));
                c=lerp(c,float3(1,0.6,0.1),saturate(t*2.6-0.85));
                return lerp(c,float3(1,0.83,0.5),saturate(t*2.6-1.7));
            }
            float4 frag(v2f i):SV_Target
            {
                float3 up=normalize(_VefxUp.xyz+float3(0,1e-5,0));
                float3 B=i.base.xyz; float H=i.base.w;
                float3 ro=_WorldSpaceCameraPos, rd=normalize(i.world-ro);
                float R=_Width*H*1.35;
                float3 oc=ro-B;
                float oy=dot(oc,up), dy=dot(rd,up);
                float3 op=oc-up*oy, dp=rd-up*dy;
                float qa=dot(dp,dp), qb=dot(op,dp), qc=dot(op,op)-R*R;
                float disc=qb*qb-qa*qc;
                if(disc<=0||qa<1e-8) discard;
                disc=sqrt(disc);
                float t0=(-qb-disc)/qa, t1=(-qb+disc)/qa;
                if(abs(dy)>1e-6)
                {
                    float s0=(0-oy)/dy, s1=(H*1.05-oy)/dy;
                    t0=max(t0,min(s0,s1)); t1=min(t1,max(s0,s1));
                }
                else if(oy<0||oy>H*1.05) discard;
                t0=max(t0,0);
                if(t1<=t0) discard;
                float age=i.misc.x, seed=i.misc.z;
                float life=smoothstep(0,0.15,age)*(1-smoothstep(0.55,1,age))*i.color.a*i.misc.w;
                if(life<0.003) discard;
                float t=_Time.y*i.misc.y;
                float steps=clamp(_Steps,6,32), dt=(t1-t0)/steps;
                float2 px=floor(i.pos.xy);
                float jitter=frac(dot(px,float2(0.375,0.4375))+frac(px.y*0.5)*0.25+0.0625);
                float3 col=0; float T=1;
                [loop] for(int k=0;k<32;k++)
                {
                    if(k>=steps||T<0.03) break;
                    float3 p=ro+rd*(t0+(k+jitter)*dt)-B;
                    float y=dot(p,up)/H;
                    float3 q=p/H-up*t+seed;
                    float4 e=tex3D(_NoiseTex,q*0.55);
                    float3 w=(e.rgb-0.5)*(0.25+0.7*y)*0.45;
                    float3 hp=p/H+w; hp-=up*dot(hp,up);
                    float rad=length(hp);
                    float wide=_Width*pow(saturate(1-y),0.65)*sqrt(saturate(y*6+0.05))*(0.8+0.4*e.a);
                    if(rad>wide*1.5) continue;
                    float4 a=tex3D(_NoiseTex,(q+w)*1.25+0.13);
                    float4 b=tex3D(_NoiseTex,(q+w)*2.7+0.57);
                    float turb=a.r*0.6+b.g*0.4;
                    float body=saturate(1-rad/max(wide,1e-3));
                    float shape=body*1.3-(1-turb)*(0.35+0.85*y)+(turb-0.5)*0.3;
                    float D=saturate(shape*1.7)*life;
                    if(D<0.002) continue;
                    float heat=D*(1.2-0.55*y)*(0.85+0.3*b.b);
                    float absorbed=1-exp(-D*9*dt/H);
                    col+=T*absorbed*Ramp(heat);
                    T*=1-absorbed;
                }
                float cover=1-T;
                if(cover<0.003) discard;
                return float4(col*_Intensity,cover*_Opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
