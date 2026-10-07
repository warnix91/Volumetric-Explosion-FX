Shader "VolumetricExplosionFX/FireVolume"
{
    Properties
    {
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _BlueNoise ("Original blue-noise dither", 2D) = "gray" {}
        _Box ("Box half width (m), half height (m), march steps, seed", Vector) = (6,10,40,0)
        _Fire ("Pool radius (m), flame height (m), fire level, puffing (Hz)", Vector) = (4,12,1,0.5)
        _Lean ("Downwind lean per metre of height (x, z), rise (flame heights per second), flicker", Vector) = (0,0,0.7,1)
        _Intensity ("Emission", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Front ZWrite Off ZTest Always
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler3D _NoiseTex;
            sampler2D _BlueNoise;
            float4 _Box, _Fire, _Lean;
            float _Intensity;
            struct v2f { float4 vertex:SV_POSITION; float3 world:TEXCOORD0; float4 screen:TEXCOORD1; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.vertex=UnityObjectToClipPos(vertex);
                o.world=mul(unity_ObjectToWorld,vertex).xyz; o.screen=ComputeScreenPos(o.vertex); return o;
            }
            float3 FlameColour(float t)
            {
                float3 c=float3(0.45,0.08,0)*saturate(t/0.45);
                c=lerp(c,float3(0.86,0.27,0.03),saturate((t-0.45)/0.15));
                c=lerp(c,float3(0.97,0.5,0.08),saturate((t-0.6)/0.15));
                c=lerp(c,float3(1,0.74,0.24),saturate((t-0.75)/0.13));
                return lerp(c,float3(1,0.9,0.58),saturate((t-0.88)/0.12));
            }
            float Fire(float3 m,out float heat)
            {
                heat=0;
                float R=max(_Fire.x,0.3), L=max(_Fire.y,0.5);
                float y=m.y/L;
                if(y<0||y>1.8) return 0;
                float2 hz=m.xz-_Lean.xy*m.y;
                float rad=length(hz)/R;
                if(rad>1.5) return 0;
                float t=_Time.y*_Lean.z, s=_Box.w;
                float3 q=float3(hz.x/R,y*1.1-t,hz.y/R)+s;
                float4 e=tex3D(_NoiseTex,q*float3(0.35,0.3,0.35));
                float3 w=(e.rgb-0.5)*float3(1.1,0.5,1.1);
                float4 a=tex3D(_NoiseTex,(q+w)*float3(0.75,0.6,0.75)+0.13);
                float4 b=tex3D(_NoiseTex,(q+w)*float3(1.9,1.55,1.9)+0.57);
                float turb=saturate((a.r*0.65+b.g*0.35-0.5)*2.6+0.5);
                float width=lerp(1.0,0.35,saturate(y/1.5))*(0.8+0.4*e.a);
                float env=saturate((width-rad+(w.x+w.z)*0.15)/(0.35*width+0.05));
                float puff=sin(6.2832*(_Fire.w*_Time.y-y*1.3)+e.b*5)*0.5+0.5;
                float thresh=lerp(0.12,0.85,saturate(y/1.35))-puff*0.18*saturate(y*2)+(1-env)*0.6;
                float D=saturate((turb-thresh)*4.5)*_Fire.z;
                heat=saturate(((1.1-0.5*y)*(0.8+0.32*b.b)+0.15*(turb-0.5))*(0.5+0.5*saturate(D*2.2))*(0.75+0.25*_Fire.z));
                return D;
            }
            float4 frag(v2f i):SV_Target
            {
                float3 worldRay=normalize(i.world-_WorldSpaceCameraPos);
                float3 o=mul(unity_WorldToObject,float4(_WorldSpaceCameraPos,1)).xyz;
                float3 d=mul((float3x3)unity_WorldToObject,worldRay);
                float3 safeDir=sign(d+1e-10)*max(abs(d),1e-6);
                float3 ta=(-1-o)/safeDir, tb=(1-o)/safeDir;
                float3 lo=min(ta,tb), hi=max(ta,tb);
                float entry=max(0,max(lo.x,max(lo.y,lo.z))), exit=min(hi.x,min(hi.y,hi.z));
                float sceneEye=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                float rayEye=max(1e-4,dot(worldRay,-UNITY_MATRIX_V[2].xyz));
                exit=min(exit,sceneEye/rayEye);
                if(exit<=entry) return 0;
                float steps=clamp(_Box.z,8,64), dt=(exit-entry)/steps;
                float2 pixel=i.screen.xy/max(i.screen.w,1e-6)*_ScreenParams.xy;
                float jitter=tex2Dlod(_BlueNoise,float4((pixel+0.5)/64.0,0,0)).r;
                float L=max(_Fire.y,0.5), kappa=14/L;
                float3 col=0; float T=1;
                [loop] for(int k=0;k<64;k++)
                {
                    if(k>=steps||T<0.02) break;
                    float t=entry+(k+jitter)*dt;
                    float3 P=o+d*t;
                    float3 m=float3(P.x*_Box.x,(P.y+1)*_Box.y,P.z*_Box.x);
                    float heat; float D=Fire(m,heat);
                    if(D>0.002)
                    {
                        float absorbed=1-exp(-D*kappa*dt);
                        col+=T*absorbed*FlameColour(heat);
                        T*=1-absorbed;
                    }
                }
                float cover=1-T;
                if(cover<0.002) return 0;
                return float4(col*_Intensity*_Lean.w,cover*0.95);
            }
            ENDCG
        }
    }
    Fallback Off
}
