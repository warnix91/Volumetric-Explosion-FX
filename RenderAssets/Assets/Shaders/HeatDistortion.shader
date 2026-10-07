Shader "VolumetricExplosionFX/HeatDistortion"
{
    Properties
    {
        _Shock ("Shock radius, offset, width, billboard half-size", Vector) = (0.6,0.02,0.08,1)
        _Haze ("Haze offset, height, half-width, age", Vector) = (0.004,1,0.55,0.5)
        _Seed ("Local seed", Float) = 37
    }
    SubShader
    {
        Tags { "Queue"="Transparent+30" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_VefxGrab" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend Off
            ColorMask RGB
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler2D _VefxGrab;
            float4 _Shock, _Haze;
            float _Seed;
            struct v2f
            {
                float4 vertex:SV_POSITION; float4 grab:TEXCOORD0;
                float2 local:TEXCOORD1;   // proxy-local units on the camera-facing plane
                float4 scale:TEXCOORD2;   // screen-uv per local unit (x, y), metres per local unit, effect eye depth
                float4 screen:TEXCOORD3;
                float3 up:TEXCOORD4;      // world up of the effect projected on screen (direction, length)
            };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o;
                float metres=length(unity_ObjectToWorld._m00_m10_m20);
                float extent=_Shock.w*metres;
                float3 view=mul(UNITY_MATRIX_V,float4(mul(unity_ObjectToWorld,float4(0,0,0,1)).xyz,1)).xyz;
                float pull=min(extent*0.6,max(-view.z-1,0));
                float depth=max(-view.z,0.01), pulled=max(depth-pull,0.01);
                float3 p=float3((view.xy+vertex.xy*extent)*(pulled/depth),-pulled);
                o.vertex=mul(UNITY_MATRIX_P,float4(p,1));
                o.grab=ComputeGrabScreenPos(o.vertex);
                o.screen=ComputeScreenPos(o.vertex);
                o.local=vertex.xy*_Shock.w;
                float flip=1;
                #if UNITY_UV_STARTS_AT_TOP
                flip=-1;
                #endif
                o.scale=float4(0.5*UNITY_MATRIX_P[0][0]*metres/depth,0.5*UNITY_MATRIX_P[1][1]*metres/depth*flip,metres,depth);
                float2 up=mul((float3x3)UNITY_MATRIX_V,normalize(unity_ObjectToWorld._m01_m11_m21)).xy;
                float len=length(up);
                o.up=float3(len>1e-3?up/len:float2(0,1),len);
                return o;
            }
            float hash(float3 p)
            {
                p=frac(p*float3(0.1031,0.11369,0.13787));
                p+=dot(p,p.yzx+19.19); return frac((p.x+p.y)*p.z);
            }
            float noise(float3 p)
            {
                float3 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(hash(i),hash(i+float3(1,0,0)),f.x),
                    lerp(hash(i+float3(0,1,0)),hash(i+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),f.x),
                    lerp(hash(i+float3(0,1,1)),hash(i+1),f.x),f.y),f.z);
            }
            float4 frag(v2f i):SV_Target
            {
                float2 p=i.local;
                float r=length(p);
                float2 dir=r>1e-4?p/r:float2(0,0);
                float age=_Haze.w;
                float wobble=1+0.05*(noise(float3(dir*3+_Seed*0.1,age*2))-0.5);
                float x=(r-_Shock.x*wobble)/max(_Shock.z,1e-3);
                float2 offset=dir*(_Shock.y*-2*x*exp(-x*x));
                float2 upDir=i.up.xy; float2 side=float2(upDir.y,-upDir.x);
                float along=dot(p,upDir), across=dot(p,side);
                float height=max(_Haze.y*i.up.z,_Haze.z*0.7);
                float mask=smoothstep(_Haze.z,_Haze.z*0.3,abs(across))*smoothstep(-0.12,0.05,along)*smoothstep(height,height*0.3,along);
                if(_Haze.x>0&&mask>0.001)
                {
                    float3 q=float3(across*24,along*18-age*7,_Seed*0.13);
                    float2 a=float2(noise(q),noise(q+float3(31.7,7.3,2.1)))-0.5;
                    float3 q2=q*2.1+float3(5.2,-age*9,1.7);
                    float2 b=float2(noise(q2),noise(q2+float3(13.1,3.9,8.4)))-0.5;
                    float2 d=a+b*0.5;
                    offset+=(upDir*d.y+side*d.x)*(_Haze.x*mask);
                }
                float sceneEye=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                offset*=saturate((sceneEye-i.scale.w)/(i.scale.z*0.5+0.5));
                float2 uv=i.grab.xy/i.grab.w+offset*i.scale.xy;
                float rim=exp(-x*x*0.45)*_Shock.y*1.3*saturate((sceneEye-i.scale.w)/(i.scale.z*0.5+0.5)+0.35);
                return float4(tex2D(_VefxGrab,uv).rgb*(1+rim)+rim*0.25,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
