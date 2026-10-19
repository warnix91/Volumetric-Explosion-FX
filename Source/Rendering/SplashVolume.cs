using UnityEngine;
using UnityEngine.Rendering;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    // 1.1: a water impact's splash drawn as a volume (Core/WaterSplash, SplashVolume.shader): the crown, the
    // jet, the spray, the surge and the foam, on the sea under the blast (the white sprite column is not drawn then).
    internal sealed class SplashVolume
    {
        readonly Transform transform;
        readonly MeshRenderer renderer;
        readonly Mesh mesh;
        readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        FxPlan plan; CelestialBody body;
        Vec3 travel, wind; uint seed;
        float gravity, sea, duration;
        bool admitted, visible;
        SplashFrame frame;
        internal Camera Target { get; private set; }
        internal bool Visible { get { return visible; } }
        internal float Duration { get { return admitted?duration:0; } }
        internal bool NeedsNearDepth { get { return visible&&Target!=CameraRig.Far; } }
        internal bool NeedsFarDepth { get { return visible&&(Target==null||Target==CameraRig.Far); } }
        internal SplashVolume(Transform root,Mesh cube,Material material)
        {
            var go=new GameObject("Original volumetric water splash"); go.layer=EffectLayer.Value; go.transform.SetParent(root,false);
            transform=go.transform; go.AddComponent<MeshFilter>().sharedMesh=cube; mesh=cube;
            renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off; renderer.enabled=false;
        }
        // localVelocity: the object's velocity in the blast frame (its share along the sea leans the crown); seaOffset: the
        // sea's height under the blast's origin (m, along its up).
        internal bool Begin(FxPlan p,bool allow,float g,Vector3 localVelocity,Vector3 breeze,uint randomSeed,float seaOffset,CelestialBody mainBody)
        {
            admitted=allow&&WaterSplash.Applies(p);
            visible=false; renderer.enabled=false; Target=null;
            plan=p; body=mainBody; gravity=g; seed=randomSeed; sea=seaOffset;
            travel=new Vec3(localVelocity.x,0,localVelocity.z); wind=new Vec3(breeze.x,0,breeze.z);
            duration=admitted?(float)WaterSplash.Duration(p,g):0;
            return admitted;
        }
        // glow: the blast's fire light (colour x strength, range in w) and where it is (blast frame).
        internal void Tick(float age,Vector4 glow,Vector3 glowLocal,float stepScale)
        {
            if(!admitted) { visible=false; renderer.enabled=false; return; }
            frame=WaterSplash.Evaluate(plan,age,gravity,travel,wind,seed);
            visible=frame.Visible;
            if(!visible) { admitted=age<duration; renderer.enabled=false; return; }
            float scale,centre; SplashProperties.Proxy(frame,out scale,out centre);
            transform.localPosition=Vector3.up*(sea+centre); transform.localRotation=Quaternion.identity; transform.localScale=Vector3.one*scale;
            Target=CameraRig.Pick(transform.position,scale*1.7320508f);
            float sun;
            Vector3 light=transform.InverseTransformDirection(HeroVolume.SunDirection(transform.position+transform.up*centre,body,out sun)).normalized;
            float ambient=Mathf.Clamp(RenderSettings.ambientLight.grayscale,0.12f,0.6f);
            SplashProperties.Apply(block,frame,light,ambient,sun,glow,glowLocal-Vector3.up*sea,Mathf.Clamp(stepScale,0.4f,1),
                (float)Numbers.Clamp(plan.Atmosphere,0,1));
            renderer.SetPropertyBlock(block);
        }
        internal void Show(Camera cam,LowResVolumes lowRes)
        {
            bool mine=visible&&CameraRig.IsFlight(cam)&&(Target==null||cam==Target);
            renderer.enabled=mine&&lowRes==null;
            if(mine&&lowRes!=null) lowRes.Add(mesh,transform.localToWorldMatrix,renderer.sharedMaterial,block);
        }
        internal float Coverage(Camera cam)
        {
            if(!visible||cam==null) return 0;
            float r=(float)System.Math.Max(frame.Reach,frame.Top*0.5), distance=Vector3.Distance(cam.transform.position,transform.position);
            if(distance<=r) return 1;
            float projected=r/(distance*Mathf.Tan(cam.fieldOfView*0.5f*Mathf.Deg2Rad));
            return Mathf.Clamp01(Mathf.PI*projected*projected/(4*Mathf.Max(cam.aspect,0.1f)));
        }
        internal void Clear() { admitted=false; visible=false; renderer.enabled=false; Target=null; plan=null; body=null; }
    }
}
