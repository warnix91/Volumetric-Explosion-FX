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
        _Patchy ("Separate flames over the spill (0 = one pool fire)", Float) = 0
        _Streak ("Burning strip along +x: length (m), box centre offset (m), half width (m) (0 = round pool)", Vector) = (0,0,0,0)
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
            #include "VolumeDepth.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler3D _NoiseTex;
            sampler2D _BlueNoise;
            float4 _Box, _Fire, _Lean, _Streak;
            float _Intensity, _Patchy;
            float4 _Blobs[8];   // pools along a crash path: along (m), across (m), radius (m), flame height (share)
            struct v2f { float4 vertex:SV_POSITION; float3 world:TEXCOORD0; float4 screen:TEXCOORD1; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.vertex=VefxProxyClip(UnityObjectToClipPos(vertex));
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
                if(_Streak.x>0)
                {
                    // Along a crash path: pools of burning fuel scattered along it, each its own size and flame
                    // height, the path wandering, gaps between them; each catches as the spreading fire reaches it. (A straight
                    // band of even fire read as fake.)
                    float best=1e6, tallest=1, size=R;
                    [unroll] for(int k=0;k<8;k++)
                    {
                        float4 B=_Blobs[k];
                        if(B.z<=0||B.x>_Streak.x+0.01) continue;
                        float dd=length(hz-B.xy)/B.z;
                        if(dd<best) { best=dd; tallest=B.w*(k==0?1:saturate((_Streak.x-B.x)/(B.z*1.5)+0.15)); size=B.z; }
                    }
                    if(best>1.5||tallest<0.03) return 0;
                    rad=best; R=size;
                    y=m.y/(L*tallest);
                    if(y>1.8) return 0;
                }
                if(rad>1.5) return 0;
                float t=_Time.y*_Lean.z, s=_Box.w;
                float tall=1;
                if(_Patchy>0)
                {
                    // Burning patches of spilled fuel: each patch has its own flame height, gaps between them.
                    float4 pn=tex3D(_NoiseTex,float3(hz.x/R*0.42+s,0.37+s*0.21,hz.y/R*0.42-s));
                    float patch=saturate((pn.r-0.3)*3.2);
                    tall=lerp(1,(0.45+0.85*pn.g)*patch,_Patchy);
                    if(tall<0.03) return 0;
                    y/=tall;
                    if(y>1.8) return 0;
                }
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
                float D=saturate((turb-thresh)*3.2)*_Fire.z;
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
                float2 range=VefxRayRange(rayEye,sceneEye);
                entry=max(entry,range.x); exit=min(exit,range.y);
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
                    float3 m=float3(P.x*_Box.x+_Streak.y,(P.y+1)*_Box.y,P.z*(_Streak.z>0?_Streak.z:_Box.x));
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
