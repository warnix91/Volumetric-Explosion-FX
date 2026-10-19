Shader "VolumetricExplosionFX/NukeShadow"
{
    // 1.1: the nuclear cloud's shadow in sunlight, laid on whatever the camera sees there: the scene's depth is
    // turned back into positions in the blast frame, and each is darkened by how much of the cap and the stem the sun's
    // ray from it crosses. It lies exactly on the land, the sea and the buildings (a mesh laid on a coarse map of the
    // ground dipped under the hills and left pale notches in the shadow). Drawn on a box round the blast (back faces).
    Properties
    {
        _NoiseTex ("Original tileable noise", 3D) = "" {}
    }
    SubShader
    {
        Tags { "Queue"="Geometry+460" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Front ZWrite Off ZTest Always
            Blend DstColor Zero
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler3D _NoiseTex;
            float4x4 _NkFrame;   // world to blast frame (metres)
            float4 _Sun;         // sun direction in the blast frame, strength
            float4 _Shadow;      // proxy size (m), darkness, opacity, seed
            float4 _NkSh0;       // cap centre (proxy units), cap radius
            float4 _NkSh1;       // cap half height, stem radius, stem top, ground
            float4 _NkSh2;       // -, stem density, strength
            float4 _NkReach;     // reach (m), lowest and highest ground (m)
            // How much of the cloud the sun's ray from p crosses (proxy units).
            float Through(float3 p,float3 L)
            {
                float d=0;
                // The cap: an ellipsoid (in units of its radii the ray crosses a unit sphere: up to 2 through its middle).
                float3 sc=float3(_NkSh0.w,_NkSh1.x,_NkSh0.w);
                float3 o=(p-_NkSh0.xyz)/sc, l=L/sc;
                float a=dot(l,l), b=dot(o,l), c=dot(o,o)-1, h=b*b-a*c;
                if(h>0&&a>1e-8)
                {
                    h=sqrt(h); float t1=(-b+h)/a;
                    if(t1>0) { float x=(t1-max((-b-h)/a,0))*sqrt(a); d+=x*x*0.5; }
                }
                // The stem: an upright column of dust from the ground to its top, a softer shadow than the cap's.
                float r=_NkSh1.y;
                if(r>1e-5&&_NkSh2.y>0.01&&L.y>1e-3)
                {
                    float2 o2=p.xz, l2=L.xz; float a2=dot(l2,l2), b2=dot(o2,l2), c2=dot(o2,o2)-r*r;
                    float t0=0, t1=1e6, across=1;
                    if(a2>1e-8)
                    {
                        float h2=b2*b2-a2*c2; if(h2<=0) return d;
                        h2=sqrt(h2); t0=(-b2-h2)/a2; t1=(-b2+h2)/a2; across=(t1-t0)*sqrt(a2)/(2*r);
                    }
                    else if(c2>0) return d;
                    t0=max(t0,max((_NkSh1.w-p.y)/L.y,0)); t1=min(t1,(_NkSh1.z-p.y)/L.y);
                    if(t1>t0) d+=min((t1-t0)/(2*r),1.4)*across*across*_NkSh2.y*0.55;
                }
                return d;
            }
            struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float4 screen:TEXCOORD1; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.pos=UnityObjectToClipPos(vertex); o.world=mul(unity_ObjectToWorld,vertex).xyz; o.screen=ComputeScreenPos(o.pos); return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float sceneEye=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                float3 ray=i.world-_WorldSpaceCameraPos;
                float rayEye=dot(ray,-UNITY_MATRIX_V[2].xyz);
                if(rayEye<=1e-4) return 1;
                float3 local=mul(_NkFrame,float4(_WorldSpaceCameraPos+ray*(sceneEye/rayEye),1)).xyz;
                // Only on the ground round the blast (not the sky, nor things high above it).
                float rr=length(local.xz)/max(_NkReach.x,1);
                if(rr>1||local.y<_NkReach.y-80||local.y>_NkReach.z+500) return 1;
                float3 p=local/max(_Shadow.x,1e-3);
                float3 L=normalize(_Sun.xyz+float3(0,1e-4,0));
                if(L.y<0.02) return 1;
                float depth=Through(p,L)*_NkSh2.z;
                if(depth<=1e-4) return 1;
                // Billowed, not a disc: the cloud's lumps break up the shadow's edge and its inside.
                float n=tex3D(_NoiseTex,p*1.7+_Shadow.w*0.031).r*0.65+tex3D(_NoiseTex,p*4.1-_Shadow.w*0.017).g*0.35;
                depth*=0.45+1.1*n;
                float shade=(1-exp(-depth*_Shadow.z))*_Shadow.y*saturate(_Sun.w)*saturate(L.y*6)*(1-smoothstep(0.8,1,rr));
                return float4(1-shade*float3(1,0.97,0.92),1);   // (what the sky still lights is a little bluer)
            }
            ENDCG
        }
    }
    Fallback Off
}
