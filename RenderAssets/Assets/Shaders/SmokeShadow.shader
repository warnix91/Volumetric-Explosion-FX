Shader "VolumetricExplosionFX/SmokeShadow"
{
    // The shadow the fireball and its smoke cast on the ground: a mesh draped on the terrain under the blast
    // darkens what the sun would light, by the gas the sun's rays cross on their way (original, 1.1).
    Properties
    {
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _Sun ("Sun direction (blast frame), strength", Vector) = (0.5,0.8,0.2,1)
        _Shadow ("Proxy size (m), darkness, opacity, seed", Vector) = (100,0.5,1.5,13)
        _L0 ("Gas 0 centre, radius (proxy units)", Vector) = (0,0,0,0)
        _L1 ("Gas 1 centre, radius", Vector) = (0,0,0,0)
        _L2 ("Gas 2 centre, radius", Vector) = (0,0,0,0)
        _L3 ("Gas 3 centre, radius", Vector) = (0,0,0,0)
        _A0 ("Simulated copy 0: heading, scale, frame", Vector) = (1,0,1,0)
        _A1 ("Simulated copy 1", Vector) = (1,0,1,0)
        _A2 ("Simulated copy 2", Vector) = (1,0,1,0)
        _A3 ("Simulated copy 3", Vector) = (1,0,1,0)
        _D0 ("Simulated copy 0: origin", Vector) = (0,0,0,0)
        _D1 ("Simulated copy 1: origin", Vector) = (0,0,0,0)
        _D2 ("Simulated copy 2: origin", Vector) = (0,0,0,0)
        _D3 ("Simulated copy 3: origin", Vector) = (0,0,0,0)
        _SimOn ("Simulated smoke (1), ground variant (1)", Vector) = (0,1,0,0)
        _SimShape ("Explosion character: vertical scale, lean x, lean z, warp", Vector) = (1,0,0,1)
    }
    SubShader
    {
        Tags { "Queue"="Geometry+460" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            // (a box round the blast, its back faces; the shadow is laid on what the camera sees there, from the
            // scene's depth: the mesh laid on the ground failed where rays missed and cut the shadow with straight edges)
            Cull Front ZWrite Off ZTest Always
            Blend DstColor Zero
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            sampler3D _NoiseTex;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4x4 _ShadowFrame;   // world to blast frame (metres)
            float4 _ShadowArea;      // where the shadow may fall: centre x, z, radius, highest point (m, blast frame)
            float4 _Sun, _Shadow, _L0, _L1, _L2, _L3, _A0, _A1, _A2, _A3, _D0, _D1, _D2, _D3, _SimOn, _SimShape;
            sampler3D _FireSimGround, _FireSimAir, _FireSimGround2, _FireSimGround3, _FireSimGround4, _FireSimAir2, _FireSimNuke, _FireSimNuke2, _FireSimNukeAir;
            float4 _SimNukeDims, _SimNukeGeom;
            float4 _NukeRing;   // w: the nuclear cloud's underside (share of its box height)
            float _SimVariant, _SimStem;
            float4 _SimDims, _SimAirDims, _SimGeom;
            // Gas of one simulated copy at p (proxy units), as HeroVolume places it (nearest baked frame, no warp).
            float Gas(float3 p,float4 A,float4 D,bool ground)
            {
                float3 d=p-D.xyz;
                float tall=_SimShape.x>0.05?_SimShape.x:1;
                d.xz-=_SimShape.yz*max(d.y,0); d.y/=tall;
                float sc=max(abs(A.z),1e-5), k2=max(dot(A.xy,A.xy),1e-4);
                float3 q=float3(A.x*d.x+A.y*d.z,d.y,(-A.y*d.x+A.x*d.z)/k2)/sc;
                if(A.z<0) q.x=-q.x;
                bool nukeRun=_SimVariant>3.5;
                float half_=nukeRun?_SimNukeGeom.x:ground?_SimGeom.x:_SimGeom.z, height=nukeRun?_SimNukeGeom.y:ground?_SimGeom.y:_SimGeom.w;
                float3 s=float3(q.x*0.5/half_+0.5,q.y/height,q.z*0.5/half_+0.5);
                if(any(s<0)||any(s>1)) return 0;
                float4 dims=nukeRun?_SimNukeDims:ground?_SimDims:_SimAirDims;
                float nz=dims.z, f=floor(clamp(A.w+0.5,0,dims.w-1));
                float4 uv=float4(s.x,s.y,(f*nz+clamp(s.z*nz,0.5,nz-0.5))/(nz*dims.w),0);
                float g;   // one run, with real branches (a ?: chain reads every texture)
                [branch] if(ground)
                {
                    [branch] if(_SimVariant<0.5) g=tex3Dlod(_FireSimGround,uv).x;
                    else [branch] if(_SimVariant<1.5) g=tex3Dlod(_FireSimGround2,uv).x;
                    else [branch] if(_SimVariant<2.5) g=tex3Dlod(_FireSimGround3,uv).x;
                    else [branch] if(_SimVariant<3.5) g=tex3Dlod(_FireSimGround4,uv).x;
                    else [branch] if(_SimVariant<4.5) g=tex3Dlod(_FireSimNuke,uv).x;
                    else g=tex3Dlod(_FireSimNuke2,uv).x;
                }
                else
                {
                    [branch] if(_SimVariant<0.5) g=tex3Dlod(_FireSimAir,uv).x;
                    else [branch] if(_SimVariant<1.5) g=tex3Dlod(_FireSimAir2,uv).x;
                    else g=tex3Dlod(_FireSimNukeAir,uv).x;
                }
                // As HeroVolume: late frames keep their density, and a fuel cloud's stem thins from the ground up.
                float frac=A.w/max(dims.w-1,1);
                g*=1+0.5*smoothstep(0.4,0.7,frac);
                if(_SimVariant>3.5)
                {
                    float cutN=_NukeRing.w*smoothstep(0.35,0.65,frac);
                    g*=smoothstep(cutN-0.1,cutN-0.02,s.y);
                }
                else if(ground&&_SimStem>0)
                {
                    float cutMax=_SimVariant<0.5?0.45:_SimVariant<1.5?0.4:_SimVariant<2.5?0.22:0.3;
                    float lift=max(smoothstep(0.3,0.85,frac),saturate(_SimStem-1))*min(_SimStem,1), cut=cutMax*lift;
                    g*=lerp(1,smoothstep(cut-0.05,cut+0.2,s.y),saturate(lift*1.6));
                }
                return g;
            }
            // Gas met by the sun's ray from p through one copy's bounding sphere (proxy units of gas x length).
            float March(float3 p,float3 L,float4 S,float4 A,float4 D,bool ground,float jitter)
            {
                if(S.w<=1e-4) return 0;
                float3 oc=p-S.xyz; float b=dot(oc,L), c=dot(oc,oc)-S.w*S.w, h=b*b-c;
                if(h<=0) return 0;
                h=sqrt(h);
                float t1=-b+h; if(t1<=0) return 0;
                float t0=max(-b-h,0), dt=(t1-t0)/16, sum=0;
                // Thin gas (eroded away on screen) casts little, eased in rather than cut (a cut drew hard outlines).
                [loop] for(int k=0;k<16;k++) { float g=Gas(p+L*(t0+(k+jitter)*dt),A,D,ground); sum+=g*smoothstep(0.03,0.22,g); }
                return sum*dt;
            }
            struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float4 screen:TEXCOORD1; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.pos=UnityObjectToClipPos(vertex); o.world=mul(unity_ObjectToWorld,vertex).xyz; o.screen=ComputeScreenPos(o.pos); return o;
            }
            // Length of the ray from p toward the sun inside a sphere, in units of its radius (0..2).
            float Through(float3 p,float3 L,float4 S)
            {
                if(S.w<=1e-4) return 0;
                float r=S.w;   // radius of the gas itself (the CPU scales each lobe's span)
                float3 oc=p-S.xyz; float b=dot(oc,L), c=dot(oc,oc)-r*r, h=b*b-c;
                if(h<=0) return 0;
                h=sqrt(h);
                float t1=-b+h; if(t1<=0) return 0;
                float t0=max(-b-h,0);
                float x=(t1-t0)/r;
                return x*x*0.5;   // soft edge: grazing rays cast little shadow
            }
            // Offset of the march: the fixed steps drew the gas in contour rings. A smooth offset over the ground (noise)
            // turns them into soft, natural variation; a little per-pixel dither melts what is left without a visible grain.
            float Dither(float2 pixel,float3 p)
            {
                float ign=frac(52.9829189*frac(dot(pixel,float2(0.06711056,0.00583715))));
                return frac(tex3Dlod(_NoiseTex,float4(p*2.3+_Shadow.w*0.013,0)).r*1.7)*0.8+ign*0.2;
            }
            float4 frag(v2f i):SV_Target
            {
                float sceneEye=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                float3 ray=i.world-_WorldSpaceCameraPos;
                float rayEye=dot(ray,-UNITY_MATRIX_V[2].xyz);
                if(rayEye<=1e-4) return 1;
                float3 local=mul(_ShadowFrame,float4(_WorldSpaceCameraPos+ray*(sceneEye/rayEye),1)).xyz;
                float2 area=(local.xz-_ShadowArea.xy)/max(_ShadowArea.z,1e-3);
                if(dot(area,area)>1||local.y>_ShadowArea.w) return 1;
                float3 p=local/max(_Shadow.x,1e-3);
                float3 L=normalize(_Sun.xyz+float3(0,1e-4,0));
                float depth;
                if(_SimOn.x>0.5)
                {
                    // 1.1 simulated smoke: follow the sun's ray through the real gas, so the shadow has the cloud's shape
                    // and fades as the smoke thins (not a disc the size of its bounding box).
                    bool ground=_SimOn.y>0.5;
                    float jitter=Dither(i.pos.xy,p);
                    float gas=March(p,L,_L0,_A0,_D0,ground,jitter)+March(p,L,_L1,_A1,_D1,ground,jitter)+
                        March(p,L,_L2,_A2,_D2,ground,jitter)+March(p,L,_L3,_A3,_D3,ground,jitter);
                    // Graded by how much smoke the sunlight crosses: a thin veil only dims the ground, the thick core shades
                    // it, never to black (the sky and the smoke's own scattered light still reach it).
                    float shadeSim=(1-exp(-gas*6*_Shadow.z))*_Shadow.y*saturate(_Sun.w)*saturate((L.y-0.08)*5)*(1-smoothstep(0.7,1,length(area)));
                    return float4(1-shadeSim*float3(1,0.97,0.92),1);
                }
                depth=Through(p,L,_L0)+Through(p,L,_L1)+Through(p,L,_L2)+Through(p,L,_L3);
                if(depth<=1e-4) return 1;
                // Billowed, not a disc: the smoke's own lumps break up the shadow's edge.
                float n=tex3D(_NoiseTex,p*1.7+_Shadow.w*0.031).r*0.65+tex3D(_NoiseTex,p*4.1-_Shadow.w*0.017).g*0.35;
                depth*=0.45+1.1*n;
                float2 c=area;
                float edge=1-smoothstep(0.75,1,length(c));
                float shade=(1-exp(-depth*_Shadow.z))*_Shadow.y*saturate(_Sun.w)*saturate(L.y*6)*edge;
                float3 keep=1-shade*float3(1,0.97,0.92);   // what the sky still lights is a little bluer
                return float4(keep,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
