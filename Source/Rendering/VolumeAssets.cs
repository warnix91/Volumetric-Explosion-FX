using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class VolumeAssets : IDisposable
    {
        internal Material Material, FlareMaterial, DistortMaterial, CompositeMaterial;
        internal Material CloudProbeMaterial;   // 1.1: grabs the image before EVE's clouds (LowResVolumes)
        internal Material NukeShadow;           // 1.1: the nuclear cloud's shadow, laid from the scene's depth
        // The same volume shader compiled with VEFX_SIM: 1.1 fireballs from the baked simulation (null when not loaded).
        internal Material SimMaterial;   // 1.1 variant: simulated fireball (VEFX_SIM)
        internal Material SmokeFire, SmokeCloud, Scorch, TrailMaterial, TrailGlow, Flame, Shadow, GroundWave;
        internal Material FireMaterial;
        internal Material NukeMaterial;   // 1.1: the nuclear cloud's own renderer (NukeVolume)
        internal Material NukeGlareMaterial;   // 1.1: the glare of a nuclear fireball, anchored in the world (NukeGlare)
        internal Material SplashMaterial;   // 1.1: a water impact's splash as a volume (SplashVolume)
        internal Shader Fragment;
        internal Mesh Cube, Quad;
        internal Shader Particle, Debris;
        Shader ownedShader, ownedFlare, ownedDistort, ownedSmoke, ownedScorch, ownedShadow, ownedWave, ownedTrail, ownedFlame, ownedFire, ownedComposite, ownedNuke, ownedGlare, ownedProbe, ownedNukeShadow, ownedSplash;
        Texture2D smokeA, smokeB;
        Texture3D noise, simGround, simAir;
        Texture3D[] simRuns;   // the extra baked runs (ground 2-4, air 2), destroyed with the rest
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
                string extras=(SmokeFire!=null?" + lit smoke":"")+(Fragment!=null?" + part fragments":"")+(Scorch!=null?" + scorch":"")+(GroundWave!=null?" + ground wave":"")+(TrailMaterial!=null?" + smoke trails":"")+(Flame!=null?" + flames":"");
                if(!volumetric) { Warmup(); Status="volumes disabled; particles"+(Particle!=null?" (original sprites)":"")+extras;return; }
                Shader shader=bundle.LoadAsset<Shader>("Assets/Shaders/HeroVolume.shader");
                if(shader==null||!shader.isSupported) { Status="unsupported volume shader; particles"+extras;return; }
                if(noise==null) { Status="noise texture missing; particles"+extras;return; }
                Material=new Material(shader);
                Material.SetTexture("_NoiseTex",noise);
                var simGround=bundle.LoadAsset<Texture3D>("Assets/FireSim/FireSim_ground.asset");
                var simAir=bundle.LoadAsset<Texture3D>("Assets/FireSim/FireSim_air.asset");
                if(simGround!=null&&simAir!=null)
                {
                    Material.SetTexture("_FireSimGround",simGround); Material.SetTexture("_FireSimAir",simAir);
                    // More baked runs (variety): explosions draw them only when they loaded.
                    var simGround2=bundle.LoadAsset<Texture3D>("Assets/FireSim/FireSim_ground2.asset");
                    var simGround3=bundle.LoadAsset<Texture3D>("Assets/FireSim/FireSim_ground3.asset");
                    var simGround4=bundle.LoadAsset<Texture3D>("Assets/FireSim/FireSim_ground4.asset");
                    var simAir2=bundle.LoadAsset<Texture3D>("Assets/FireSim/FireSim_air2.asset");
                    var simNuke=bundle.LoadAsset<Texture3D>("Assets/FireSim/FireSim_nuke.asset");
                    var simNuke2=bundle.LoadAsset<Texture3D>("Assets/FireSim/FireSim_nuke2.asset");
                    var simNukeAir=bundle.LoadAsset<Texture3D>("Assets/FireSim/FireSim_nukeair.asset");
                    FireSim.NukeHave[0]=simNuke!=null; FireSim.NukeHave[1]=simNuke2!=null; FireSim.NukeHave[2]=simNukeAir!=null;
                    FireSim.GroundLoaded=simGround2==null?1:simGround3==null||simGround4==null?2:4;
                    FireSim.AirLoaded=simAir2==null?1:2;
                    BindRuns(Material,simGround2,simGround3,simGround4,simAir2,simNuke,simNuke2,simNukeAir);
                    simRuns=new[]{simGround2,simGround3,simGround4,simAir2,simNuke,simNuke2,simNukeAir};
                    if(Shadow!=null)
                    {
                        // The smoke shadow follows the sun's ray through the same simulated gas.
                        Shadow.SetTexture("_FireSimGround",simGround); Shadow.SetTexture("_FireSimAir",simAir);
                        BindRuns(Shadow,simGround2,simGround3,simGround4,simAir2,simNuke,simNuke2,simNukeAir);
                        Shadow.SetVector("_SimDims",new Vector4(FireSimTable.GroundNx,FireSimTable.GroundNy,FireSimTable.GroundNz,FireSimTable.GroundFrames));
                        Shadow.SetVector("_SimAirDims",new Vector4(FireSimTable.AirNx,FireSimTable.AirNy,FireSimTable.AirNz,FireSimTable.AirFrames));
                        Shadow.SetVector("_SimGeom",new Vector4(FireSimTable.GroundHalf,FireSimTable.GroundHeight,FireSimTable.AirHalf,FireSimTable.AirHeight));
                    }
                    Material.SetVector("_SimDims",new Vector4(FireSimTable.GroundNx,FireSimTable.GroundNy,FireSimTable.GroundNz,FireSimTable.GroundFrames));
                    Material.SetVector("_SimAirDims",new Vector4(FireSimTable.AirNx,FireSimTable.AirNy,FireSimTable.AirNz,FireSimTable.AirFrames));
                    Material.SetVector("_SimGeom",new Vector4(FireSimTable.GroundHalf,FireSimTable.GroundHeight,FireSimTable.AirHalf,FireSimTable.AirHeight));
                    this.simGround=simGround; this.simAir=simAir;
                    VolumeEvolution.UseSim=true;
                }
                blueNoise=bundle.LoadAsset<Texture2D>("Assets/Noise/VefxBlueNoise.asset");
                if(blueNoise!=null) Material.SetTexture("_BlueNoise",blueNoise);
                if(VolumeEvolution.UseSim) { SimMaterial=new Material(Material); SimMaterial.EnableKeyword("VEFX_SIM"); }
                ownedShader=shader;
                Shader flare=Supported(bundle,"Assets/Shaders/HeroFlare.shader");
                if(flare!=null) { FlareMaterial=new Material(flare); ownedFlare=flare; }
                Shader nukeGlare=Supported(bundle,"Assets/Shaders/NukeGlare.shader");
                if(nukeGlare!=null) { ownedGlare=nukeGlare; NukeGlareMaterial=new Material(nukeGlare); }
                Shader distort=Supported(bundle,"Assets/Shaders/HeatDistortion.shader");
                if(distort!=null) { DistortMaterial=new Material(distort); ownedDistort=distort; }
                Shader composite=Supported(bundle,"Assets/Shaders/Composite.shader");
                if(composite!=null) { CompositeMaterial=new Material(composite); ownedComposite=composite; }
                Shader probe=Supported(bundle,"Assets/Shaders/CloudProbe.shader");
                if(probe!=null) { CloudProbeMaterial=new Material(probe); ownedProbe=probe; }
                Shader nuke=Supported(bundle,"Assets/Shaders/NukeVolume.shader");
                if(nuke!=null)
                {
                    ownedNuke=nuke; NukeMaterial=new Material(nuke); NukeMaterial.SetTexture("_NoiseTex",noise);
                    if(blueNoise!=null) NukeMaterial.SetTexture("_BlueNoise",blueNoise);
                    // (It writes the cloud's opaque front like the simulated fireballs: LowResVolumes keys that on VEFX_SIM.)
                    NukeMaterial.EnableKeyword("VEFX_SIM");
                }
                Shader splash=Supported(bundle,"Assets/Shaders/SplashVolume.shader");
                if(splash!=null)
                {
                    ownedSplash=splash; SplashMaterial=new Material(splash); SplashMaterial.SetTexture("_NoiseTex",noise);
                    if(blueNoise!=null) SplashMaterial.SetTexture("_BlueNoise",blueNoise);
                    SplashMaterial.EnableKeyword("VEFX_SIM");   // (it writes its front too: LowResVolumes keys that on VEFX_SIM)
                }
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
                Warmup();
                Status="shock dome / "+platform+" / "+api+(FlareMaterial!=null?" + glare":" (no glare)")+(DistortMaterial!=null?" + refraction":" (no refraction)")+(FireMaterial!=null?" + 3D fires":"")+(VolumeEvolution.UseSim?" + simulated fireballs ("+FireSim.GroundLoaded+" ground, "+FireSim.AirLoaded+" air shapes)":"")+(NukeMaterial!=null?" + nuclear clouds":"")+(SplashMaterial!=null?" + water splashes":"")+
                    (Particle!=null?" + original sprites":"")+extras+" (runtime visual test required)";
            }
            catch(Exception ex)
            {
                Dispose();Material=null;SimMaterial=null;Cube=null;FlareMaterial=null;NukeGlareMaterial=null;DistortMaterial=null;CompositeMaterial=null;FireMaterial=null;NukeMaterial=null;SplashMaterial=null;Quad=null;Particle=null;Debris=null;
                SmokeFire=null;SmokeCloud=null;Scorch=null;TrailMaterial=null;TrailGlow=null;Flame=null;Fragment=null;blueNoise=null;noise=null;smokeA=null;smokeB=null;Status="bundle load failed; particles";
                Debug.LogWarning("[VEFX] Volume fallback: "+ex.GetType().Name);
            }
            finally { if(bundle!=null) bundle.Unload(!InUse); }
            if(Material==null&&Fragment==null&&Scorch==null&&TrailMaterial==null&&Flame==null&&noise!=null) { UnityEngine.Object.Destroy(noise); noise=null; }
        }
        bool InUse { get { return Material!=null||Particle!=null||Debris!=null||SmokeFire!=null||Fragment!=null||Scorch!=null||TrailMaterial!=null||Flame!=null; } }
        // Prepares every shader program at flight start (hidden by the scene load) instead of on the
        // first explosion, which otherwise stalls the frame while the driver compiles them.
        static void BindRuns(Material m,Texture3D ground2,Texture3D ground3,Texture3D ground4,Texture3D air2,Texture3D nuke,Texture3D nuke2,Texture3D nukeAir)
        {
            if(ground2!=null) m.SetTexture("_FireSimGround2",ground2); if(ground3!=null) m.SetTexture("_FireSimGround3",ground3);
            if(ground4!=null) m.SetTexture("_FireSimGround4",ground4); if(air2!=null) m.SetTexture("_FireSimAir2",air2);
            if(nuke2!=null) m.SetTexture("_FireSimNuke2",nuke2);
            if(nukeAir!=null) m.SetTexture("_FireSimNukeAir",nukeAir);
            if(nuke!=null||nuke2!=null||nukeAir!=null)
            {
                if(nuke!=null) m.SetTexture("_FireSimNuke",nuke);
                m.SetVector("_SimNukeDims",new Vector4(FireSimTable.NukeNx,FireSimTable.NukeNy,FireSimTable.NukeNz,FireSimTable.NukeFrames));
                m.SetVector("_SimNukeGeom",new Vector4(FireSimTable.NukeHalf,FireSimTable.NukeHeight,0,0));
            }
        }
        void Warmup()
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var variants=new ShaderVariantCollection();
            int added=0;
            foreach(Shader shader in new[]{ownedShader,ownedFlare,ownedDistort,ownedFire,ownedComposite,ownedSmoke,ownedScorch,ownedShadow,ownedWave,ownedTrail,ownedFlame,ownedNuke,ownedGlare,ownedSplash,Fragment,Particle,Debris})
            {
                if(shader==null) continue;
                foreach(PassType pass in new[]{PassType.Normal,PassType.ForwardBase,PassType.ShadowCaster})
                {
                    try { if(variants.Add(new ShaderVariantCollection.ShaderVariant(shader,pass))) added++; }
                    catch(ArgumentException) { } // the shader has no pass of this type
                    if(shader==ownedShader&&SimMaterial!=null)
                    {
                        try { if(variants.Add(new ShaderVariantCollection.ShaderVariant(shader,pass,"VEFX_SIM"))) added++; }
                        catch(ArgumentException) { }
                    }
                }
            }
            try { variants.WarmUp(); }
            catch(Exception ex) { Debug.LogWarning("[VEFX] Shader warm-up skipped: "+ex.GetType().Name); }
            finally { UnityEngine.Object.Destroy(variants); }
            Debug.Log("[VEFX] Shader warm-up: "+added+" programs in "+watch.ElapsedMilliseconds+" ms");
        }
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
            Shader nukeShadow=Supported(bundle,"Assets/Shaders/NukeShadow.shader");
            if(nukeShadow!=null) { ownedNukeShadow=nukeShadow; NukeShadow=new Material(nukeShadow); NukeShadow.SetTexture("_NoiseTex",noise); }
            Shader shadow=Supported(bundle,"Assets/Shaders/SmokeShadow.shader");
            if(shadow!=null) { ownedShadow=shadow; Shadow=new Material(shadow); Shadow.SetTexture("_NoiseTex",noise); }
            Shader wave=Supported(bundle,"Assets/Shaders/GroundWave.shader");
            if(wave!=null) { ownedWave=wave; GroundWave=new Material(wave); GroundWave.SetTexture("_NoiseTex",noise); }
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
            if(Material!=null) UnityEngine.Object.Destroy(Material); if(SimMaterial!=null) UnityEngine.Object.Destroy(SimMaterial); if(Cube!=null) UnityEngine.Object.Destroy(Cube);
            if(FlareMaterial!=null) UnityEngine.Object.Destroy(FlareMaterial); if(Quad!=null) UnityEngine.Object.Destroy(Quad);
            if(NukeGlareMaterial!=null) UnityEngine.Object.Destroy(NukeGlareMaterial); if(ownedGlare!=null) UnityEngine.Object.Destroy(ownedGlare);
            if(DistortMaterial!=null) UnityEngine.Object.Destroy(DistortMaterial); if(ownedDistort!=null) UnityEngine.Object.Destroy(ownedDistort);
            if(CompositeMaterial!=null) UnityEngine.Object.Destroy(CompositeMaterial); if(ownedComposite!=null) UnityEngine.Object.Destroy(ownedComposite);
            if(CloudProbeMaterial!=null) UnityEngine.Object.Destroy(CloudProbeMaterial); if(ownedProbe!=null) UnityEngine.Object.Destroy(ownedProbe);
            if(NukeShadow!=null) UnityEngine.Object.Destroy(NukeShadow); if(ownedNukeShadow!=null) UnityEngine.Object.Destroy(ownedNukeShadow);
            if(ownedShader!=null) UnityEngine.Object.Destroy(ownedShader); if(ownedFlare!=null) UnityEngine.Object.Destroy(ownedFlare);
            if(SmokeFire!=null) UnityEngine.Object.Destroy(SmokeFire); if(SmokeCloud!=null) UnityEngine.Object.Destroy(SmokeCloud);
            if(Scorch!=null) UnityEngine.Object.Destroy(Scorch); if(ownedScorch!=null) UnityEngine.Object.Destroy(ownedScorch);
            if(GroundWave!=null) UnityEngine.Object.Destroy(GroundWave); if(ownedWave!=null) UnityEngine.Object.Destroy(ownedWave);
            if(Shadow!=null) UnityEngine.Object.Destroy(Shadow); if(ownedShadow!=null) UnityEngine.Object.Destroy(ownedShadow);
            if(TrailMaterial!=null) UnityEngine.Object.Destroy(TrailMaterial); if(TrailGlow!=null) UnityEngine.Object.Destroy(TrailGlow);
            if(ownedTrail!=null) UnityEngine.Object.Destroy(ownedTrail);
            if(Flame!=null) UnityEngine.Object.Destroy(Flame); if(ownedFlame!=null) UnityEngine.Object.Destroy(ownedFlame);
            if(FireMaterial!=null) UnityEngine.Object.Destroy(FireMaterial); if(ownedFire!=null) UnityEngine.Object.Destroy(ownedFire);
            if(NukeMaterial!=null) UnityEngine.Object.Destroy(NukeMaterial); if(ownedNuke!=null) UnityEngine.Object.Destroy(ownedNuke);
            if(SplashMaterial!=null) UnityEngine.Object.Destroy(SplashMaterial); if(ownedSplash!=null) UnityEngine.Object.Destroy(ownedSplash);
            if(ownedSmoke!=null) UnityEngine.Object.Destroy(ownedSmoke); if(Fragment!=null) UnityEngine.Object.Destroy(Fragment);
            if(smokeA!=null) UnityEngine.Object.Destroy(smokeA); if(smokeB!=null) UnityEngine.Object.Destroy(smokeB);
            if(Particle!=null) UnityEngine.Object.Destroy(Particle);
            if(Debris!=null) UnityEngine.Object.Destroy(Debris);
            if(noise!=null) UnityEngine.Object.Destroy(noise);
            if(simGround!=null) UnityEngine.Object.Destroy(simGround); if(simAir!=null) UnityEngine.Object.Destroy(simAir);
            if(simRuns!=null) foreach(var t in simRuns) if(t!=null) UnityEngine.Object.Destroy(t);
            VolumeEvolution.UseSim=false; FireSim.GroundLoaded=1; FireSim.AirLoaded=1; FireSim.NukeHave[0]=FireSim.NukeHave[1]=FireSim.NukeHave[2]=false;
            if(blueNoise!=null) UnityEngine.Object.Destroy(blueNoise);
        }
    }
}
