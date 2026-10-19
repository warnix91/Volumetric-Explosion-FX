Shader "VolumetricExplosionFX/Composite"
{
    // Lays the reduced-resolution volume image (premultiplied colour, alpha) over the scene.
    Properties { _MainTex ("Reduced-resolution volumes", 2D) = "black" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _VefxCompositeSpread;   // tap offset in texels (0.6 reduced resolution, wider at full resolution)
            sampler2D _VefxOverlay, _VefxOverlayZ; float _VefxOverlayOn;   // 1.1: engine flames in front of the clouds, drawn again over them
            // 1.1: hidden behind another mod's clouds (LowResVolumes).
            sampler2D _VefxLowResFront, _VefxPreClouds, _VefxAfterAlpha; float4 _VefxLowResFront_TexelSize;
            float _VefxCloudOcc; float4 _VefxCloudBands, _VefxPlanet, _VefxEye; float4x4 _VefxFrustum;
            // 1 where the whole layer lies between the eye and the point (eye above its top and point under its base, or the
            // reverse): any cloud on that pixel is then in front. (where a cloud could also be behind, nothing is
            // hidden: a layer only overlapping the way hid explosions in front of clouds behind them.)
            float Between(float ha,float hb,float2 band)
            {
                if(band.y<=band.x) return 0;
                float up=saturate((ha-band.y)/150)*saturate((band.x-hb)/150), down=saturate((band.x-ha)/150)*saturate((hb-band.y)/150);
                return max(up,down);
            }
            float CloudCover(float2 uv)
            {
                // (the thin fringe has no front of its own: the nearest of its neighbours' is taken, or it showed as an outline)
                float4 f=tex2D(_VefxLowResFront,uv);
                float2 o=_VefxLowResFront_TexelSize.xy*2;
                float4 g=tex2D(_VefxLowResFront,uv+o); if(g.a>f.a) f=g;
                g=tex2D(_VefxLowResFront,uv-o); if(g.a>f.a) f=g;
                g=tex2D(_VefxLowResFront,uv+float2(o.x,-o.y)); if(g.a>f.a) f=g;
                g=tex2D(_VefxLowResFront,uv+float2(-o.x,o.y)); if(g.a>f.a) f=g;
                if(f.a<0.05) return 0;
                float eye=f.r/f.a;
                float3 ray=lerp(lerp(_VefxFrustum[0].xyz,_VefxFrustum[3].xyz,uv.x),lerp(_VefxFrustum[1].xyz,_VefxFrustum[2].xyz,uv.x),uv.y);
                float3 p=_VefxEye.xyz+ray*eye;
                float ha=length(_VefxEye.xyz-_VefxPlanet.xyz)-_VefxPlanet.w, hb=length(p-_VefxPlanet.xyz)-_VefxPlanet.w;
                float between=max(Between(ha,hb,_VefxCloudBands.xy),Between(ha,hb,_VefxCloudBands.zw));
                if(between<=0) return 0;
                // How much the clouds covered this pixel (the overlay queue's change: a cloud over the land or the sky
                // changes it a lot).
                // (averaged over a few pixels and only a clear change: a faint, square particle of another mod's
                // clouds cut a dark rectangle out of the explosion)
                float2 s=_VefxLowResFront_TexelSize.xy*3;
                float diff=0;
                [unroll] for(int k=0;k<5;k++)
                {
                    float2 o2=k==0?float2(0,0):k==1?float2(s.x,s.y):k==2?float2(-s.x,s.y):k==3?float2(s.x,-s.y):float2(-s.x,-s.y);
                    diff+=length(tex2D(_VefxAfterAlpha,uv+o2).rgb-tex2D(_VefxPreClouds,uv+o2).rgb);
                }
                return between*smoothstep(0.1,0.45,diff/5);
            }
            float4 Volumes(v2f_img i);
            float4 frag(v2f_img i):SV_Target
            {
                float4 c=Volumes(i);
                if(_VefxCloudOcc>0.5) c*=1-0.97*CloudCover(i.uv);
                // What the cloud hid of a flame in front of it comes back (the flame was drawn into the image before), where
                // the flame is nearer than the cloud's front.
                if(_VefxOverlayOn>0.5)
                {
                    float oz=tex2D(_VefxOverlayZ,i.uv).r;
                    float4 f=tex2D(_VefxLowResFront,i.uv);
                    float front=f.a>0.05?f.r/f.a:1e7;
                    c.rgb+=tex2D(_VefxOverlay,i.uv).rgb*c.a*saturate((front-oz)/(0.03*front+2)+0.5);
                }
                return c;
            }
            // Centre plus four diagonal bilinear taps: hides the coarser ray-march dither at reduced resolution.
            float4 Volumes(v2f_img i)
            {
                // Full resolution (1.1): a small Gaussian over 3x3 bilinear taps (about 5x5 pixels). Four diagonal taps
                // alone left a regular cross-hatch, like a halftone screen, over the blue-noise dither.
                if(_VefxCompositeSpread>1)
                {
                    float2 g=_MainTex_TexelSize.xy*_VefxCompositeSpread;
                    float4 c=tex2D(_MainTex,i.uv)*0.25;
                    c+=(tex2D(_MainTex,i.uv+float2(g.x,0))+tex2D(_MainTex,i.uv-float2(g.x,0))+
                        tex2D(_MainTex,i.uv+float2(0,g.y))+tex2D(_MainTex,i.uv-float2(0,g.y)))*0.125;
                    c+=(tex2D(_MainTex,i.uv+g)+tex2D(_MainTex,i.uv-g)+
                        tex2D(_MainTex,i.uv+float2(g.x,-g.y))+tex2D(_MainTex,i.uv+float2(-g.x,g.y)))*0.0625;
                    return c;
                }
                float2 o=_MainTex_TexelSize.xy*(_VefxCompositeSpread>0?_VefxCompositeSpread:0.6);
                float4 c=tex2D(_MainTex,i.uv)*0.4;
                c+=(tex2D(_MainTex,i.uv+float2(o.x,o.y))+tex2D(_MainTex,i.uv+float2(-o.x,o.y))+
                    tex2D(_MainTex,i.uv+float2(o.x,-o.y))+tex2D(_MainTex,i.uv+float2(-o.x,-o.y)))*0.15;
                return c;
            }
            ENDCG
        }
        Pass
        {
            // 1.1: the cloud's front into the depth buffer, where it is nearly opaque (drawn before the transparent pass).
            ZTest LEqual ZWrite On Cull Off ColorMask 0
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            sampler2D _VefxLowResFront;
            float4 frag(v2f_img i,out float depth:SV_Depth):SV_Target
            {
                float4 f=tex2D(_VefxLowResFront,i.uv);
                clip(f.a-0.6);
                float eye=max(f.r/max(f.a,1e-4),0.05);
                depth=(1/eye-_ZBufferParams.w)/_ZBufferParams.z;   // linear eye depth back to the depth buffer's value
                return 0;
            }
            ENDCG
        }
        Pass
        {
            // 1.1: the eye depth of the flames and trails drawn again over the clouds, nearest kept (drawn with
            // each overlay's own mesh). Pass 0 shows an overlay only where it is nearer than the cloud's front. (Their own
            // depth test did not hold: the flames and contrails behind a nuclear cloud showed through it, in the game and at
            // the bench.)
            ZTest Always ZWrite Off Cull Off
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            struct v2f { float4 pos:SV_POSITION; float eye:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.pos=UnityObjectToClipPos(vertex); o.eye=-UnityObjectToViewPos(vertex.xyz).z; return o;
            }
            float4 frag(v2f i):SV_Target { return float4(i.eye,0,0,0); }
            ENDCG
        }
        Pass
        {
            // The same, over each flame's bounding box at the depth of its centre: a flame whose shader moves its
            // vertices (PlumeDynamics bends Waterfall's flames on the GPU, from a mesh laid out for it) is not where its mesh
            // is, and its pixels stayed "behind" every cloud. Nearest kept with pass 2.
            ZTest Always ZWrite Off Cull Off
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            float _VefxOverlayCentre;   // eye depth of the flame's centre
            float4 vert(float4 vertex:POSITION):SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 frag():SV_Target { return float4(_VefxOverlayCentre,0,0,0); }
            ENDCG
        }
    }
    Fallback Off
}
