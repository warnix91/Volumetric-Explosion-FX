using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class VolumeAssets : IDisposable
    {
        internal Material Material, FlareMaterial, DistortMaterial;
        internal Material SmokeFire, SmokeCloud, Scorch, TrailMaterial, TrailGlow, Flame;
        internal Material FireMaterial;
        internal Shader Fragment;
        internal Mesh Cube, Quad;
        internal Shader Particle, Debris;
        Shader ownedShader, ownedFlare, ownedDistort, ownedSmoke, ownedScorch, ownedTrail, ownedFlame, ownedFire;
        Texture2D smokeA, smokeB;
        Texture3D noise;
        Texture2D blueNoise;
        internal Texture3D Noise { get { return noise; } }
        internal string Status="disabled";
        internal VolumeAssets(bool volumetric)
        {
            GraphicsDeviceType api=SystemInfo.graphicsDeviceType;
            if(api!=GraphicsDeviceType.Direct3D11 && api!=GraphicsDeviceType.OpenGLCore && api!=GraphicsDeviceType.Metal)
            { Status="unsupported API; particles";return; }
            string platform=Application.platform==RuntimePlatform.WindowsPlayer?"windows":
                Application.platform==RuntimePlatform.OSXPlayer?"mac":null;
            if(platform==null) { Status="unsupported platform; particles";return; }
            string path=Path.Combine(KSPUtil.ApplicationRootPath,"GameData/VolumetricExplosionFX/Assets/vefx-"+platform+".unity3d");
            if(!File.Exists(path)) { Status="missing "+platform+" bundle; particles";return; }
            AssetBundle bundle=null;
            try
            {
                bundle=AssetBundle.LoadFromFile(path);
                if(bundle==null) { Status="bundle load failed; particles";return; }
                Particle=Supported(bundle,"Assets/Shaders/Particle.shader");
                Debris=Supported(bundle,"Assets/Shaders/Debris.shader");
                noise=bundle.LoadAsset<Texture3D>("Assets/Noise/VefxNoise3D.asset");
                LoadResidue(bundle);
                string extras=(SmokeFire!=null?" + lit smoke":"")+(Fragment!=null?" + part fragments":"")+(Scorch!=null?" + scorch":"")+(TrailMaterial!=null?" + smoke trails":"")+(Flame!=null?" + flames":"");
                if(!volumetric) { Status="volumes disabled; particles"+(Particle!=null?" (original sprites)":"")+extras;return; }
                Shader shader=bundle.LoadAsset<Shader>("Assets/Shaders/HeroVolume.shader");
                if(shader==null||!shader.isSupported) { Status="unsupported volume shader; particles"+extras;return; }
                if(noise==null) { Status="noise texture missing; particles"+extras;return; }
                Material=new Material(shader);
                Material.SetTexture("_NoiseTex",noise);
                blueNoise=bundle.LoadAsset<Texture2D>("Assets/Noise/VefxBlueNoise.asset");
                if(blueNoise!=null) Material.SetTexture("_BlueNoise",blueNoise);
                ownedShader=shader;
                Shader flare=Supported(bundle,"Assets/Shaders/HeroFlare.shader");
                if(flare!=null) { FlareMaterial=new Material(flare); ownedFlare=flare; }
                Shader distort=Supported(bundle,"Assets/Shaders/HeatDistortion.shader");
                if(distort!=null) { DistortMaterial=new Material(distort); ownedDistort=distort; }
                Shader fire=Supported(bundle,"Assets/Shaders/FireVolume.shader");
                if(fire!=null)
                {
                    ownedFire=fire; FireMaterial=new Material(fire); FireMaterial.SetTexture("_NoiseTex",noise);
                    if(blueNoise!=null) FireMaterial.SetTexture("_BlueNoise",blueNoise);
                }
                if(FlareMaterial!=null||DistortMaterial!=null)
                {
                    Quad=new Mesh(); Quad.name="Original camera-facing billboard";
                    Quad.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
                    Quad.triangles=new[]{0,1,2,0,2,3};
                    Quad.bounds=new Bounds(Vector3.zero,Vector3.one*8);
                }
                Cube=new Mesh(); Cube.name="Original volume bounds";
                Cube.vertices=new[]{new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),
                    new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)};
                Cube.triangles=new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,0,1,5,0,5,4,3,7,6,3,6,2};
                Cube.RecalculateBounds();
                Status="shock dome / "+platform+" / "+api+(FlareMaterial!=null?" + glare":" (no glare)")+(DistortMaterial!=null?" + refraction":" (no refraction)")+(FireMaterial!=null?" + 3D fires":"")+
                    (Particle!=null?" + original sprites":"")+extras+" (runtime visual test required)";
            }
            catch(Exception ex)
            {
                Dispose();Material=null;Cube=null;FlareMaterial=null;DistortMaterial=null;FireMaterial=null;Quad=null;Particle=null;Debris=null;
                SmokeFire=null;SmokeCloud=null;Scorch=null;TrailMaterial=null;TrailGlow=null;Flame=null;Fragment=null;blueNoise=null;noise=null;smokeA=null;smokeB=null;Status="bundle load failed; particles";
                Debug.LogWarning("[VEFX] Volume fallback: "+ex.GetType().Name);
            }
            finally { if(bundle!=null) bundle.Unload(!InUse); }
            if(Material==null&&Fragment==null&&Scorch==null&&TrailMaterial==null&&Flame==null&&noise!=null) { UnityEngine.Object.Destroy(noise); noise=null; }
        }
        bool InUse { get { return Material!=null||Particle!=null||Debris!=null||SmokeFire!=null||Fragment!=null||Scorch!=null||TrailMaterial!=null||Flame!=null; } }
        static Shader Supported(AssetBundle bundle,string asset)
        {
            Shader shader=bundle.LoadAsset<Shader>(asset);
            return shader!=null&&shader.isSupported?shader:null;
        }
        void LoadResidue(AssetBundle bundle)
        {
            Shader smoke=Supported(bundle,"Assets/Shaders/Smoke.shader");
            Texture2D a=bundle.LoadAsset<Texture2D>("Assets/Smoke/VefxSmokeA.asset"), b=bundle.LoadAsset<Texture2D>("Assets/Smoke/VefxSmokeB.asset");
            if(smoke!=null&&a!=null&&b!=null)
            {
                ownedSmoke=smoke; smokeA=a; smokeB=b;
                SmokeFire=SmokeMaterial(smoke,1.3f); SmokeCloud=SmokeMaterial(smoke,0);
            }
            if(noise==null) return;
            Shader fragment=Supported(bundle,"Assets/Shaders/PartFragment.shader");
            if(fragment!=null) Fragment=fragment;
            Shader scorch=Supported(bundle,"Assets/Shaders/Scorch.shader");
            if(scorch!=null) { ownedScorch=scorch; Scorch=new Material(scorch); Scorch.SetTexture("_NoiseTex",noise); }
            Shader trail=Supported(bundle,"Assets/Shaders/Trail.shader");
            if(trail!=null)
            {
                ownedTrail=trail; TrailMaterial=new Material(trail); TrailMaterial.SetTexture("_NoiseTex",noise);
                TrailGlow=new Material(trail); TrailGlow.SetTexture("_NoiseTex",noise); TrailGlow.SetFloat("_Glow",1);
            }
            Shader flame=Supported(bundle,"Assets/Shaders/Flame.shader");
            if(flame!=null) { ownedFlame=flame; Flame=new Material(flame); Flame.SetTexture("_NoiseTex",noise); }
        }
        Material SmokeMaterial(Shader shader,float emission)
        {
            var m=new Material(shader); m.SetTexture("_SmokeA",smokeA); m.SetTexture("_SmokeB",smokeB); m.SetFloat("_Emission",emission);
            return m;
        }
        public void Dispose()
        {
            if(Material!=null) UnityEngine.Object.Destroy(Material); if(Cube!=null) UnityEngine.Object.Destroy(Cube);
            if(FlareMaterial!=null) UnityEngine.Object.Destroy(FlareMaterial); if(Quad!=null) UnityEngine.Object.Destroy(Quad);
            if(DistortMaterial!=null) UnityEngine.Object.Destroy(DistortMaterial); if(ownedDistort!=null) UnityEngine.Object.Destroy(ownedDistort);
            if(ownedShader!=null) UnityEngine.Object.Destroy(ownedShader); if(ownedFlare!=null) UnityEngine.Object.Destroy(ownedFlare);
            if(SmokeFire!=null) UnityEngine.Object.Destroy(SmokeFire); if(SmokeCloud!=null) UnityEngine.Object.Destroy(SmokeCloud);
            if(Scorch!=null) UnityEngine.Object.Destroy(Scorch); if(ownedScorch!=null) UnityEngine.Object.Destroy(ownedScorch);
            if(TrailMaterial!=null) UnityEngine.Object.Destroy(TrailMaterial); if(TrailGlow!=null) UnityEngine.Object.Destroy(TrailGlow);
            if(ownedTrail!=null) UnityEngine.Object.Destroy(ownedTrail);
            if(Flame!=null) UnityEngine.Object.Destroy(Flame); if(ownedFlame!=null) UnityEngine.Object.Destroy(ownedFlame);
            if(FireMaterial!=null) UnityEngine.Object.Destroy(FireMaterial); if(ownedFire!=null) UnityEngine.Object.Destroy(ownedFire);
            if(ownedSmoke!=null) UnityEngine.Object.Destroy(ownedSmoke); if(Fragment!=null) UnityEngine.Object.Destroy(Fragment);
            if(smokeA!=null) UnityEngine.Object.Destroy(smokeA); if(smokeB!=null) UnityEngine.Object.Destroy(smokeB);
            if(Particle!=null) UnityEngine.Object.Destroy(Particle);
            if(Debris!=null) UnityEngine.Object.Destroy(Debris);
            if(noise!=null) UnityEngine.Object.Destroy(noise);
            if(blueNoise!=null) UnityEngine.Object.Destroy(blueNoise);
        }
    }
}
