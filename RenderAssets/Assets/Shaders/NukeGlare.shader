Shader "VolumetricExplosionFX/NukeGlare"
{
    // 1.1: the glare of a nuclear fireball as a camera sees it (far brighter than the sun): at the flash a
    // blinding star with diffraction rays, then a soft bloom around the white-hot ball, anchored on the fireball in the
    // world. Its size stays true to the distance (never wider than the view from close by: the old one was pulled up to
    // the lens and followed the camera), and it is hidden when the land stands between the eye and the fireball (the
    // depth there, read around its centre).
    Properties
    {
        _Glare ("Size (m), flash, bloom, warmth", Vector) = (100,1,0,1)
        _Halo ("Halo radius (m), strength, temperature", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+40" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _Glare, _Halo;
            // uv: offset from the centre in metres (the quad holds the glare and the halo of lit air round the cloud).
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float seen:TEXCOORD1; float size:TEXCOORD2; };
            // How much of the fireball's centre the eye sees: the scene's depth around its pixel against its own.
            float Seen(float3 centre,float dist)
            {
                float4 clip=mul(UNITY_MATRIX_VP,float4(centre,1));
                if(clip.w<=0) return 0;
                float4 sp=ComputeScreenPos(clip); float2 c=sp.xy/sp.w;
                if(any(c<0)||any(c>1)) return 1;   // off screen: nothing to read, keep it
                float seen=0;
                [unroll] for(int k=0;k<5;k++)
                {
                    float2 o=k==0?float2(0,0):float2(k==1?1:k==2?-1:0,k==3?1:k==4?-1:0)*0.006;
                    float eye=LinearEyeDepth(tex2Dlod(_CameraDepthTexture,float4(c+o,0,0)).r);
                    seen+=saturate((eye-dist*0.97)/(0.04*dist+1));
                }
                return seen/5;
            }
            v2f vert(float4 vertex:POSITION)
            {
                v2f o;
                float3 centre=mul(unity_ObjectToWorld,float4(0,0,0,1)).xyz;
                float3 view=mul(UNITY_MATRIX_V,float4(centre,1)).xyz;
                float dist=max(-view.z,0.5);
                float size=min(max(_Glare.x,_Halo.y>0.001?_Halo.x:0),0.6*dist);
                view.xy+=vertex.xy*size;
                o.pos=mul(UNITY_MATRIX_P,float4(view,1));
                o.uv=vertex.xy*size; o.size=size;
                o.seen=-view.z>0.5?Seen(centre,dist):0;
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float3 h=0;
                // The air round the glowing cloud lit by it: a wide orange glow (at night above all).
                if(_Halo.y>0.001)
                {
                    float rh=length(i.uv)/max(min(_Halo.x,i.size),1e-3);
                    float3 hc=lerp(float3(1,0.3,0.07),float3(1,0.62,0.3),saturate(_Halo.z));
                    h=hc*_Halo.y*0.45*exp(-rh*3.2)*saturate(1-rh)*lerp(0.5,1,i.seen);
                }
                float2 ug=i.uv/max(min(_Glare.x,i.size),1e-3);
                float r2=dot(ug,ug), r=sqrt(r2);
                if(r>1||i.seen<0.001) return float4(h,0);
                float flash=_Glare.y, bloom=_Glare.z;
                float3 warm=lerp(float3(1,0.6,0.28),float3(1,0.97,0.92),saturate(_Glare.w));
                // The flash: a blinding core, a wide halo and six faint diffraction rays.
                float a=atan2(ug.y,ug.x);
                float rays=pow(abs(cos(3*a)),60)*exp(-r*4)*0.6;
                float3 c=float3(1,0.98,0.95)*exp(-r2*60)*2*flash+warm*(exp(-r*7)*1.5+exp(-r*3.2)*(1-r)*0.6+rays)*flash;
                // The bloom of the white-hot ball: a soft glow spilling round it.
                c+=warm*(exp(-r*4.5)*(1-r))*bloom*0.55;
                return float4(c*i.seen+h,0);
            }
            ENDCG
        }
    }
    Fallback Off
}
