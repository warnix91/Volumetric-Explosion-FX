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
        _Look0 ("Look: shape, turbulence, temperature gradient, flame heat", Vector) = (1,0,0,0)
        _Look1 ("Look: smoke opacity, soot darkness", Vector) = (1,0,0,0)
        _Steps ("March steps across the burning region", Float) = 32
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _FireSimGround ("Baked fireball on the ground", 3D) = "" {}
        _FireSimGround2 ("Second baked fireball on the ground", 3D) = "" {}
        _FireSimGround3 ("Third baked fireball on the ground", 3D) = "" {}
        _FireSimGround4 ("Fourth baked fireball on the ground", 3D) = "" {}
        _FireSimAir ("Baked fireball in the air", 3D) = "" {}
        _FireSimAir2 ("Second baked fireball in the air", 3D) = "" {}
        _FireSimNuke ("Baked nuclear fireball", 3D) = "" {}
        _FireSimNuke2 ("Second baked nuclear fireball", 3D) = "" {}
        _FireSimNukeAir ("Baked nuclear air burst", 3D) = "" {}
        _SimNukeDims ("Nuclear texture: nx, ny, nz, frames", Vector) = (56,112,56,24)
        _SimNukeGeom ("Nuclear half width, height (simulation units)", Vector) = (2.4,9.6,0,0)
        _SimDims ("Ground texture: nx, ny, nz, frames", Vector) = (56,70,56,24)
        _SimAirDims ("Air texture: nx, ny, nz, frames", Vector) = (56,77,56,24)
        _SimGeom ("Ground half width, height, air half width, height (simulation units)", Vector) = (1.6,4,1.6,4.4)
        _BlueNoise ("Original blue-noise dither", 2D) = "gray" {}
        _SimFlame ("1.1: flame luminosity (hydrogen burns faint and clear)", Vector) = (1,0,0,0)
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
            // VEFX_SIM: the 1.1 baked-simulation fireball. Without it the program is the 1.0/analytic renderer, so the
            // Classic look compiles to exactly the code it always had (the shader compiler reorders maths when code is added).
            #pragma multi_compile __ VEFX_SIM
            #include "UnityCG.cginc"
            #include "VolumeDepth.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler3D _NoiseTex;
            sampler3D _FireSimGround, _FireSimAir, _FireSimGround2, _FireSimGround3, _FireSimGround4, _FireSimAir2, _FireSimNuke, _FireSimNuke2, _FireSimNukeAir;
            float4 _SimNukeDims, _SimNukeGeom;
            float _SimVariant;   // which baked run this explosion's copies use (ground 0..3, air 0..1)
            float _SimLinger;    // a warhead's cooled cloud keeps its density (0: as baked)
            float _SimStem;      // 1: the late column under the cloud thins out from the ground up (fuel explosions)
            float4 _SmokeMix;    // a second smoke mixed through the cloud: colour, amount (1.1)
            float4 _Nuke, _NukeStem, _NukeRing, _NukeGround, _NukeIce, _NukeWilson;   // nuclear detonation: on, stem rise, ring, white-hot; stem; ring (see VolumeEvolution.NukeLook)
            float4 _NukeCap;    // the ripe nuclear cap drawn as its own shape: share, roll speed, height scale, 0
            float4 _SimFlame;   // x: how luminous and opaque the flame is (1; hydrogen far less)
            float4 _SimDims, _SimAirDims, _SimGeom;
            float4 _SimShape;   // 1.1 per-explosion character: vertical scale, lean x, lean z (per unit height), warp amplitude
            sampler2D _BlueNoise;
            float4 _Shell, _Core, _Cloud, _Context, _LightDirection, _SunColor, _Fire, _Smoke, _Bounds;
            float4 _L0, _L1, _L2, _L3, _L4, _A0, _A1, _A2, _A3, _A4, _D0, _D1, _D2, _D3, _D4, _Motion, _Dust, _Look0, _Look1;
            float _Steps;
            #ifdef VEFX_SIM
            float4 _VefxFire0, _VefxFire1, _VefxFireRange;   // the two strongest fires (world position, glow; ranges in m)
            float4 _VefxLamp, _VefxLampColor, _VefxLampSpot; // the strongest lamp near the blasts: position, intensity; colour, range; spot axis, cos half-angle
            float4 _VefxBolt; float _VefxBoltRange;
            // A fire or lamp in proxy units: position, intensity; and its range.
            float4 LightLocal(float4 F,float range,float boxR,out float reach)
            {
                reach=max(range/boxR,1e-3);
                return float4(mul(unity_WorldToObject,float4(F.xyz,1)).xyz,F.w);
            }
            // Light a point receives from a small source, softened like the particle smoke's fire light.
            float LightFall(float4 F,float reach,float3 p)
            {
                float3 dd=F.xyz-p; float r2=dot(dd,dd)+reach*reach*0.02;   // a lamp or fire is not a point: no hot spot right at it
                return F.w/(1+r2/(reach*reach*0.25))*saturate(2.2-sqrt(r2)/reach);
            }
            // Smoke and dust scatter sunlight mostly forward (Henyey-Greenstein, a strong forward lobe and a weak back one):
            // a thin edge seen against the sun lights up. 1 seen side-on, about 1 with the sun behind the camera, capped at 4.
            float HG(float g,float c) { return (1-g*g)/pow(max(1+g*g-2*g*c,1e-4),1.5); }
            float Phase(float c) { return min((0.75*HG(0.55,c)+0.25*HG(-0.2,c))/0.578,4); }
            #endif
            struct v2f { float4 vertex:SV_POSITION; float3 world:TEXCOORD0; float4 screen:TEXCOORD1; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.vertex=VefxProxyClip(UnityObjectToClipPos(vertex));
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
                #ifdef VEFX_SIM
                // 1.1: white flames whiten only their very hottest core, so the fireball keeps its colour and relief.
                return lerp(c,float3(1,0.94,0.8),0.5*_Motion.y*saturate(t*2.5-1.6));
                #else
                return lerp(c,float3(1,0.96,0.86),_Motion.y*saturate(t*2.5-1.4));
                #endif
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
                #ifdef VEFX_SIM
                // 1.1: a white cloud (cryogenic fog, steam, dust) in full sun is white, not blown out: its sunlit side was
                // lit up to 1.8 times over, and the fire inside it burnt it to a white dome. A hydrogen flame lights it less.
                sun*=0.72; inner*=0.55*_SimFlame.x;
                #endif
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
                float alpha=1-exp(-density*path*16*(_Dust.w>0?_Dust.w:1));
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
                float shapeK=_Look0.x, turb=_Look0.y, grad=_Look0.z;
                float amp=_Fire.z*(1.45+0.2*cool)*shapeK, lump=(0.5+0.3*cool)*shapeK, heads=0.55*cool*shapeK;
                float age=_Context.z;
                float3 s=Seed();
                float3 w=N(q*0.21+s*0.031+float3(0.021,0.013,0.017)*age).agr-0.5;
                float base=1-r+lump*w.x+heads*w.z+0.05;
                float margin=turb*0.12;   // the fine erosion below can add this much
                if(base+amp*0.42+margin<=0&&!inStem) return 0;
                float3 qa=q*0.42+w*0.24+s*0.017-float3(0,0.035,0)*age;
                float4 a=N(qa);
                float n=a.r*0.62+a.g*0.38;
                if(base+amp*(n-0.5)+margin<=0&&!inStem) return 0;
                float f=N(q*1.13+w*0.5+s*0.023+float3(0.013,-0.05,0.011)*age).b;
                float shape=1-r+amp*(n-0.5)+0.1*(f-0.5)+lump*w.x+heads*w.z;
                if(turb>0.001&&shape>-margin&&shape<0.35)
                {
                    // Fine curling detail only in the visible skin: ragged edges, no extra cost inside.
                    float g=N(q*2.9+w*0.9+s*0.041+float3(0.02,-0.11,0.017)*age).r;
                    shape+=turb*0.24*(g-0.5)*(1-saturate(shape*3));
                }
                if(_Look1.z>0.5&&_Motion.z>0.001&&shape>-0.02)
                {
                    // The cooled gas breaks up from its edges into tendrils, thinnest parts first.
                    float g2=N(q*2.1+w*0.7+s*0.053+float3(0.02,-0.08,0.015)*age).r;
                    shape-=_Motion.z*(0.1+0.9*smoothstep(0.25,0.8,1-g2))*(0.35+0.65*cool);
                }
                float density=saturate(shape*lerp(9,3.5,cool))*(0.55+0.7*n);
                density*=saturate(1-cool*cool*(1-f)*saturate(1-shape*3)*1.2);
                density*=_Fire.w;
                float depth=saturate(shape*2.5);
                float crest=saturate((n-0.5)*2.4+0.5), inner=saturate(depth*1.5);
                float T=saturate(0.45+0.38*crest+0.2*inner+0.1*(f-0.5)+0.08*(a.g-0.5)+0.08*saturate(1.1-r)-0.2*cool*(1-inner));
                // Hot pockets and cooler lanes across the whole fireball (low-frequency channel, no extra fetch).
                float pocket=saturate(w.y*2+0.5)*2-1;
                // Asymmetric: small white-hot cores, wide darker lanes (big hot areas read as washed-out cream).
                T=saturate(T+grad*(pocket*(pocket>0?0.08:0.26)+0.12*(crest-0.5)*(1-0.5*saturate(pocket))+0.06*(inner-0.5)-0.3*cool*(1-saturate(pocket*1.5)))+_Look0.w);
                // Lanes cool first and hot pockets last, so the fire goes out patch by patch.
                cool=saturate(cool-grad*(0.32*pocket+0.1*(f-0.5))*smoothstep(0.05,0.5,cool));
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
            // ---- baked fireball simulation (1.1) ---------------------------------------------------------------
            float4 SimFetch(sampler3D tex,float4 dims,float3 s,float frame)
            {
                float nz=dims.z, count=dims.w;
                float f=clamp(frame,0,count-1.0001);
                float f0=floor(f), t=f-f0;
                float zc=clamp(s.z*nz,0.5,nz-0.5);
                float inv=1.0/(nz*count);
                float4 a=tex3Dlod(tex,float4(s.x,s.y,(f0*nz+zc)*inv,0));
                float4 b=tex3Dlod(tex,float4(s.x,s.y,((f0+1)*nz+zc)*inv,0));
                return lerp(a,b,t);
            }
            // Nearest frame only: enough for the soft sun-side shading, half the fetches.
            float SimFetch1(sampler3D tex,float4 dims,float3 s,float frame)
            {
                float nz=dims.z, f=floor(clamp(frame+0.5,0,dims.w-1));
                return tex3Dlod(tex,float4(s.x,s.y,(f*nz+clamp(s.z*nz,0.5,nz-0.5))/(nz*dims.w),0)).x;
            }
            // One baked run, chosen with real branches: a ?: chain samples every run and keeps one (seven textures read
            // for one used). _SimVariant is the same for the whole draw, so the branches are coherent.
            float4 SimSample(bool ground,float4 dims,float3 s,float frame)
            {
                [branch] if(ground)
                {
                    [branch] if(_SimVariant<0.5) return SimFetch(_FireSimGround,dims,s,frame);
                    [branch] if(_SimVariant<1.5) return SimFetch(_FireSimGround2,dims,s,frame);
                    [branch] if(_SimVariant<2.5) return SimFetch(_FireSimGround3,dims,s,frame);
                    [branch] if(_SimVariant<3.5) return SimFetch(_FireSimGround4,dims,s,frame);
                    [branch] if(_SimVariant<4.5) return SimFetch(_FireSimNuke,dims,s,frame);
                    return SimFetch(_FireSimNuke2,dims,s,frame);
                }
                [branch] if(_SimVariant<0.5) return SimFetch(_FireSimAir,dims,s,frame);
                [branch] if(_SimVariant<1.5) return SimFetch(_FireSimAir2,dims,s,frame);
                return SimFetch(_FireSimNukeAir,dims,s,frame);
            }
            float SimSample1(bool ground,float4 dims,float3 s,float frame)
            {
                [branch] if(ground)
                {
                    [branch] if(_SimVariant<0.5) return SimFetch1(_FireSimGround,dims,s,frame);
                    [branch] if(_SimVariant<1.5) return SimFetch1(_FireSimGround2,dims,s,frame);
                    [branch] if(_SimVariant<2.5) return SimFetch1(_FireSimGround3,dims,s,frame);
                    [branch] if(_SimVariant<3.5) return SimFetch1(_FireSimGround4,dims,s,frame);
                    [branch] if(_SimVariant<4.5) return SimFetch1(_FireSimNuke,dims,s,frame);
                    return SimFetch1(_FireSimNuke2,dims,s,frame);
                }
                [branch] if(_SimVariant<0.5) return SimFetch1(_FireSimAir,dims,s,frame);
                [branch] if(_SimVariant<1.5) return SimFetch1(_FireSimAir2,dims,s,frame);
                return SimFetch1(_FireSimNukeAir,dims,s,frame);
            }
            float SimGas(bool ground,float4 dims,float3 s,float frame)
            {
                [branch] if(any(s<0)||any(s>1)) return 0;
                return SimSample1(ground,dims,s,frame);
            }
            // One copy of the simulation: gas, temperature, soot; also soft billow noise and how much gas lies toward the sun.
            float4 SimLobe(float3 p,float4 L,float4 A,float4 D,bool ground,bool sunDue,out float fine,out float toSun,out float mid)
            {
                fine=0.5; toSun=0; mid=0.5;
                float3 d=p-L.xyz;
                [branch] if(dot(d,d)>L.w*L.w) return 0;
                d=p-D.xyz;
                // This explosion's own shape: taller or squatter and leaning (the CPU moves the bounding sphere to match).
                float tall=_SimShape.x>0.05?_SimShape.x:1;
                d.xz-=_SimShape.yz*max(d.y,0); d.y/=tall;
                float sc=max(abs(A.z),1e-5), mirror=A.z<0?-1:1;
                float k2=max(dot(A.xy,A.xy),1e-4);   // |heading|^2 = stretch^2: the cross axis gets 1/stretch
                float3 q=float3(A.x*d.x+A.y*d.z,d.y,(-A.y*d.x+A.x*d.z)/k2)/sc;
                q.x*=mirror;
                float age=_Context.z;
                float3 sd=Seed();
                // Large, soft warp only: the simulation gives the form, the noise must not turn it into a sponge.
                float3 w=N(q*0.42+sd*0.017+float3(0.01,-0.03,0.008)*age).agr-0.5;
                // (A young nuclear fireball is nearly a perfect sphere: little warp until it starts to roll.)
                q+=w*0.22*(0.5+0.5*_Look0.y)*(1+1.3*exp(-A.w*0.45))*(_SimShape.w>0.05?_SimShape.w:1)*(_SimVariant>3.5?lerp(0.06,1,smoothstep(2,10,A.w)):1);

                bool nukeRun=_SimVariant>3.5;   // the nuclear runs have their own, taller box (ground or air)
                float half_=nukeRun?_SimNukeGeom.x:ground?_SimGeom.x:_SimGeom.z, height=nukeRun?_SimNukeGeom.y:ground?_SimGeom.y:_SimGeom.w;
                float3 inv=float3(0.5/half_,1/height,0.5/half_);
                float3 s=float3(q.x*inv.x+0.5,q.y*inv.y,q.z*inv.z+0.5);
                [branch] if(any(s<0)||any(s>1)) return 0;
                float4 dims=nukeRun?_SimNukeDims:ground?_SimDims:_SimAirDims;
                float4 v=SimSample(ground,dims,s,A.w);
                // Soft walls: gas that reaches the edge of the simulated box fades instead of being cut.
                float3 wall=min(s,1-s); v.x*=smoothstep(0,0.08,min(min(wall.x,wall.z),ground?1-s.y:wall.y));
                // Once the fireball has burnt out, the column that fed it thins out from the ground up: the bake's late,
                // thin stem otherwise read as dark strings hanging under the cloud. Warheads keep theirs (the mushroom's).
                // The cut rises with the cloud (to just under its late cap), so no dark strands hang under it.
                if(_SimVariant>3.5)
                {
                    // The nuclear runs: once the fireball has lifted off, its wake under the cap is cut just below the cap's
                    // underside (the dust stem takes its place); the cap itself is never sliced.
                    float cutN=_NukeRing.w*smoothstep(0.35,0.65,A.w/max(dims.w-1,1));
                    v.x*=smoothstep(cutN-0.1,cutN-0.02,s.y);
                }
                else if(ground&&_SimStem>0)
                {
                    // How high it may go: under the mushroom's cap, under the squat cloud, only the foot of a column of fire (the
                    // column is its body) or of the chaotic ball.
                    // (1.1 realism pass: higher, and ragged: the wake left dark strings hanging to the ground under the cloud
                    // like tentacles; the smoke of what burns on the ground is the residue's own column.)
                    float cutMax=_SimVariant<0.5?0.66:_SimVariant<1.5?0.6:_SimVariant<2.5?0.3:0.46;
                    float lift=max(smoothstep(0.14,0.62,A.w/max(dims.w-1,1)),saturate(_SimStem-1))*min(_SimStem,1), cut=cutMax*lift;   // (starts as soon as the ball lifts off)
                    cut+=0.12*w.x*lift;   // eaten from below in rounded bites, not sliced flat
                    v.x*=lerp(1,smoothstep(cut-0.08,cut+0.22,s.y),saturate(lift*1.6));
                }
                else if(!ground)
                {
                    // In the air too: once the puff has risen off its burst point, the wake trailing under it thins away
                    // (it hung under air bursts as dark strings). The air runs burst at about a third of their box's height.
                    float liftA=smoothstep(0.2,0.7,A.w/max(dims.w-1,1));
                    float cutA=(0.36+0.1*w.x)*liftA;
                    v.x*=lerp(1,smoothstep(cutA-0.1,cutA+0.18,s.y),liftA);
                }
                // The bake's gas thins as the cloud spreads; a real smoke cloud stays opaque far longer (its soot does not
                // vanish, it is spread over a bigger cloud): late frames keep their density.
                v.x*=1+0.5*smoothstep(0.4,0.7,A.w/max(dims.w-1,1));
                [branch] if(v.x>0.01)
                {
                    float3 Ld=_LightDirection.xyz;
                    float3 Ls=float3(A.x*Ld.x+A.y*Ld.z,Ld.y,-A.y*Ld.x+A.x*Ld.z); Ls.x*=mirror;
                    // Sunlight reaching this point through the gas of this copy: a short march toward the sun, the gas met
                    // summed over the length it covers (simulation units). Every other step only (the frag keeps the last).
                    if(sunDue)
                    {
                        // (A nuclear cloud's cells are tens of metres wide: a march of a few cells lit it cell by cell, a
                        // grain; it looks six times farther, which also gives the cap its broad self-shadow.)
                        float3 st=Ls*inv*(nukeRun?6:1);
                        toSun=0.16*SimGas(ground,dims,s+st*0.12,A.w)+0.26*SimGas(ground,dims,s+st*0.34,A.w)+0.4*SimGas(ground,dims,s+st*0.72,A.w);
                    }
                    float4 nf=N(q*0.75+w*0.4+sd*0.029+float3(0.012,-0.05,0.01)*age);
                    fine=nf.r;
                    mid=N(q*0.95+w*0.6+sd*0.041+float3(0.02,-0.09,0.017)*age).g*0.6+nf.a*0.4;
                }
                return v;
            }
            #ifdef VEFX_SIM
            // Cauliflower relief of a nuclear cap: three octaves of rounded lumps (x in about a third of a cap radius).
            float CapNoise(float3 x) { return N(x*0.14).r*0.46+N(x*0.4+3.1).r*0.3+N(x*1.0+7.7).r*0.16+N(x*2.4+1.9).g*0.08; }
            // The ripe mushroom cap (Glasstone, LANL footage): a domed cloud whose underside is hollowed by the inflow that
            // feeds its toroidal roll (the dark ring under the cap), covered in rounded billows a few hundred metres wide.
            // The baked simulation, stretched over kilometres, has cells tens of metres wide and no such relief: this shape
            // takes over once the cap has formed. od: optical depth toward the sun through the whole cap, with each lump
            // shading its own far side.
            float NukeCap(float3 p,float3 Ld,out float od)
            {
                od=0;
                float R=max(_NukeIce.y,1e-4)*1.05, H=max(0.5*(_NukeIce.w-_NukeStem.z),0.5*R)*_NukeCap.z;   // (thickness about half its width)
                // Never a perfect dome: the outline swells and sags in a few great lobes around it.
                float3 dd=p-float3(_NukeStem.x,_NukeStem.z,_NukeStem.y);
                float lobes=N(float3(normalize(dd.xz+1e-5)*0.55,dd.y/max(H,1e-4)*0.12)+Seed()*0.11).r;
                R*=0.86+0.28*lobes; H*=0.9+0.2*lobes;
                // Sitting on the stem's top (the cloud's underside), so no gap opens between them.
                float3 c=float3(_NukeStem.x,_NukeStem.z+H*0.85,_NukeStem.y), k=float3(R,H,R);
                float3 d=p-c;
                float e=length(d/k);
                [branch] if(e>1.25) return 0;
                // The lumps ride the roll: up the middle, out over the top, down the rim (slowly: the cloud is huge).
                float rho=length(d.xz)/R;
                float roll=_Context.z*_NukeCap.y;
                float3 x=d/R*3.2+Seed()*0.07;
                x.xz-=d.xz/R*roll*saturate(0.5+d.y/H);
                x.y-=roll*(1.2-rho)*2;
                float n=CapNoise(x);
                // A domed top and, a little lower, the rolled rim of the toroidal circulation bulging out all round.
                float dome=1-length(d/float3(0.93*R,H,0.93*R));
                float rim=1-length(float2((rho-0.66)/0.36,(d.y+0.18*H)/(0.62*H)));
                dome=max(dome,rim);
                float hollow=(length((d-float3(0,-H,0))/float3(0.62*R,0.8*H,0.62*R))-1)*0.6;
                float s=min(dome,hollow)+(n-0.55)*0.45;
                // A sharp, opaque surface (a cloud kilometres thick): the relief reads in its light and shade.
                float dens=smoothstep(0,0.035,s)*(1+2*smoothstep(0.03,0.25,s));
                [branch] if(dens<=0) return 0;
                float3 q=d/k, l=Ld/k;
                float a=max(dot(l,l),1e-6), b=dot(q,l), cc=dot(q,q)-1, disc=b*b-a*cc;
                float far=disc>0?max((-b+sqrt(disc))/a,0):0;
                od=max(2.2*far/R+3.2*(CapNoise(x+Ld*0.3)-n),0);
                return dens;
            }
            #endif
            float3 FireballSim(float3 p,bool sunDue,out float shade,out float heat,out bool fringe)
            {
                shade=0.5; heat=0; fringe=false;
                bool ground=_Context.x>0.5;
                float gas=0, T=0, S=0, hs=0, fineAcc=0, sunAcc=0, midAcc=0, weight=1e-4;
                float4 Ls[4]={_L0,_L1,_L2,_L3}; float4 As[4]={_A0,_A1,_A2,_A3}; float4 Ds[4]={_D0,_D1,_D2,_D3};
                [unroll] for(int i=0;i<4;i++)
                {
                    [branch] if(Ls[i].w<=1e-4) continue;
                    float fine, toSun, mid;
                    float4 v=SimLobe(p,Ls[i],As[i],Ds[i],ground,sunDue,fine,toSun,mid);
                    float g=v.x;   // the gas stays once the flames are out: it becomes the smoke
                    gas=1-(1-gas)*(1-g);
                    T+=v.y*g; S+=v.z*g; hs+=Ds[i].w*g; fineAcc+=fine*g; sunAcc+=toSun*g; midAcc+=mid*g; weight+=g;
                }
                #ifdef VEFX_SIM
                float capK=_NukeCap.x, capD=0, capOd=0;
                [branch] if(capK>0.001) capD=NukeCap(p,_LightDirection.xyz,capOd);
                if(gas<0.01&&capD*capK<0.005) return 0;
                #else
                if(gas<0.01) return 0;
                #endif
                // A warhead's cooled cloud (earth, nuclear debris) hangs dense instead of thinning into wisps.
                if(_SimLinger>0) gas=1-pow(1-saturate(gas),1+_SimLinger*smoothstep(0.3,0.9,1-hs/weight));
                T/=weight; S/=weight; heat=hs/weight; float fine=fineAcc/weight, toSun=sunAcc/weight, mid=midAcc/weight;
                float turb=_Look0.y, grad=_Look0.z;
                // Smooth body with soft billowed edges (the old look's cohesion), driven by the simulated gas.
                float th=0.05+0.1*turb*(0.85-fine)*(1-saturate(gas*1.4));
                float density=smoothstep(th,th+0.42,gas)*(0.82+0.36*fine);
                // A cooled smoke cloud has a well-defined surface (rounded billows), not a cotton-wool fuzz.
                density=lerp(density,smoothstep(th,th+0.2,gas)*(0.85+0.3*fine),smoothstep(0.3,0.8,1-heat));
                // A nuclear cloud is the bake stretched over kilometres: its cells are tens of metres wide, and a sharp
                // threshold on them read as a grainy, cotton-wool surface. A soft ramp instead; the cloud-sized billows
                // below give it its relief.
                if(_Nuke.x>0.5) density=lerp(density,smoothstep(0.02,0.8,gas)*(0.9+0.2*fine),smoothstep(0.2,0.7,1-heat)*smoothstep(0.1,0.4,_Nuke.y));
                // Fine curls only in the skin, like the analytic fireball: ragged edge, smooth inside.
                float skin=1-saturate(gas*1.6);
                // (A white-hot nuclear fireball has a smooth surface: no curls while it is white.)
                density*=saturate(1-skin*turb*0.55*(0.62-mid)*2.2*(1-_Nuke.w));
                // Cooled smoke is diffuse: thin gas stays as a soft veil instead of being eroded into strings; but thin smoke
                // stays thin (translucent haze): a filament of cooled gas made opaque read as a dark string.
                float veil=pow(saturate(gas*1.5),1.6)*(0.55+0.45*fine)*(0.8+0.4*mid);
                density=lerp(density,max(density,veil),smoothstep(0.35,0.9,1-heat));
                density*=1-0.35*skin*smoothstep(0.4,0.9,heat);
                // Cauliflower billows on the cooled smoke's surface: small rounded lumps that rise with it (the bake, about
                // 56 cells across, is too smooth up close). Only the skin of cooled gas; the flame keeps the bake's folds.
                // (A nuclear cloud grows far past its fireball: its lumps follow the cloud's own size, with a second, finer
                // octave; sized to the fireball they covered the cap in a cotton-wool fuzz.)
                float billR=_Nuke.x>0.5?max(_Fire.x,abs(_A0.z)*0.55):_Fire.x;
                float3 qd=p/max(billR,1e-3)*0.8+Seed()*0.051+float3(0.013,-0.06,0.011)*_Context.z*(_Nuke.x>0.5?0.15:1);
                float lumpy=(1-saturate(gas*1.2))*smoothstep(0.25,0.75,1-heat)*saturate(turb);
                float bill=0.5;
                [branch] if(lumpy>0.02)
                {
                    bill=saturate((N(qd).r-0.38)/0.35);   // the billow channel spans about 0.38-0.73: stretched to 0-1
                    if(_Nuke.x>0.5) bill=saturate(bill*0.68+saturate((N(qd*2.3+3.7).r-0.38)/0.35)*0.32);
                    density*=saturate(1-lumpy*(0.7-bill)*1.8);
                }
                #ifdef VEFX_SIM
                // The ripe nuclear cap takes over from the stretched simulation (see NukeCap).
                if(capK>0.001) { density=lerp(density,capD,capK); gas=lerp(gas,capD,capK); }
                #endif
                density*=_Fire.w;
                // End of its life (or handed over to a newer blast): the thin parts go first, then the whole cloud fades.
                // (It used to vanish at once when its time was up.)
                float dissolve=_Motion.z*0.6*(0.6+0.8*fine);
                if(dissolve>0.001) density*=smoothstep(dissolve,dissolve+0.2,gas);
                density*=_Cloud.w;
                fringe=gas<0.5;
                float cool=1-heat;
                // Hot pockets glowing through cooler lanes, as in the analytic fireball.
                float pocket=fine*2-1;
                // Mid-scale contrast: bright hot pockets and darker, sooty lanes between them (the old look's "soul").
                float lane=smoothstep(0.62,0.25,mid);
                // (1.1 realism pass: stronger mid-scale contrast and a little less overall heat: a fireball is bright
                // yellow pockets in deep orange, not one even peach glow)
                float temp=pow(saturate(T),0.7)*(0.8+0.4*fine+1.6*(mid-0.5)*turb)*(0.25+0.95*heat)*0.97;
                temp+=grad*(pocket*(pocket>0?0.09:0.2))*heat-grad*lane*(0.12+0.18*cool)+_Look0.w*0.5*step(0.03,T);
                // Brighter core deep inside the gas: the eye sees the hottest fire through the outer layers.
                temp+=0.14*saturate(gas*1.6-0.9)*heat*(0.6+0.8*fine);
                // Temperature falls toward the skin: white-yellow heart, deep orange-red outer layer.
                // A hot fireball's thin edge is still flame (less of it, not darker): only a cooling one gets a dull red skin.
                temp*=lerp(lerp(0.74,0.93,smoothstep(0.4,0.9,heat)),1.1,smoothstep(0.25,0.8,gas));
                // While the fireball is hot its burning edge glows orange instead of reading as a brown outline.
                temp=max(temp,0.4*smoothstep(0.45,0.95,heat)*saturate(gas*3)*(0.75+0.5*fine));
                temp=saturate(temp)*smoothstep(0,0.14,heat);
                // A nuclear fireball is white-hot at first, all through (its surface far hotter than any chemical flame).
                temp=lerp(temp,max(temp,0.97*saturate(gas*2.5)),_Nuke.w);
                float gather=smoothstep(0.18,0.85,(1-saturate(temp*1.5))*(0.45+0.6*(1-mid))+S*0.9+cool*0.35+skin*cool*0.4+lane*0.35*grad+skin*lane*0.55*grad*saturate(_Smoke.w*3)*smoothstep(0.25,0.65,cool));
                // A hot fireball's skin glows: no uniform dark rim (it read as a cartoon outline); soot takes the edge
                // only in patches, and fully once the gas cools.
                gather*=1-0.7*skin*smoothstep(0.3,0.75,heat)*saturate(0.4+1.2*fine);
                // Once cooler, soot still gathers at the skin in clumps, never as an even band around the silhouette.
                gather*=1-skin*0.75*smoothstep(0.62,0.3,mid)*(1-smoothstep(0.3,0.75,heat));
                float soot=saturate(_Smoke.w*1.6)*gather;
                // Optical depth toward the sun (see SimLobe), with the billows' own relief; -1: not computed this step.
                shade=sunDue?toSun*4.2*(1+(0.7*(0.5-fine)+0.35*(0.5-mid))*(_Nuke.x>0.5?0.3:1)):-1;
                // Each lump shades its own far side: more billow toward the sun, more shadow.
                [branch] if(sunDue&&lumpy>0.02) shade=max(shade+3*(saturate((N(qd+_LightDirection.xyz*0.04).r-0.38)/0.35)-bill)*(0.4+0.6*lumpy),0);
                #ifdef VEFX_SIM
                if(capK>0.001&&sunDue) shade=lerp(shade,capOd,capK);
                #endif
                return float3(density,temp,soot);
            }
            float3 FireballSim(float3 p,out float shade,out float heat,out bool fringe) { return FireballSim(p,true,shade,heat,fringe); }
            float OcclusionSim(float3 p,float3 Ldir)
            {
                float4 axis=float4(0,1,0,1);
                return Chord(p,Ldir,float4(_L0.xyz,_L0.w*0.55),axis)+Chord(p,Ldir,float4(_L1.xyz,_L1.w*0.55),axis)+
                    Chord(p,Ldir,float4(_L2.xyz,_L2.w*0.55),axis)+Chord(p,Ldir,float4(_L3.xyz,_L3.w*0.55),axis);
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
                    #ifdef VEFX_SIM
                    // Fade out before the bounding sphere: the larger 1.1 proxy made a hard circular edge visible.
                    float edgeR=rs*1.15+3*(0.04+0.14*rs)+0.02;
                    skirt*=smoothstep(edgeR,edgeR*0.7,length(p));
                    #endif
                }
                float2 nuke=0;
                #ifdef VEFX_SIM
                if(_Nuke.x>0.5&&_Nuke.y+_Nuke.z+_NukeIce.z+_NukeWilson.y>0.002)
                {
                    // From the ground under the burst (below the burst point for an air burst).
                    float3 sp=p-float3(_NukeStem.x,_NukeGround.x,_NukeStem.y);
                    float top=max(_NukeStem.z-_NukeGround.x,1e-3), rad=max(_NukeStem.w,1e-3), y=sp.y/top, rho=length(sp.xz);
                    if(_Nuke.y>0.002&&y>-0.05&&y<1.1)
                    {
                        // The dust stem: sucked up from the ground by the afterwinds, climbing to the cloud's underside
                        // (_Nuke.y: how far it has got), wider near the cloud, twisting as it rises; a skirt of dust at its foot.
                        // Turbulent, rising, twisting: two scales of billows in a frame that turns slowly with height.
                        float tw=y*2.2+_Context.z*0.03, ct=cos(tw), st=sin(tw);
                        float3 tq=float3(ct*sp.x-st*sp.z,sp.y,st*sp.x+ct*sp.z)/rad;
                        float4 sn=N(float3(tq.x*0.45,tq.y*0.12-_Context.z*0.06,tq.z*0.45)+Seed()*0.03);
                        float4 sf=N(float3(tq.x*1.3,tq.y*0.6-_Context.z*0.15,tq.z*1.3)-Seed()*0.02);
                        // Narrow in the middle, flaring into the cloud's underside (the inflow) and into the skirt at the foot.
                        float flare=1+0.9*smoothstep(0.8,1.0,y)+0.35*(1-smoothstep(0.0,0.1,y));
                        // Lumpy and ragged: the width wanders, the edge is torn by finer billows.
                        float width=rad*(0.7+0.3*saturate(y))*flare*(0.6+0.8*sn.a)*(0.8+0.4*sf.b);
                        float column=smoothstep(width,width*0.45,rho)*smoothstep(-0.05,0.05,y)*(1-smoothstep(0.96,1.12,y));
                        float risen=smoothstep(_Nuke.y*1.1,_Nuke.y*1.1-0.2,y);
                        float streaks=0.15+1.2*smoothstep(0.32,0.7,sn.r*0.55+sf.g*0.45);   // denser and thinner vertical bands
                        float foot=exp(-max(sp.y,0)/(0.2*rad))*smoothstep(rad*2.4,rad*0.7,rho)*(0.6+0.6*sf.r);
                        // Thick dust low down, thinner toward the cloud.
                        nuke.y=(column*risen*streaks*(1.7-0.9*saturate(y))*_NukeGround.z+foot*_NukeGround.y)*saturate(_Nuke.y*3)*4.6;
                    }
                    if(_Nuke.z>0.002)
                    {
                        // Condensation rings around the stem: thin, ragged white tori in moist air, for a few seconds (one,
                        // or a ladder of them at megaton yields, the lower ones wider).
                        [unroll] for(int k=0;k<3;k++)
                        {
                            if(k>=_NukeGround.w) break;
                            // Just under the cap, the next ones lower down the stem, each a little wider.
                            float ringY=_NukeStem.z-_NukeGround.x-_NukeRing.x*(0.25+0.45*k), ringR=_NukeRing.x*(1.15+0.2*k);
                            if(ringY<_NukeRing.x*0.8) continue;   // not down on the ground
                            // A thin, flat collar (a puffy torus read as a second, smaller cap under the first).
                            float dr=length(float2(rho-ringR,(sp.y-ringY)*2.6));
                            float rn=N(float3(sp.xz/max(ringR,1e-3)*0.6,_Context.z*0.03+k*0.37)+Seed()*0.05).g;
                            nuke.x+=_Nuke.z*smoothstep(_NukeRing.y*1.3,_NukeRing.y*0.2,dr)*smoothstep(0.25,0.65,rn)*(1.7-0.4*k);
                        }
                    }
                    if(_NukeIce.z>0.002)
                    {
                        // The ice cap: a smooth, thin white dome just over the rising cloud.
                        // A flattened dome hugging the cloud's top, thick enough to read as cloud (a thin sphere read as a
                        // glass lens floating above it).
                        float rH=_NukeIce.y*1.12, rV=_NukeIce.y*0.6;
                        float3 c=float3(_NukeStem.x,_NukeIce.w-rV*1.02,_NukeStem.y);   // its top just at the cloud's top
                        float3 e=(p-c)/float3(rH,rV,rH); float dc=length(e);
                        float shell=smoothstep(0.16,0,abs(dc-1))*smoothstep(0.2,0.7,e.y);
                        float soft=0.6+0.8*N(p/rH*1.1+Seed()*0.03).g;
                        nuke.x+=_NukeIce.z*shell*soft*2.6;
                    }
                    if(_NukeWilson.y>0.002)
                    {
                        // The Wilson cloud: a mottled white dome around the fireball, out at the shock front, that clears
                        // from the top down into a ring before it vanishes.
                        float R=max(_NukeWilson.x,1e-3), d=length(p);
                        float band=exp(-pow((d-R*0.92)/max(_NukeWilson.z,1e-4),2));
                        float hgt=(p.y-_NukeGround.x)/R;   // height above the ground, in shock radii
                        float keep=smoothstep(0.15,-0.1,hgt-(1.35-1.2*_NukeWilson.w));   // the top clears first (a full dome at first)
                        float wn=N(p/R*1.6+Seed()*0.07+float3(0,0.02,0)*_Context.z).r;
                        // As thick (optically) whatever its size: density in inverse shock radii.
                        float wn2=N(p/R*4.2-Seed()*0.05).g;   // finer tatters: the fireball glows through the gaps
                        // Seen from inside (a camera within the shock radius) it would fog the whole view: it fades out.
                        // ...and the part sweeping over the camera too (it whited out the screen as the front passed it).
                        float3 eyeO=mul(unity_WorldToObject,float4(_WorldSpaceCameraPos,1)).xyz;
                        float inside=smoothstep(0.9*R,1.3*R,length(eyeO))*smoothstep(0.2*R,0.7*R,length(p-eyeO));
                        nuke.x+=_NukeWilson.y*band*keep*smoothstep(0.38,0.72,wn*0.6+wn2*0.4)*step(_NukeGround.x,p.y)*0.2/R*inside;
                    }
                }
                #endif
                if(vapour+skirt+nuke.x+nuke.y<0.004) return 0;
                mottle=N(p*0.9+Seed()*0.02+float3(0.04,-0.09,0.03)*_Context.z).r;
                return (float2(vapour*smoothstep(0.3,0.7,mottle),skirt*smoothstep(0.28,0.66,mottle))+nuke)*_Cloud.w;
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
            #ifdef VEFX_SIM
            // Where the cloud becomes opaque along this pixel's ray (metres from the camera; -1 until then).
            static float gFront=-1;
            #define VEFX_FRAG fragColour
            #else
            #define VEFX_FRAG frag
            #endif
            float4 VEFX_FRAG(v2f i):SV_Target
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
                float2 range=VefxRayRange(rayEye,sceneEye);
                entry=max(entry,range.x); exit=min(exit,range.y);
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
                #ifdef VEFX_SIM
                const bool simMode=true;
                #else
                const bool simMode=false;
                #endif
                bool vapour=_Cloud.x>0.002;
                bool skirt=_Context.x>0.5&&_Cloud.y>0.002;
                #ifdef VEFX_SIM
                bool nukeMedia=_Nuke.x>0.5&&_Nuke.y+_Nuke.z+_NukeIce.z+_NukeWilson.y>0.002;
                #else
                const bool nukeMedia=false;
                #endif
                float2 span=float2(1e9,-1e9);
                float extent=0;
                if(fireball)
                {
                    float2 k0,k1,k2,k3,k4;
                    if(simMode)
                    {
                        k0=_L0.w>1e-4?Sphere(o,dn,_L0.xyz,_L0.w):float2(1e9,-1e9); k1=_L1.w>1e-4?Sphere(o,dn,_L1.xyz,_L1.w):float2(1e9,-1e9);
                        k2=_L2.w>1e-4?Sphere(o,dn,_L2.xyz,_L2.w):float2(1e9,-1e9); k3=_L3.w>1e-4?Sphere(o,dn,_L3.xyz,_L3.w):float2(1e9,-1e9);
                        k4=float2(1e9,-1e9);
                    }
                    else
                    {
                        k0=LobeSpan(o,dn,_L0,_A0,2); k1=LobeSpan(o,dn,_L1,_A1,2); k2=LobeSpan(o,dn,_L2,_A2,2);
                        k3=LobeSpan(o,dn,_L3,_A3,2); k4=LobeSpan(o,dn,_L4,_A4,1.3);
                    }
                    float2 k=float2(min(min(min(k0.x,k1.x),min(k2.x,k3.x)),k4.x),max(max(max(k0.y,k1.y),max(k2.y,k3.y)),k4.y));
                    span=float2(min(span.x,k.x),max(span.y,k.y)); extent=max(extent,(simMode?0.85:1.5)*_Bounds.w);
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
                #ifdef VEFX_SIM
                if(nukeMedia)
                {
                    // Ground to the top of the ice cap, the stem's skirt, and the Wilson cloud.
                    float gy=_NukeGround.x, topY=max(_NukeStem.z,_NukeIce.w+_NukeIce.y*0.4);
                    float mid=0.5*(gy+topY), rr=0.5*(topY-gy)+max(_NukeStem.w*3.5,_NukeIce.z>0.002?_NukeIce.y*1.4:0);
                    float2 k=Sphere(o,dn,float3(_NukeStem.x,mid,_NukeStem.y),rr); span=float2(min(span.x,k.x),max(span.y,k.y)); extent=max(extent,2*rr);
                    if(_NukeWilson.y>0.002)
                    {
                        float rw=_NukeWilson.x*0.92+2*_NukeWilson.z;
                        k=Sphere(o,dn,float3(0,0,0),rw); span=float2(min(span.x,k.x),max(span.y,k.y)); extent=max(extent,2*rw);
                    }
                }
                #endif
                float tA=max(entry,span.x/dl), tB=min(exit,span.y/dl);
                float stepT=0;
                if(tB>tA&&extent>0)
                {
                    float stepLocal=extent/max(_Steps,4);
                    // 1.1: four times finer, rendered one resolution step lower (FxPool): coarse steps sliced the billows.
                    float steps=simMode?clamp(ceil((tB-tA)*dl/stepLocal)*4,12,256):clamp(ceil((tB-tA)*dl/stepLocal),3,64);
                    stepT=(tB-tA)/steps;
                }
                else { tA=0; tB=-1; }   // nothing to march (tA can be 1e9, where tA-1 rounds back to tA and the loop never ends)
                float t=tA+stepT*Dither(floor(i.vertex.xy));
                const float softDistance=0.08;
                float soft0=saturate((sceneT-t0)*dl/softDistance), soft1=saturate((sceneT-t1)*dl/softDistance);
                if(_Context.x>0.5)
                {
                    soft0*=saturate(((o+d*t0).y+0.1)/softDistance);
                    soft1*=saturate(((o+d*t1).y+0.1)/softDistance);
                }
                // The baked fireball has sharp, opaque billows: finer steps inside it or its dither shows as grain;
                // the thin dust and empty space around it keep the coarse 1.0 steps (they are most of a distant ray).
                float fineT=simMode?stepT/1.8:stepT;
                float4 acc=0;
                float3 emis=0;   // emitted light, exposed at the end like film
                float h=stepT;
                float scale=max(_Fire.x,0.02);
                #ifdef VEFX_SIM
                // A nuclear cloud grows many times past its fireball: its gas thins in proportion (kept at the fireball's
                // opacity, its surface turned hard and the march dither read as a cotton-wool grain).
                // (Not the ripe cap drawn as its own shape: that one keeps a sharp, opaque surface.)
                if(_Nuke.x>0.5) scale*=pow(max(1,abs(_A0.z)/max(_Fire.x,1e-4)*1.2),0.75*(1-_NukeCap.x));
                #endif
                float body=_Fire.w*(3.0+3.5*_Smoke.w)/scale*0.35;
                float last=0;       // length of the step that reached t
                bool outside=true;  // the previous sample was clear of the fireball
                float sunT=1, skyT=1; bool shadowDue=true;
                #ifdef VEFX_SIM
                // Fires burning under the cloud (and lamps) light the smoke from below: what makes a smoke column read at
                // night. The fireball's own heat already lights it from inside (inner), so a hot fireball skips this.
                float boxR=max(length(unity_ObjectToWorld._m00_m10_m20),1e-3), reach0, reach1, reachL;
                float own=1-saturate(_Fire.y*4);
                float4 fire0=LightLocal(_VefxFire0,_VefxFireRange.x,boxR,reach0), fire1=LightLocal(_VefxFire1,_VefxFireRange.y,boxR,reach1);
                float4 lamp=LightLocal(_VefxLamp,_VefxLampColor.w,boxR,reachL);
                bool sunDue=true; float odCache=0.5;   // optical depth toward the sun, refreshed every other step
                float phase=Phase(dot(dn,_LightDirection.xyz));
                float reachB; float4 bolt=LightLocal(_VefxBolt,_VefxBoltRange,boxR,reachB); bool litB=bolt.w>0.01;
                // A camera exposed for daylight barely sees fire light on the smoke; at night it is the main light.
                float daylit=saturate(_SunColor.a)*saturate(_LightDirection.y*4+0.5);
                fire0.w*=own*lerp(1,0.3,daylit); fire1.w*=own*lerp(1,0.3,daylit);
                float3 lampAxis=normalize(mul((float3x3)unity_WorldToObject,_VefxLampSpot.xyz)+1e-6);
                bool lit0=fire0.w>0.01, lit1=fire1.w>0.01, litL=lamp.w>0.01;
                float fireT0=1, fireT1=1, lampT=1;
                #endif
                const int maxSteps=simMode?384:128;
                [loop] for(int s=0;s<maxSteps;s++)
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
                    #ifdef VEFX_SIM
                    // Nothing of a ground or water blast exists under the ground or the sea: seen where the scene writes no
                    // depth there (water, a camera under the terrain) it showed as a glowing box below the surface.
                    if(_Context.x>0.5) faces*=smoothstep(-0.05,0.0,p.y);
                    #endif
                    if(fireball&&_Look1.z>0.5&&_Fire.y>0.03)
                    {
                        // Light the hot gas scatters into the air around it: a soft glow that sells the brightness.
                        float3 hc=p-_Bounds.xyz; float hr=max(_Bounds.w*0.75,1e-3);
                        float hd=dot(hc,hc)/(hr*hr); float halo=exp(-hd*3.2)*smoothstep(4.0,1.5,hd)*_Fire.y*_Fire.y*faces;
                        #ifdef VEFX_SIM
                        halo*=saturate((0.95-length(p))/0.25)*(1-0.5*_Motion.y)*_SimFlame.x;   // round, never cut by the box; less for white (and hydrogen) flames
                        halo*=lerp(1,0.35,daylit);   // by day the air around a fireball hardly glows (it read as a foggy glare)
                        #endif
                        emis+=(1-acc.a)*Chroma(0.2+0.3*_Fire.y)*halo*(0.09+0.2*_Context.y)*h*dl/hr;
                    }
                    if(fireball)
                    {
                        float shade,heat; bool fringe;
                        float3 f;
                        #ifdef VEFX_SIM
                        f=FireballSim(p,sunDue,shade,heat,fringe);
                        #else
                        if(simMode) f=FireballSim(p,shade,heat,fringe); else f=Fireball(p,shade,heat,fringe);
                        #endif
                        f.x*=faces;
                        if(f.x>0.002&&outside&&last>0)
                        {
                            float lo=t-last, hi=t;
                            [unroll] for(int k=0;k<2;k++)
                            {
                                float mid=(lo+hi)*0.5, sm, hm; bool nm;
                                float3 fm;
                                #ifdef VEFX_SIM
                                fm=FireballSim(o+d*mid,true,sm,hm,nm);
                                #else
                                if(simMode) fm=FireballSim(o+d*mid,sm,hm,nm); else fm=Fireball(o+d*mid,sm,hm,nm);
                                #endif
                                if(fm.x>0.002) { hi=mid; f=fm; shade=sm; heat=hm; } else lo=mid;
                            }
                            t=hi; p=o+d*t;
                        }
                        outside=f.x<=0.002;
                        if(fringe&&acc.a<0.3) next=simMode?fineT:stepT*0.7;
                        if(f.x>0.002)
                        {
                            #ifdef VEFX_SIM
                            float extinction=f.x*(2.2*lerp(1,_Look1.x,1-heat)+9*heat+2*f.y+4.5*f.z*_Look1.x*(_Look1.z>0.5?0.5:1))/scale*0.8;
                            // Cooled soot smoke is very opaque: the eye sees only its first metres, so its billows read sharp
                            // instead of being averaged through a thick translucent layer (soft, cotton-wool look).
                            // Thin wisps stay translucent.
                            extinction*=1+1.6*smoothstep(0.3,0.8,1-heat)*smoothstep(0.3,0.75,f.x)*(_Nuke.x>0.5?0.2:1);
                            // A hydrogen flame is nearly clear: the burning gas hardly hides what is behind it.
                            extinction*=lerp(1,0.3+0.7*_SimFlame.x,saturate(f.y*2.5));
                            float alpha=1-exp(-extinction*h*dl);
                            if(shadowDue)
                            {
                                sunT=exp(-body*OcclusionSim(p,_LightDirection.xyz));
                                skyT=exp(-body*0.6*OcclusionSim(p,float3(0,1,0)));
                                if(lit0) fireT0=exp(-body*OcclusionSim(p,normalize(fire0.xyz-p+1e-6)));
                                if(lit1) fireT1=exp(-body*OcclusionSim(p,normalize(fire1.xyz-p+1e-6)));
                                if(litL) lampT=exp(-body*OcclusionSim(p,normalize(lamp.xyz-p+1e-6)));
                            }
                            shadowDue=!shadowDue;
                            if(shade>=0) odCache=shade;
                            sunDue=!sunDue;
                            float od=odCache;
                            // A nuclear cloud is kilometres thick: its shadowed side is deep and its billows read in relief
                            // (shaded as a fireball-sized cloud it was a flat, evenly lit blob).
                            if(_Nuke.x>0.5) od*=1.8;
                            float3 albedo=min(_Smoke.rgb*1.2,0.8)*lerp(1,(_Look1.z>0.5?1.05:0.8)-(_Look1.z>0.5?0.5:0.62)*_Look1.y,f.z);
                            // One explosion, several smokes: patches of a second tone wander through the cooled gas (not the
                            // flame), and the upper, older smoke is a little paler than the sooty base.
                            if(_SmokeMix.w>0)
                            {
                                float patch=smoothstep(0.32,0.66,N(p*0.5+Seed()*0.037+float3(0.01,-0.03,0.02)*_Context.z).b);
                                albedo=lerp(albedo,min(_SmokeMix.rgb*1.2,0.85),patch*_SmokeMix.w*(1-saturate(f.y*2)));
                                albedo*=1+0.25*_SmokeMix.w*saturate(p.y*0.8+0.2);
                            }
                            // Pale smoke (aluminium oxide), dust and steam scatter light many times, so their shadows stay
                            // luminous; soot absorbs most of it, so sooty smoke keeps deep, dark shadows.
                            float pale=saturate((dot(_Smoke.rgb,0.3333)-0.45)*3)*(1-f.z*_Look1.y);
                            float ms=lerp(0.32,0.72,pale)*(_Nuke.x>0.5?0.72:1);
                            // Sunlight: through this cloud's own gas (od), scattered forward (a thin edge against the sun lights
                            // up), plus two orders of multiple scattering that soften and lift the shadows. 1 = a lit side-on point.
                            float direct=exp(-od);
                            float sunVis=(direct*phase+ms*exp(-0.3*od)*lerp(1,phase,0.3)+ms*ms*exp(-0.1*od))/(1+ms+ms*ms);
                            // The other copies' shadow (spheres, coarse) only darkens the light softly: the march does the detail.
                            float sun=_SunColor.a*(0.06+1.18*sunVis)*lerp(0.45,1,sunT);
                            // White (solid propellant) flames: only the hottest core glows, the rest is lit white smoke.
                            float glowShare=smoothstep(0.42+0.12*_Motion.y,0.68+0.14*_Motion.y,f.y);
                            float3 sky=float3(0.72,0.8,0.95)*max(_LightDirection.w,0.22*_SunColor.a)*lerp(0.45,1,skyT)*(0.6+0.4*exp(-0.5*od));
                            // Sunlit ground under a ground blast lights the underside of the cloud (warm, from below).
                            float3 bounce=0;
                            if(_Context.x>0.5&&_Context.x<1.5)
                                bounce=saturate(_Dust.rgb*1.1)*_SunColor.rgb*_SunColor.a*saturate(_LightDirection.y)*0.32*lerp(1,0.6,skyT)*exp(-max(p.y,0)/(0.35*_Bounds.w+0.05));
                            // The flames light the smoke that wraps them: strongest right beside the fire (local temperature),
                            // a little through the whole cloud while it is hot. Smoke far from the flames stays black.
                            float3 inner=Chroma(0.5)*(1-f.y)*(0.08*heat*heat+2.0*heat*smoothstep(0.08,0.36,f.y))*(_Context.x>1.5?0.25:1)*(1-0.65*_Motion.y);
                            // In full sun even black smoke shows its billows: its sunlit side reads dark grey-brown, its shadowed
                            // side near black. Sunlight sees at least this much albedo (same tint), the sky and fires do not.
                            float3 sunAlbedo=max(albedo,albedo/max(dot(albedo,0.3333),1e-3)*0.17);
                            float3 lit=sunAlbedo*_SunColor.rgb*sun+albedo*(sky+bounce+inner);
                            {
                                float3 under=0;
                                if(lit0) under+=LightFall(fire0,reach0,p)*(0.15+0.85*fireT0);
                                if(lit1) under+=LightFall(fire1,reach1,p)*(0.15+0.85*fireT1);
                                float3 lampLight=0;
                                if(litL)
                                {
                                    float cone=_VefxLampSpot.w>-1.5?smoothstep(_VefxLampSpot.w,lerp(_VefxLampSpot.w,1,0.35),dot(normalize(p-lamp.xyz+1e-6),lampAxis)):1;
                                    lampLight=_VefxLampColor.rgb*LightFall(lamp,reachL,p)*cone*(0.1+0.9*lampT);
                                }
                                lit+=albedo*(float3(1,0.52,0.2)*under*7+lampLight*2.5)*(1+0.6*pale);
                                // Lightning lights the cloud from inside for a tenth of a second, blue-white.
                                if(litB) lit+=albedo*float3(0.75,0.82,1)*LightFall(bolt,reachB,p)*lerp(5,1.6,daylit);
                            }
                            float glowT=f.y;
                            // Burning aluminium (solid propellant) glows yellow-white through its white smoke, not red (pink).
                            glowT=lerp(glowT,saturate(glowT*1.25+0.12),saturate((dot(_Smoke.rgb,0.3333)-0.45)*3));
                            // Flames facing the sun read a little brighter: relief, without a hard terminator.
                            float3 e=Glow(glowT)*(1-0.92*f.z)*lerp(0.62,1.22,saturate(exp(-0.45*od)*1.1));
                            // A hydrogen flame is faint (a pale pinkish orange, not a white-hot ball).
                            e*=_SimFlame.x;
                            // By day the dull red of cooling soot is lost against the sunlit smoke; only real flame shows. At
                            // night the embers keep glowing.
                            e*=lerp(1,smoothstep(0.15,0.38,f.y),0.85*daylit);
                            // Light smoke (aluminium oxide) does not glow red as it cools: only the flame itself shines.
                            e*=lerp(1,glowShare,pale);
                            // White (solid propellant) flame: less light so its billows and hotter cores still read, not one white blob.
                            e*=1-0.62*_Motion.y;
                            // A young nuclear fireball's surface is mottled with brighter and darker cells (Trinity at 25 ms),
                            // not one flat white disc; the pattern boils.
                            if(_Nuke.w>0.01)
                            {
                                float cell=N(p*7+Seed()*0.021+float3(0,-0.02,0)*_Context.z).r*0.65+N(p*16-Seed()*0.017).g*0.35;
                                e*=lerp(1,0.45+1.1*smoothstep(0.3,0.75,cell),_Nuke.w*0.85);
                                // White-hot: far hotter than any flame, its light is white with a touch of yellow.
                                e=lerp(e,dot(e,float3(0.3,0.5,0.2))*float3(1.08,1,0.86)*1.4,_Nuke.w*0.75);
                            }
                            // The ionised air of a nuclear cloud glows a deep red-violet for tens of seconds: lost in daylight,
                            // it is what keeps the mushroom visible at night.
                            if(_Nuke.x>1.001) e+=float3(0.55,0.13,0.22)*(_Nuke.x-1)*saturate(f.x*1.5)*(1-0.85*daylit)*0.7*(0.5+N(p*3+Seed()*0.01).r);
                            float wgt=(1-acc.a)*alpha;
                            float litK=lerp(1-saturate(f.y*2.5),1-glowShare,pale);
                            acc.rgb+=wgt*lit*litK; emis+=wgt*e; acc.a+=wgt;
                            next=fineT;
                            #else
                            float extinction=f.x*(2.2*lerp(1,_Look1.x,1-heat)+9*heat+2*f.y+4.5*f.z*_Look1.x*(_Look1.z>0.5?0.5:1))/scale;
                            if(simMode) extinction*=0.8;   // a deeper, softer glow: no single dithered step decides the colour
                            float alpha=1-exp(-extinction*h*dl);
                            if(shadowDue)
                            {
                                float occSun, occSky;
                                if(simMode) { occSun=OcclusionSim(p,_LightDirection.xyz); occSky=OcclusionSim(p,float3(0,1,0)); }
                                else { occSun=Occlusion(p,_LightDirection.xyz); occSky=Occlusion(p,float3(0,1,0)); }
                                sunT=exp(-body*occSun);
                                skyT=exp(-body*0.6*occSky);
                                #ifdef VEFX_SIM
                                if(lit0) fireT0=exp(-body*OcclusionSim(p,normalize(fire0.xyz-p+1e-6)));
                                if(lit1) fireT1=exp(-body*OcclusionSim(p,normalize(fire1.xyz-p+1e-6)));
                                if(litL) lampT=exp(-body*OcclusionSim(p,normalize(lamp.xyz-p+1e-6)));
                                #endif
                            }
                            shadowDue=!shadowDue;
                            float3 albedo=min(_Smoke.rgb*1.2,0.8)*lerp(1,(_Look1.z>0.5?1.05:0.8)-(_Look1.z>0.5?0.5:0.62)*_Look1.y,f.z);
                            #ifdef VEFX_SIM
                            // One explosion, several smokes: patches of a second tone wander through the cooled gas (not the
                            // flame), and the upper, older smoke is a little paler than the sooty base.
                            if(simMode&&_SmokeMix.w>0)
                            {
                                float patch=smoothstep(0.32,0.66,N(p*0.5+Seed()*0.037+float3(0.01,-0.03,0.02)*_Context.z).b);
                                albedo=lerp(albedo,min(_SmokeMix.rgb*1.2,0.85),patch*_SmokeMix.w*(1-saturate(f.y*2)));
                                albedo*=1+0.25*_SmokeMix.w*saturate(p.y*0.8+0.2);
                            }
                            #endif
                            float sun=_SunColor.a*(sunT+0.25*(1-sunT))*(simMode?lerp(0.5,1.35,shade):lerp(0.35,1.5,shade));
                            // Pale smoke (aluminium oxide): scatters light many times, so its shadows stay bright,
                            // and it does not glow as it cools; only the flame itself shines (1.1 fireball only).
                            float pale=0, glowShare=0;
                            if(simMode)
                            {
                                pale=saturate((dot(_Smoke.rgb,0.3333)-0.45)*3)*(1-f.z*_Look1.y);
                                sun=lerp(sun,_SunColor.a*(0.55+0.45*sunT)*lerp(0.7,1.25,shade),pale);
                                // White (solid propellant) flames: only the hottest core glows, the rest is lit white smoke.
                                glowShare=smoothstep(0.42+0.12*_Motion.y,0.68+0.14*_Motion.y,f.y);
                            }
                            float3 sky=float3(0.72,0.8,0.95)*max(_LightDirection.w,0.22*_SunColor.a)*(0.35+0.65*skyT);
                            float3 inner=Chroma(0.55)*heat*heat*1.0*(1-f.y)*(_Context.x>1.5?0.25:1)*(1-0.65*_Motion.y);
                            float3 lit=albedo*(_SunColor.rgb*sun+sky+inner);
                            #ifdef VEFX_SIM
                            {
                                float3 under=0;
                                if(lit0) under+=LightFall(fire0,reach0,p)*(0.15+0.85*fireT0);
                                if(lit1) under+=LightFall(fire1,reach1,p)*(0.15+0.85*fireT1);
                                float3 lampLight=0;
                                if(litL)
                                {
                                    float cone=_VefxLampSpot.w>-1.5?smoothstep(_VefxLampSpot.w,lerp(_VefxLampSpot.w,1,0.35),dot(normalize(p-lamp.xyz+1e-6),lampAxis)):1;
                                    lampLight=_VefxLampColor.rgb*LightFall(lamp,reachL,p)*cone*(0.1+0.9*lampT);
                                }
                                lit+=albedo*(float3(1,0.52,0.2)*under*7+lampLight*2.5)*(1+0.6*pale);
                                // Lightning lights the cloud from inside for a tenth of a second, blue-white.
                                if(litB) lit+=albedo*float3(0.75,0.82,1)*LightFall(bolt,reachB,p)*lerp(5,1.6,daylit);
                            }
                            #endif
                            float glowT=f.y;
                            // Burning aluminium (solid propellant) glows yellow-white through its white smoke, not red (pink).
                            if(simMode) glowT=lerp(glowT,saturate(glowT*1.25+0.12),saturate((dot(_Smoke.rgb,0.3333)-0.45)*3));
                            float3 e=Glow(glowT)*(1-0.92*f.z)*(simMode?lerp(0.5,1.3,shade):lerp(0.7,1.2,shade));
                            // Light smoke (aluminium oxide) does not glow red as it cools: only the flame itself shines.
                            if(simMode) e*=lerp(1,glowShare,pale);
                            // White (solid propellant) flame: less light so its billows and hotter cores still read, not one white blob.
                            if(simMode) e*=1-0.62*_Motion.y;
                            float wgt=(1-acc.a)*alpha;
                            float litK=1-saturate(f.y*2.5);
                            if(simMode) litK=lerp(litK,1-glowShare,pale);
                            acc.rgb+=wgt*lit*litK; emis+=wgt*e; acc.a+=wgt;
                            // 1.1: short steps through the visible skin, until the gas is nearly opaque: the colour a pixel shows
                            // is then an average over its first metres, not one dithered sample (speckle).
                            next=simMode?fineT:(acc.a<0.6&&f.y>0.2?stepT*0.5:stepT);
                            #endif
                        }
                    }
                    if(vapour||skirt||nukeMedia)
                    {
                        float mottle;
                        float2 m=Media(p,mottle)*faces;
                        float extinction=(m.x*4+m.y*6)*(_Dust.w>0?_Dust.w:1);
                        if(extinction>1e-4)
                        {
                            float alpha=1-exp(-extinction*h*dl);
                            float3 vapourAlbedo=float3(0.86,0.88,0.92);
                            float3 dustAlbedo=_Context.x>1.5?float3(0.86,0.9,0.94):saturate(_Dust.rgb*1.2);
                            #ifdef VEFX_SIM
                            if(_Nuke.x>0.5) dustAlbedo=saturate(_Dust.rgb*0.78);   // a nuke's stem: soil and soot, darker than lifted dust
                            #endif
                            float3 albedo=(vapourAlbedo*m.x+dustAlbedo*m.y)/(m.x+m.y)*lerp(0.75,1.1,mottle);
                            float3 n=normalize(p-cc+float3(0,0.25,0));
                            #ifdef VEFX_SIM
                            // A nuclear stem is a column: lit on the side facing the sun, in shade on the other.
                            if(_Nuke.x>0.5&&m.y>m.x) n=normalize(float3(p.x-_NukeStem.x,0.15,p.z-_NukeStem.y)+1e-5);
                            #endif
                            acc=Over(acc,float4(albedo*Lighting(n,dn,p,1)*alpha,alpha));
                        }
                    }
                    #ifdef VEFX_SIM
                    if(gFront<0&&acc.a>0.45) gFront=t;
                    #endif
                    last=h; t+=h; h=next;
                }
                if(pend0) acc=Over(acc,ShellCrossing(o+d*t0,rsl,path,dn,soft0));
                if(pendc) { float4 core=CoreLayer(cb2,cp,coreVisible); emis+=(1-acc.a)*core.rgb; acc.a+=(1-acc.a)*core.a; }
                if(pend1) acc=Over(acc,ShellCrossing(o+d*t1,rsl,path,dn,soft1));
                return float4(acc.rgb+Expose(emis),acc.a);
            }
            #ifdef VEFX_SIM
            // 1.1: the colour, and in a second target the eye depth of the cloud's opaque front weighted by its coverage
            // (premultiplied like the colour): the composite writes it to the depth buffer so that transparent things in
            // front of the cloud (engine plumes, particles) stay visible and those behind it stay hidden.
            struct FragOut { float4 colour:SV_Target0; float4 front:SV_Target1; };
            FragOut frag(v2f i)
            {
                FragOut o; gFront=-1;
                o.colour=fragColour(i);
                float3 worldRay=normalize(i.world-_WorldSpaceCameraPos);
                float eye=gFront>0?gFront*max(1e-4,dot(worldRay,-UNITY_MATRIX_V[2].xyz)):0;
                float a=gFront>0?o.colour.a:0;
                o.front=float4(eye*a,0,0,a);
                return o;
            }
            #endif
            ENDCG
        }
    }
    Fallback Off
}
