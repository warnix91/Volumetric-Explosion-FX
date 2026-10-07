using System;
using UnityEngine;
using VolumetricExplosionFX.Core;
using VolumetricExplosionFX.Ksp;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class FxInstance : IDisposable
    {
        internal readonly GameObject Root, DebrisRoot;
        readonly BlastParticles particles;
        readonly ParticleSystem poolFire, column, tongues;
        readonly FireVolumeRenderer fireVolume;      // the pool fire in 3D (null: flame particles)
        Camera fireTarget;
        readonly MeshRenderer scorch;
        readonly Mesh scorchMesh;
        readonly MaterialPropertyBlock scorchProperties=new MaterialPropertyBlock();
        readonly ResidueEmitter residueEmitter=new ResidueEmitter();
        static readonly int ScorchId=Shader.PropertyToID("_Scorch");
        readonly Light flash;
        readonly HeroVolume volume;
        internal bool HasVolume { get { return active&&volume!=null&&volume.Active; } }
        internal bool ReservedVolume { get { return active&&volume!=null&&volume.Reserved; } }
        internal double Score { get { return plan==null?0:plan.VisualEnergyScore*(Lingering?0.25:1); } }
        bool Lingering { get { return plan!=null&&age>Math.Max(VolumeEvolution.VolumeDuration(plan),groundFireEnd)+2; } }
        internal float Age { get { return age; } }
        internal double CoverRadius { get { return plan==null?0:ReservedVolume?VolumeEvolution.DomeRadius(plan)*1.5:Math.Max(4,plan.Radius*1.5); } }
        internal void ReleaseVolume() { if(volume!=null) volume.Clear(); }
        internal void ShowFor(Camera cam)
        {
            if(!active) return;
            if(volume!=null) volume.ShowFor(cam);
            if(fireVolume!=null) fireVolume.Show(CameraRig.IsFlight(cam)&&(fireTarget==null||cam==fireTarget));
        }
        bool FireShown { get { return fireVolume!=null&&fireVolume.Visible; } }
        internal bool HasFireVolume { get { return active&&FireShown; } }
        internal bool NeedsNearDepth { get { return active&&(volume!=null&&volume.NeedsNearDepth||FireShown&&fireTarget!=CameraRig.Far); } }
        internal bool NeedsFarDepth { get { return active&&(volume!=null&&volume.NeedsFarDepth||FireShown&&(fireTarget==null||fireTarget==CameraRig.Far)); } }
        FxPlan plan; EventKind kind;
        BlastLayout layout;
        ResiduePlan residue;
        bool hasScorch;
        uint scorchSeed;
        internal float FireGlow { get; private set; }
        internal Vector3 FirePosition { get; private set; }
        internal float FireRange { get; private set; }
        Vector3d inertialVelocity, airVelocity;
        CelestialBody body;
        double latitude,longitude,surfaceAltitude;
        float age,groundFireEnd,domeRadius,air,flickerPhase;
        double gasRate;
        bool active, surface, lit, dome;
        internal int ReservedDebris { get { return active?particles.ReservedDebris:0; } }
        internal bool HasLight { get { return active && flash.enabled; } }
        internal bool Active { get { return active; } }
        internal int ParticleCount
        {
            get
            {
                if(!active) return 0;
                int residueCount=poolFire!=null&&column!=null?poolFire.particleCount+column.particleCount+tongues.particleCount:0;
                return particles.Count+residueCount;
            }
        }
        internal FxInstance(int index,ProceduralAssets assets,FxSettings settings,VolumeAssets volumes)
        {
            Root=new GameObject("VolumetricExplosionFX visual slot "+index); Root.layer=EffectLayer.Value; // TransparentFX
            DebrisRoot=new GameObject("VolumetricExplosionFX visual debris "+index); DebrisRoot.layer=EffectLayer.Value;
            particles=new BlastParticles(Root.transform,DebrisRoot.transform,new BlastMaterials {
                Glow=assets.Glow, Cloud=assets.Cloud, Debris=assets.Debris, Trail=assets.Trail, Shard=assets.Shard, HeatDebris=assets.HeatDebris,
                SmokeFire=volumes.SmokeFire, SmokeCloud=volumes.SmokeCloud, TrailSmoke=volumes.TrailMaterial, TrailGlow=volumes.TrailGlow, Flame=volumes.Flame },settings);
            var lightObject=new GameObject("core light"); lightObject.layer=EffectLayer.Value; lightObject.transform.SetParent(Root.transform,false);
            flash=lightObject.AddComponent<Light>(); flash.type=LightType.Point; flash.shadows=LightShadows.None; flash.enabled=false;
            if(volumes.Material!=null) volume=new HeroVolume(Root.transform,volumes);
            if(volumes.FireMaterial!=null&&volumes.Cube!=null) fireVolume=new FireVolumeRenderer(Root.transform,volumes.Cube,volumes.FireMaterial,EffectLayer.Value);
            if(volumes.SmokeFire!=null)
            {
                poolFire=SmokeParticles.Create(Root.transform,"pool fire",volumes.SmokeFire,Math.Min(160,settings.ParticlesPerEvent+32),0,0.45f);
                column=SmokeParticles.Create(Root.transform,"smoke column",volumes.SmokeCloud,Math.Min(240,settings.ParticlesPerEvent+96),0.3f,1);
                tongues=volumes.Flame!=null?SmokeParticles.CreateFlameSheets(Root.transform,"flames",volumes.Flame,200):
                    SmokeParticles.CreateFlames(Root.transform,"flame tongues",assets.Glow,160);
                residueEmitter.Sheets=volumes.Flame!=null;
            }
            if(volumes.Scorch!=null)
            {
                var scorchObject=new GameObject("scorched ground"); scorchObject.layer=EffectLayer.Value; scorchObject.transform.SetParent(Root.transform,false);
                scorchMesh=new Mesh(); scorchMesh.name="Original scorch draped on the ground";
                scorchObject.AddComponent<MeshFilter>().sharedMesh=scorchMesh;
                scorch=scorchObject.AddComponent<MeshRenderer>(); scorch.sharedMaterial=volumes.Scorch;
                scorch.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; scorch.receiveShadows=false; scorch.enabled=false;
            }
            Root.SetActive(false); DebrisRoot.SetActive(false);
        }
        internal void Begin(EventCluster cluster,FxPlan fx,FxSettings settings,int debrisBudget,bool light,bool allowVolume,bool allowDistortion)
        {
            Clear(); active=true; plan=fx; kind=cluster.Representative.Kind;
            PartSnapshot s=cluster.Representative.Part;
            bool ground=cluster.Ground&&settings.Ground, water=kind==EventKind.WaterImpact&&settings.Water;
            surface=ground||kind!=EventKind.Destruction;
            air=(float)Numbers.Clamp(plan.Atmosphere,0,1);
            Vector3 anchor=SnapshotReader.Vector(ground?cluster.GroundPoint:cluster.Position);
            Quaternion frame=Quaternion.FromToRotation(Vector3.up,SnapshotReader.Vector(s.Environment.Up));
            Root.transform.position=anchor; Root.transform.rotation=frame;
            DebrisRoot.transform.position=anchor; DebrisRoot.transform.rotation=frame;
            Root.SetActive(true); DebrisRoot.SetActive(true);
            inertialVelocity=new Vector3d(s.InertialVelocity.X,s.InertialVelocity.Y,s.InertialVelocity.Z);
            airVelocity=inertialVelocity-new Vector3d(s.SurfaceVelocity.X,s.SurfaceVelocity.Y,s.SurfaceVelocity.Z);
            body=null;
            for(int i=0;i<FlightGlobals.Bodies.Count;i++)
                if(FlightGlobals.Bodies[i].bodyName==s.Environment.Body) { body=FlightGlobals.Bodies[i];break; }
            if(body!=null && surface)
            {
                latitude=body.GetLatitude(anchor); longitude=body.GetLongitude(anchor);
                surfaceAltitude=body.GetAltitude(anchor);
            }
            float scale=(float)Math.Min(1.8,1+Math.Log(1+cluster.Members)*0.15);
            plan.Radius*=scale; // bounded visual clustering gain
            Quaternion toLocal=Quaternion.Inverse(frame);
            Vector3 localVelocity=toLocal*SnapshotReader.Vector(s.SurfaceVelocity);
            BlastSource[] sources=null;
            if(cluster.SampleCount>0)
            {
                sources=new BlastSource[cluster.SampleCount];
                for(int i=0;i<cluster.SampleCount;i++)
                {
                    Vector3 offset=toLocal*(SnapshotReader.Vector(cluster.SamplePositions[i])-anchor);
                    sources[i]=new BlastSource(new Vec3(offset.x,offset.y,offset.z),cluster.SampleTimes[i]-cluster.FirstTime,
                        cluster.SampleScores[i]/Math.Max(cluster.RepresentativeScore,1e-3));
                }
            }
            layout=BlastLayout.Create(plan,new Vec3(localVelocity.x,localVelocity.y,localVelocity.z),ground,water,(uint)cluster.Id*2654435761u^s.PersistentId,sources);
            double gasTime=Reentry.GasTime(air,layout.RadiusMeters);
            gasRate=double.IsInfinity(gasTime)?0:1/gasTime;
            residue=settings.Residue&&settings.Explosions&&(ground||water)?Residue.Plan(plan,ground,water,settings.ResidueSeconds):default(ResiduePlan);
            if(poolFire==null) residue.FireSeconds=0;
            uint seed=(uint)(cluster.Id*7919)^s.PersistentId;
            Vec3 breeze=Aftermath.Wind(seed,air); Vector3 wind=new Vector3((float)breeze.X,0,(float)breeze.Z);
            flickerPhase=(seed%1000)*0.01f;
            bool fire3d=residue.HasFire&&fireVolume!=null&&settings.Volumetric;
            residueEmitter.Volume=fire3d;
            if(fire3d) fireVolume.Begin(residue.FireRadius,residue.FlameHeight,wind,s.Environment.Gravity,seed);
            else if(fireVolume!=null) fireVolume.Clear();
            if(residue.HasFire)
            {
                residueEmitter.Begin(residue,plan,(uint)cluster.Id*747796405u^s.PersistentId,0,wind);
                SmokeParticles.Bend(column,wind,0.12f); poolFire.Play(); column.Play(); tongues.Play();
            }
            else residueEmitter.Stop();
            scorchSeed=(uint)cluster.Id*2891336453u^s.PersistentId;
            hasScorch=scorch!=null&&residue.ScorchRadius>0&&BuildScorch((float)residue.ScorchRadius);
            groundFireEnd=!residue.HasFire&&ground&&settings.Explosions&&plan.FireFraction>0.05&&air>0.1?0.4f+2.5f+3*(float)plan.FireFraction:0;
            if(volume!=null) { volume.Begin(allowVolume,allowDistortion,cluster.Id,settings.VolumeSteps,ground,water,body,layout); volume.Tick(plan,0,settings,0,1); }
            dome=volume!=null&&volume.Reserved;
            domeRadius=(float)VolumeEvolution.DomeRadius(plan);
            age=0;
            float? floor=null; bool wet=false;
            if(!surface&&body!=null&&air>0.3f)
            {
                double below=DebrisField.Floor(body,anchor,frame*Vector3.up,2);
                double sea=body.ocean?-body.GetAltitude(anchor):double.NegativeInfinity;
                if(sea>below) { below=sea; wet=true; }
                if(below>-1500) floor=(float)below;
            }
            particles.Begin(new BlastContext {
                Plan=plan, Layout=layout, Settings=settings, Kind=kind, Ground=ground, Water=water, Surface=surface, Dome=dome,
                Air=air, DomeRadius=domeRadius, Gravity=(float)s.Environment.Gravity,
                ImpactSpeed=(float)Math.Abs(Vec3.Dot(s.SurfaceVelocity,s.Environment.Up)), Velocity=localVelocity,
                Floor=floor, FloorWet=wet, Sunlit=(float)FxPool.Starlight(anchor), DebrisBudget=debrisBudget, Seed=seed, GasRate=gasRate });
            bool burning=settings.Explosions&&plan.FireFraction>0.05;
            lit=light&&settings.Lighting&&(dome?VolumeEvolution.LightIntensity(volume.State)>0.05:burning);
            flash.enabled=lit;
            flash.color=Color.Lerp(new Color(0.92f,0.94f,1),new Color(1,0.84f,0.62f),air); flash.transform.localPosition=Vector3.zero;
            flash.range=Mathf.Clamp((float)Math.Max(layout.RadiusMeters*2.5,plan.Radius*5),10,120);
            flash.intensity=dome?(float)VolumeEvolution.LightIntensity(volume.State):3*(float)plan.FireFraction;
        }
        internal bool AddSecondary(Vector3 worldPosition,double weight)
        {
            if(!active||volume==null) return false;
            return volume.AddSecondary(worldPosition,age,weight);
        }
        internal float Coverage(Camera cam) { return active?(volume!=null?volume.Coverage(cam):0)+(fireVolume!=null?fireVolume.Coverage(cam):0):0; }
        internal bool Tick(float dt,FxSettings settings,float stepScale)
        {
            if(!active) return false;
            age+=dt;
            if(!surface)
            {
                Vector3d frame=Krakensbane.GetFrameVelocity();
                Vector3d gas=airVelocity+(inertialVelocity-airVelocity)*Math.Exp(-gasRate*age);
                Vector3d solid=airVelocity+(inertialVelocity-airVelocity)*Math.Exp(-0.8*air*age);
                Root.transform.position+=(Vector3)(gas-frame)*dt;
                DebrisRoot.transform.position+=(Vector3)(solid-frame)*dt;
            }
            else if(body!=null)
            {
                Vector3 here=(Vector3)body.GetWorldSurfacePosition(latitude,longitude,surfaceAltitude);
                Root.transform.position=here; DebrisRoot.transform.position=here;
            }
            float groundFire=residue.HasFire?(float)Residue.FireLevel(residue,age):
                age<groundFireEnd?Mathf.Clamp01((groundFireEnd-age)/(groundFireEnd-0.4f)):0;
            if(volume!=null) volume.Tick(plan,age,settings,groundFire,stepScale);
            if(residue.HasFire) residueEmitter.Tick(age,dt,poolFire,column,tongues);
            if(residueEmitter.Volume&&fireVolume!=null)
            {
                float level=residue.HasFire?(float)Residue.FireLevel(residue,age):0;
                float flame=(float)residue.FlameHeight*(0.55f+0.45f*Mathf.Sqrt(level));
                float wobble=1+0.08f*Mathf.Sin(age*9.1f+flickerPhase);
                fireVolume.Tick(level,flame,residue.PuffHz,Mathf.Max(16,40*Mathf.Clamp(stepScale,0.4f,1)),wobble);
                fireTarget=CameraRig.Pick(fireVolume.Center,fireVolume.Reach);
            }
            particles.Tick(age,dt,residue.HasFire?0:groundFire,HasVolume,volume!=null?volume.State:default(VolumeState));
            if(hasScorch)
            {
                scorchProperties.SetVector(ScorchId,new Vector4((float)Residue.ScorchLevel(residue,age),
                    Mathf.Clamp01(1-age/14)*(float)plan.FireFraction*(air>0.1f?1:0),scorchSeed%997,air<0.1f?1:0));
                scorch.SetPropertyBlock(scorchProperties);
            }
            float flicker=1+0.15f*(0.6f*Mathf.Sin(age*13.7f+flickerPhase)+0.4f*Mathf.Sin(age*23.3f+flickerPhase*2.3f));
            if(HasVolume)
            {
                float intensity=(float)VolumeEvolution.LightIntensity(volume.State)*(age>0.3f?flicker:1);
                flash.enabled=lit&&intensity>0.03f; flash.intensity=intensity;
                flash.transform.localPosition=Vector3.up*(float)(VolumeEvolution.LightHeight(volume.State)*volume.State.BoxRadius);
            }
            else if(groundFire>0&&lit)
            {
                flash.enabled=true; flash.intensity=0.8f*groundFire*flicker;
                flash.range=residue.HasFire?Mathf.Max(domeRadius,(float)residue.FireRadius*8):domeRadius;
                flash.transform.localPosition=Vector3.up*(residue.HasFire?(float)residue.FlameHeight*0.35f:1);
            }
            else if(age>0.18f) flash.enabled=false;
            else flash.intensity=3*(float)plan.FireFraction*Mathf.Pow(Mathf.Clamp01(1-age/0.18f),2);
            float glow=HasVolume?(float)VolumeEvolution.LightIntensity(volume.State):0, pool=groundFire*1.4f;
            FireGlow=Mathf.Max(glow,pool)*(age>0.3f?flicker:1);
            FirePosition=glow>=pool?flash.transform.position:Root.transform.TransformPoint(Vector3.up*(residue.HasFire?(float)residue.FlameHeight*0.35f:1));
            FireRange=glow>=pool?Mathf.Max(flash.range,10):Mathf.Max((float)residue.FireRadius*8+5,10);
            bool alive=particles.Alive||(poolFire!=null&&(poolFire.IsAlive()||column.IsAlive()||tongues.IsAlive()))||FireShown;
            bool refracting=volume!=null&&volume.Distorting;
            double residueEnd=Math.Max(residue.HasFire?residue.SmokeSeconds:0,hasScorch?residue.ScorchSeconds:0);
            bool lingering=age<residueEnd;
            double limit=Math.Max(Math.Max(VolumeEvolution.VolumeDuration(plan),groundFireEnd)+4,Math.Max(residueEnd+14,particles.Horizon));
            if(age>limit||(!HasVolume&&!refracting&&!alive&&!particles.Pending&&groundFire<=0&&!lingering))
            { Clear(); return false; }
            return true;
        }
        internal void Shift(Vector3 offset)
        {
            if(!active) return;
            Root.transform.position-=offset; DebrisRoot.transform.position-=offset;
        }
        bool BuildScorch(float radius)
        {
            Transform frame=Root.transform; int mask=SnapshotReader.SurfaceMask();
            bool built=ScorchMesh.Build(scorchMesh,radius,16,(Vector3 local,out Vector3 point,out Vector3 normal)=>
            {
                Vector3 world=frame.TransformPoint(local), up=frame.up; RaycastHit hit;
                if(Physics.Raycast(world+up*(radius+5),-up,out hit,2*radius+10,mask,QueryTriggerInteraction.Ignore)&&
                    hit.collider!=null&&hit.collider.GetComponentInParent<Part>()==null)
                { point=frame.InverseTransformPoint(hit.point); normal=frame.InverseTransformDirection(hit.normal); return true; }
                point=local; normal=Vector3.up; return false;
            });
            scorch.enabled=built;
            return built;
        }
        internal void Clear()
        {
            if(volume!=null) volume.Clear();
            particles.Clear();
            if(poolFire!=null) { poolFire.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); column.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); tongues.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
            residueEmitter.Stop(); residue=default(ResiduePlan); FireGlow=0;
            if(fireVolume!=null) fireVolume.Clear(); fireTarget=null;
            if(scorch!=null) scorch.enabled=false; hasScorch=false;
            active=false; flash.enabled=false; layout=null;
            Root.SetActive(false); DebrisRoot.SetActive(false);
        }
        public void Dispose() { UnityEngine.Object.Destroy(Root); UnityEngine.Object.Destroy(DebrisRoot); }
    }
}
