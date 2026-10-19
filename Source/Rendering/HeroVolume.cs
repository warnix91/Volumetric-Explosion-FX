using UnityEngine;
using UnityEngine.Rendering;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class HeroVolume
    {
        readonly Transform transform;
        readonly Mesh mesh;
        readonly MeshRenderer renderer, flareRenderer, distortRenderer;
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock(), flareProperties=new MaterialPropertyBlock(),
            distortProperties=new MaterialPropertyBlock();
        static readonly int Shell=Shader.PropertyToID("_Shell"),Core=Shader.PropertyToID("_Core"),
            Cloud=Shader.PropertyToID("_Cloud"),Context=Shader.PropertyToID("_Context"),
            Steps=Shader.PropertyToID("_Steps"),Light=Shader.PropertyToID("_LightDirection"),
            SunColor=Shader.PropertyToID("_SunColor"),Flare=Shader.PropertyToID("_Flare"),
            Fire=Shader.PropertyToID("_Fire"),Smoke=Shader.PropertyToID("_Smoke"),Bounds=Shader.PropertyToID("_Bounds"),Motion=Shader.PropertyToID("_Motion"),Dust=Shader.PropertyToID("_Dust"),
            Shock=Shader.PropertyToID("_Shock"),ShockGain=Shader.PropertyToID("_ShockGain"),RingDepth=Shader.PropertyToID("_RingDepth"),Haze=Shader.PropertyToID("_Haze"),Seed=Shader.PropertyToID("_Seed"),
            Look0=Shader.PropertyToID("_Look0"),Look1=Shader.PropertyToID("_Look1"),SimShape=Shader.PropertyToID("_SimShape"),SimVariant=Shader.PropertyToID("_SimVariant"),SimLinger=Shader.PropertyToID("_SimLinger"),SimStem=Shader.PropertyToID("_SimStem"),SmokeMix=Shader.PropertyToID("_SmokeMix"),
            NukeId=Shader.PropertyToID("_Nuke"),NukeStemId=Shader.PropertyToID("_NukeStem"),NukeRingId=Shader.PropertyToID("_NukeRing"),
            NukeGroundId=Shader.PropertyToID("_NukeGround"),NukeIceId=Shader.PropertyToID("_NukeIce"),NukeWilsonId=Shader.PropertyToID("_NukeWilson"),NukeCapId=Shader.PropertyToID("_NukeCap"),
            NkBall=Shader.PropertyToID("_NkBall"),NkTorus=Shader.PropertyToID("_NkTorus"),NkStem=Shader.PropertyToID("_NkStem"),NkSkirt=Shader.PropertyToID("_NkSkirt"),
            NkCap=Shader.PropertyToID("_NkCap"),NkDust=Shader.PropertyToID("_NkDust"),NkExtra=Shader.PropertyToID("_NkExtra"),NkScale=Shader.PropertyToID("_NkScale"),
            NkSun=Shader.PropertyToID("_NkSun"),NkHeat=Shader.PropertyToID("_NkHeat"),SimFlameId=Shader.PropertyToID("_SimFlame"),
            NkPile0=Shader.PropertyToID("_NkPile0"),NkPile1=Shader.PropertyToID("_NkPile1"),NkPile2=Shader.PropertyToID("_NkPile2"),
            NkLate=Shader.PropertyToID("_NkLate"),NkAir=Shader.PropertyToID("_NkAir"),NkJets=Shader.PropertyToID("_NkJets"),NkWave=Shader.PropertyToID("_NkWave"),
            NkGroundMap=Shader.PropertyToID("_NkGroundMap"),NkGroundInfo=Shader.PropertyToID("_NkGroundInfo"),
            GlareId=Shader.PropertyToID("_Glare"), HaloId=Shader.PropertyToID("_Halo");
        static readonly int[] LobeCenter={Shader.PropertyToID("_L0"),Shader.PropertyToID("_L1"),Shader.PropertyToID("_L2"),Shader.PropertyToID("_L3"),Shader.PropertyToID("_L4")};
        static readonly int[] LobeAxis={Shader.PropertyToID("_A0"),Shader.PropertyToID("_A1"),Shader.PropertyToID("_A2"),Shader.PropertyToID("_A3"),Shader.PropertyToID("_A4")};
        static readonly int[] LobeMotion={Shader.PropertyToID("_D0"),Shader.PropertyToID("_D1"),Shader.PropertyToID("_D2"),Shader.PropertyToID("_D3"),Shader.PropertyToID("_D4")};
        readonly Material baseMaterial, simMaterial, distortMaterial, nukeMaterial;
        // 1.1: the glare of a nuclear fireball, a quad anchored on it in the world (NukeGlare): the old one, the
        // core glare stretched and pulled toward the lens, followed the camera.
        readonly MeshRenderer glareRenderer; readonly Transform glareTransform;
        readonly MaterialPropertyBlock glareProperties=new MaterialPropertyBlock();
        bool showGlare;
        static Mesh glareQuad;
        // The nuclear cloud's state last drawn (its fireball's height lights the land from there).
        internal NukeFrame Nuke { get; private set; }
        // The ground under a nuclear blast (FxInstance fills it): polar height map, and (valid, radius covered,
        // lowest, highest ground) in proxy units. The blast wave's dust wall follows it.
        internal Texture GroundMap; internal Vector4 GroundInfo;

        bool admitted, distort, ground, water;
        CelestialBody body;
        BlastLayout layout;
        int seed, steps;
        internal VolumeState State;
        // Sun direction in the blast frame and its strength (0 in a body's shadow), as last given to the volume.
        internal Vector4 Sun { get; private set; }
        // Select one flight camera per volume before culling.
        bool showVolume, showFlare, showDistort;
        internal Camera Target { get; private set; }
        internal bool Active { get { return admitted&&showVolume; } }
        internal bool Reserved { get { return admitted&&retireAge<0; } }
        // Handed over to a newer blast (FxPool): this volume fades out over RetireSeconds instead of vanishing; it no
        // longer counts against the budget meanwhile.
        const float RetireSeconds=0.9f;
        float retireAge=-1, lastAge;
        internal void Retire() { if(admitted&&retireAge<0) retireAge=lastAge; }
        internal bool Distorting { get { return showDistort; } }
        internal bool NeedsNearDepth { get { return showVolume&&Target!=CameraRig.Far||showDistort; } }
        internal bool NeedsFarDepth { get { return showVolume&&(Target==null||Target==CameraRig.Far)||showDistort&&Target==CameraRig.Far; } }
        internal void ShowFor(Camera cam,LowResVolumes lowRes=null)
        {
            bool mine=CameraRig.IsFlight(cam)&&(Target==null||cam==Target);
            renderer.enabled=showVolume&&mine&&lowRes==null;
            if(showVolume&&mine&&lowRes!=null) lowRes.Add(mesh,transform.localToWorldMatrix,renderer.sharedMaterial,properties);
            if(flareRenderer!=null) flareRenderer.enabled=showFlare&&mine;
            if(glareRenderer!=null) glareRenderer.enabled=showGlare&&mine;
            // The refraction is drawn by one camera: the far one too in 1.1 (beyond ~400 m KSP draws with its far camera).
            if(distortRenderer!=null) distortRenderer.enabled=showDistort&&mine&&(cam==CameraRig.Near||State.Modern&&cam==Target);
        }
        internal HeroVolume(Transform root,VolumeAssets assets)
        {
            var go=new GameObject("Original shock dome and fireball volume");go.layer=EffectLayer.Value;go.transform.SetParent(root,false);
            transform=go.transform;go.AddComponent<MeshFilter>().sharedMesh=assets.Cube; mesh=assets.Cube;
            renderer=Configure(go.AddComponent<MeshRenderer>(),assets.Material);
            baseMaterial=assets.Material; simMaterial=assets.SimMaterial;
            if(assets.FlareMaterial!=null) flareRenderer=Child("Original core glare",assets.Quad,assets.FlareMaterial);
            if(assets.DistortMaterial!=null) distortRenderer=Child("Original shock and heat refraction",assets.Quad,assets.DistortMaterial);
            distortMaterial=assets.DistortMaterial; nukeMaterial=assets.NukeMaterial;
            if(assets.NukeGlareMaterial!=null)
            {
                if(glareQuad==null)
                {
                    glareQuad=new Mesh(); glareQuad.name="Original nuclear glare quad";
                    glareQuad.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
                    glareQuad.triangles=new[]{0,1,2,0,2,3}; glareQuad.bounds=new Bounds(Vector3.zero,Vector3.one*2e5f);
                }
                var g=new GameObject("Original nuclear glare"); g.layer=EffectLayer.Value; g.transform.SetParent(root,false); glareTransform=g.transform;
                g.AddComponent<MeshFilter>().sharedMesh=glareQuad;
                glareRenderer=Configure(g.AddComponent<MeshRenderer>(),assets.NukeGlareMaterial);
            }
        }
        MeshRenderer Child(string name,Mesh mesh,Material material)
        {
            var child=new GameObject(name);child.layer=EffectLayer.Value;child.transform.SetParent(transform,false);
            child.AddComponent<MeshFilter>().sharedMesh=mesh;
            return Configure(child.AddComponent<MeshRenderer>(),material);
        }
        static MeshRenderer Configure(MeshRenderer r,Material m)
        {
            r.sharedMaterial=m;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;r.enabled=false;
            return r;
        }
        internal void Begin(bool allow,bool allowDistortion,int clusterSeed,int marchSteps,bool groundImpact,bool waterImpact,CelestialBody mainBody,BlastLayout blast)
        {
            admitted=allow;distort=allowDistortion&&distortRenderer!=null; retireAge=-1; lastAge=0;
            renderer.enabled=false;if(flareRenderer!=null) flareRenderer.enabled=false;if(distortRenderer!=null) distortRenderer.enabled=false;
            showVolume=false;showFlare=false;showDistort=false;
            seed=clusterSeed;steps=marchSteps;ground=groundImpact;water=waterImpact;body=mainBody;layout=blast;
        }
        internal float Coverage(Camera cam)
        {
            if(!Active||cam==null) return 0;
            double local=System.Math.Max(State.ShellRadius,State.BoundsCenter.Length+State.BoundsRadius);
            float radius=(float)(State.BoxRadius*System.Math.Min(local,1.8));
            Vector3 center=transform.position;
            float distance=Vector3.Distance(cam.transform.position,center);
            if(distance<=radius) return 1;
            float projected=radius/(distance*Mathf.Tan(cam.fieldOfView*0.5f*Mathf.Deg2Rad));
            return Mathf.Clamp01(Mathf.PI*projected*projected/(4*Mathf.Max(cam.aspect,0.1f)));
        }
        internal void Tick(FxPlan plan,float age,FxSettings settings,float groundFire,float stepScale)
        {
            State=VolumeEvolution.Evaluate(plan,age,settings.Explosions,settings.Burst,ground&&settings.Ground,water&&settings.Water,layout);
            lastAge=age;
            if(retireAge>=0) State.Fade*=Mathf.Clamp01(1-(age-retireAge)/RetireSeconds);
            if(State.Fade<=0) { admitted=false; retireAge=-1; }
            // 1.1: a nuclear cloud has its own renderer (NukeVolume).
            bool nuclear=plan.Warhead==WarheadKind.Nuke&&State.Modern&&nukeMaterial!=null;
            Material wanted=nuclear?nukeMaterial:State.Sim&&simMaterial!=null?simMaterial:baseMaterial;
            if(renderer.sharedMaterial!=wanted) renderer.sharedMaterial=wanted;
            showVolume=admitted&&settings.Volumetric&&State.Visible;
            showFlare=flareRenderer!=null&&showVolume&&State.Glow+State.Heat>0.01;
            Target=CameraRig.Pick(transform.position,(float)(State.BoxRadius*1.7320508));
            bool refract=distort&&settings.Shockwave&&VolumeEvolution.DistortionVisible(State,groundFire)&&(Target==null||Target==CameraRig.Near||State.Modern);
            Camera eye=CameraRig.Near;
            if(refract&&eye!=null)
            {
                float tanV=Mathf.Tan(eye.fieldOfView*0.5f*Mathf.Deg2Rad);
                if(VolumeEvolution.RingOutOfView(State,Vector3.Distance(eye.transform.position,transform.position),tanV*Mathf.Sqrt(1+eye.aspect*eye.aspect))) refract=false;
            }
            showDistort=distortRenderer!=null&&refract;
            renderer.enabled=showVolume; if(flareRenderer!=null) flareRenderer.enabled=showFlare; if(distortRenderer!=null) distortRenderer.enabled=showDistort;
            if(!showVolume&&!refract) return;
            // Keep proxy size fixed so noise remains attached to the gas.
            transform.localScale=Vector3.one*(float)State.BoxRadius;
            if(refract)
            {
                Vec4 shock,haze;
                VolumeEvolution.Distortion(State,groundFire,out shock,out haze);
                distortProperties.SetVector(Shock,V(shock));distortProperties.SetVector(Haze,V(haze));
                distortProperties.SetFloat(Seed,seed%997); distortProperties.SetFloat(ShockGain,(float)VolumeEvolution.ShockRimGain(State));
                distortProperties.SetFloat(RingDepth,(float)VolumeEvolution.RingDepth(State));
                // Every blast's wave is the 1.0 ring.
                if(distortRenderer.sharedMaterial!=distortMaterial) distortRenderer.sharedMaterial=distortMaterial;
                distortRenderer.SetPropertyBlock(distortProperties);
            }
            if(!showVolume) return;
            Vec4 shell,core,cloud,context,fireball,smoke;
            VolumeEvolution.Pack(State,seed,out shell,out core,out cloud,out context,out fireball,out smoke);
            properties.SetVector(Fire,V(fireball));properties.SetVector(Smoke,V(smoke));
            LookProfile look=VolumeEvolution.Look(plan);
            properties.SetVector(Look0,V(look.Shader0));properties.SetVector(Look1,V(look.Shader1));
            properties.SetVector(Shell,V(shell));properties.SetVector(Core,V(core));
            properties.SetVector(Cloud,V(cloud));properties.SetVector(Context,V(context));
            properties.SetVector(Bounds,V(VolumeEvolution.Bounds(State)));
            properties.SetVector(Motion,V(VolumeEvolution.Motion(State)));
            properties.SetVector(Dust,V(VolumeEvolution.Dust(State)));
            properties.SetVector(SimShape,V(State.SimShape)); properties.SetFloat(SimVariant,State.SimRun); properties.SetFloat(SimLinger,(float)State.SimLinger); properties.SetFloat(SimStem,(float)State.SimStem); properties.SetVector(SmokeMix,V(State.SmokeMix));
            properties.SetVector(SimFlameId,new Vector4((float)(1-State.FlameFaint),0,0,0));
            properties.SetVector(NukeId,V(State.Nuke)); properties.SetVector(NukeStemId,V(State.NukeStem)); properties.SetVector(NukeRingId,V(State.NukeRing));
            properties.SetVector(NukeGroundId,V(State.NukeGround)); properties.SetVector(NukeIceId,V(State.NukeIce)); properties.SetVector(NukeWilsonId,V(State.NukeWilson)); properties.SetVector(NukeCapId,V(State.NukeCap));
            for(int i=0;i<5;i++)
            {
                Vec4 c,a,d; VolumeEvolution.PackLobe(State,i,out c,out a,out d);
                properties.SetVector(LobeCenter[i],V(c));properties.SetVector(LobeAxis[i],V(a));properties.SetVector(LobeMotion[i],V(d));
            }
            properties.SetFloat(Steps,Mathf.Max(10,steps*Mathf.Clamp(stepScale,0.35f,1)));
            float sun;
            Vector3 light=transform.InverseTransformDirection(SunDirection(out sun)).normalized;
            float ambient=Mathf.Clamp(RenderSettings.ambientLight.grayscale,0.12f,0.6f);
            properties.SetVector(Light,new Vector4(light.x,light.y,light.z,ambient));
            Sun=new Vector4(light.x,light.y,light.z,sun);
            properties.SetColor(SunColor,new Color(1,0.96f,0.9f,sun));
            showGlare=false;
            if(nuclear)
            {
                NukeFrame f=NukeCloud.Evaluate(plan,layout,State,age,State.BoxRadius,seed,body!=null?body.Radius:600000);
                Nuke=f;
                // The glare replaces the core glare for a nuclear burst: anchored on the fireball in the world.
                showFlare=false; if(flareRenderer!=null) flareRenderer.enabled=false;
                if(glareRenderer!=null)
                {
                    double tf=VolumeEvolution.FireballDuration(plan), rf=VolumeEvolution.FireballRadius(plan);
                    Vec4 g=NukeCloud.Glare(f,age,tf), h=NukeCloud.Halo(f,State.BoxRadius,1-FxPool.Daylight);
                    showGlare=showVolume&&g.Y+g.Z+h.Y>0.01;
                    glareRenderer.enabled=showGlare;
                    if(showGlare)
                    {
                        glareTransform.localPosition=Vector3.up*(float)(System.Math.Max(f.Ball.X*State.BoxRadius,0.5*rf));
                        glareProperties.SetVector(GlareId,new Vector4((float)(g.X*rf),(float)g.Y,(float)g.Z,(float)g.W));
                        glareProperties.SetVector(HaloId,V(h));
                        glareRenderer.SetPropertyBlock(glareProperties);
                    }
                }
                properties.SetVector(NkPile0,V(f.Pile0)); properties.SetVector(NkPile1,V(f.Pile1)); properties.SetVector(NkPile2,V(f.Pile2));
                properties.SetVector(NkLate,V(f.Late)); properties.SetVector(NkAir,V(f.Air)); properties.SetVector(NkJets,V(f.Jets)); properties.SetVector(NkWave,V(f.Wave));
                if(GroundMap!=null) properties.SetTexture(NkGroundMap,GroundMap);
                properties.SetVector(NkGroundInfo,GroundMap!=null?GroundInfo:Vector4.zero);
                properties.SetVector(NkBall,V(f.Ball)); properties.SetVector(NkTorus,V(f.Torus)); properties.SetVector(NkStem,V(f.Stem));
                properties.SetVector(NkSkirt,V(f.Skirt)); properties.SetVector(NkCap,V(f.Cap)); properties.SetVector(NkDust,V(f.Dust));
                properties.SetVector(NkExtra,V(f.Extra)); properties.SetVector(NkScale,V(f.Scale));
                // The frame governor's detail (adaptive performance): coarser steps when frames run long.
                properties.SetVector(NkHeat,new Vector4((float)f.Heat.X,(float)f.Heat.Y,Mathf.Clamp(stepScale,0.4f,1),(float)f.Heat.W));
                // The sun as the cloud's top sees it (a tall cloud stays lit after sunset at the ground).
                double topY=(f.Ball.X+f.Ball.Z)*State.BoxRadius;
                float topSun; SunDirection(transform.position+transform.up*(float)topY,out topSun);
                properties.SetVector(NkSun,new Vector4(sun,topSun,(float)(f.Ball.X+f.Ball.Z),0));
                properties.SetColor(SunColor,new Color(1,0.96f,0.9f,Mathf.Max(sun,topSun)));
            }
            renderer.SetPropertyBlock(properties);
            if(showFlare)
            { flareProperties.SetVector(Flare,V(VolumeEvolution.Flare(State)));flareRenderer.SetPropertyBlock(flareProperties); }
        }
        internal bool AddSecondary(Vector3 worldPosition,float age,double weight)
        {
            if(layout==null||!admitted) return false;
            Vector3 local=Quaternion.Inverse(transform.parent.rotation)*(worldPosition-transform.parent.position);
            return layout.AddSecondary(new Vec3(local.x,local.y,local.z),age,weight);
        }
        Vector3 SunDirection(out float strength) { return SunDirection(transform.position,out strength); }
        Vector3 SunDirection(Vector3 at,out float strength) { return SunDirection(at,body,out strength); }
        // The sun's direction from a point (world) and its strength there (0 in the body's shadow). (shared with
        // the water splash's volume)
        internal static Vector3 SunDirection(Vector3 at,CelestialBody body,out float strength)
        {
            strength=1;
            CelestialBody star=Planetarium.fetch!=null?Planetarium.fetch.Sun:null;
            if(star==null) return Vector3.up;
            Vector3d point=at;
            Vector3d direction=(star.position-point).normalized;
            if(body!=null&&body!=star)
            {
                double along=Vector3d.Dot(body.position-point,direction);
                if(along>0)
                {
                    double miss=(point+direction*along-body.position).magnitude;
                    strength=(float)UtilMath.Clamp01((miss-body.Radius*0.995)/(body.Radius*0.01));
                }
            }
            return (Vector3)direction;
        }
        static Vector4 V(Vec4 v) { return new Vector4((float)v.X,(float)v.Y,(float)v.Z,(float)v.W); }
        internal void Clear()
        {
            admitted=false;distort=false;renderer.enabled=false;layout=null;showVolume=false;showFlare=false;showDistort=false;Target=null;retireAge=-1;
            GroundMap=null; GroundInfo=Vector4.zero;
            if(flareRenderer!=null) flareRenderer.enabled=false;if(distortRenderer!=null) distortRenderer.enabled=false;
            if(glareRenderer!=null) glareRenderer.enabled=false; showGlare=false;
        }
    }
}
