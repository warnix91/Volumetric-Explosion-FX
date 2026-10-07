Shader "VolumetricExplosionFX/HeroVolume"
{
    Properties
    {
        _Shell ("Shell radius, thickness, opacity, erosion", Vector) = (0.6,0.06,0.8,0)
        _Core ("Core radius, heat, glow, rise", Vector) = (0.15,1,1,0)
        _Cloud ("Vapour, dust, vapour flag, fade", Vector) = (0,0.5,0,1)
        _Context ("Surface kind, atmosphere, age, seed", Vector) = (1,1,0.2,37)
        _LightDirection ("Local sun direction, ambient", Vector) = (-0.6,0.8,0.2,0.35)
        _SunColor ("Sun color, strength", Color) = (1,0.96,0.9,1)
        _Fire ("Noise radius, hottest lobe, roughness, body", Vector) = (0.3,0,0.6,0)
        _Smoke ("Smoke colour, soot", Vector) = (0.5,0.48,0.45,0)
        _Bounds ("Burning region centre, radius", Vector) = (0,0,0,0)
        _L0 ("Lobe 0 centre, radius", Vector) = (0,0,0,0)
        _L1 ("Lobe 1 centre, radius", Vector) = (0,0,0,0)
        _L2 ("Lobe 2 centre, radius", Vector) = (0,0,0,0)
        _L3 ("Lobe 3 centre, radius", Vector) = (0,0,0,0)
        _L4 ("Stem centre, radius", Vector) = (0,0,0,0)
        _A0 ("Lobe 0 axis, stretch", Vector) = (0,1,0,1)
        _A1 ("Lobe 1 axis, stretch", Vector) = (0,1,0,1)
        _A2 ("Lobe 2 axis, stretch", Vector) = (0,1,0,1)
        _A3 ("Lobe 3 axis, stretch", Vector) = (0,1,0,1)
        _A4 ("Stem axis, stretch", Vector) = (0,1,0,1)
        _D0 ("Lobe 0 displacement, heat", Vector) = (0,0,0,0)
        _D1 ("Lobe 1 displacement, heat", Vector) = (0,0,0,0)
        _D2 ("Lobe 2 displacement, heat", Vector) = (0,0,0,0)
        _D3 ("Lobe 3 displacement, heat", Vector) = (0,0,0,0)
        _D4 ("Stem displacement, density", Vector) = (0,0,0,0)
        _Motion ("Roll of the rising head (rad), white-hot flame share", Vector) = (0,0,0,0)
        _Dust ("Colour of the body's dust", Vector) = (0.62,0.56,0.46,0)
        _Steps ("March steps across the burning region", Float) = 32
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _BlueNoise ("Original blue-noise dither", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            float4 _Shell, _Core, _Cloud, _Context, _LightDirection, _SunColor, _Fire, _Smoke, _Bounds;
            float4 _L0, _L1, _L2, _L3, _L4, _A0, _A1, _A2, _A3, _A4, _D0, _D1, _D2, _D3, _D4, _Motion, _Dust;
            float _Steps;
            struct v2f { float4 vertex:SV_POSITION; float3 world:TEXCOORD0; float4 screen:TEXCOORD1; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.vertex=UnityObjectToClipPos(vertex);
                o.world=mul(unity_ObjectToWorld,vertex).xyz; o.screen=ComputeScreenPos(o.vertex); return o;
            }
            float4 N(float3 p) { return tex3Dlod(_NoiseTex,float4(p,0)); }
            float Dither(float2 pixel) { return tex2Dlod(_BlueNoise,float4((pixel+0.5)/64.0,0,0)).r; }
            float3 Seed() { return float3(_Context.w*0.37,_Context.w*0.21,_Context.w*0.13); }
            float3 CoreCenter() { return float3(0,(_Context.x>0.5?_Core.x*0.7:0)+_Core.w,0); }
            float3 Chroma(float t)
            {
                float3 c=lerp(float3(1,0.1,0),float3(1,0.3,0.01),saturate(t*4));
                c=lerp(c,float3(1,0.43,0.04),saturate(t*4-1));
                c=lerp(c,float3(1,0.52,0.1),saturate(t*4-2));
                return lerp(c,float3(1,0.62,0.24),saturate(t*4-3));
            }
            float3 FlameColour(float t)
            {
                float3 c=float3(0.45,0.08,0)*saturate(t/0.45);
                c=lerp(c,float3(0.86,0.27,0.03),saturate((t-0.45)/0.15));
                c=lerp(c,float3(0.97,0.5,0.08),saturate((t-0.6)/0.15));
                c=lerp(c,float3(1,0.74,0.24),saturate((t-0.75)/0.13));
                c=lerp(c,float3(1,0.9,0.58),saturate((t-0.88)/0.12));
                return lerp(c,float3(1,0.96,0.86),_Motion.y*saturate(t*2.5-1.4));
            }
            float3 Glow(float t) { return -log(1-min(FlameColour(saturate(t)),0.985)); }
            float3 Expose(float3 e) { return 1-exp(-e); }
            float3 Rotate(float3 v,float3 axis,float angle)
            {
                float c=cos(angle), s=sin(angle);
                return v*c+cross(axis,v)*s+axis*dot(axis,v)*(1-c);
            }
            float3 Lighting(float3 n,float3 view,float3 p,float fireLight)
            {
                float3 L=_LightDirection.xyz;
                float diffuse=saturate(dot(n,L)*0.5+0.5);
                float forward=pow(saturate(dot(view,L)),6);
                float3 sky=float3(0.78,0.86,1)*max(_LightDirection.w,0.24*_SunColor.a);
                float3 sun=_SunColor.rgb*_SunColor.a*(0.22+0.78*diffuse*diffuse+0.3*forward);
                float3 c=p-CoreCenter();
                float3 inner=lerp(Chroma(_Core.y),float3(1,0.92,0.8),0.5)*_Core.y*0.9/(1+40*dot(c,c)/(_Shell.x*_Shell.x+0.01));
                float3 f=(p-_L0.xyz)/(_L0.w+0.03);
                inner+=Chroma(_Fire.y*0.8)*_Fire.y*1.3*fireLight*(_Context.x>1.5?0.25:1)/(1+dot(f,f)*1.5);
                return sky+sun+inner;
            }
            float3 Albedo(float3 p,float mottle)
            {
                float3 c=float3(0.93,0.91,0.87);
                float low=saturate(1-max(p.y,0)/(_Shell.x*0.9+0.05));
                if(_Context.x>1.5) c=float3(0.88,0.93,0.98);
                else if(_Context.x>0.5) c=lerp(c,saturate(_Dust.rgb*1.25),saturate(_Cloud.y*1.3)*(0.35+0.65*low));
                return c*lerp(0.42,1.15,saturate(mottle*1.35-0.17));
            }
            float4 Over(float4 acc,float4 layer) { return acc+(1-acc.a)*layer; }
            float4 ShellCrossing(float3 p,float radius,float path,float3 view,float soft)
            {
                float3 n=p/max(radius,1e-3);
                float age=_Context.z;
                bool water=_Context.x>1.5;
                float3 s=Seed();
                float3 w=N(n*0.4+s*0.013+float3(0.011,0.01,0.012)*age).agr-0.5;
                float3 q=water?n*float3(1,0.45,1):n;
                float mottle=N(q*0.78+w*0.35+s*0.021+float3(0,-0.02,0)*age).r;
                float detail=N(q*2.0+w*0.6+s*0.029+float3(0.03,-0.05,0.02)*age).g;
                float v=mottle*0.4+detail*0.6;
                float threshold=_Shell.w+(water?0.45*saturate(n.y)*saturate(_Shell.w*3+0.15):0);
                float mask=smoothstep(threshold-0.2,threshold+0.12,v)*(1-0.4*_Shell.w);
                if(water) mask*=smoothstep(0.85,0.5,n.y);
                float density=max(_Shell.z*mask*(0.35+1.5*(mottle-0.4)),0)*_Cloud.w;
                if(_Context.x>0.5) density*=smoothstep(-0.1,0.15*radius+0.02,p.y);
                float alpha=1-exp(-density*path*16);
                if(_Context.x>0.5) alpha=saturate(alpha+(1-alpha)*_Cloud.y*(water?0.45:0.7)*exp(-max(p.y,0)/(0.12*radius+0.02))*mask*saturate(_Shell.z*4));
                alpha*=soft;
                float3 color=Albedo(p,mottle*0.6+detail*0.4)*Lighting(n,view,p,1);
                color+=float3(1,0.9,0.75)*_Core.y*(1-_Context.y)*0.5;
                return float4(color*alpha,alpha);
            }
            float4 CoreLayer(float b2,float3 closest,float visible)
            {
                float sigma=max(_Core.x,1e-3);
                float g=exp(-b2/(sigma*sigma));
                float reach=saturate(1-sqrt(b2)/(4*sigma));
                float halo=exp(-b2/(sigma*sigma*6))*reach*reach;
                float h=_Core.y;
                float n=N(closest*1.8+Seed()*0.03+_Context.z*0.6).a*0.6+N(closest*3.7-Seed()*0.02+_Context.z*1.1).g*0.4;
                float3 e=Glow(saturate(h*(1.1-0.5*sqrt(b2)/sigma)))*g*(0.65+0.7*n)*(_Fire.w>0.02?0.16:0.26);
                e+=float3(1,0.86,0.66)*_Core.z*halo*0.6;
                e+=float3(1,0.95,0.88)*_Core.z*g*1.6;
                float a=saturate(0.45*g*saturate(h*2));
                return float4(e,a)*visible*_Cloud.w;
            }
            float LobeDistance(float3 p,float4 L,float4 A)
            {
                float3 d=p-L.xyz;
                float along=dot(d,A.xyz);
                return sqrt(along*along/(A.w*A.w)+max(dot(d,d)-along*along,0))/max(L.w,1e-4);
            }
            float SMin(float a,float b,float k) { float h=saturate(0.5+0.5*(b-a)/k); return lerp(b,a,h)-k*h*(1-h); }
            float Chord(float3 p,float3 L,float4 C,float4 A)
            {
                if(C.w<1e-4) return 0;
                float3 d=p-C.xyz;
                float da=dot(d,A.xyz), la=dot(L,A.xyz), inv=1/max(A.w,0.2);
                float3 dp=d+A.xyz*(da*inv-da), lp=L+A.xyz*(la*inv-la);
                float a=max(dot(lp,lp),1e-6), b=dot(dp,lp), c=dot(dp,dp)-C.w*C.w;
                float h=b*b-a*c;
                if(h<=0) return 0;
                h=sqrt(h);
                float t1=(-b+h)/a;
                if(t1<=0) return 0;
                return t1-max((-b-h)/a,0);
            }
            float Occlusion(float3 p,float3 L)
            {
                return Chord(p,L,_L0,_A0)+Chord(p,L,_L1,_A1)+Chord(p,L,_L2,_A2)+Chord(p,L,_L3,_A3)+Chord(p,L,_L4,_A4)*saturate(_D4.w*2);
            }
            float3 Fireball(float3 p,out float shade,out float heat,out bool fringe)
            {
                shade=0.5; heat=0;
                float r0=_L0.w>1e-4?LobeDistance(p,_L0,_A0):8;
                float r1=_L1.w>1e-4?LobeDistance(p,_L1,_A1):8;
                float r2=_L2.w>1e-4?LobeDistance(p,_L2,_A2):8;
                float r3=_L3.w>1e-4?LobeDistance(p,_L3,_A3):8;
                float rs=_L4.w>1e-4?LobeDistance(p,_L4,_A4):8;
                float kk=0.4+0.45*saturate(1-_Fire.y*1.6);
                float r=SMin(SMin(r0,r1,kk),SMin(r2,r3,kk),kk);
                fringe=r<1.6||rs<1.4;
                if(r>1.9&&rs>1.3) return 0;
                bool inStem=rs<1.3;
                float w0=exp(-3*r0), w1=exp(-3*r1), w2=exp(-3*r2), w3=exp(-3*r3), ws=exp(-3*rs)*0.5;
                float wsum=w0+w1+w2+w3+ws+1e-5;
                heat=(w0*_D0.w+w1*_D1.w+w2*_D2.w+w3*_D3.w)/wsum;
                float3 disp=(w0*_D0.xyz+w1*_D1.xyz+w2*_D2.xyz+w3*_D3.xyz+ws*_D4.xyz)/wsum;
                float scale=max(_Fire.x,1e-3);
                float3 q=(p-disp)/scale;
                if(abs(_Motion.x)>1e-3&&_L0.w>1e-4)
                {
                    float3 rel=(p-_L0.xyz)/_L0.w;
                    float hl=length(rel.xz);
                    if(hl>1e-3)
                    {
                        float3 axis=float3(rel.z,0,-rel.x)/hl;
                        float3 c=(_L0.xyz-disp)/scale;
                        q=c+Rotate(q-c,axis,-_Motion.x*saturate(hl*1.5)*exp(-dot(rel,rel)*0.35));
                    }
                }
                float cool=1-heat;
                float amp=_Fire.z*(1.45+0.2*cool), lump=0.5+0.3*cool, heads=0.55*cool;
                float age=_Context.z;
                float3 s=Seed();
                float3 w=N(q*0.21+s*0.031+float3(0.021,0.013,0.017)*age).agr-0.5;
                float base=1-r+lump*w.x+heads*w.z+0.05;
                if(base+amp*0.42<=0&&!inStem) return 0;
                float3 qa=q*0.42+w*0.24+s*0.017-float3(0,0.035,0)*age;
                float4 a=N(qa);
                float n=a.r*0.62+a.g*0.38;
                if(base+amp*(n-0.5)<=0&&!inStem) return 0;
                float f=N(q*1.13+w*0.5+s*0.023+float3(0.013,-0.05,0.011)*age).b;
                float shape=1-r+amp*(n-0.5)+0.1*(f-0.5)+lump*w.x+heads*w.z;
                float density=saturate(shape*lerp(9,3.5,cool))*(0.55+0.7*n);
                density*=saturate(1-cool*cool*(1-f)*saturate(1-shape*3)*1.2);
                density*=_Fire.w;
                float depth=saturate(shape*2.5);
                float crest=saturate((n-0.5)*2.4+0.5), inner=saturate(depth*1.5);
                float T=saturate(0.45+0.38*crest+0.2*inner+0.1*(f-0.5)+0.08*(a.g-0.5)+0.08*saturate(1.1-r)-0.2*cool*(1-inner));
                float temp=T*saturate((T-cool)*4+0.5)*smoothstep(0,0.12,heat);
                float fade=cool*cool;
                float skin=saturate(1-inner*1.4);
                float gather=smoothstep(0.22,0.85,saturate((0.66-temp)*3)*(0.5+0.9*a.g)+skin*sqrt(fade)*cool*1.5*(0.5+0.9*f));
                float soot=saturate(_Smoke.w*1.6)*gather;
                if(inStem)
                {
                    float stem=saturate((1-rs+0.6*(n-0.5))*2.2)*_D4.w*0.65;
                    if(stem>density) { soot=max(soot,_Smoke.w*0.7); temp*=0.3; }
                    density=max(density,stem);
                }
                if(density>0.002)
                {
                    float4 a2=N(qa+_LightDirection.xyz*0.05);
                    shade=saturate(0.5+(n-(a2.r*0.62+a2.g*0.38))*6);
                }
                return float3(density,temp,soot);
            }
            float2 Media(float3 p,out float mottle)
            {
                mottle=0.5;
                float rs=_Shell.x;
                float vapour=0, skirt=0;
                if(_Cloud.x>0.002)
                {
                    float3 c=float3(0,(_Context.x>0.5?0.4*rs:0)+_Core.w,0);
                    vapour=_Cloud.x*saturate(1.2-length((p-c)*float3(1,1.1,1))/(0.7*rs+0.12));
                }
                if(_Context.x>0.5&&_Cloud.y>0.002)
                {
                    float rho=length(p.xz);
                    float haze=0.45*smoothstep(rs*1.05,rs*0.3,rho)*exp(-max(p.y,0)/(0.04+0.12*rs));
                    float surge=exp(-pow((rho-rs*0.92)/(0.1*rs+0.02),2))*exp(-max(p.y,0)/(0.03+0.14*rs))*1.6;
                    skirt=_Cloud.y*(haze+surge);
                }
                if(vapour+skirt<0.004) return 0;
                mottle=N(p*0.9+Seed()*0.02+float3(0.04,-0.09,0.03)*_Context.z).r;
                return float2(vapour*smoothstep(0.3,0.7,mottle),skirt*smoothstep(0.28,0.66,mottle))*_Cloud.w;
            }
            float2 Sphere(float3 o,float3 dn,float3 c,float radius)
            {
                float3 oc=o-c; float b=dot(oc,dn), q=dot(oc,oc)-radius*radius, h=b*b-q;
                if(h<=0||radius<=0) return float2(1e9,-1e9);
                h=sqrt(h); return float2(-b-h,-b+h);
            }
            float2 LobeSpan(float3 o,float3 dn,float4 L,float4 A,float reach)
            {
                return L.w>1e-4?Sphere(o,dn,L.xyz,L.w*max(A.w,1)*reach):float2(1e9,-1e9);
            }
            float4 frag(v2f i):SV_Target
            {
                float3 worldRay=normalize(i.world-_WorldSpaceCameraPos);
                float3 o=mul(unity_WorldToObject,float4(_WorldSpaceCameraPos,1)).xyz;
                float3 d=mul((float3x3)unity_WorldToObject,worldRay);
                float dl=max(length(d),1e-6); float3 dn=d/dl;
                float3 safeDir=sign(d+1e-10)*max(abs(d),1e-6);
                float3 a=(-1-o)/safeDir, b=(1-o)/safeDir;
                float3 lo=min(a,b), hi=max(a,b);
                float entry=max(0,max(lo.x,max(lo.y,lo.z))), exit=min(hi.x,min(hi.y,hi.z));
                float sceneEye=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                float rayEye=max(1e-4,dot(worldRay,-UNITY_MATRIX_V[2].xyz));
                float sceneT=sceneEye/rayEye;
                exit=min(exit,sceneT);
                if(_Context.x>0.5)
                {
                    const float clipY=-0.1;
                    if(abs(d.y)>1e-6)
                    {
                        float tp=(clipY-o.y)/d.y;
                        if(d.y<0) exit=min(exit,tp); else entry=max(entry,tp);
                    }
                    else if(o.y<clipY) return 0;
                }
                if(exit<=entry) return 0;
                float rs=_Shell.x;
                float ob=dot(o,dn), b2=max(dot(o,o)-ob*ob,0);
                float3 closestDir=o-dn*ob; closestDir=dot(closestDir,closestDir)>1e-6?normalize(closestDir):dn;
                float rsl=rs*(1+0.18*(N(closestDir*0.45+Seed()*0.011+float3(0,0.075,0.05)*_Context.z).r-0.5));
                float t0=-1, t1=-1, path=0;
                float hs=rsl*rsl-b2;
                if(hs>0)
                {
                    float sqs=sqrt(hs), ri=max(rsl-_Shell.y,0);
                    path=sqs-sqrt(max(ri*ri-b2,0));
                    t0=(-ob-sqs)/dl; t1=(-ob+sqs)/dl;
                }
                bool pend0=hs>0&&_Shell.z>0.001&&t0>=entry&&t0<=exit;
                bool pend1=hs>0&&_Shell.z>0.001&&t1>=entry&&t1<=exit;
                float3 cc=CoreCenter();
                float cs=dot(cc-o,dn); float3 cp=o+dn*cs; float cb2=dot(cp-cc,cp-cc);
                float tc=cs/dl, sigma=max(_Core.x,1e-3);
                bool pendc=(_Core.y>0.002||_Core.z>0.002)&&cb2<sigma*sigma*16&&tc+3*sigma/dl>=entry;
                float coreVisible=saturate((exit-tc)*dl/sigma+0.5);
                bool fireball=_Fire.w>0.002&&_Bounds.w>1e-3;
                bool vapour=_Cloud.x>0.002;
                bool skirt=_Context.x>0.5&&_Cloud.y>0.002;
                float2 span=float2(1e9,-1e9);
                float extent=0;
                if(fireball)
                {
                    float2 k0=LobeSpan(o,dn,_L0,_A0,2), k1=LobeSpan(o,dn,_L1,_A1,2), k2=LobeSpan(o,dn,_L2,_A2,2);
                    float2 k3=LobeSpan(o,dn,_L3,_A3,2), k4=LobeSpan(o,dn,_L4,_A4,1.3);
                    float2 k=float2(min(min(min(k0.x,k1.x),min(k2.x,k3.x)),k4.x),max(max(max(k0.y,k1.y),max(k2.y,k3.y)),k4.y));
                    span=float2(min(span.x,k.x),max(span.y,k.y)); extent=max(extent,1.5*_Bounds.w);
                }
                if(vapour)
                {
                    float vr=0.84*rs+0.15; float3 vc=float3(0,(_Context.x>0.5?0.4*rs:0)+_Core.w,0);
                    float2 k=Sphere(o,dn,vc,vr); span=float2(min(span.x,k.x),max(span.y,k.y)); extent=max(extent,2*vr);
                }
                if(skirt)
                {
                    float top=3*(0.04+0.14*rs)+0.02, rr=rs*1.15+top;
                    float2 k=Sphere(o,dn,float3(0,0,0),rr);
                    if(abs(dn.y)>1e-5)
                    {
                        float ya=(-0.1-o.y)/dn.y, yb=(top-o.y)/dn.y;
                        k=float2(max(k.x,min(ya,yb)),min(k.y,max(ya,yb)));
                    }
                    else if(o.y>top) k=float2(1e9,-1e9);
                    span=float2(min(span.x,k.x),max(span.y,k.y)); extent=max(extent,2*rr);
                }
                float tA=max(entry,span.x/dl), tB=min(exit,span.y/dl);
                float stepT=0;
                if(tB>tA&&extent>0)
                {
                    float stepLocal=extent/max(_Steps,4);
                    float steps=clamp(ceil((tB-tA)*dl/stepLocal),3,64);
                    stepT=(tB-tA)/steps;
                }
                else tB=tA-1;
                float t=tA+stepT*Dither(floor(i.vertex.xy));
                const float softDistance=0.08;
                float soft0=saturate((sceneT-t0)*dl/softDistance), soft1=saturate((sceneT-t1)*dl/softDistance);
                if(_Context.x>0.5)
                {
                    soft0*=saturate(((o+d*t0).y+0.1)/softDistance);
                    soft1*=saturate(((o+d*t1).y+0.1)/softDistance);
                }
                float4 acc=0;
                float3 emis=0;   // emitted light, exposed at the end like film
                float h=stepT;
                float scale=max(_Fire.x,0.02);
                float body=_Fire.w*(3.0+3.5*_Smoke.w)/scale*0.35;
                float last=0;       // length of the step that reached t
                bool outside=true;  // the previous sample was clear of the fireball
                float sunT=1, skyT=1; bool shadowDue=true;
                [loop] for(int s=0;s<96;s++)
                {
                    if(t>tB||acc.a>0.99) break;
                    if(pend0&&t0<=t) { acc=Over(acc,ShellCrossing(o+d*t0,rsl,path,dn,soft0)); pend0=false; }
                    if(pendc&&tc<=t)
                    {
                        float4 core=CoreLayer(cb2,cp,coreVisible);
                        emis+=(1-acc.a)*core.rgb; acc.a+=(1-acc.a)*core.a; pendc=false;
                    }
                    if(pend1&&t1<=t) { acc=Over(acc,ShellCrossing(o+d*t1,rsl,path,dn,soft1)); pend1=false; }
                    float3 p=o+d*t;
                    float next=stepT;
                    float3 ap=abs(p); float faces=saturate((1-max(ap.x,max(ap.y,ap.z)))/0.15);
                    if(fireball)
                    {
                        float shade,heat; bool fringe;
                        float3 f=Fireball(p,shade,heat,fringe);
                        f.x*=faces;
                        if(f.x>0.002&&outside&&last>0)
                        {
                            float lo=t-last, hi=t;
                            [unroll] for(int k=0;k<2;k++)
                            {
                                float mid=(lo+hi)*0.5, sm, hm; bool nm;
                                float3 fm=Fireball(o+d*mid,sm,hm,nm);
                                if(fm.x>0.002) { hi=mid; f=fm; shade=sm; heat=hm; } else lo=mid;
                            }
                            t=hi; p=o+d*t;
                        }
                        outside=f.x<=0.002;
                        if(fringe&&acc.a<0.3) next=stepT*0.7;
                        if(f.x>0.002)
                        {
                            float extinction=f.x*(2.2+9*heat+2*f.y+4.5*f.z)/scale;
                            float alpha=1-exp(-extinction*h*dl);
                            if(shadowDue)
                            {
                                sunT=exp(-body*Occlusion(p,_LightDirection.xyz));
                                skyT=exp(-body*0.6*Occlusion(p,float3(0,1,0)));
                            }
                            shadowDue=!shadowDue;
                            float3 albedo=min(_Smoke.rgb*1.2,0.8)*lerp(1,0.8,f.z);
                            float sun=_SunColor.a*(sunT+0.25*(1-sunT))*lerp(0.35,1.5,shade);
                            float3 sky=float3(0.72,0.8,0.95)*max(_LightDirection.w,0.22*_SunColor.a)*(0.35+0.65*skyT);
                            float3 inner=Chroma(0.55)*heat*heat*1.0*(1-f.y)*(_Context.x>1.5?0.25:1)*(1-0.65*_Motion.y);
                            float3 lit=albedo*(_SunColor.rgb*sun+sky+inner);
                            float3 e=Glow(f.y)*(1-0.92*f.z)*lerp(0.7,1.2,shade);
                            float wgt=(1-acc.a)*alpha;
                            acc.rgb+=wgt*lit*(1-saturate(f.y*2.5)); emis+=wgt*e; acc.a+=wgt;
                            next=acc.a<0.6&&f.y>0.2?stepT*0.5:stepT;
                        }
                    }
                    if(vapour||skirt)
                    {
                        float mottle;
                        float2 m=Media(p,mottle)*faces;
                        float extinction=m.x*4+m.y*6;
                        if(extinction>1e-4)
                        {
                            float alpha=1-exp(-extinction*h*dl);
                            float3 vapourAlbedo=float3(0.86,0.88,0.92);
                            float3 dustAlbedo=_Context.x>1.5?float3(0.86,0.9,0.94):saturate(_Dust.rgb*1.2);
                            float3 albedo=(vapourAlbedo*m.x+dustAlbedo*m.y)/(m.x+m.y)*lerp(0.75,1.1,mottle);
                            float3 n=normalize(p-cc+float3(0,0.25,0));
                            acc=Over(acc,float4(albedo*Lighting(n,dn,p,1)*alpha,alpha));
                        }
                    }
                    last=h; t+=h; h=next;
                }
                if(pend0) acc=Over(acc,ShellCrossing(o+d*t0,rsl,path,dn,soft0));
                if(pendc) { float4 core=CoreLayer(cb2,cp,coreVisible); emis+=(1-acc.a)*core.rgb; acc.a+=(1-acc.a)*core.a; }
                if(pend1) acc=Over(acc,ShellCrossing(o+d*t1,rsl,path,dn,soft1));
                return float4(acc.rgb+Expose(emis),acc.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
