Shader "VolumetricExplosionFX/CloudProbe"
{
    // 1.1: grabs the image just before the overlay queue, where EVE lays its clouds over the scene. LowResVolumes
    // compares it with the image after the transparent pass to know where clouds were drawn (comparing with the image
    // before the whole transparent pass also caught the atmosphere's haze and our own glare: the clouds faded over the
    // land). It draws nothing.
    SubShader
    {
        Tags { "Queue"="Overlay-1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_VefxPreClouds" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            float4 vert(float4 vertex:POSITION):SV_POSITION { return float4(0,0,-2,1); }   // outside the view: nothing is drawn
            float4 frag():SV_Target { return 0; }
            ENDCG
        }
    }
    Fallback Off
}
