Shader "VolumetricExplosionFX/Scorch"
{
    Properties
    {
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _Scorch ("Darkness, ember heat, seed, dust (airless)", Vector) = (0.7,0,13,0)
        _Strip ("Scar along a path: length / half width (0 = round scorch), share burnt so far", Vector) = (0,1,0,0)
        _ScorchLook ("1.1 mark (0 = as 1.0), crater, drawn out along the impact, its direction (rad)", Vector) = (0,0,0,0)
        _ScorchSoil ("Ground colour round the blast (rgb), 1 snow / 2 hard ground (rock, concrete)", Vector) = (0.4,0.35,0.28,0)
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
            float4 _Scorch, _Strip, _ScorchLook, _ScorchSoil;
            float4 _VefxSun;   // world direction toward the star, strength (0 at night)
            float4 _Blobs[8];   // the pools burning along the path (strip frame, m); _Strip.z half width (m), w burnt length (m)
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 sun:TEXCOORD1; };
            v2f vert(float4 vertex:POSITION,float2 uv:TEXCOORD0)
            {
                v2f o; o.pos=UnityObjectToClipPos(vertex); o.uv=uv;
                o.sun=normalize(mul((float3x3)unity_WorldToObject,_VefxSun.xyz)+float3(0,1e-5,0));
                return o;
            }
            // ---- the 1.1 mark ----
            // Each mark its own: an uneven outline from its own place in the noise, a fuel burn (grey ash where it burned
            // hottest, charred black patches, singed ground round them, splashes of burnt fuel) or a warhead's crater (a
            // bowl with a raised rim lit by the sun on one side, fresh earth thrown out in uneven rays and clods, soot), drawn
            // out along a grazing impact. Mark units: 1 = the edge of the mesh.
            float Nz(float2 p,float z,int ch)
            {
                float4 n=tex3Dlod(_NoiseTex,float4(p,z,0));
                return ch==0?n.r:ch==1?n.g:ch==2?n.b:n.a;
            }
            void Over(inout float4 acc,float3 col,float a)
            {
                a=saturate(a); acc.rgb=col*a+acc.rgb*(1-a); acc.a=a+acc.a*(1-a);
            }
            // The mark's frame: along the impact, stretched, warped.
            float2 MarkSpace(float2 c,float3 sd)
            {
                float stretch=_ScorchLook.z; float2 ax=float2(cos(_ScorchLook.w),sin(_ScorchLook.w));
                float2 q=float2(dot(c,ax),dot(c,float2(-ax.y,ax.x)));
                q.x=q.x/(1+0.8*stretch)+0.15*stretch;
                float2 w=float2(Nz(q*0.42+sd.xy,sd.z,3),Nz(q*0.42+sd.yz+0.5,sd.x+0.3,3))-0.5;
                return q+w*0.32;
            }
            float CraterSize(float3 sd) { return lerp(0.15,0.27,sd.x); }
            // Height of the ground (mark units) round a warhead's crater: the bowl, its rim, the blanket thrown out.
            // (uneven from the place, not the direction: that drew spokes)
            float CraterHeight(float2 q,float3 sd)
            {
                float rc=CraterSize(sd), x=length(q)/rc;
                x*=0.9+0.2*Nz(q*0.9+sd.zx,sd.y+0.2,3);
                float bowl=-0.28*rc*saturate(1-x*x)*(0.85+0.3*Nz(q*2.6+sd.xy,sd.z+0.4,0));   // (loose earth in it)
                float rim=0.1*rc*exp(-pow((x-1.0)/0.3,2));
                float blanket=0.04*rc*saturate(1.6-x*0.5)*smoothstep(0.9,1.1,x)*(0.4+Nz(q*1.4+sd.yx,sd.z+0.7,0));
                return (bowl+rim+blanket)*_ScorchLook.y*(1-0.65*step(1.5,_ScorchSoil.w));   // (a shallow pit in hard ground)
            }
            float4 Mark11(float2 c,float r)
            {
                float s=_Scorch.z;
                float3 sd=float3(frac(s*0.0731),frac(s*0.1913),frac(s*0.3371));
                float2 q=MarkSpace(c,sd);
                float rw=length(q);
                float2 dir=q/max(rw,1e-4);
                // Patches (billows), smaller patches, a smooth field and a texture: low frequencies only (fine cells read
                // as hair and salt).
                float big=Nz(q*0.5+sd.zx,sd.y+0.3,0), mid=Nz(q*1.1+sd.xy+0.2,sd.z+0.6,0), soft=Nz(q*0.9+sd.yz+0.4,sd.x+0.8,3),
                    grain=Nz(q*1.9+sd.yx+0.7,sd.x+0.1,1);
                // Hard ground (rock, the space centre's concrete) is only sooted and scuffed: no singed halo, no earth
                // thrown out, a shallow pit;.
                float hard=step(1.5,_ScorchSoil.w), snow=saturate(_ScorchSoil.w)*(1-hard);
                float crater=_ScorchLook.y*(1-0.65*hard), burnt=1-0.6*crater, airless=_Scorch.w;
                float3 soil=_ScorchSoil.rgb;
                float level=saturate(_Scorch.x/0.5);
                float4 acc=0;
                // The outline: its reach changes round the mark, by its own seed.
                float edge=0.5+0.38*Nz(dir*0.5+sd.xy,sd.z,3)+0.12*Nz(dir*1.3+sd.yz,sd.x,0);
                // Singed ground round the burn (grass browns and yellows, sand and soil darken a little).
                float grassy=saturate((soil.g-max(soil.r,soil.b))*14);
                float3 singe=lerp(soil*float3(0.62,0.52,0.42),float3(0.34,0.26,0.13),grassy*0.8);
                singe=lerp(singe,soil*0.7,hard);
                float halo=smoothstep(edge+0.3,edge-0.1,rw+0.3*(soft-0.5))*(0.45+0.55*mid)*(1-airless)*(1-snow*0.6);
                Over(acc,singe,halo*(0.36-0.2*hard)*burnt);
                // How hard it burned here: from singed at the edge through dark brown char to black, patchy.
                float burn=smoothstep(edge+0.05,edge-0.6,rw+0.3*(big-0.5));
                float B=saturate(burn*(0.4+0.8*(big*0.5+mid*0.5)));
                float3 black=lerp(float3(0.028,0.025,0.022),float3(0.14,0.135,0.13),airless);
                float3 brown=lerp(float3(0.11,0.08,0.05),black*1.6,airless);
                float3 charCol=lerp(brown,black*1.4,smoothstep(0.4,0.85,B))*(0.85+0.3*grain);
                Over(acc,lerp(charCol,soil*0.08,0.5*hard),smoothstep(0.08,0.75,B)*(0.3+0.25*level)*burnt);
                // Ash: faint grey where it burned out (not on airless ground, not in a crater).
                float ash=smoothstep(0.66,0.82,soft*0.6+big*0.4+0.45*(B-0.7))*smoothstep(0.55,0.8,B)*(1-airless)*(1-crater)*(1-0.6*hard);
                Over(acc,lerp(float3(0.26,0.25,0.24),float3(0.38,0.37,0.35),grain),ash*0.45);
                // Splashes of burnt fuel round a burn.
                float drops=smoothstep(0.8,0.86,Nz(q*3.2+sd.zy,sd.x+0.45,0))*smoothstep(edge+0.35,edge-0.05,rw)*smoothstep(edge-0.35,edge,rw)*(1-crater);
                Over(acc,black,drops*0.7*(1-airless));
                [branch] if(crater>0.01)
                {
                    float rc=CraterSize(sd);
                    // Earth thrown out: a blanket round the rim and a few broad, clumpy rays, each its own length; clods.
                    float rays=Nz(dir*(0.7+0.5*sd.y)+sd.xz,rw*0.55+sd.z,0);
                    float reach=rc*(1.5+2.2*Nz(dir*0.6+sd.zy,sd.x+0.55,3));
                    float rayMask=smoothstep(0.45,0.7,rays)*smoothstep(reach,reach*0.4,rw)*smoothstep(rc*0.8,rc*1.1,rw);
                    float blanket=smoothstep(rc*(1.7+0.7*big),rc*1.0,rw);
                    float clods=smoothstep(0.8,0.87,Nz(q*5+sd.xy,sd.y+0.8,0))*smoothstep(rc*4,rc*1.2,rw);
                    // The earth from under the surface: brown under grass, darker sand, dirt through snow; fresh regolith
                    // on airless ground is brighter than the old surface.
                    float3 under=lerp(soil*float3(0.78,0.72,0.66),float3(0.33,0.25,0.17),grassy);
                    under=lerp(under,soil*0.85,hard);   // (on concrete: grit and dust, not earth)
                    under=lerp(under,float3(0.3,0.24,0.18),snow);
                    under=lerp(under,soil*1.18+0.03,airless);
                    float thrown=saturate(max(blanket*(0.55+0.45*mid),rayMask*0.75)+clods*0.85);
                    Over(acc,under*(0.8+0.35*grain),thrown*crater);
                    // Soot over the crater and round it, patchy.
                    float soot=smoothstep(rc*(2.2+1.2*big),rc*0.4,rw)*(0.35+0.65*mid)*(1-airless*0.7);
                    Over(acc,black,soot*crater*(0.4+0.35*level));
                    // The crater's floor: churned, darker earth, fading into the walls.
                    float floorM=smoothstep(rc*1.0,rc*0.45,rw*(0.9+0.2*soft));
                    Over(acc,under*lerp(0.35,0.62,mid),floorM*crater*0.6);
                }
                return acc;
            }

            float4 frag(v2f i):SV_Target
            {
                float2 c=i.uv*2-1;
                float r=length(c);
                if(_Strip.x>0)
                {
                    // A scar along the crash path: distance to the path in half widths, burnt as far as the fire has spread.
                    float x=i.uv.x*(_Strip.x+2)-1;
                    c=float2(x,c.y);
                    r=length(float2(x-clamp(x,0,_Strip.x),c.y))*(0.85+0.3*tex3Dlod(_NoiseTex,float4(x*0.21+_Scorch.z*0.03,0.4,0.1,0)).r);
                    r=max(r,smoothstep(_Strip.y*_Strip.x-0.5,_Strip.y*_Strip.x+1.5,x)*2);
                    // Under the pools of burning fuel scattered along the path, not a straight band.
                    if(_Strip.z>0&&_Blobs[0].z>0)
                    {
                        float2 m=float2(x,c.y)*_Strip.z; float best=1e6; float2 near=0;
                        [unroll] for(int k=0;k<8;k++)
                        {
                            float4 B=_Blobs[k];
                            if(B.z<=0||(k>0&&B.x>_Strip.w)) continue;
                            float dd=length(m-B.xy)/(B.z*1.35);
                            if(dd<best) { best=dd; near=(m-B.xy)/(B.z*1.35); }
                        }
                        r=best; c=near;
                    }
                }
                [branch] if(_ScorchLook.x>0.5)
                {
                    float4 m=Mark11(c,r);
                    // Relief: the sun across the crater (lit rim, shadowed bowl), from the slope of the ground there.
                    [branch] if(_ScorchLook.y>0.01&&_VefxSun.w>0)
                    {
                        float s1=_Scorch.z; float3 sd=float3(frac(s1*0.0731),frac(s1*0.1913),frac(s1*0.3371));
                        float e=0.006;
                        float h0=CraterHeight(MarkSpace(c,sd),sd), hx=CraterHeight(MarkSpace(c+float2(e,0),sd),sd), hz=CraterHeight(MarkSpace(c+float2(0,e),sd),sd);
                        float3 n=normalize(float3(-(hx-h0)/e,1,-(hz-h0)/e));
                        float3 L=i.sun;
                        float day=saturate(_VefxSun.w)*saturate(L.y*4);
                        float lit=saturate(dot(n,L))/max(L.y,0.2)-1;
                        float3 soil=_ScorchSoil.rgb;
                        float4 relief=0;
                        Over(relief,float3(0,0,0),saturate(-lit)*0.45*day);
                        Over(relief,soil*1.2+0.04,saturate(lit)*0.3*day);
                        m=relief+(1-relief.a)*m;
                    }
                    float specks11=smoothstep(0.74,0.9,tex3Dlod(_NoiseTex,float4(c*3.3+_Scorch.z*0.051,_Scorch.z*0.029,0)).b)*saturate(m.a*1.5);
                    float heat11=_Scorch.y*specks11;
                    m.rgb+=lerp(float3(0.9,0.15,0.02),float3(1,0.6,0.2),heat11)*heat11*2.2;
                    m*=smoothstep(1,0.86,r)*saturate(_Scorch.x*2.5);
                    return m;
                }
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
