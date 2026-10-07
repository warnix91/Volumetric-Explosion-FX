Shader "VolumetricExplosionFX/Particle"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST;
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=TRANSFORM_TEX(v.uv,_MainTex); return o;
            }
            float4 frag(v2f i):SV_Target { return tex2D(_MainTex,i.uv)*i.color; }
            ENDCG
        }
    }
    Fallback Off
}
