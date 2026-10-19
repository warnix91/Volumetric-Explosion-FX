Shader "VolumetricExplosionFX/NukeVolume"
{
    // 1.1: the nuclear cloud, drawn from scratch (see Core/NukeCloud.cs for its sequence and sizes). A white-hot
    // sphere that rolls up into a toroidal cap whose gas really circulates (up the middle, over the top, down and in under the
    // rim), cauliflower billows at five scales riding that circulation, a dust stem sucked up from the ground with a skirt at
    // its foot; lit like a real cloud: optical depth marched toward the sun with several orders of scattering, sky light
    // from above, light bounced off the ground, the glow of its own hot core; a low sun reddens and leaves its lower parts in
    // shadow first; it hazes with distance. Proxy units: the volume's box radius is one unit, y up.
    Properties
    {
        _NkBall ("Cap centre y, radius, half height, mushroom", Vector) = (0,0.1,0.1,0)
        _NkTorus ("Ring radius, tube radius, roll angle, heat", Vector) = (0,0.05,0,1)
        _NkStem ("Ground y, stem radius, stem top, density", Vector) = (0,0,0,0)
        _NkSkirt ("Skirt radius, height, density, flow", Vector) = (0,0,0,0)
        _NkCap ("Smoke albedo, white-hot share", Vector) = (0.5,0.3,0.2,0)
        _NkDust ("Dust albedo, fade", Vector) = (0.5,0.45,0.4,1)
        _NkExtra ("Wilson radius, strength, collar, ice cap", Vector) = (0,0,0,0)
        _NkScale ("Extinction per unit, boiling, age, box per body radius", Vector) = (100,0,0,0.001)
        _NkSun ("Sun strength at the ground, at the top, top y", Vector) = (1,1,1,0)
        _NkHeat ("Skin cooled into smoke, gas density, detail", Vector) = (0,1,1,0)
        _NkPile0 ("Pileus disc: height, radius, strength, half thickness", Vector) = (0,0,0,0)
        _NkPile1 ("Pileus disc", Vector) = (0,0,0,0)
        _NkPile2 ("Pileus disc", Vector) = (0,0,0,0)
        _NkLate ("Erosion, wind shear x, z at the cap, stem gone below y", Vector) = (0,0,0,-10)
        _NkAir ("Blast front radius, -, Wilson cleared", Vector) = (0,0,0,0)
        _NkJets ("Ejecta radius, -, strength, -", Vector) = (0,0,0,0)
        _NkWave ("Blast wave over the ground: radius, wall depth, height, strength", Vector) = (0,0,0,0)
        _NkGroundMap ("Ground under the blast (polar: height, sea)", 2D) = "black" {}
        _NkGroundInfo ("Ground map valid, radius, lowest, highest", Vector) = (0,1,0,0)
        _LightDirection ("Local sun direction, ambient", Vector) = (-0.6,0.8,0.2,0.35)
        _SunColor ("Sun color, strength", Color) = (1,0.96,0.9,1)
        _Context ("Surface kind, atmosphere, age, seed", Vector) = (1,1,0,37)
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
            #include "VolumeDepth.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler3D _NoiseTex;
            sampler2D _BlueNoise;
            float4 _NkBall, _NkTorus, _NkStem, _NkSkirt, _NkCap, _NkDust, _NkExtra, _NkScale, _NkSun, _NkHeat;
            float4 _NkPile0, _NkPile1, _NkPile2, _NkLate, _NkAir, _NkJets, _NkWave, _NkGroundInfo;
            sampler2D _NkGroundMap;
            // The ground's height (and 1 over the sea) under a point, from the map the game fills (flat at the burst's
            // ground without it: the bench).
            float2 GroundAt(float2 xz,float yg)
            {
                if(_NkGroundInfo.x<0.5) return float2(yg,0);
                float u=atan2(xz.y,xz.x)/6.2831853+0.5, v=(saturate(length(xz)/_NkGroundInfo.y)*15+0.5)/16;
                return tex2Dlod(_NkGroundMap,float4(u,v,0,0)).rg;
            }
            float4 _LightDirection, _SunColor, _Context;
            float4 _VefxSky;   // sky light of the scene (FxPool), w = 1 once set
            float4 _VefxBolt; float _VefxBoltRange;   // a lightning stroke flashing in the cloud (world, strength; range m)
            struct v2f { float4 vertex:SV_POSITION; float3 world:TEXCOORD0; float4 screen:TEXCOORD1; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.vertex=VefxProxyClip(UnityObjectToClipPos(vertex));
                o.world=mul(unity_ObjectToWorld,vertex).xyz; o.screen=ComputeScreenPos(o.vertex); return o;
            }
            float4 N(float3 p) { return tex3Dlod(_NoiseTex,float4(p,0)); }
            float3 Seed() { return float3(_Context.w*0.37,_Context.w*0.21,_Context.w*0.13); }
            // A pileus: a smooth hood of condensation (the shell of a flattened dome, its top at D.x), thinning and patchy
            // toward its rim. (Flat discs read as thin lines from the ground, and as rings of wire round the stem.)
            float Pileus(float3 p,float4 D)
            {
                if(D.z<0.002) return 0;
                float H=0.32*D.y;   // (a low, smooth hood)
                float3 q=(p-float3(0,D.x-H,0))/float3(D.y,H,D.y);
                float e=length(q);
                float th=max(D.w/H,0.03);
                if(abs(e-1)>th*1.5||q.y<-0.3) return 0;
                float n=N(q*1.3+Seed()*0.05).r*0.65+N(q*3.1+Seed()*0.07+0.4).r*0.35;
                float shell=saturate(1-abs(e-1)/th+(n-0.5)*0.6);
                float rim=smoothstep(-0.25,0.45,q.y);
                return D.z*shell*rim*(0.5+0.5*n);
            }
            // Rounded cauliflower lumps at five scales (x in cap radii: the largest about half a radius across).
            float Billows(float3 x)
            {
                return N(x*0.45).r*0.36+N(x*1.0+3.1).r*0.3+N(x*2.3+7.7).r*0.19+N(x*5.2+1.9).r*0.11+N(x*11.5+5.3).r*0.04;
            }
            float BillowsCheap(float3 x) { return N(x*0.45).r*0.55+N(x*1.0+3.1).r*0.45; }
            // The earth a ground burst throws out: fingers of dirt flying out between about 10 and 65 degrees, long
            // along their flight, a dozen or two round the fireball, thinning toward their tips.
            float Jets(float3 p,float yg)
            {
                float h=p.y-yg, rho=length(p.xz), rr=length(float2(rho,h)), Re=_NkJets.x;
                if(h<0||rr>Re*1.1) return 0;
                float el=atan2(h,max(rho,1e-6));
                float2 dir2=p.xz/max(rho,1e-6);
                // (thick, lumpy plumes, not thin spikes: a brush of lines in the first try)
                float f=N(float3(dir2*1.3,el*0.9+rr/Re*0.35)+Seed()*0.1).r*0.6+N(float3(p.x,p.y-yg,p.z)/Re*2.4+Seed()*0.13+0.5).r*0.4;
                float finger=saturate((f-0.5)/0.16);
                float band=smoothstep(0.12,0.3,el)*smoothstep(1.2,0.85,el);
                float along=smoothstep(0.1*Re,0.3*Re,rr)*smoothstep(Re*1.05,Re*0.6,rr);
                return _NkJets.z*finger*band*along;
            }
            // The cap: a sphere at first, rolled into a domed toroid with a hollow underside (the inflow that feeds the roll).
            // Positive inside, roughly in cap radii. core: distance from the toroid's core circle (0 there, 1 at its skin).
            float CapShape(float3 q,out float core)
            {
                float R=_NkBall.y, H=_NkBall.z, m=_NkBall.w;
                float sphere=1-length(q)/R;
                float rho=length(q.xz);
                float2 uv=float2(rho-_NkTorus.x,q.y+0.2*H);
                float tube=length(uv/float2(_NkTorus.y,0.78*H));
                float dome=1-length((q-float3(0,0.16*H,0))/float3(0.8*R,0.92*H,0.8*R));
                float hollow=(length((q-float3(0,-1.05*H,0))/float3(0.5*R,0.7*H,0.5*R))-1)*0.7;
                float mush=min(max(dome,1-tube),hollow);
                core=lerp(length(q)/R,tube,m);
                return lerp(sphere,mush,m);
            }
            // Where a point of the gas was a while ago, following the toroidal circulation (backward along it): the gas moves
            // up the middle, out over the top, down the rim and in underneath, so its lumps are looked up turned the other way.
            float3 Rolled(float3 q)
            {
                float rho=length(q.xz)+1e-5; float2 dir=q.xz/rho;
                float a=_NkTorus.x, H=_NkBall.z;
                float2 uv=float2(rho-a,q.y+0.2*H);
                float ang=_NkTorus.z*smoothstep(0.15*a,0.7*a,rho)*_NkBall.w;
                float cs=cos(ang), sn=sin(ang);
                float2 r=float2(cs*uv.x-sn*uv.y,sn*uv.x+cs*uv.y);
                float rr=a+r.x;
                return float3(dir.x*rr,r.y-0.2*H,dir.y*rr);
            }
            // What fills a point: x cap smoke, y dust, z temperature (0..1), w white vapour (Wilson cloud, collar, ice cap).
            // core: the cap's core distance (for the glow it spreads). cheap: fewer octaves (shadow rays).
            float4 Media(float3 p,bool cheap,out float core,out float cavity)
            {
                float4 r=0; core=2; cavity=0;
                float yg=_NkStem.x;
                if(p.y<yg) return 0;
                // The wind shears it: the cap drifts downwind, the stem leaning under it.
                p.xz-=_NkLate.yz*saturate((p.y-yg)/max(_NkBall.x-yg,1e-4));
                float R=_NkBall.y;
                float3 q=p-float3(0,_NkBall.x,0);
                float s=CapShape(q,core);
                [branch] if(s>-0.45)
                {
                    float t=_NkScale.z;
                    float3 x=Rolled(q)/R+Seed()*0.07+float3(0.01,-0.03,0.02)*t*0.12;
                    // It boils: its lumps swell, merge and split all the time (two evolving warps of the pattern), fast
                    // while it is hot, calmer as it cools and spreads. (Not in the cheap shadow lookups.)
                    float churn=lerp(0.4,1,saturate(_NkTorus.w*1.6))*(1-0.35*_NkBall.w);
                    if(!cheap) x+=(N(x*0.33+float3(0.17,t*0.05,0.41)).rgb-0.5)*0.55*churn+(N(x*0.85+float3(t*0.035,0.63,-t*0.025)).rgb-0.5)*0.2*churn;
                    float b=cheap?BillowsCheap(x):Billows(x);
                    cavity=saturate((0.6-b)*3.5);   // the folds between the lumps (less light reaches them)
                    // The lumps raise and hollow the surface (a white-hot fireball is smooth, then it boils).
                    // (the five octaves average out: their sum varies by about a tenth, so it is stretched)
                    // At the end of its life it breaks up from its edges into wisps and holes (the lumps go last).
                    float e=s+(b-0.52)*lerp(0.25,1.5,_NkScale.y)-_NkLate.x*(0.2+0.6*_NkLate.x);
                    float dens=saturate(e/(0.05+0.2*_NkLate.x));
                    dens*=_NkHeat.y;
                    if(dens>0)
                    {
                        r.x=dens;
                        // Its underside is dirty, brown with the dust drawn up into it (a uniform pink ball had no soul).
                        float dirty=smoothstep(0.15,-0.6,q.y/max(_NkBall.z,1e-4))*_NkBall.w*_NkStem.w;
                        r.y=max(r.y,dens*1.2*dirty); r.x*=1-0.6*dirty;
                        if(!cheap)
                        {
                            // Hot all through at first; then in the toroid's core, glowing out to its skin in places.
                            // Where the glow shows (the test films): the cloud is opaque, so the eye sees only its surface; the
                            // incandescent gas is fed in from below, so the underside and lower rim of the rolling cap glow,
                            // and the folds between the lumps, under a crust of cooling smoke on the top.
                            float fold=saturate((0.6-b)*3.5);
                            float under=smoothstep(0.45,-0.7,q.y/max(_NkBall.z,1e-4));
                            float hotM=(0.3+0.7*under)*(0.45+0.55*fold);
                            // The rolling toroid's core glows through its underside: a ring of fire under the cap.
                            hotM+=0.9*smoothstep(0.85,0.3,core)*smoothstep(0.1,-0.5,q.y/max(_NkBall.z,1e-4))*_NkBall.w;
                            float hot=lerp(saturate(1.3-core*0.6),hotM,_NkBall.w);
                            // The young fireball is mottled: bright cells, darker seams between them (a flat glowing ball).
                            float cells=smoothstep(0.42,0.62,b);
                            hot*=lerp(0.6+0.7*cells,1,_NkBall.w);
                            float pockets=N(x*1.7+5.3+float3(0,-t*0.06,t*0.035)).g, cool=_NkHeat.x;   // (they drift and pulse)
                            // The skin cools first, and the high lumps first of all: a crust of dark smoke over the glow.
                            hot*=lerp(1,0.3+0.7*max(fold,under*0.6),cool);
                            hot*=1-cool*0.8*smoothstep(0.5,0.62,b)*(1-0.6*under);
                            // (The glow stays an intense orange while the cloud is hot, dimming to red as it cools: it was scaled
                            // by the heat twice over and faded to nothing within seconds.)
                            r.z=saturate(hot*(0.35+0.9*_NkTorus.w)*(0.7+0.6*pockets))*smoothstep(0.03,0.16,_NkTorus.w);
                            // White-hot fireball: its whole body at first, mottled with brighter and darker cells (Trinity).
                            r.z=max(r.z,_NkCap.w*(0.66+0.45*pockets)*lerp(0.8,1.1,cells)*saturate(dens*3));
                        }
                    }
                }
                // The stem: a column of dust sucked up by the afterwinds, not a tube (the test films: Grable,
                // Licorne). Its width varies along it: flared into the dust skirt at its foot, a narrower neck, a funnel
                // opening into the cap's underside (the inflow). Its surface is rounded billows that climb fast and spiral
                // slowly, with a wispy fringe; toward the top it takes the cap's colour (the cap's gas is drawn into it).
                [branch] if(_NkStem.w>0.001&&p.y<_NkStem.z+_NkStem.y*2)
                {
                    float under=_NkBall.x-0.55*_NkBall.z;
                    float yn=saturate((p.y-yg)/max(under-yg,1e-4));
                    float S=max(_NkStem.y,1e-4);
                    float hh=(p.y-yg)/S;
                    // Wavering along its height: it swells and pinches and leans this way and that.
                    float4 wv=N(float3(hh*0.04+Seed().x*0.1,_NkScale.z*0.003+Seed().y*0.1,0.37));
                    float prof=0.8+1.5*exp(-hh/1.4)+1.3*pow(smoothstep(0.62,1.0,yn),2)+0.3*(wv.r-0.5);
                    float rs=S*prof;
                    float2 c2=p.xz-(wv.ga-0.5)*S*1.1*smoothstep(0.05,0.4,yn)*(1-smoothstep(0.8,1,yn));
                    float rho=length(c2);
                    if(rho<rs*1.75)
                    {
                        // Billows in a frame that climbs with the flow and turns slowly with height and time (the spiral of
                        // the afterwinds).
                        float tw=yn*2.4+_NkScale.z*0.04, ct=cos(tw), st=sin(tw);
                        // The cap's own cauliflower billows, about a stem radius across, climbing with the flow: plain
                        // noise read as a column of sand in game.
                        float3 xs=float3(ct*c2.x-st*c2.y,(p.y-yg-_NkSkirt.w*0.6)*0.8,st*c2.x+ct*c2.y)/S*0.8+Seed()*0.05;
                        float bs=cheap?BillowsCheap(xs):Billows(xs);
                        float surf=1-rho/(rs*(1+(bs-0.5)*2.4))-_NkLate.x*0.6;
                        // A dense core with a cauliflower edge, and a thin fringe of dust around it.
                        float d=saturate(surf/0.06)+0.12*saturate((surf+0.2)/0.2)*(1-saturate(surf/0.06));
                        d*=_NkStem.w*lerp(1,0.65,yn);
                        d*=saturate((_NkStem.z-p.y)/(0.8*S)+(bs-0.5)*3);   // the climbing dust's ragged top
                        d*=saturate((p.y-_NkLate.w)/(1.5*S)+(bs-0.5)*3);   // at the end it goes from the ground up
                        float toCap=smoothstep(0.7,1.02,yn);
                        r.y=max(r.y,d*(1-0.7*toCap));
                        r.x=max(r.x,d*0.7*toCap);
                        if(!cheap) cavity=max(cavity,saturate((0.56-bs)*4)*saturate(d*2));
                    }
                }
                // The dust skirt at its foot.
                [branch] if(_NkSkirt.z>0.001&&p.y<yg+_NkSkirt.y*2.2)
                {
                    // A low cloud of dust rolling out from ground zero, built of the cap's cauliflower billows, its
                    // outline uneven all round, thinner outward (a ring of fine-grained dust read as a dome of sand).
                    float rho=length(p.xz), X=_NkSkirt.x, H=_NkSkirt.y;
                    float2 dir2=p.xz/max(rho,1e-6);
                    float4 o4=N(float3(dir2*0.3,0.53)+Seed()*0.1);   // (smooth all round: a few broad lobes)
                    float Xa=X*(0.78+0.5*o4.r), Ha=H*(0.7+0.7*o4.g);
                    float rim=0.5*Xa, hR=lerp(0.4,1,smoothstep(0.1*Xa,rim,rho))*Ha;
                    float sk=1-length(float2(max(rho-rim,0)/max(Xa-rim,1e-5),(p.y-yg)/max(hR,1e-5)));
                    if(sk>-0.6)
                    {
                        float3 xk=float3(p.x,(p.y-yg)*1.5,p.z)/H*0.9+Seed()*0.03+float3(0,-_NkScale.z*0.004,0);
                        float n=cheap?BillowsCheap(xk):Billows(xk);
                        float dk=saturate((sk+(n-0.5)*2.2-_NkLate.x*0.8)/0.1)*_NkSkirt.z*0.8*(1-0.5*saturate(rho/Xa));
                        r.y=max(r.y,dk);
                        if(!cheap) cavity=max(cavity,saturate((0.56-n)*4)*saturate(dk*2));
                    }
                }
                // White vapour: the Wilson cloud at the blast front, a condensation collar under the cap, an ice cap on top.
                [branch] if(_NkExtra.y>0.002)
                {
                    // A milky veil a few hundred metres thick with broad patches (a fine pattern read as grain on a dome
                    // kilometres wide).
                    // A thick white dome of billows, banded where it condenses in the layers of moister air (the
                    // test films over the sea), its outline lumpy.
                    float Rw=max(_NkExtra.x,1e-4);
                    float n1=N(p/Rw*0.8+Seed()*0.02).r, n2=N(p/Rw*2.1+3.3).r;
                    float dr=(length(p)-Rw*(1+(n1-0.5)*0.14))/(0.26*Rw);
                    float bands=0.5+0.5*smoothstep(0.2,0.8,0.5+0.5*sin((p.y-yg)/Rw*21+n2*4.5));
                    float clear=lerp(1,smoothstep(0.55,0.12,(p.y-yg)/Rw+(n2-0.5)*0.45),_NkAir.z);
                    r.w=max(r.w,_NkExtra.y*exp(-dr*dr)*smoothstep(0.2,0.8,n1*0.6+n2*0.4)*bands*2.4*clear);
                }
                [branch] if(_NkExtra.z>0.002)
                {
                    // A puffy skirt of condensation round the stem under the cap (a thin flat ring did not show).
                    float rho=length(p.xz), S=max(_NkStem.y,0.05*R);
                    float yc=_NkBall.x-1.15*_NkBall.z-0.1*R*smoothstep(1.5*S,0.95*R,rho);   // (its outer part a little lower)
                    float2 dc=p.xz/max(rho,1e-5);
                    float lumpC=N(float3(dc*0.85,0.77)+Seed()*0.05).a;
                    float2 k=float2((rho-(0.5*R+0.6*S))/(0.48*R*(0.75+0.5*lumpC)),(p.y-yc)/(0.13*R));
                    float pf=BillowsCheap(float3(p.x,(p.y-yc)*2,p.z)/R*1.6+Seed()*0.04+float3(0,_NkScale.z*0.004,0))*0.7+N(p/R*3.7+0.3).g*0.3;
                    // It condenses and evaporates through its lumps: the densest form first and go last, holes opening
                    // and widening (it faded out evenly, a sheet of glass vanishing). z: its strength over its life (0..1).
                    float life=saturate(_NkExtra.z);
                    float form=saturate((1-length(k)+(pf-0.5)*2.2-(1-life)*1.3)/0.25);
                    r.w=max(r.w,form*sqrt(life)*smoothstep(1.1*S,1.6*S,rho)*2.0);
                }
                [branch] if(_NkExtra.w>0.002)
                {
                    float3 qi=q-float3(0,0.55*_NkBall.z,0);
                    float ei=length(qi/float3(1.05*R,0.75*_NkBall.z+0.1*R,1.05*R));
                    float shell=saturate(1-abs(ei-1)/0.07)*smoothstep(-0.2,0.4,qi.y/max(_NkBall.z,1e-4));
                    r.w=max(r.w,_NkExtra.w*shell*(0.6+0.8*N(qi/R*1.3).g)*0.6);
                }
                // Not flat plates: each ring spreads out from the stem as it forms, its outline uneven all round,
                // built of puffs with gaps between them, domed on top.
                // Its own life (z: age (s), w: the glowing time (s)): it condenses as a wave running out from the
                // stem (its outer edge eased out over a couple of glowing times), the densest lumps first, then fills; at
                // the end it evaporates from its edge inward and through widening holes, thinning as it goes, and its
                // lumps keep drifting slowly outward all along (alive). The lowest forms first.
                [branch] if(_NkPile2.y>0.002&&_NkStem.w>0.001)
                {
                    float S=max(_NkStem.y,1e-4), under=_NkBall.x-0.55*_NkBall.z, rho=length(p.xz);
                    float2 dir2=p.xz/max(rho,1e-5);
                    float tfr=max(_NkPile2.w,1e-3);
                    [unroll] for(int k=0;k<3;k++)
                    {
                        if(k>=_NkPile2.x) break;
                        float u=(_NkPile2.z-(0.6+0.25*k)*tfr)/tfr;   // this ring's age, in glowing times
                        if(u<=0) continue;
                        float grow=1-exp(-u/1.1);
                        float evap=smoothstep(4.5+0.5*k,10+0.5*k,u);
                        float hk=yg+(under-yg)*((k==0?0.36:k==1?0.58:0.78)+0.05*grow);
                        float th=S*(0.24+0.2*grow)*(1-0.35*evap);
                        if(abs(p.y-hk)>th*2.6) continue;
                        float lump=N(float3(dir2*0.9,0.31+k*0.37)+Seed()*0.05).a;
                        float Ro=S*(1.4+(2.4+0.7*k)*grow+0.6*evap)*(0.72+0.56*lump), Ri=S*1.02;
                        float ex=(rho-0.5*(Ro+Ri))/max(0.5*(Ro-Ri),1e-4);
                        if(abs(ex)>1.5) continue;
                        float x=saturate((rho-Ri)/max(Ro-Ri,1e-4));
                        float3 xr=float3(p.x,(p.y-hk)*2.2,p.z)/S*0.9+Seed()*0.05+k*1.7-float3(dir2.x,0,dir2.y)*u*0.12;
                        float b=BillowsCheap(xr)*0.7+N(xr*2.3+1.1).r*0.3;
                        float shape=1-max(abs(ex)-0.35,0)*1.5-pow(abs(p.y-hk)/(th*(0.55+0.9*b)),2);
                        // Condensing: the front runs out from the stem; the lumps fill in behind it.
                        float front=smoothstep(grow*1.05+0.05,grow*1.05-0.15,x);
                        float appear=smoothstep(0,0.7,u);
                        // Evaporating: from the edge inward, and through holes.
                        float gone=saturate(evap*1.3+(x-0.5)*0.6*evap);
                        float d=saturate((shape+(b-0.5)*1.8-(1-appear)*0.8-gone*1.1)/0.22)*front;
                        r.w=max(r.w,_NkPile2.y*d*1.8*sqrt(appear)*(1-0.5*evap));
                    }
                }
                [branch] if(_NkJets.z>0.002) r.y=max(r.y,Jets(p,yg)*1.3);
                // Pileus discs over the cap, pierced by the cloud climbing through them.
                [branch] if(_NkPile0.z+_NkPile1.z>0.002)
                    r.w=max(r.w,max(Pileus(p,_NkPile0),Pileus(p,_NkPile1))*4);
                return r*_NkDust.w;
            }
            float HGn(float g,float c) { return (1-g*g)/pow(max(1+g*g-2*g*c,1e-4),1.5); }   // 1 = isotropic
            float Phase(float g,float c) { return 0.72*HGn(g,c)+0.28*HGn(-0.25*g/0.6,c); }
            // Incandescence: deep red, orange, yellow, white with temperature (0..1).
            float3 BlackBody(float t)
            {
                float3 c=lerp(float3(0.35,0.03,0),float3(1,0.22,0.02),saturate(t*4));
                c=lerp(c,float3(1,0.5,0.1),saturate(t*4-1));
                c=lerp(c,float3(1,0.78,0.38),saturate(t*4-2));
                return lerp(c,float3(1,0.95,0.88),saturate(t*4-3));
            }
            float3 Expose(float3 e) { return 1-exp(-e); }
            // The blast wave racing over the ground: the wall of dust it raises at its foot, tallest at the front and
            // trailing lower behind it, ragged along its top (what shows a blast wave in the films); drawn exactly over the ring it
            // fills (thinner than the march's strides at a distance), a few samples through it. Premultiplied, and where (m).
            float4 DustWall(float3 o,float3 d,float2 range,float yg,float3 L,float c,float3 sky,out float tw)
            {
                tw=-1;
                float Rg=_NkWave.x, W=_NkWave.y, H=_NkWave.z, Ri=max(Rg-W,0);
                float2 o2=o.xz, d2=d.xz; float A=dot(d2,d2);
                if(A<1e-16) return 0;
                float B=dot(o2,d2), disc=B*B-A*(dot(o2,o2)-Rg*Rg);
                if(disc<=0) return 0;
                float sq=sqrt(disc), t0=(-B-sq)/A, t1=(-B+sq)/A, t2=t1, t3=t1;
                float di=B*B-A*(dot(o2,o2)-Ri*Ri);
                if(di>0&&Ri>0) { float si=sqrt(di); t2=(-B-si)/A; t3=(-B+si)/A; }
                float lo=range.x, hi=range.y, Htop=1.7*H;
                // It follows the ground (a flat ring at the burst's height floated over the sea and cut into hills).
                float gLo=_NkGroundInfo.x>0.5?min(_NkGroundInfo.z,yg):yg, gHi=_NkGroundInfo.x>0.5?max(_NkGroundInfo.w,yg):yg;
                if(abs(d.y)>1e-12) { float ta=(gLo-o.y)/d.y, tb=(gHi+Htop-o.y)/d.y; lo=max(lo,min(ta,tb)); hi=min(hi,max(ta,tb)); }
                else if(o.y<gLo||o.y>gHi+Htop) return 0;
                float k=4.5*sqrt(dot(d,d))/max(W,1e-6)*_NkWave.w;   // per metre: about 4 through the wall at full strength
                float3 lt=(float3(1,0.95,0.88)*_NkSun.x*smoothstep(-0.02,0.1,L.y)*(Phase(0.4,c)*0.5+0.4)+sky*1.5)
                    +(BlackBody(_NkTorus.w)*_NkTorus.w*0.6+_NkCap.w*float3(1,0.9,0.8)*0.8)*0.7;   // (lit by the fireball too: at night)
                float3 col=0; float T=1;
                [unroll] for(int sg=0;sg<2;sg++)
                {
                    float a=max(sg==0?t0:t3,lo), b=min(sg==0?t2:t1,hi);
                    if(b<=a) continue;
                    if(tw<0) tw=a;
                    float dt=(b-a)/8;
                    [unroll] for(int j=0;j<8;j++)
                    {
                        float3 p=o+d*(a+(j+0.5)*dt);
                        float rho=length(p.xz);
                        float2 dir2=p.xz/max(rho,1e-6);
                        // Its front runs on unevenly, further here than there, frayed (a perfect circle with a sharp
                        // rim and a flat grey veil inside read as a disc laid on the land).
                        float reachN=N(float3(dir2*1.4,0.37)+Seed()*0.05).a, fray=N(float3(dir2*6.5,rho/max(W,1e-6)*0.3)+Seed()*0.13).g;
                        float Rf=Rg*(0.93+0.07*reachN)-W*0.08*fray;
                        float rr=(Rf-rho)/max(W,1e-6);
                        if(rr<0) continue;
                        rr=saturate(rr);
                        float2 g=GroundAt(p.xz,yg);
                        float n=N(float3(dir2*3.1,rr*0.8+_NkScale.z*0.012)+Seed()*0.07).r*0.6+N(float3(p.x,p.y-g.x,p.z)/max(H,1e-5)*0.35+Seed()*0.11).r*0.4;
                        // (over the sea: a lower sheet of white spray)
                        float hgt=p.y-g.x, top=H*(0.25+1.5*n*n)*(1-0.75*rr)*(1-0.5*g.y);
                        if(hgt<0) continue;
                        // Patchy all round (more dust over bare ground than elsewhere: a perfect brown donut from the air).
                        float patch=0.3+0.7*smoothstep(0.32,0.68,N(float3(dir2*1.2,0.71)+Seed()*0.09).g);
                        // (a rolling front, soft at its leading edge, and behind it only a patchy haze, not an even veil)
                        float haze=0.14*(1-rr)*(1-rr)*smoothstep(0.38,0.66,N(float3(p.xz/max(W,1e-5)*0.9,0.53)+Seed()*0.07).r);
                        float dens=smoothstep(0,0.08,rr)*(exp(-rr*5)+haze)*saturate((top-hgt)/(0.25*H))*(0.5+0.9*n)*patch;
                        float al=1-exp(-k*dens*dt);
                        col+=T*al*lerp(_NkDust.rgb,float3(0.86,0.88,0.9),g.y)*lt*(0.55+0.45*saturate(hgt/max(top,1e-5)));
                        T*=1-al;
                    }
                }
                return float4(col,1-T);
            }
            static const float NkLens[4]={0.05,0.14,0.32,0.7};   // sun-ray samples, in cap radii
            struct FragOut { float4 colour:SV_Target0; float4 front:SV_Target1; };
            FragOut frag(v2f i)
            {
                FragOut fo; fo.colour=0; fo.front=0;
                float3 worldRay=normalize(i.world-_WorldSpaceCameraPos);
                float3 o=mul(unity_WorldToObject,float4(_WorldSpaceCameraPos,1)).xyz;
                float3 d=mul((float3x3)unity_WorldToObject,worldRay);   // proxy units per metre along the ray
                float dl=max(length(d),1e-9); float3 dn=d/dl;
                float sceneEye=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                float rayEye=max(1e-4,dot(worldRay,-UNITY_MATRIX_V[2].xyz));
                float2 range=VefxRayRange(rayEye,sceneEye);
                // The box around the cloud (the proxy cube is much bigger than the cloud for most of its life).
                float R=_NkBall.y, yg=_NkStem.x;
                // (Room for the biggest lumps: they swell the cap by up to about half its radius.)
                float halfX=max(max(R*1.65,_NkSkirt.x*1.5),_NkExtra.y>0.002?_NkExtra.x*1.75:0);
                float yTop=_NkBall.x+max(_NkBall.z*1.75,R*0.5)+(_NkExtra.w>0.002?0.3*R:0);
                if(_NkExtra.y>0.002) yTop=max(yTop,_NkExtra.x*1.75);
                if(_NkPile0.z>0.002) { yTop=max(yTop,_NkPile0.x+2*_NkPile0.w); halfX=max(halfX,_NkPile0.y*1.15); }
                if(_NkPile1.z>0.002) { yTop=max(yTop,_NkPile1.x+2*_NkPile1.w); halfX=max(halfX,_NkPile1.y*1.15); }
                halfX+=max(abs(_NkLate.y),abs(_NkLate.z));
                if(_NkJets.z>0.002) { halfX=max(halfX,_NkJets.x*1.1); yTop=max(yTop,yg+_NkJets.x); }
                float3 sd=sign(d+1e-12)*max(abs(d),1e-12);
                float3 ta=(float3(-halfX,yg,-halfX)-o)/sd, tb=(float3(halfX,yTop,halfX)-o)/sd;
                float3 lo=min(ta,tb), hi=max(ta,tb);
                float entry=max(max(lo.x,max(lo.y,lo.z)),range.x), exit=min(min(hi.x,min(hi.y,hi.z)),range.y);
                float3 L=normalize(_LightDirection.xyz);
                float c=dot(dn,L);
                float3 sky=_VefxSky.w>0.5?_VefxSky.rgb:float3(0.3,0.36,0.46)*0.9;
                float4 wall=0; float tw=-1;
                [branch] if(_NkWave.w>0.002) wall=DustWall(o,d,range,yg,L,c,sky,tw);
                if(exit<=entry)
                {
                    fo.colour=wall; fo.front=float4(tw>0?tw*rayEye*wall.a:0,0,0,tw>0?wall.a:0);
                    return fo;
                }
                float sigma=_NkScale.x;
                float night=1-saturate(max(_NkSun.x,_NkSun.y)*1.5);   // 1 at night
                // The lightning stroke in proxy units; it lights half a cap radius round it at least.
                float4 bolt=float4(mul(unity_WorldToObject,float4(_VefxBolt.xyz,1)).xyz,_VefxBolt.w);
                float boltReach=max(_VefxBoltRange/max(length(unity_ObjectToWorld._m00_m10_m20),1e-3),0.7*R);
                // Steps of about a fiftieth of the cap's radius in the gas, longer through empty space.
                float hs=max(R*0.02,(exit-entry)*dl/240)/clamp(_NkHeat.z,0.4,1);   // (z: the frame governor's detail)
                float t=entry+hs/dl*tex2Dlod(_BlueNoise,float4((floor(i.vertex.xy)+0.5)/64.0,0,0)).r;
                float T=1, front=-1;
                float3 col=0, emis=0;
                [loop] for(int s=0;s<300;s++)
                {
                    if(t>exit||T<0.01) break;
                    float3 p=o+d*t;
                    float core, cavity;
                    float4 m=Media(p,false,core,cavity);
                    // White vapour (Wilson cloud, collar, rings, ice cap): real white clouds, thinner than the smoke
                    float ext=sigma*(m.x+m.y*0.5+m.w*0.07);
                    // Glowing gas is thinner than the cooled smoke around it: the glow shows through.
                    ext*=lerp(1,0.4,saturate(m.z*2));
                    float adv=hs;
                    if(ext>1e-5)
                    {
                        // A low sun reddens and lights the cloud from the top down (higher up it sees the sun longer).
                        float above=max(p.y-yg,0);
                        float elev=L.y+sqrt(2*above*_NkScale.w);
                        float sunVis=smoothstep(-0.02,0.025,elev)*lerp(_NkSun.x,_NkSun.y,saturate(above/max(_NkSun.z-yg,1e-4)));
                        float3 sunCol=lerp(float3(1,0.4,0.14),float3(1,0.95,0.88),smoothstep(0,0.28,elev))*lerp(0.55,1,smoothstep(0,0.2,elev));
                        float3 light;
                        [branch] if(m.x+m.y>0.002)
                        {
                            // Optical depth toward the sun through the whole cloud, and toward the sky above it.
                            float od=0, prev=0, cc, cv;
                            [unroll] for(int k=0;k<4;k++)
                            {
                                float4 mk=Media(p+L*(NkLens[k]*R),true,cc,cv);
                                od+=sigma*(mk.x+mk.y*0.5+mk.w*0.07)*(NkLens[k]-prev)*R; prev=NkLens[k];
                            }
                            // The stem is a column far thinner than the sun samples (a fraction of the cap's radius): its own
                            // side away from the sun is shaded through the column's chord (it was flat, lit both sides).
                            [branch] if(m.y>0.002&&_NkStem.w>0.001)
                            {
                                float2 qx=p.xz, lx=L.xz; float ll=max(dot(lx,lx),1e-4);
                                float rs=_NkStem.y*1.15, b2=dot(qx,lx), c2=dot(qx,qx)-rs*rs, disc=b2*b2-ll*c2;
                                float chord=disc>0?max((-b2+sqrt(disc))/ll,0)*sqrt(ll)/max(sqrt(ll),0.08):0;
                                od+=sigma*0.5*_NkStem.w*0.6*chord;
                            }
                            float4 mu=Media(p+float3(0,0.35*R,0),true,cc,cv);
                            float odUp=sigma*(mu.x+mu.y*0.5)*0.5*R;
                            // Several orders of scattering: each sees less of the cloud's depth and a broader phase.
                            float sunL=0, aa=1, bb=1, gg=0.6;
                            [unroll] for(int o2=0;o2<4;o2++) { sunL+=bb*exp(-od*aa)*Phase(gg,c); aa*=0.2; bb*=0.7; gg*=0.5; }
                            // "Powder" (Horizon Zero Dawn's clouds): the thin outer gas has had little chance to scatter light
                            // toward the eye, so seen with the sun behind, the cloud's edges and folds read darker: its relief
                            // survives front lighting (it was a flat pink mass).
                            float powder=1-exp(-(m.x+m.y)*3);
                            sunL*=lerp(1,powder,saturate(0.5-0.5*c)*0.75);
                            light=sunCol*sunVis*(sunL*0.78+0.05)+sky*(0.28+0.72*exp(-odUp))*1.05
                                +_NkDust.rgb*sunCol*sunVis*saturate(L.y)*0.3*(0.5+0.5*exp(-above/(1.5*R+1e-4)));
                            // The hot core lights the smoke around it from inside (an orange underside, glow in the folds),
                            // and the fireball lights the dust rising under it (what shows the stem at night).
                            // (Only near the glowing core and in the folds: spread through the whole cap it lit it a flat orange.)
                            float heat2=_NkTorus.w*_NkTorus.w;
                            // ... and its glow reaches down the stem and over the dust (inverse square from the cap's
                            // underside), stronger to the eye at night.
                            float3 under=p-float3(0,_NkBall.x-0.4*_NkBall.z,0);
                            // (from below: its underside and the stem glow, the crust on top stays dark; spread over the
                            // whole cap it made a flat pink ball at night)
                            float below=smoothstep(0.7,-0.5,(p.y-_NkBall.x)/max(_NkBall.z,1e-4));
                            light+=BlackBody(_NkTorus.w)*(heat2*smoothstep(1.3,0.35,core)*4*(0.35+0.65*cavity)
                                +_NkTorus.w*(1.2+2*night)*R*R/(R*R+dot(under,under))*(0.25+0.75*below));
                            light*=1-0.68*cavity;   // (deeper folds: contrast)
                        }
                        // A thin veil of vapour alone (Wilson cloud, collar, ice cap): lit straight, no shadow march (the
                        // Wilson dome is kilometres wide and nearly transparent: marching its shadow cost 27 ms).
                        else
                        {
                            // Its own shade, from one look toward the sun (the Wilson dome is kilometres wide: a
                            // full shadow march cost 27 ms): sunlit tops, greyer undersides, as a cloud.
                            float cc2,cv2;
                            float4 ms=Media(p+L*(0.14*R),true,cc2,cv2);
                            float sh=exp(-sigma*(ms.x+ms.y*0.5+ms.w*0.07)*0.14*R-sigma*m.w*0.07*0.06*R);
                            light=sunCol*sunVis*(Phase(0.6,c)*0.55+0.3)*(0.45+0.55*sh)+sky*(0.45+0.25*sh)
                                // (the fireball lights them orange while it burns, and its afterglow at night)
                                +BlackBody(_NkTorus.w)*_NkTorus.w*_NkTorus.w*4*exp(-length(p-float3(0,_NkBall.x,0))/(1.2*R))
                                +float3(0.62,0.2,0.14)*_NkHeat.w*night*1.2*R*R/(R*R+dot(p-float3(0,_NkBall.x,0),p-float3(0,_NkBall.x,0)));
                        }
                        // Lightning in the cloud: each stroke lights the gas round it, bluish white (the strokes alone
                        // were thin lines over a dark cloud).
                        [branch] if(bolt.w>0.01)
                        {
                            float3 db=bolt.xyz-p; float r2=dot(db,db);
                            light+=float3(0.72,0.8,1)*bolt.w*(11-7.5*(1-night))/(1+r2/(boltReach*boltReach*0.3))*saturate(2.4-sqrt(r2)/boltReach);
                        }
                        float3 albedo=(m.x*_NkCap.rgb+m.y*_NkDust.rgb+m.w*float3(0.92,0.93,0.95))/max(m.x+m.y+m.w,1e-4);
                        float alpha=1-exp(-ext*hs);
                        col+=T*alpha*albedo*light;
                        // (blinding while white-hot, then exposed so its cells and darker seams show: it stayed a flat,
                        // saturated ball for seconds)
                        if(m.z>0.02) emis+=T*alpha*BlackBody(m.z)*pow(m.z,2.2)*(9+11*_NkCap.w)*(1+1.2*night);
                        // The ionised air's red-violet afterglow (night only: daylight drowns it).
                        // (brighter in the hotter pockets, dimmer in the folds: it kept no relief as an even glow)
                        if(_NkHeat.w>0.01) emis+=T*alpha*float3(0.6,0.12,0.13)*_NkHeat.w*m.x*(1.3+0.6*night)*(1-0.9*saturate(sunVis))*(0.15+1.9*saturate(m.z*5))*(0.5+0.9*cavity);
                        // By night the ember at the mushroom's heart lights its own stem and dust from above, dull red.
                        if(night>0.05&&m.y>0.002)
                        {
                            float3 dc=p-float3(0,_NkBall.x-0.4*_NkBall.z,0);
                            emis+=T*alpha*float3(0.7,0.22,0.12)*night*(_NkHeat.w*0.6+_NkTorus.w*0.8)*m.y*R*R/(R*R+dot(dc,dc))*0.9;
                        }
                        T*=1-alpha;
                        if(front<0&&T<0.55) front=t;
                        if(m.x+m.y<0.002) adv=hs*2;   // a smooth veil of vapour alone: longer strides
                    }
                    else adv=hs*2.5;   // empty space: longer strides (the dither hides them)
                    t+=adv/dl;
                }
                float a=1-T;
                float3 rgb=col+Expose(emis);
                // Aerial perspective: kilometres of air between the eye and the cloud blue and soften it.
                if(front>0)
                {
                    float fog=(1-exp(-front/40000.0))*saturate(_Context.y)*0.8;
                    float3 haze=lerp(sky*1.25,max(sky*2.2,float3(0.62,0.7,0.8)),smoothstep(-0.05,0.2,L.y));
                    rgb=lerp(rgb,haze*a,fog);
                }
                // The dust wall, in front of the cloud or behind it. (the blast front itself is bent air only,
                // HeatDistortion: its white veil read as a glass dome)
                float4 res=float4(rgb,a);
                float tc=front>0?front:exit;
                if(wall.a>0) res=tw<tc?wall+(1-wall.a)*res:res+(1-res.a)*wall;
                fo.colour=res;
                float eye=front>0?front*rayEye:0;
                fo.front=float4(eye*res.a,0,0,eye>0?res.a:0);
                return fo;
            }
            ENDCG
        }
    }
    Fallback Off
}
