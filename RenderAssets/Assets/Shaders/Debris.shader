Shader "VolumetricExplosionFX/Debris"
{
    Properties
    {
        _Glow ("Incandescence strength", Float) = 2.2
    }
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
            #include "UnityCG.cginc"
            float4 _VefxSun;      // world direction toward the star, strength (0 at night)
            float4 _VefxAmbient;  // ambient colour
            float _Glow;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float3 normal:TEXCOORD0; float4 color:COLOR; };
            v2f vert(appdata v)
            {
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex);
                o.normal=UnityObjectToWorldNormal(v.normal); o.color=v.color; return o;
            }
            float4 frag(v2f i,float facing:VFACE):SV_Target
            {
                float3 n=normalize(i.normal)*(facing>0?1:-1);
                float3 L=normalize(_VefxSun.xyz+float3(0,1e-5,0));
                float diffuse=saturate(dot(n,L)*0.75+0.25);
                float3 lit=i.color.rgb*(_VefxAmbient.rgb+float3(1,0.96,0.9)*_VefxSun.w*diffuse);
                float h=saturate(i.color.a);
                float3 heat=lerp(float3(0.9,0.18,0.03),float3(1,0.75,0.4),h)*h*h*_Glow;
                return float4(lit*(1-h*0.7)+heat,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
