using UnityEngine;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    // 1.1: hands a water splash's state (WaterSplash) to SplashVolume.shader; the game and the bench share it.
    internal static class SplashProperties
    {
        static readonly int CrownId=Shader.PropertyToID("_SpCrown"), SheetId=Shader.PropertyToID("_SpSheet"), JetId=Shader.PropertyToID("_SpJet"),
            SprayId=Shader.PropertyToID("_SpSpray"), HazeId=Shader.PropertyToID("_SpHaze"), SurgeId=Shader.PropertyToID("_SpSurge"),
            FoamId=Shader.PropertyToID("_SpFoam"), DomeId=Shader.PropertyToID("_SpDome"), DriftId=Shader.PropertyToID("_SpDrift"),
            WindId=Shader.PropertyToID("_SpWind"), BoxId=Shader.PropertyToID("_SpBox"), GlowId=Shader.PropertyToID("_SpGlow"),
            GlowAtId=Shader.PropertyToID("_SpGlowAt"), LightId=Shader.PropertyToID("_LightDirection"), SunColorId=Shader.PropertyToID("_SunColor"),
            ContextId=Shader.PropertyToID("_Context");
        // The proxy cube (-1..1) round the splash: its uniform scale and its centre's height above the sea (m).
        internal static void Proxy(SplashFrame f,out float scale,out float centre)
        {
            centre=(float)f.Top*0.5f;
            scale=Mathf.Max((float)f.Reach,centre)*1.02f;
        }
        // light: the sun's direction in the splash frame (y up); glow: the blast's fire light (colour x strength, range in
        // w) and where it is (m, splash frame); detail: the frame governor's (coarser steps when frames run long).
        internal static void Apply(MaterialPropertyBlock b,SplashFrame f,Vector3 light,float ambient,float sun,Vector4 glow,Vector3 glowAt,
            float detail,float atmosphere)
        {
            float scale,centre; Proxy(f,out scale,out centre);
            b.SetVector(CrownId,V(f.Crown)); b.SetVector(SheetId,V(f.Sheet)); b.SetVector(JetId,V(f.Jet));
            b.SetVector(SprayId,V(f.Spray)); b.SetVector(HazeId,V(f.Haze)); b.SetVector(SurgeId,V(f.Surge));
            b.SetVector(FoamId,V(f.Foam)); b.SetVector(DomeId,V(f.Dome)); b.SetVector(DriftId,V(f.Drift)); b.SetVector(WindId,V(f.Wind));
            b.SetVector(BoxId,new Vector4((float)f.Reach,(float)f.Top,scale,centre));
            b.SetVector(GlowId,glow); b.SetVector(GlowAtId,new Vector4(glowAt.x,glowAt.y,glowAt.z,detail));
            b.SetVector(LightId,new Vector4(light.x,light.y,light.z,ambient));
            b.SetColor(SunColorId,new Color(1,0.96f,0.9f,sun));
            b.SetVector(ContextId,new Vector4(2,atmosphere,(float)f.Wind.W,0));
        }
        static Vector4 V(Vec4 v) { return new Vector4((float)v.X,(float)v.Y,(float)v.Z,(float)v.W); }
    }
}
