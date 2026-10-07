using UnityEngine;
using UnityEngine.Rendering;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class HeroVolume
    {
        readonly Transform transform;
        readonly MeshRenderer renderer, flareRenderer, distortRenderer;
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock(), flareProperties=new MaterialPropertyBlock(),
            distortProperties=new MaterialPropertyBlock();
        static readonly int Shell=Shader.PropertyToID("_Shell"),Core=Shader.PropertyToID("_Core"),
            Cloud=Shader.PropertyToID("_Cloud"),Context=Shader.PropertyToID("_Context"),
            Steps=Shader.PropertyToID("_Steps"),Light=Shader.PropertyToID("_LightDirection"),
            SunColor=Shader.PropertyToID("_SunColor"),Flare=Shader.PropertyToID("_Flare"),
            Fire=Shader.PropertyToID("_Fire"),Smoke=Shader.PropertyToID("_Smoke"),Bounds=Shader.PropertyToID("_Bounds"),Motion=Shader.PropertyToID("_Motion"),Dust=Shader.PropertyToID("_Dust"),
            Shock=Shader.PropertyToID("_Shock"),Haze=Shader.PropertyToID("_Haze"),Seed=Shader.PropertyToID("_Seed");
        static readonly int[] LobeCenter={Shader.PropertyToID("_L0"),Shader.PropertyToID("_L1"),Shader.PropertyToID("_L2"),Shader.PropertyToID("_L3"),Shader.PropertyToID("_L4")};
        static readonly int[] LobeAxis={Shader.PropertyToID("_A0"),Shader.PropertyToID("_A1"),Shader.PropertyToID("_A2"),Shader.PropertyToID("_A3"),Shader.PropertyToID("_A4")};
        static readonly int[] LobeMotion={Shader.PropertyToID("_D0"),Shader.PropertyToID("_D1"),Shader.PropertyToID("_D2"),Shader.PropertyToID("_D3"),Shader.PropertyToID("_D4")};
        bool admitted, distort, ground, water;
        CelestialBody body;
        BlastLayout layout;
        int seed, steps;
        internal VolumeState State;
        // Select one flight camera per volume before culling.
        bool showVolume, showFlare, showDistort;
        internal Camera Target { get; private set; }
        internal bool Active { get { return admitted&&showVolume; } }
        internal bool Reserved { get { return admitted; } }
        internal bool Distorting { get { return showDistort; } }
        internal bool NeedsNearDepth { get { return showVolume&&Target!=CameraRig.Far||showDistort; } }
        internal bool NeedsFarDepth { get { return showVolume&&Target!=null&&Target==CameraRig.Far; } }
        internal void ShowFor(Camera cam)
        {
            bool mine=CameraRig.IsFlight(cam)&&(Target==null||cam==Target);
            renderer.enabled=showVolume&&mine;
            if(flareRenderer!=null) flareRenderer.enabled=showFlare&&mine;
            if(distortRenderer!=null) distortRenderer.enabled=showDistort&&mine&&cam==CameraRig.Near;
        }
        internal HeroVolume(Transform root,VolumeAssets assets)
        {
            var go=new GameObject("Original shock dome and fireball volume");go.layer=EffectLayer.Value;go.transform.SetParent(root,false);
            transform=go.transform;go.AddComponent<MeshFilter>().sharedMesh=assets.Cube;
            renderer=Configure(go.AddComponent<MeshRenderer>(),assets.Material);
            if(assets.FlareMaterial!=null) flareRenderer=Child("Original core glare",assets.Quad,assets.FlareMaterial);
            if(assets.DistortMaterial!=null) distortRenderer=Child("Original shock and heat refraction",assets.Quad,assets.DistortMaterial);
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
            admitted=allow;distort=allowDistortion&&distortRenderer!=null;
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
            if(State.Fade<=0) admitted=false;
            showVolume=admitted&&settings.Volumetric&&State.Visible;
            showFlare=flareRenderer!=null&&showVolume&&State.Glow+State.Heat>0.01;
            Target=CameraRig.Pick(transform.position,(float)(State.BoxRadius*1.7320508));
            bool refract=distort&&settings.Shockwave&&VolumeEvolution.DistortionVisible(State,groundFire)&&(Target==null||Target==CameraRig.Near);
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
                distortProperties.SetFloat(Seed,seed%997);
                distortRenderer.SetPropertyBlock(distortProperties);
            }
            if(!showVolume) return;
            Vec4 shell,core,cloud,context,fireball,smoke;
            VolumeEvolution.Pack(State,seed,out shell,out core,out cloud,out context,out fireball,out smoke);
            properties.SetVector(Fire,V(fireball));properties.SetVector(Smoke,V(smoke));
            properties.SetVector(Shell,V(shell));properties.SetVector(Core,V(core));
            properties.SetVector(Cloud,V(cloud));properties.SetVector(Context,V(context));
            properties.SetVector(Bounds,V(VolumeEvolution.Bounds(State)));
            properties.SetVector(Motion,V(VolumeEvolution.Motion(State)));
            properties.SetVector(Dust,V(VolumeEvolution.Dust(State)));
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
            properties.SetColor(SunColor,new Color(1,0.96f,0.9f,sun));
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
        Vector3 SunDirection(out float strength)
        {
            strength=1;
            CelestialBody star=Planetarium.fetch!=null?Planetarium.fetch.Sun:null;
            if(star==null) return Vector3.up;
            Vector3d point=transform.position;
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
            admitted=false;distort=false;renderer.enabled=false;layout=null;showVolume=false;showFlare=false;showDistort=false;Target=null;
            if(flareRenderer!=null) flareRenderer.enabled=false;if(distortRenderer!=null) distortRenderer.enabled=false;
        }
    }
}
