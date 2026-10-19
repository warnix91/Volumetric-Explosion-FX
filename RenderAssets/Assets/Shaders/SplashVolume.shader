Shader "VolumetricExplosionFX/SplashVolume"
{
    // 1.1: a water impact, ray-marched (see Core/WaterSplash.cs for its sequence and sizes): a quick burst of
    // spray flung out in ragged sheaves, a short jet on a fast impact (the plumes of a deep blast, over its dome), a light
    // veil of spray, a base surge after a deep blast, whitewater on the sea. Lit like our clouds: the sun's light marched
    // through it with several orders of scattering, strongly forward (the drops shine with the sun behind them), the sky
    // above, the sea under it, the fire of the blast. Metres, the blast frame: y up, the sea at y = 0.
    Properties
    {
        _SpCrown ("Burst radius, opening time, life, clock", Vector) = (0,0.3,1,0)
        _SpSheet ("Burst thickness, extinction, thinned, steep (a warhead's plume)", Vector) = (1,0,0,0)
        _SpJet ("Jet radius, top, extinction, torn", Vector) = (0,0,0,0)
        _SpSpray ("Spray ring radius, half width, height, extinction", Vector) = (0,1,0,0)
        _SpHaze ("Spray half height, jet spray extinction, height, radius", Vector) = (1,0,0,1)
        _SpSurge ("Surge radius, half width, height, extinction", Vector) = (0,1,1,0)
        _SpFoam ("Foam radius, ring width, strength, plumes", Vector) = (0,1,0,0)
        _SpDome ("Dome radius, height, extinction", Vector) = (0,0,0,0)
        _SpDrift ("Gravity, lean toward the travel x, z, shadow length and march scale", Vector) = (9.81,0,0,10)
        _SpWind ("Drift x, z (m), seed, age", Vector) = (0,0,0,0)
        _SpBox ("Reach, top, proxy scale, proxy centre height", Vector) = (10,20,20,10)
        _SpGlow ("Fire light colour x strength, range", Vector) = (0,0,0,1)
        _SpGlowAt ("Fire light position (m), detail", Vector) = (0,0,0,1)
        _LightDirection ("Local sun direction, ambient", Vector) = (-0.6,0.8,0.2,0.35)
        _SunColor ("Sun color, strength", Color) = (1,0.96,0.9,1)
        _Context ("-, atmosphere, -, -", Vector) = (2,1,0,0)
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _BlueNoise ("Original blue-noise dither", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            float4 _SpCrown, _SpSheet, _SpJet, _SpSpray, _SpHaze, _SpSurge, _SpFoam, _SpDome, _SpDrift, _SpWind, _SpBox, _SpGlow, _SpGlowAt;
            float4 _LightDirection, _SunColor, _Context;
            float4 _VefxSky;   // sky light of the scene (FxPool), w = 1 once set
            struct v2f { float4 vertex:SV_POSITION; float3 world:TEXCOORD0; float4 screen:TEXCOORD1; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.vertex=VefxProxyClip(UnityObjectToClipPos(vertex));
                o.world=mul(unity_ObjectToWorld,vertex).xyz; o.screen=ComputeScreenPos(o.vertex); return o;
            }
            float4 N(float3 p) { return tex3Dlod(_NoiseTex,float4(p,0)); }
            float3 Seed() { return float3(_SpWind.z*0.37,_SpWind.z*0.21,_SpWind.z*0.13); }
            float HGn(float g,float c) { return (1-g*g)/pow(max(1+g*g-2*g*c,1e-4),1.5); }   // 1 = isotropic
            // Drops scatter mostly forward (bright rims against the sun), a little back.
            float Phase(float g,float c) { return 0.7*HGn(g,c)+0.3*HGn(-0.3*g,c); }
            // The burst: one quick, clean splash. The water flung up from the impact flies out as a dome of spray in
            // streaks, opens in a fraction of a second, sags as it falls and thins away within about two seconds. Thickest
            // at middle elevations (the crown), leaning toward where the object went. body: solid water near the sea.
            float Burst(float3 p,bool cheap,out float body)
            {
                body=0;
                float t=_SpCrown.w;
                if(t<0.002||_SpSheet.y<=0||p.y<0) return 0;
                float Rb=_SpCrown.x, tau=_SpCrown.y, g=_SpDrift.x, prog=_SpSheet.z, steep=_SpSheet.w;
                float R=Rb*(1-exp(-t/max(tau,1e-3)));
                float3 q=p;
                q.xz-=_SpDrift.yz*p.y*1.2;
                // (it sags as it falls; a little taller than wide; a warhead's plume about twice as tall as wide)
                q.y=(q.y+0.3*g*t*t)/(1.15+0.9*steep);
                float r=length(q);
                float w=_SpSheet.x*(0.5+0.8*R/max(Rb,1e-3))*(1+0.8*prog);
                if(r>R*1.35+w*3) return 0;
                float3 dir=q/max(r,1e-4);
                float3 sd=Seed();
                // Streaks running out along the rays (the noise depends on the direction only), broken into clumps.
                // (a few broad sheaves with gaps between them, finer streaks within: an even fuzz read as a dandelion)
                float ray=N(dir*1.25+sd*0.1).a*0.55+N(dir*3.4+sd*0.13+0.4).r*0.3+N(dir*7.5+sd*0.17).g*0.15;
                float clump=cheap?0.5:N(dir*7.1+float3(0,0,r/max(w,0.3)*0.35)+sd*0.2).r;
                // (the sheaves fly out further than the gaps between them: a ragged, spiky outline, not a box)
                float Rd=R*(0.65+0.7*ray);
                float shell=exp(-pow((r-Rd)/w,2)*1.6)+0.45*smoothstep(Rd,0.55*Rd,r)*smoothstep(0.3,0.6,ray)*(1-prog);
                // An open crown, not a ball: the water leaves at 20-60 degrees, none straight up; only a low heave of white
                // water inside, at its foot. A warhead's plume: thrown up at 25-90 degrees, broadening as it climbs.
                float fill=0.35*smoothstep(R,0.3*R,r)*smoothstep(0.35*R,0.05*R,q.y)*(1-prog);
                float elev=smoothstep(0.08+0.3*steep,0.35+0.35*steep,dir.y)*lerp(1-smoothstep(0.5+0.25*ray,0.92,dir.y),1,steep);
                float d=(shell*elev+fill)*smoothstep(0.42+0.26*prog,0.6+0.22*prog,ray*0.78+clump*0.22);
                d*=1-0.85*prog;
                body=saturate(1-q.y/(0.3*max(Rb,1e-3)))*(1-prog);
                d*=1+0.6*body;
                return d*_SpSheet.y;
            }
            // The jet up the middle (or the plumes of a deep blast, _SpFoam.w = 1): a column whose top carries a head of
            // spray, swaying, breaking into rain as it falls back.
            float Jet(float3 p,bool cheap)
            {
                float r=_SpJet.x, top=_SpJet.y, torn=_SpJet.w, plumes=_SpFoam.w;
                if(_SpJet.z<=0||p.y<0||p.y>top+r*3) return 0;
                float u=saturate(p.y/max(top,1e-3));
                float3 sd=Seed();
                // (never straight up: it leans a little, each its own way, and sways; a perfect white line read
                // as fake from the air)
                float2 tilt=(frac(_SpWind.z*float2(0.1373,0.2917))-0.5)*0.32*(1-plumes);
                float2 c=float2(_SpWind.x,_SpWind.y)*torn*u*u*0.6+tilt*p.y
                    +(float2(N(float3(0.13,u*0.9,0.71)+sd*0.05).a,N(float3(0.57,u*0.9,0.29)+sd*0.05).a)-0.5)*r*(1.2+1.8*torn)*u*(1-plumes*0.7);
                float2 xz=p.xz-c;
                float rad=lerp(r*(1.05-0.35*u)+r*(0.25+0.6*torn)*smoothstep(0.75,1,u),r*(1+0.6*u),plumes);
                float q=length(xz)/rad;
                if(q>2.3) return 0;
                float n=N(float3(xz/max(r,1e-3)*0.45,u*2.2)+sd*0.13).r;
                // A dense core breaking into clusters of spray and a faint halo (a smooth white rod otherwise).
                float n2=cheap?0.5:N(float3(xz/max(r,1e-3)*0.9,u*3.2)+sd*0.17+0.4).g;
                float dens=saturate((1-q+(n2-0.5)*0.8)*2.6)*(0.7+0.6*n);
                // Fluted: a few strands of water round the column, braided as they climb.
                float2 jdir=xz/max(length(xz),1e-4);
                float flute=N(float3(jdir*0.8,u*1.3)+sd*0.15+0.5).a;
                dens*=lerp(1,0.25+1.5*smoothstep(0.35,0.7,flute),saturate(q*1.2)*(1-plumes));
                // its head: a ragged top
                dens*=smoothstep(top+r*1.6,top-r*0.4,p.y+(n-0.5)*r*4);
                [branch] if(plumes>0.5)
                {
                    // Plumes: separate columns of water, each its own height, fanning out as they climb.
                    float2 fan=xz/(rad*(1+0.4*u));
                    float col=N(float3(fan*0.9,0.5)+sd*0.17).r;
                    float tall=top*(0.5+0.65*N(float3(fan*0.45,0.9)+sd*0.19).g);
                    dens*=smoothstep(0.38,0.55,col)*1.6*smoothstep(tall,tall*0.82,p.y);
                }
                if(!cheap||torn>0.05)
                {
                    float drops=N(float3(xz/max(r,1e-3)*1.1,u*5.5)+sd*0.21+0.3).g;
                    dens*=smoothstep(torn*0.7-0.06,torn*0.7+0.2,drops);
                }
                return dens*_SpJet.z;
            }
            float Billows(float3 x) { return N(x*0.5).r*0.55+N(x*1.13+3.1).r*0.3+N(x*2.6+7.7).r*0.15; }
            // Fine spray hanging where the crown and the jet tore, sinking and drifting; the surge rolling out over the sea.
            float Mist(float3 p,bool cheap,out float surge)
            {
                surge=0;
                float r=0;
                float3 q=p-float3(_SpWind.x,0,_SpWind.y);
                float3 sd=Seed();
                [branch] if(_SpSpray.w>0)
                {
                    float rho=length(q.xz);
                    float2 qdir=q.xz/max(rho,1e-4);
                    // (a billowing cloud of spray filling the middle, its lumps rolling up and out with time, not a
                    // ring of separate clumps)
                    float lump=N(float3(qdir*0.9,0.21)+sd*0.05).a;
                    float2 e=float2(max(rho-_SpSpray.x*(0.35+0.4*lump),0)/(_SpSpray.y*1.05),(q.y-_SpSpray.z)/_SpHaze.x);
                    float l=dot(e,e);
                    if(l<4.5)
                    {
                        float3 qm=q-float3(qdir.x,0,qdir.y)*_SpWind.w*_SpSpray.y*0.25-float3(0,_SpWind.w*_SpSpray.y*0.18,0);
                        float n=cheap?N(qm/(_SpSpray.y*2.2)+sd*0.03).r:Billows(qm/(_SpSpray.y*0.9)+sd*0.03);
                        r+=_SpSpray.w*exp(-1.2*l)*(cheap?0.8:saturate((n-0.36)*2.8));   // (no hard edge where it ends)
                    }
                }
                [branch] if(_SpHaze.y>0)
                {
                    float2 e=float2(length(q.xz)/_SpHaze.w,(q.y-_SpHaze.z)/max(_SpHaze.z,1e-3));
                    float l=dot(e,e);
                    if(l<4.5)
                    {
                        float n=cheap?N(q/(_SpHaze.w*2.2)+sd*0.05+0.4).r:Billows(q/(_SpHaze.w*0.9)+sd*0.05+0.4);
                        r+=_SpHaze.y*exp(-1.2*l)*(cheap?0.8:saturate((n-0.36)*2.8));
                    }
                }
                [branch] if(_SpSurge.w>0&&p.y<_SpSurge.z*2.2)
                {
                    float3 s=p-float3(_SpWind.x,0,_SpWind.y)*0.6;
                    float rho=length(s.xz);
                    float2 sdir=s.xz/max(rho,1e-4);
                    // (banks of mist rolling out unevenly, with gaps between them and further downwind, never a
                    // ring: a perfect circle read as fake from the air)
                    float2 wd=float2(_SpWind.x,_SpWind.y); float wl=length(wd);
                    // (round the rim only the reach varies, gently; the banks and gaps are blobs placed in space, not
                    // sectors: those drew petals)
                    float lump=N(float3(sdir*0.85,0.43)+sd*0.07).a, lump2=N(float3(s.xz/(_SpSurge.y*2.8),0.83)+sd*0.08).a;
                    float reachS=_SpSurge.x*(0.8+0.4*lump)*(1+0.35*(wl>0.5?dot(sdir,wd/wl):0)*saturate(wl/max(_SpSurge.x,1)*3));
                    float e=(rho-reachS)/(_SpSurge.y*(0.7+0.6*lump2));
                    if(e<2.4)
                    {
                        float3 sm=s-float3(sdir.x,0,sdir.y)*_SpWind.w*_SpSurge.y*0.6+float3(0,_SpWind.w*_SpSurge.z*0.25,0);
                        float n=cheap?N(sm/(_SpSurge.y*2)+sd*0.07+0.7).r:Billows(sm/(_SpSurge.y*1.3)+sd*0.07+0.7);
                        // (a rolling front, a thinner, patchy veil behind it)
                        float bank=0.55+0.6*smoothstep(0.3,0.68,lump2);
                        float ring=(exp(-e*e)+0.3*smoothstep(0.5,-1.5,e)*smoothstep(0.3,0.6,n))*bank;
                        float h=_SpSurge.z*(0.55+0.9*n)*(0.55+0.45*exp(-e*e));
                        float m=ring*saturate((h-s.y)/(0.4*h))*(0.4+1.0*n);
                        surge=m*_SpSurge.w; r+=surge;
                    }
                }
                return r;
            }
            // A deep blast's dome: the sea heaped up white over the gas bubble before it bursts.
            float Dome(float3 p)
            {
                if(_SpDome.z<=0||p.y<0) return 0;
                float R=_SpDome.x, h=max(_SpDome.y,0.04*R);
                float l=length(p/float3(R,h,R));
                if(l>1.15) return 0;
                float n=N(p/(0.3*R)+Seed()*0.03).r*0.6+N(p/(0.09*R)+Seed()*0.05+0.3).g*0.4;
                return _SpDome.z*smoothstep(1.08,0.88,l+(n-0.5)*0.3)*(0.5+1.0*n);
            }
            // What fills a point: extinction (1/m), the share that is mist, the share that is solid water.
            float3 Media(float3 p,bool cheap)
            {
                float body=0, surge;
                float c=Burst(p,cheap,body);
                float j=Jet(p,cheap);
                float m=Mist(p,cheap,surge);
                float d=Dome(p);
                float e=c+j+m+d;
                return float3(e,m/max(e,1e-5),c*body/max(e,1e-5));
            }
            static const float SpLens[3]={0.04,0.14,0.45};   // sun-ray samples, in shadow lengths
            struct FragOut { float4 colour:SV_Target0; float4 front:SV_Target1; };
            FragOut frag(v2f i)
            {
                FragOut fo; fo.colour=0; fo.front=0;
                float3 worldRay=normalize(i.world-_WorldSpaceCameraPos);
                float S=_SpBox.z;
                float3 o=mul(unity_WorldToObject,float4(_WorldSpaceCameraPos,1)).xyz*S+float3(0,_SpBox.w,0);
                float3 d=mul((float3x3)unity_WorldToObject,worldRay)*S;   // metres in the blast frame per metre along the ray
                float dl=max(length(d),1e-6); float3 dn=d/dl;
                float sceneEye=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                float rayEye=max(1e-4,dot(worldRay,-UNITY_MATRIX_V[2].xyz));
                float2 range=VefxRayRange(rayEye,sceneEye);
                float reach=_SpBox.x, top=_SpBox.y;
                float3 sdir=sign(d+1e-12)*max(abs(d),1e-12);
                float3 ta=(float3(-reach,0,-reach)-o)/sdir, tb=(float3(reach,top,reach)-o)/sdir;
                float3 lo=min(ta,tb), hi=max(ta,tb);
                float entry=max(max(lo.x,max(lo.y,lo.z)),range.x), exit=min(min(hi.x,min(hi.y,hi.z)),range.y);
                float3 L=normalize(_LightDirection.xyz);
                float c=dot(dn,L);
                float3 sky=_VefxSky.w>0.5?_VefxSky.rgb:float3(0.3,0.36,0.46)*0.9;
                float sunVis=_SunColor.a*smoothstep(-0.03,0.06,L.y);
                float3 sunCol=lerp(float3(1,0.45,0.2),float3(1,0.96,0.9),smoothstep(0,0.25,L.y));
                float3 sd=Seed();
                // ---- the foam on the water, where the ray meets the sea (behind everything else) ----
                float4 foam=0;
                [branch] if(_SpFoam.z>0.002&&d.y<-1e-5)
                {
                    float tf=-o.y/d.y;
                    if(tf>range.x&&tf<range.y*1.03+1)
                    {
                        float3 pf=o+d*tf;
                        float R=_SpFoam.x;
                        float2 xz=pf.xz-float2(_SpWind.x,_SpWind.y)*0.3;
                        float2 q=xz/R;
                        // Whitewater, as it looks from the air: an irregular patch (its outline from noise laid in the
                        // plane: angular noise round the centre drew a clover with lines meeting in the middle) of bubbly white
                        // foam in clumps at three scales with dark water between, denser in the middle, breaking up as it ages,
                        // over a halo of pale green aerated water.
                        float shapeN=N(float3(q*0.6,0.31)+sd*0.07).a*0.65+N(float3(q*1.4,0.73)+sd*0.05).a*0.35;
                        float e=length(q)*(0.75+0.55*(1-shapeN));
                        if(e<1.6)
                        {
                            float breakup=1-saturate(_SpFoam.z*1.3);
                            float n=N(float3(q*1.1,0.2)+sd*0.03).g*0.6+N(float3(q*2.7,0.6)+sd*0.04).b*0.4;   // (no finer octave: it glittered)
                            float cover=smoothstep(1.2,0.15,e);
                            float lo=0.6-0.3*cover+0.18*breakup;
                            float white=smoothstep(lo,lo+0.08,n)*smoothstep(1.25,0.7,e);
                            float churn=smoothstep(1.4,0.35,e)*(0.45+0.55*shapeN);
                            float3 g3=pf-_SpGlowAt.xyz;
                            float3 lt=sunCol*sunVis*(saturate(L.y)*0.85+0.1)+sky*1.15+_SpGlow.rgb*_SpGlow.w*_SpGlow.w/(_SpGlow.w*_SpGlow.w+dot(g3,g3));
                            float a=_SpFoam.z*saturate(white*0.85+churn*0.3*(1-white));
                            float3 c3=lerp(float3(0.3,0.58,0.56),float3(0.84,0.89,0.9),saturate(white*0.85/max(white*0.85+churn*0.3*(1-white),1e-4)));
                            foam=float4(c3*lt*a,a);
                        }
                    }
                }
                if(exit<=entry) { fo.colour=foam; return fo; }
                float Ls=_SpDrift.w;
                float detail=clamp(_SpGlowAt.w,0.4,1);
                float hs=max(Ls*0.022,(exit-entry)*dl/220)/detail;
                float t=entry+hs/dl*tex2Dlod(_BlueNoise,float4((floor(i.vertex.xy)+0.5)/64.0,0,0)).r;
                float T=1, front=-1;
                float3 col=0;
                [loop] for(int s=0;s<260;s++)
                {
                    if(t>exit||T<0.01) break;
                    float3 p=o+d*t;
                    float3 m=Media(p,false);
                    float adv=hs;
                    if(m.x>1e-4)
                    {
                        // Optical depth toward the sun, and toward the sky above.
                        float od=0, prev=0;
                        [unroll] for(int k=0;k<3;k++)
                        {
                            od+=Media(p+L*(SpLens[k]*Ls),true).x*(SpLens[k]-prev)*Ls; prev=SpLens[k];
                        }
                        float odUp=Media(p+float3(0,0.3*Ls,0),true).x*0.3*Ls;
                        // Several orders of scattering, each broader and seeing less of the depth.
                        float sunL=0, aa=1, bb=1, gg=0.6;
                        [unroll] for(int o2=0;o2<3;o2++) { sunL+=bb*exp(-od*aa)*Phase(gg,c); aa*=0.25; bb*=0.65; gg*=0.5; }
                        // (the thin outer spray has scattered little toward the eye: its folds read against the sun)
                        sunL=min(sunL,2.4);
                        float powder=1-exp(-m.x*hs*2.5);
                        sunL*=lerp(1,0.4+0.6*powder,saturate(0.5-0.5*c)*0.6);
                        float3 g3=p-_SpGlowAt.xyz;
                        float3 light=sunCol*sunVis*(sunL*0.85+0.03)+sky*(0.22+0.55*exp(-odUp))*1.05
                            // the sea under it: sunlight off the water on its underside, blue-green
                            +float3(0.16,0.27,0.3)*sunVis*saturate(L.y)*0.7*exp(-p.y/max(0.5*Ls,1e-3))
                            // the blast's fire, if it burns
                            +_SpGlow.rgb*_SpGlow.w*_SpGlow.w/(_SpGlow.w*_SpGlow.w+dot(g3,g3));
                        float3 albedo=lerp(float3(0.9,0.92,0.94),float3(0.6,0.78,0.8),m.z*0.45)*lerp(1,0.94,m.y);
                        float alpha=1-exp(-m.x*hs);
                        col+=T*alpha*albedo*light;
                        T*=1-alpha;
                        if(front<0&&T<0.55) front=t;
                    }
                    else adv=hs*2.5;   // empty: longer strides (the dither hides them)
                    t+=adv/dl;
                }
                float a=1-T;
                float3 rgb=col;
                if(front>0)
                {
                    float fog=(1-exp(-front/40000.0))*saturate(_Context.y)*0.8;
                    float3 haze=lerp(sky*1.25,max(sky*2.2,float3(0.62,0.7,0.8)),smoothstep(-0.05,0.2,L.y));
                    rgb=lerp(rgb,haze*a,fog);
                }
                float4 res=float4(rgb,a);
                res+=(1-res.a)*foam;
                fo.colour=res;
                float eye=front>0?front*rayEye:0;
                fo.front=float4(eye*a,0,0,eye>0?a:0);
                return fo;
            }
            ENDCG
        }
    }
    Fallback Off
}
