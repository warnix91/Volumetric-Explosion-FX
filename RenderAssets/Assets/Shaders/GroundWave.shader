Shader "VolumetricExplosionFX/GroundWave"
{
    // The dust a blast wave lifts as it runs along the ground (original, 1.1). A disk draped on the terrain: where the front
    // has passed, the ground's dust rises as a sheet that billows up and thins over seconds (less of it far out, where the
    // blast is weaker); at the front itself a low crest. Lit by the sun and the sky.
    Properties
    {
        _NoiseTex ("Original tileable noise", 3D) = "" {}
        _Wave ("Front (share of the reach), crest height (m), opacity, crest width (share)", Vector) = (0.3,4,0.8,0.06)
        _Trail ("Front speed (m/s), hang time (s), billowing (m/s), full-lift radius (m)", Vector) = (340,4,1.5,30)
        _Ground ("Reach (m), seed, age (s), nuclear (1)", Vector) = (100,13,1,0)
        _Dust ("Dust colour, unused", Vector) = (0.62,0.56,0.46,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore metal
            #include "UnityCG.cginc"
            sampler3D _NoiseTex;
            float4 _Wave, _Trail, _Ground, _Dust, _VefxSun, _VefxAmbient;
            struct v2f { float4 pos:SV_POSITION; float4 polar:TEXCOORD0; float3 local:TEXCOORD1; };
            // Share of the full lift at distance r (m): the overpressure falls off about as 1/r, the dust ends raggedly.
            float Amount(float r,float u,float lump,float rays)
            {
                return saturate(pow(_Trail.w/max(r,1),1.2))*(1-smoothstep(0.45,0.9,u+0.18*(lump-0.5)))*(0.35+0.85*rays);
            }
            v2f vert(float4 vertex:POSITION,float2 uv:TEXCOORD0)
            {
                v2f o;
                float reach=max(_Ground.x,1), r=length(vertex.xz), front=_Wave.x*reach;
                float2 ring=vertex.xz/max(r,1e-3);
                float lump=tex3Dlod(_NoiseTex,float4(ring*0.9+_Ground.y*0.031,r/reach*0.6+_Ground.y*0.017,0)).r;
                // Only the slow sheet left behind lifts the mesh (smooth at any mesh size); the sharp crest is drawn per pixel.
                float behind=max(front-r,0), since=behind/max(_Trail.x,1);
                float sheet=behind>0&&_Ground.w>0.5?Amount(r,r/reach,lump,0.6)*exp(-since/max(_Trail.y,0.1))*smoothstep(0,_Wave.w*reach*2,behind):0;
                float4 p=vertex; p.y+=(_Wave.y*0.35+_Trail.z*since)*(0.5+0.8*lump)*saturate(sheet*3);
                o.pos=UnityObjectToClipPos(p);
                o.polar=float4(r,atan2(vertex.z,vertex.x),uv.x,0);
                o.local=p.xyz;
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float reach=max(_Ground.x,1), r=i.polar.x, u=r/reach, front=_Wave.x*reach;
                float2 ring=i.local.xz/max(r,1e-3);
                float lump=tex3D(_NoiseTex,float3(ring*0.9+_Ground.y*0.031,u*0.6+_Ground.y*0.017)).r;
                // Patches: more dust in some places than others. (no fine radial streaks: the white starburst they
                // left round a blast read as painted on the ground, "les marquages d'onde au sol")
                float2 xz=i.local.xz/reach;
                float rays=smoothstep(0.25,0.75,tex3D(_NoiseTex,float3(xz*1.6+_Ground.y*0.047,_Ground.y*0.023)).a*0.7+
                    tex3D(_NoiseTex,float3(xz*3.5+_Ground.y*0.029+0.4,_Ground.y*0.011+0.5)).r*0.3);
                float amount=Amount(r,u,lump,_Ground.w>0.5?0.55+0.45*rays:rays);   // (a nuke's dust: hardly any rays)
                // The front is not a perfect circle: it frays where the ground slows it.
                float fray=(tex3D(_NoiseTex,float3(ring*3.1+_Ground.y*0.019,_Ground.z*0.05)).b-0.5)*_Wave.w*reach*0.8;
                float d=(r-front-fray)/max(_Wave.w*reach,0.4);
                // A thin skin of dust kicked up right at the front (it appears with the front, nowhere ahead of it)...
                float crest=(d>0?exp(-d*d*6):exp(-d*d*(_Ground.w>0.5?0.35:1.6)))*amount;
                // ...and the haze it leaves behind, rising and thinning over seconds.
                bool nuke=_Ground.w>0.5;
                float behind=max(front-r,0), since=behind/max(_Trail.x,1);
                // An ordinary blast leaves nothing behind its running ring (a sheet of dust, even brief, read as an
                // ugly stain round the blast, "une sorte de tache moche"); a nuclear one its wall of dust.
                float sheet=nuke?amount*exp(-since/max(_Trail.y,0.1))*smoothstep(0,_Wave.w*reach*2,behind):0;
                // A nuclear blast's dust: big rolling billows (the disc spans kilometres), a sharp wall at the front, the
                // haze behind it thicker near ground zero and thinning outward.
                float3 q=i.local/reach*(nuke?3.5:4.5)+_Ground.y*0.013+float3(0.02,-0.05,0.01)*_Ground.z;
                float mottle=tex3D(_NoiseTex,q).a*0.6+tex3D(_NoiseTex,q*2.1+0.3).r*0.4;   // (soft: fine cells read as grain)
                if(nuke) { sheet*=0.35+0.65*exp(-u*2.5); crest*=1.6; }
                float density=crest*(0.5+1.1*mottle)+sheet*smoothstep(0.32,0.78,mottle)*(nuke?1.5:0.9);
                float edge=smoothstep(0.0,0.05,u)*(1-smoothstep(0.85,1,u));
                // Dust, not paint: soft and fairly thin, so the ground shows through.
                float alpha=_Wave.z*0.55*(1-exp(-2*density))*edge;
                clip(alpha-0.003);
                float3 sun=normalize(mul((float3x3)unity_WorldToObject,_VefxSun.xyz));
                float lit=saturate(sun.y*0.5+0.5)*(0.85+0.3*mottle);
                float3 ambient=_VefxAmbient.w>0?_VefxAmbient.rgb:float3(0.35,0.37,0.42);
                // Airborne dust scatters light: paler and hazier than the ground it came from (same colour would vanish into it).
                float3 dust=nuke?_Dust.rgb*0.95:lerp(_Dust.rgb*1.08,float3(0.74,0.72,0.68),0.2);   // (a nuke's: the soil's own brown)
                // Over grass the dust is the earth's, not green, and not chalk white.
                float grassy=saturate((_Dust.g-max(_Dust.r,_Dust.b))*14);
                dust=lerp(dust,float3(0.5,0.44,0.35),grassy*0.8);
                float3 c=dust*(ambient*1.6+float3(1,0.97,0.92)*saturate(_VefxSun.w)*lit);
                return float4(c,alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
