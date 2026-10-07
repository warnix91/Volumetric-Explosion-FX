Shader "VolumetricExplosionFX/PartFragment"
{
    Properties
    {
        _MainTex ("Texture of the destroyed part", 2D) = "white" {}
        _Color ("Tint of the destroyed part", Color) = (1,1,1,1)
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _Age ("Seconds since the part was destroyed", Float) = 0
        _Gravity ("Local gravity (m/s2)", Float) = 9.81
        _Life ("Crumbling starts, ends (s)", Vector) = (40,45,0,0)
        _Cool ("Cooling time of hot rims (s)", Float) = 4
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex; float4 _MainTex_ST; float4 _Color;
    sampler3D _NoiseTex;
    float _Age, _Gravity, _Cool; float4 _Life;
    struct appdata
    {
        float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR;
        float4 uv:TEXCOORD0;      // texture, side of the sheet (0 outer, 1 inner, 0.5 torn edge)
        float4 pivot:TEXCOORD1;   // piece pivot, landing time
        float4 launch:TEXCOORD2;  // velocity, drag
        float4 spin:TEXCOORD3;    // spin axis * rate, rest height of the pivot (lying flat)
        float4 settle:TEXCOORD4;  // turn that lays it flat (axis * angle), hop speed
        float4 after:TEXCOORD5;   // skid velocity (x, z), skid time, touchdown height of the pivot
    };
    float E1(float u) { return u<0.02?1-u*(0.5-u*(1.0/6-u/24)):(1-exp(-u))/u; }
    float E2(float u) { return u<0.02?0.5-u*(1.0/6-u*(1.0/24-u/120)):(u-1+exp(-u))/(u*u); }
    float3 Rotate(float3 v,float3 axis,float angle)
    {
        float c=cos(angle), s=sin(angle);
        return v*c+cross(axis,v)*s+axis*dot(axis,v)*(1-c);
    }
    void Move(appdata v,out float3 p,out float3 n,out float3 local)
    {
        float land=v.pivot.w, k=v.launch.w;
        float t=min(_Age,land), u=k*t;
        float3 at=v.pivot.xyz+v.launch.xyz*(t*E1(u));
        at.y-=_Gravity*t*t*E2(u);
        float rate=length(v.spin.xyz);
        float3 axis=rate>1e-5?v.spin.xyz/rate:float3(0,1,0);
        float angle=rate*t*E1(u);
        local=v.vertex.xyz-v.pivot.xyz;
        float3 q=Rotate(local,axis,angle);
        n=Rotate(v.normal,axis,angle);
        if(_Age>=land)
        {
            float tau=_Age-land, ts=v.after.z, s=min(tau,ts);
            if(ts>1e-6) at.xz+=v.after.xy*(s-s*s/(2*ts));
            float g=max(_Gravity,0.1), th=v.settle.w>0?2*v.settle.w/g:0;
            float hop=tau<th?v.settle.w*tau-0.5*g*tau*tau:0;
            float sa=length(v.settle.xyz);
            float x=saturate(tau/(th+0.35+0.25*min(sa,1.6)));
            float settled=x*x*(3-2*x);
            if(sa>1e-4) { float3 sx=v.settle.xyz/sa; q=Rotate(q,sx,sa*settled); n=Rotate(n,sx,sa*settled); }
            at.y=(settled>=1?v.spin.w:lerp(v.after.w,v.spin.w,settled))+max(hop,0);
        }
        p=at+q;
    }
    float4 Crumble(float3 local,float seed)
    {
        float4 n=tex3D(_NoiseTex,local*0.6+seed*7.31);
        float crumble=saturate((_Age-_Life.x)/max(_Life.y-_Life.x,0.01));
        clip(n.b*0.85+n.r*0.15-crumble*1.02);
        return n;
    }
    ENDCG
    SubShader
    {
        Tags { "Queue"="Geometry+10" "RenderType"="Opaque" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            float4 _VefxSun, _VefxAmbient, _VefxSky, _VefxUp, _VefxFire0, _VefxFire1, _VefxFireRange;
            struct v2f
            {
                float4 pos:SV_POSITION; float3 uv:TEXCOORD0; float3 world:TEXCOORD1; float3 normal:TEXCOORD2;
                float3 local:TEXCOORD3; float4 data:TEXCOORD4;
            };
            v2f vert(appdata v)
            {
                v2f o; float3 p,n,local;
                Move(v,p,n,local);
                o.pos=UnityObjectToClipPos(float4(p,1));
                o.world=mul(unity_ObjectToWorld,float4(p,1)).xyz;
                o.normal=UnityObjectToWorldNormal(n);
                o.local=local; o.uv=float3(TRANSFORM_TEX(v.uv.xy,_MainTex),v.uv.z); o.data=v.color;
                return o;
            }
            float3 Incandescence(float t)
            {
                float3 c=lerp(float3(0.5,0.05,0.01),float3(1,0.35,0.05),saturate(t*2));
                return lerp(c,float3(1,0.78,0.45),saturate(t*2-1));
            }
            float3 FireLight(float4 F,float range,float3 world,float3 N)
            {
                float3 d=F.xyz-world; float r2=dot(d,d)+0.25;
                return float3(1,0.55,0.22)*F.w*saturate(dot(N,d*rsqrt(r2))*0.75+0.25)/(1+r2/max(range*range*0.12,1));
            }
            float4 frag(v2f i,float facing:VFACE):SV_Target
            {
                float heat=i.data.r, charAmount=i.data.g, seed=i.data.b, rim=i.data.a;
                float4 n=Crumble(i.local,seed);
                bool back=facing<0;
                float side=i.uv.z;
                bool inner=side>0.75, wall=side>0.25&&side<=0.75;
                float3 albedo=tex2D(_MainTex,i.uv.xy).rgb*_Color.rgb;
                if(back||inner) albedo=albedo*0.3+float3(0.07,0.065,0.06);
                if(wall) albedo=float3(0.46,0.45,0.43)*(0.75+0.5*n.a);
                float edge=saturate((rim-0.55)*2.2)*smoothstep(0.42,0.72,n.r);
                float burnt=saturate((charAmount*1.2+edge*0.45-n.g*1.05-0.2)*3.5);
                albedo=lerp(albedo,float3(0.045,0.04,0.035),burnt*0.93);
                float temp=0.75*heat*exp(-_Age/max(_Cool,0.1))*saturate(edge*0.9+(n.r-0.6)*2.5*charAmount);
                float3 glow=Incandescence(temp)*temp*temp*1.8;
                float3 N=normalize(i.normal)*(back?-1:1);
                float3 L=normalize(_VefxSun.xyz+float3(0,1e-5,0));
                float3 V=normalize(_WorldSpaceCameraPos-i.world);
                float diffuse=saturate(dot(N,L));
                float spec=pow(saturate(dot(N,normalize(L+V))),40)*0.35*(1-burnt)*(back?0.15:1);
                float up=dot(N,_VefxUp.xyz);
                float3 ambient=_VefxAmbient.rgb*(0.75+0.25*up)+_VefxSky.rgb*saturate(0.5+0.5*up)*0.55;
                float3 fire=FireLight(_VefxFire0,_VefxFireRange.x,i.world,N)+FireLight(_VefxFire1,_VefxFireRange.y,i.world,N);
                float3 sun=float3(1,0.96,0.9)*_VefxSun.w;
                float3 color=albedo*(sun*diffuse+ambient+fire)+sun*spec+glow;
                return float4(color,1);
            }
            ENDCG
        }
        Pass
        {
            // Use the same deformed vertices for shadows and camera depth.
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 3.0
            #pragma multi_compile_shadowcaster
            #pragma only_renderers d3d11 glcore metal
            struct v2s { V2F_SHADOW_CASTER; float3 local:TEXCOORD1; float seed:TEXCOORD2; };
            v2s vertShadow(appdata v)
            {
                v2s o; float3 p,n,local;
                Move(v,p,n,local);
                #if defined(SHADOWS_CUBE) && !defined(SHADOWS_CUBE_IN_DEPTH_TEX)
                    o.vec=mul(unity_ObjectToWorld,float4(p,1)).xyz-_LightPositionRange.xyz;
                    o.pos=UnityObjectToClipPos(float4(p,1));
                #else
                    o.pos=UnityClipSpaceShadowCasterPos(float4(p,1),n);
                    o.pos=UnityApplyLinearShadowBias(o.pos);
                #endif
                o.local=local; o.seed=v.color.b;
                return o;
            }
            float4 fragShadow(v2s i):SV_Target
            {
                Crumble(i.local,i.seed);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    Fallback Off
}
