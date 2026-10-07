using System;
using UnityEngine;
using VolumetricExplosionFX.Core;
using VolumetricExplosionFX.Ksp;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class FxPool : IDisposable
    {
        readonly FxInstance[] slots;
        readonly ProceduralAssets assets;
        readonly VolumeAssets volumes;
        readonly DebrisField debris;
        readonly FxSettings settings;
        internal int Dropped { get; private set; }
        internal int Spawned { get; private set; }
        internal int NoCamera { get; private set; }
        internal int OutOfRange { get; private set; }
        internal int BudgetDrops { get; private set; }
        internal int Absorbed { get; private set; }
        internal string ShaderSummary { get { return assets.ShaderSummary+" | volume="+volumes.Status; } }
        internal int Volumes { get { int n=0;for(int i=0;i<slots.Length;i++) if(slots[i].HasVolume)n++;return n; } }
        int ReservedVolumes { get { int n=0;for(int i=0;i<slots.Length;i++) if(slots[i].ReservedVolume)n++;return n; } }
        internal int Particles { get { int n=0;for(int i=0;i<slots.Length;i++) n+=slots[i].ParticleCount;return n; } }
        internal int Active { get { int n=0;for(int i=0;i<slots.Length;i++) if(slots[i].Active)n++;return n; } }
        internal int Debris { get { int n=0;for(int i=0;i<slots.Length;i++) n+=slots[i].ReservedDebris;return n; } }
        internal int Lights { get { int n=0;for(int i=0;i<slots.Length;i++) if(slots[i].HasLight)n++;return n; } }
        internal int FireVolumes { get { int n=0;for(int i=0;i<slots.Length;i++) if(slots[i].HasFireVolume)n++;return n; } }
        internal EffectLoad Load
        {
            get
            {
                return new EffectLoad { Blasts=Active, FireballVolumes=Volumes, FireVolumes=FireVolumes, Particles=Particles,
                    Wrecks=debris.Available?debris.Active:0, WreckPieces=debris.Available?debris.Pieces:0, Lights=Lights };
            }
        }
        internal string Wreckage { get { return debris.Available?debris.Active+" wrecks / "+debris.Pieces+" pieces / "+debris.Pending+" cutting / "+
            debris.Captured+" captured / "+debris.Missed+" missed / "+debris.Dropped+" dropped / "+debris.Failed+" failed":"part debris unavailable"; } }
        internal FxPool(FxSettings s)
        {
            settings=s;
            CameraRig.Setup();
            volumes=new VolumeAssets(s.Volumetric); assets=new ProceduralAssets(volumes.Particle,volumes.Debris); slots=new FxInstance[s.MaxEvents];
            debris=new DebrisField(s,volumes,assets.Glow);
            try { for(int i=0;i<slots.Length;i++) slots[i]=new FxInstance(i,assets,s,volumes); }
            catch { Dispose();throw; }
            Camera.onPreCull+=OnPreCull; hooked=true;
        }
        bool hooked;
        void OnPreCull(Camera cam)
        {
            for(int i=0;i<slots.Length;i++) if(slots[i]!=null) slots[i].ShowFor(cam);
        }
        internal bool Spawn(EventCluster c,FxPlan plan)
        {
            Camera cam=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:Camera.main;
            Vector3 position=SnapshotReader.Vector(c.Position);
            if(cam==null) { NoCamera++;Dropped++;return false; }
            if(Vector3.Distance(cam.transform.position,position)>settings.Distance) { OutOfRange++;Dropped++;return false; }
            CameraRig.Refresh();
            float distance=Vector3.Distance(cam.transform.position,position);
            FxInstance host=Covering(position,plan.VisualEnergyScore);
            Vector3 origin=position+SnapshotReader.Vector(c.Representative.Origin-c.Representative.Part.Position);
            if(host!=null) { host.AddSecondary(origin,plan.VisualEnergyScore/Math.Max(host.Score,1e-3)); Absorbed++; return true; }
            double lod=Numbers.Clamp(1-distance/settings.Distance,0.1,1);
            plan.DebrisCount=(int)(plan.DebrisCount*lod); plan.PuffCount=Math.Max(4,(int)(plan.PuffCount*lod));
            int target=-1,weakest=-1,weakestVolume=-1;
            for(int i=0;i<slots.Length;i++)
            {
                if(!slots[i].Active&&target<0) target=i;
                if(slots[i].Active&&(weakest<0||slots[i].Score<slots[weakest].Score)) weakest=i;
                if(slots[i].ReservedVolume&&(weakestVolume<0||slots[i].Score<slots[weakestVolume].Score)) weakestVolume=i;
            }
            if(target<0&&weakest>=0&&plan.VisualEnergyScore>slots[weakest].Score+0.5)
            { slots[weakest].Clear();target=weakest; }
            if(target>=0)
            {
                bool ground=c.Ground&&settings.Ground, water=c.Representative.Kind==EventKind.WaterImpact&&settings.Water;
                bool candidate=!Reentry.Applies(plan,ground,plan.ImpactSpeed)&&volumes.Material!=null&&settings.Volumetric&&!cam.orthographic&&distance<settings.Distance&&
                    VolumeEvolution.Evaluate(plan,0.1,settings.Explosions,settings.Burst,ground,water).Visible;
                if(candidate&&ReservedVolumes>=settings.MaxVolumes&&weakestVolume>=0&&
                    plan.VisualEnergyScore>slots[weakestVolume].Score+0.5) slots[weakestVolume].ReleaseVolume();
                bool volume=candidate&&ReservedVolumes<settings.MaxVolumes;
                bool refraction=settings.Shockwave&&!cam.orthographic&&distance<2500;
                slots[target].Begin(c,plan,settings,Math.Max(0,settings.MaxDebris-Debris),Lights<settings.MaxLights&&distance<1500,volume,refraction);
                Spawned++;
                return true;
            }
            BudgetDrops++;Dropped++;
            return false;
        }
        internal bool Covers(Vector3 position,double score) { return Covering(position,score)!=null; }
        internal FxInstance Covering(Vector3 position,double score)
        {
            for(int i=0;i<slots.Length;i++) if(slots[i].Active&&slots[i].Age<2.5f&&score<=slots[i].Score+1.2&&
                Vector3.Distance(slots[i].Root.transform.position,position)<=slots[i].CoverRadius) return slots[i];
            return null;
        }
        internal void PrepareDebris(Part part)
        {
            if(part!=null&&InRange(part.transform.position)) debris.Prepare(part);
        }
        internal void CaptureDebris(Part part,DestructionEvent e,FxPlan plan)
        {
            if(InRange(SnapshotReader.Vector(e.Part.Position))) debris.Capture(part,e,plan);
        }
        internal void CaptureTestDebris(CapturedPart model,DestructionEvent e,FxPlan plan,CelestialBody body)
        {
            if(InRange(SnapshotReader.Vector(e.Part.Position))) debris.Capture(model,e,plan,body);
        }
        internal bool InRange(Vector3 position)
        {
            Camera cam=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:Camera.main;
            return cam!=null&&Vector3.Distance(cam.transform.position,position)<=settings.Distance;
        }
        static readonly int SunId=Shader.PropertyToID("_VefxSun"), AmbientId=Shader.PropertyToID("_VefxAmbient"),
            SkyId=Shader.PropertyToID("_VefxSky"), UpId=Shader.PropertyToID("_VefxUp"),
            Fire0Id=Shader.PropertyToID("_VefxFire0"), Fire1Id=Shader.PropertyToID("_VefxFire1"), FireRangeId=Shader.PropertyToID("_VefxFireRange");
        internal static double Starlight(Vector3d point)
        {
            CelestialBody star=Planetarium.fetch!=null?Planetarium.fetch.Sun:null;
            CelestialBody body=FlightGlobals.currentMainBody;
            if(star==null||body==null||body==star) return 1;
            Vector3d direction=(star.position-point).normalized;
            double along=Vector3d.Dot(body.position-point,direction);
            return along>0?UtilMath.Clamp01(((point+direction*along-body.position).magnitude-body.Radius*0.995)/(body.Radius*0.01)):1;
        }
        void PublishLighting(Camera cam)
        {
            CelestialBody star=Planetarium.fetch!=null?Planetarium.fetch.Sun:null;
            if(star==null||cam==null) return;
            Vector3d point=cam.transform.position, direction=(star.position-point).normalized;
            double strength=Starlight(point);
            CelestialBody body=FlightGlobals.currentMainBody;
            Shader.SetGlobalVector(SunId,new Vector4((float)direction.x,(float)direction.y,(float)direction.z,(float)strength));
            Color ambient=RenderSettings.ambientLight;
            Shader.SetGlobalVector(AmbientId,new Vector4(Mathf.Max(ambient.r,0.07f),Mathf.Max(ambient.g,0.07f),Mathf.Max(ambient.b,0.08f),1));
            Vector3 up=Vector3.up; float sky=0;
            if(body!=null)
            {
                Vector3d radial=point-body.position; up=(Vector3)radial.normalized;
                if(body.atmosphere)
                {
                    double air=UtilMath.Clamp01(body.GetPressure(radial.magnitude-body.Radius)/101.325);
                    sky=(float)(Math.Pow(air,0.35)*Math.Max(strength,0.12));
                }
            }
            Shader.SetGlobalVector(SkyId,new Vector4(0.36f*sky,0.43f*sky,0.55f*sky,1));
            Shader.SetGlobalVector(UpId,new Vector4(up.x,up.y,up.z,0));
            int first=-1, second=-1;
            for(int i=0;i<slots.Length;i++)
            {
                if(!slots[i].Active||slots[i].FireGlow<=0.01f) continue;
                if(first<0||slots[i].FireGlow>slots[first].FireGlow) { second=first; first=i; }
                else if(second<0||slots[i].FireGlow>slots[second].FireGlow) second=i;
            }
            Shader.SetGlobalVector(Fire0Id,Fire(first)); Shader.SetGlobalVector(Fire1Id,Fire(second));
            Shader.SetGlobalVector(FireRangeId,new Vector4(first>=0?slots[first].FireRange:10,second>=0?slots[second].FireRange:10,0,0));
        }
        Vector4 Fire(int slot)
        {
            if(slot<0) return Vector4.zero;
            Vector3 p=slots[slot].FirePosition; return new Vector4(p.x,p.y,p.z,slots[slot].FireGlow);
        }
        internal void Tick(float dt)
        {
            Camera cam=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:Camera.main;
            if(Active>0||debris.Active>0) PublishLighting(cam);
            debris.Tick(Mathf.Clamp(dt,0,0.1f),cam);
            float coverage=0;
            for(int i=0;i<slots.Length;i++) coverage+=slots[i].Coverage(cam);
            float stepScale=coverage>1.2f?1.2f/coverage:1;
            bool nearDepth=false, farDepth=false;
            for(int i=0;i<slots.Length;i++) if(slots[i].Active)
            {
                slots[i].Tick(Mathf.Clamp(dt,0,0.1f),settings,slots[i].Coverage(cam)>0.2f?stepScale:1);
                nearDepth|=slots[i].NeedsNearDepth; farDepth|=slots[i].NeedsFarDepth;
            }
            CameraRig.RequestDepth(nearDepth,farDepth);
        }
        internal void Shift(Vector3 offset) { for(int i=0;i<slots.Length;i++) slots[i].Shift(offset); debris.Shift(offset); }
        internal void Clear() { for(int i=0;i<slots.Length;i++) slots[i].Clear(); debris.Clear(); }
        public void Dispose()
        {
            if(hooked) { Camera.onPreCull-=OnPreCull; hooked=false; }
            CameraRig.Release();
            for(int i=0;i<slots.Length;i++) if(slots[i]!=null) slots[i].Dispose();
            if(debris!=null) debris.Dispose(); volumes.Dispose(); assets.Dispose();
        }
    }
}
