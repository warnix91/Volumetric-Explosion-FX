Shader "VolumetricExplosionFX/Smoke"
{
    Properties
    {
        _SmokeA ("Lightmaps +X -X +Y, opacity", 2D) = "black" {}
        _SmokeB ("Lightmaps -Y +Z -Z, incandescence", 2D) = "black" {}
        _Emission ("Incandescence strength", Float) = 0
        _Density ("Opacity scale", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            sampler2D _SmokeA, _SmokeB;
            float _Emission, _Density;
            float4 _VefxSun;        // world direction toward the star, strength
            float4 _VefxAmbient;    // ambient colour
            float4 _VefxSky;        // daylight sky colour (black on airless worlds)
            float4 _VefxFire0, _VefxFire1;   // world position, intensity of the two strongest fires
            float4 _VefxFireRange;           // their light ranges (m)
            struct appdata
            {
                float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; float4 tangent:TANGENT;
                float4 uv:TEXCOORD0;     // this frame, next frame
                float4 tc1:TEXCOORD1;    // frame blend, particle centre
                float size:TEXCOORD2;    // particle size
            };
            struct v2f
            {
                float4 pos:SV_POSITION; float4 color:COLOR; float4 uv:TEXCOORD0;
                float3 sun:TEXCOORD1; float4 fire:TEXCOORD2; float blend:TEXCOORD3; float fade:TEXCOORD4;
            };
            // Light direction in sprite coordinates: right, up, toward the viewer.
            float3 Local(float3 L,float3 T,float3 B,float3 N) { return float3(dot(L,T),dot(L,B),dot(L,N)); }
            float4 Fire(float4 F,float range,float3 world,float3 T,float3 B,float3 N)
            {
                float3 d=F.xyz-world; float r2=dot(d,d)+0.25;
                float strength=F.w/(1+r2/max(range*range*0.12,1));
                return float4(Local(d*rsqrt(r2),T,B,N),strength);
            }
            v2f vert(appdata v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex);
                float3 N=normalize(UnityObjectToWorldNormal(v.normal));
                float3 T=normalize(UnityObjectToWorldDir(v.tangent.xyz));
                float3 B=cross(N,T)*(v.tangent.w<0?-1:1);
                o.sun=Local(normalize(_VefxSun.xyz+float3(0,1e-5,0)),T,B,N);
                float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
                float4 f0=Fire(_VefxFire0,_VefxFireRange.x,world,T,B,N), f1=Fire(_VefxFire1,_VefxFireRange.y,world,T,B,N);
                o.fire=f0.w>=f1.w?float4(f0.xyz,f0.w+f1.w*0.5):float4(f1.xyz,f1.w+f0.w*0.5);
                float3 centre=mul(unity_ObjectToWorld,float4(v.tc1.yzw,1)).xyz;
                float size=max(v.size*length(unity_ObjectToWorld._m00_m10_m20),0.01);
                o.fade=saturate((distance(centre,_WorldSpaceCameraPos)-0.35*size)/(0.5*size+1));
                o.color=v.color; o.uv=v.uv; o.blend=v.tc1.x; return o;
            }
            // Squared direction components weight the six lightmaps.
            float SixWay(float3 s,float4 a,float4 b)
            {
                float3 w=s*s;
                return (s.x>0?a.r:a.g)*w.x+(s.y>0?a.b:b.r)*w.y+(s.z>0?b.g:b.b)*w.z;
            }
            float3 Incandescence(float t)
            {
                float3 c=lerp(float3(0.35,0.03,0),float3(1,0.32,0.03),saturate(t*2.2));
                c=lerp(c,float3(1,0.62,0.18),saturate(t*2-0.8));
                return lerp(c,float3(1,0.9,0.68),saturate(t*2.5-1.6));
            }
            float4 frag(v2f i):SV_Target
            {
                float4 a=lerp(tex2D(_SmokeA,i.uv.xy),tex2D(_SmokeA,i.uv.zw),i.blend);
                float4 b=lerp(tex2D(_SmokeB,i.uv.xy),tex2D(_SmokeB,i.uv.zw),i.blend);
                float alpha=saturate(pow(a.a,1.45)*i.color.a*_Density*1.12)*i.fade;
                if(alpha<0.002) discard;
                float3 sun=float3(1,0.96,0.9)*_VefxSun.w*SixWay(i.sun,a,b)*2.5;
                float3 sky=_VefxSky.rgb*(a.b*0.7+(a.r+a.g+b.g+b.b)*0.12)*2.5+_VefxAmbient.rgb*(a.b+b.r)*0.6;
                float3 fire=float3(1,0.55,0.22)*i.fire.w*SixWay(i.fire.xyz,a,b)*2.5;
                float bright=dot(i.color.rgb,float3(0.3,0.59,0.11));
                float around=(a.r+a.g+a.b+b.r+b.g+b.b)*(1.0/6);
                float3 fill=(float3(1,0.96,0.9)*_VefxSun.w*0.9+_VefxSky.rgb*1.5)*around*bright*bright;
                float3 lit=(sun+sky+fire+fill)*i.color.rgb;
                float e=b.a*_Emission;
                float3 glow=Incandescence(pow(saturate(e),0.4))*e*1.6;
                float3 color=lit*(1-saturate(e*1.5))+glow;
                return float4(color*alpha,alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
