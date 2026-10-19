using System;
using UnityEngine;
using VolumetricExplosionFX.Core;
using VolumetricExplosionFX.Ksp;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class FxInstance : IDisposable
    {
        // The ground round a nuclear blast, for its dust wall: a polar map of the surface's height (terrain, or the
        // sea where it is lower) in the blast frame, filled from the inside out over a few frames (KSP's terrain heights,
        // not colliders: they are not loaded kilometres away).
        const int GroundAngles=64, GroundRings=16;
        // The nuclear cloud's shadow on the land (in sunlight). (laid on what the camera sees, from the
        // scene's depth (NukeShadow.shader), on a box round the blast: a mesh laid on the coarse ground map dipped under
        // the hills and left notches in it.)
        bool nukeShadow;
        readonly MeshRenderer nukeShade; readonly MaterialPropertyBlock nukeShadeProperties=new MaterialPropertyBlock();
        static readonly int NukeShadow0=Shader.PropertyToID("_NkSh0"), NukeShadow1=Shader.PropertyToID("_NkSh1"), NukeShadow2=Shader.PropertyToID("_NkSh2"),
            NukeFrameId=Shader.PropertyToID("_NkFrame"), NukeReachId=Shader.PropertyToID("_NkReach");
        void UpdateNukeShadow()
        {
            NukeFrame f=volume.Nuke; Vector4 sun=volume.Sun;
            float fade=(float)f.Dust.W, box=(float)volume.State.BoxRadius, reach=0.95f*box;
            bool on=volume.Active&&fade>0.01f&&sun.y>0.05f&&sun.w>0.05f&&box>0;
            nukeShade.enabled=on;
            if(!on) return;
            // The box: the ground round the blast, from below its lowest ground to above its highest.
            float low=groundPixels!=null&&groundNext<0?groundLow*box:-0.4f*box, high=groundPixels!=null&&groundNext<0?groundHigh*box:0.4f*box;
            Transform t=nukeShade.transform;
            t.localPosition=new Vector3(0,0.5f*(low+high)+200,0); t.localRotation=Quaternion.identity;
            t.localScale=new Vector3(2*reach,high-low+1000,2*reach);
            nukeShadeProperties.SetMatrix(NukeFrameId,Root.transform.worldToLocalMatrix);
            nukeShadeProperties.SetVector(NukeReachId,new Vector4(reach,low,high,0));
            nukeShadeProperties.SetVector(SunId,sun);
            nukeShadeProperties.SetVector(ShadowId,new Vector4(box,0.5f,1.4f,scorchSeed%997));
            // Cap (sheared downwind as the shader draws it), its half height, the stem's radius, top and foot; strength.
            nukeShadeProperties.SetVector(NukeShadow0,new Vector4((float)f.Late.Y,(float)f.Ball.X,(float)f.Late.Z,(float)f.Ball.Y));
            nukeShadeProperties.SetVector(NukeShadow1,new Vector4((float)Math.Max(f.Ball.Z*1.1,0.5*f.Ball.Y),(float)(f.Stem.Y*1.1),(float)f.Stem.Z,(float)f.Stem.X));
            nukeShadeProperties.SetVector(NukeShadow2,new Vector4(1,(float)f.Stem.W,fade*(1-0.8f*(float)f.Late.X),0));
            nukeShade.SetPropertyBlock(nukeShadeProperties);
        }
        Texture2D groundMap; Color[] groundPixels; int groundNext=-1; float groundLow, groundHigh;
        void FillGround(int budget)
        {
            if(body==null||volume==null||volume.State.BoxRadius<=0) return;
            int total=GroundAngles*GroundRings;
            if(groundMap==null)
            {
                groundMap=new Texture2D(GroundAngles,GroundRings,TextureFormat.RGFloat,false,true);
                groundMap.wrapModeU=TextureWrapMode.Repeat; groundMap.wrapModeV=TextureWrapMode.Clamp; groundMap.filterMode=FilterMode.Bilinear;
                groundPixels=new Color[total];
            }
            if(groundNext==0) { for(int i=0;i<total;i++) groundPixels[i]=new Color(0,0,0,0); groundLow=0; groundHigh=0; }
            double box=volume.State.BoxRadius; float reach=(float)(0.95*box);
            Transform frame=Root.transform;
            int end=Math.Min(total,groundNext+budget);
            for(;groundNext<end;groundNext++)
            {
                int ring=groundNext/GroundAngles, a=groundNext%GroundAngles;
                float ang=((a+0.5f)/GroundAngles-0.5f)*2*Mathf.PI, r=ring/(float)(GroundRings-1)*reach;
                Vector3 world=frame.TransformPoint(new Vector3(Mathf.Cos(ang)*r,0,Mathf.Sin(ang)*r));
                double lat=body.GetLatitude(world), lon=body.GetLongitude(world), terrain=body.TerrainAltitude(lat,lon,true);
                if(double.IsNaN(terrain)) terrain=0;
                bool sea=body.ocean&&terrain<0;
                float y=frame.InverseTransformPoint((Vector3)body.GetWorldSurfacePosition(lat,lon,sea?0:terrain)).y/(float)box;
                groundPixels[groundNext]=new Color(y,sea?1:0,0,0);
                groundLow=Mathf.Min(groundLow,y); groundHigh=Mathf.Max(groundHigh,y);
            }
            groundMap.SetPixels(groundPixels); groundMap.Apply(false,false);
            volume.GroundMap=groundMap; volume.GroundInfo=new Vector4(1,(float)(reach/box),groundLow,groundHigh);
            if(groundNext>=total) groundNext=-1;
        }
        internal readonly GameObject Root, DebrisRoot;
        readonly BlastParticles particles;
        readonly ParticleSystem poolFire, column, tongues;
        readonly FireVolumeRenderer fireVolume;      // the pool fire in 3D (null: flame particles)
        readonly SplashVolume splashVolume;          // 1.1: a water impact's splash in 3D (null: the sprite column)
        Camera fireTarget;
        readonly MeshRenderer scorch, scar;
        readonly Mesh scorchMesh, scarMesh;
        readonly MaterialPropertyBlock scarProperties=new MaterialPropertyBlock();
        static readonly int StripId=Shader.PropertyToID("_Strip"), BlobsId=Shader.PropertyToID("_Blobs");
        static readonly int ShadowFrameId=Shader.PropertyToID("_ShadowFrame"), ShadowAreaId=Shader.PropertyToID("_ShadowArea");
        Vector4 shadowArea;
        float scarBuilt;   // strip length the scar mesh was laid for
        readonly MeshRenderer shadow;        // ground shadow of the fireball and its smoke (1.1)
        readonly Mesh shadowMesh;
        readonly MaterialPropertyBlock shadowProperties=new MaterialPropertyBlock();
        bool hasShadow;
        readonly MeshRenderer wave;          // dust lifted by the blast wave running along the ground (1.1)
        readonly Mesh waveMesh;
        readonly MaterialPropertyBlock waveProperties=new MaterialPropertyBlock();
        bool hasWave; double waveReach;
        static readonly int WaveId=Shader.PropertyToID("_Wave"), WaveGroundId=Shader.PropertyToID("_Ground"), WaveDustId=Shader.PropertyToID("_Dust"), WaveTrailId=Shader.PropertyToID("_Trail");
        static readonly int SunId=Shader.PropertyToID("_Sun"), ShadowId=Shader.PropertyToID("_Shadow");
        static readonly int[] ShadowLobeIds={Shader.PropertyToID("_L0"),Shader.PropertyToID("_L1"),Shader.PropertyToID("_L2"),Shader.PropertyToID("_L3")},
            ShadowAxisIds={Shader.PropertyToID("_A0"),Shader.PropertyToID("_A1"),Shader.PropertyToID("_A2"),Shader.PropertyToID("_A3")},
            ShadowOriginIds={Shader.PropertyToID("_D0"),Shader.PropertyToID("_D1"),Shader.PropertyToID("_D2"),Shader.PropertyToID("_D3")};
        static readonly int SimOnId=Shader.PropertyToID("_SimOn"), SimShapeId=Shader.PropertyToID("_SimShape"), SimVariantId=Shader.PropertyToID("_SimVariant"), SimStemId=Shader.PropertyToID("_SimStem"), NukeRingId=Shader.PropertyToID("_NukeRing");
        readonly MaterialPropertyBlock scorchProperties=new MaterialPropertyBlock();
        readonly ResidueEmitter residueEmitter=new ResidueEmitter();
        static readonly int ScorchId=Shader.PropertyToID("_Scorch"), ScorchLookId=Shader.PropertyToID("_ScorchLook"), ScorchSoilId=Shader.PropertyToID("_ScorchSoil");
        readonly Light flash;
        readonly HeroVolume volume;
        internal bool HasVolume { get { return active&&volume!=null&&volume.Active; } }
        internal bool ReservedVolume { get { return active&&volume!=null&&volume.Reserved; } }
        // 1.1 pool fires merge: a burning pool that a new spill nearby can feed.
        // The smoke's shadow, so that the pool lays only one where several overlap (each darkens what is under it:
        // the shadows of a crash's blasts multiplied to black, over the vessels standing there too).
        internal float ShadowWeight;
        internal bool ShadowOn { get { return active&&shadow!=null&&shadow.enabled; } }
        internal Vector3 ShadowCentre { get { return Root.transform.TransformPoint(new Vector3(shadowArea.x,0,shadowArea.y)); } }
        internal float ShadowRadius { get { return shadowArea.z; } }
        internal void HideShadow() { if(shadow!=null) shadow.enabled=false; }
        internal bool PoolBurning { get { return active&&residue.HasFire&&plan!=null&&plan.Look!=null&&plan.Look.Modern&&age<residue.FireSeconds*0.85; } }
        internal Vector3 PoolCenter { get { return Root.transform.position; } }
        internal ResiduePlan Pool { get { return residue; } }
        // Burning fuel thrown ahead by a sideways crash (FlightController fire trail), and how fast it was going.
        internal bool TrailFire { get; private set; }
        internal float TrailSpeed { get { return plan!=null?(float)plan.ImpactSpeed:0; } }
        internal bool Extend(Vector3 world,ResiduePlan other,float speed)
        {
            if(fireVolume==null||!residueEmitter.Volume) return false;
            Vector3 local=Quaternion.Inverse(Root.transform.rotation)*(world-Root.transform.position);
            var flat=new Vector2(local.x,local.z); float d=flat.magnitude;
            if(d<1||d>170||!fireVolume.Extend(flat/d,d,speed)) return false;
            residue=residue.Joined(other,age); residueEmitter.Grow(residue);
            return true;
        }
        internal void Feed(ResiduePlan other)
        {
            residue=residue.Fed(other,age);
            if(fireVolume!=null) fireVolume.Resize(residue.FireRadius,residue.FlameHeight);
            residueEmitter.Grow(residue);
        }
        internal void DropPool()
        {
            residue.FireSeconds=0; residue.SmokeSeconds=0; residueEmitter.Stop();
            if(fireVolume!=null) fireVolume.Clear();
            if(poolFire!=null) { poolFire.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); column.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); tongues.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
        }
        // A 1.1 simulated smoke cloud a lamp of the scene may light: its centre (world) and radius (m).
        internal bool SimSmoke { get { return HasVolume&&volume.State.Sim; } }
        internal Vector3 SmokeCenter { get { return Root.transform.position+Root.transform.up*(float)(volume.State.BoxRadius*0.5); } }
        internal float SmokeSize { get { return (float)volume.State.BoxRadius; } }
        internal double Score { get { return plan==null?0:plan.VisualEnergyScore*(Lingering?0.25:1); } }
        bool Lingering { get { return plan!=null&&age>Math.Max(VolumeEvolution.VolumeDuration(plan),groundFireEnd)+2; } }
        internal float Age { get { return age; } }
        internal bool Nuclear { get { return plan!=null&&plan.Warhead==WarheadKind.Nuke; } }
        internal double FireballRadius { get { return plan==null?0:VolumeEvolution.FireballRadius(plan); } }
        internal double CoreRadius { get { return plan==null?0:Math.Max(2,plan.Radius*0.5); } }   // inside the fireball itself
        internal double CoverRadius { get { return plan==null?0:ReservedVolume?VolumeEvolution.DomeRadius(plan)*1.5:Math.Max(4,plan.Radius*1.5); } }
        internal void ReleaseVolume() { if(volume!=null) volume.Retire(); }
        internal void ShowFor(Camera cam,LowResVolumes lowRes=null)
        {
            if(!active) return;
            if(volume!=null) volume.ShowFor(cam,lowRes);
            if(fireVolume!=null) fireVolume.Show(CameraRig.IsFlight(cam)&&(fireTarget==null||cam==fireTarget),lowRes);
            if(splashVolume!=null) splashVolume.Show(cam,lowRes);
        }
        bool FireShown { get { return fireVolume!=null&&fireVolume.Visible; } }
        internal bool HasFireVolume { get { return active&&FireShown; } }
        internal bool NeedsNearDepth { get { return active&&(volume!=null&&volume.NeedsNearDepth||FireShown&&fireTarget!=CameraRig.Far||splashVolume!=null&&splashVolume.NeedsNearDepth); } }
        internal bool NeedsFarDepth { get { return active&&(volume!=null&&volume.NeedsFarDepth||FireShown&&(fireTarget==null||fireTarget==CameraRig.Far)||splashVolume!=null&&splashVolume.NeedsFarDepth); } }
        internal bool HasSplash { get { return active&&splashVolume!=null&&splashVolume.Visible; } }
        FxPlan plan; EventKind kind;
        BlastLayout layout;
        ResiduePlan residue;
        bool hasScorch;
        uint scorchSeed;
        Vector4 scorchLook;   // the 1.1 mark: crater, drawn out, direction (Residue.ScorchLook)
        internal float FireGlow { get; private set; }
        internal Vector3 FirePosition { get; private set; }
        internal float FireRange { get; private set; }
        Vector3d inertialVelocity, airVelocity;
        CelestialBody body;
        double latitude,longitude,surfaceAltitude;
        float age,groundFireEnd,domeRadius,air,flickerPhase;
        double gasRate;
        bool active, surface, lit, dome, lightAllowed;
        float lightScale=1;
        // Light this blast would like to cast this frame (0 = none); FxPool decides which are shown.
        internal float LightWanted { get; private set; }
        internal float LightRange { get { return flash.range; } }
        internal Vector3 LightPosition { get { return flash.transform.position; } }
        internal int ReservedDebris { get { return active?particles.ReservedDebris:0; } }
        internal bool HasLight { get { return active && flash.enabled; } }
        internal LookKind LookKind { get; private set; }
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
            if(volumes.SplashMaterial!=null&&volumes.Cube!=null) splashVolume=new SplashVolume(Root.transform,volumes.Cube,volumes.SplashMaterial);
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
                var scarObject=new GameObject("burnt crash path"); scarObject.layer=EffectLayer.Value; scarObject.transform.SetParent(Root.transform,false);
                scarMesh=new Mesh(); scarMesh.name="Original scar draped along a crash path";
                scarObject.AddComponent<MeshFilter>().sharedMesh=scarMesh;
                scar=scarObject.AddComponent<MeshRenderer>(); scar.sharedMaterial=volumes.Scorch;
                scar.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; scar.receiveShadows=false; scar.enabled=false;
            }
            if(volumes.Shadow!=null)
            {
                var shadowObject=new GameObject("smoke shadow"); shadowObject.layer=EffectLayer.Value; shadowObject.transform.SetParent(Root.transform,false);
                // A box round the blast: the shadow is laid from the scene's depth (SmokeShadow).
                shadowMesh=Resources.GetBuiltinResource<Mesh>("Cube.fbx");   // (no collider: CreatePrimitive's would stay a frame)
                shadowObject.AddComponent<MeshFilter>().sharedMesh=shadowMesh;
                shadow=shadowObject.AddComponent<MeshRenderer>(); shadow.sharedMaterial=volumes.Shadow;
                if(volumes.NukeShadow!=null)
                {
                    var shadeObject=new GameObject("nuclear cloud shadow");
                    shadeObject.layer=EffectLayer.Value; shadeObject.transform.SetParent(Root.transform,false);
                    shadeObject.AddComponent<MeshFilter>().sharedMesh=Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                    nukeShade=shadeObject.AddComponent<MeshRenderer>(); nukeShade.sharedMaterial=volumes.NukeShadow;
                    nukeShade.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; nukeShade.receiveShadows=false; nukeShade.enabled=false;
                }
                shadow.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; shadow.receiveShadows=false; shadow.enabled=false;
            }
            if(volumes.GroundWave!=null)
            {
                var waveObject=new GameObject("ground wave"); waveObject.layer=EffectLayer.Value; waveObject.transform.SetParent(Root.transform,false);
                waveMesh=new Mesh(); waveMesh.name="Original ground wave disk draped on the ground";
                waveObject.AddComponent<MeshFilter>().sharedMesh=waveMesh;
                wave=waveObject.AddComponent<MeshRenderer>(); wave.sharedMaterial=volumes.GroundWave;
                wave.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; wave.receiveShadows=false; wave.enabled=false;
            }
            Root.SetActive(false); DebrisRoot.SetActive(false);
        }
        // Time spent starting explosions, by stage (ms, summed over the frame; FlightController reads and clears it):
        // set-up and layout, smoke shadow, volume, particles, residue and scorch.
        internal static readonly double[] BeginMs=new double[5];
        static long stageAt;
        static void Stage(int i) { long now=System.Diagnostics.Stopwatch.GetTimestamp(); BeginMs[i]+=(now-stageAt)*1000.0/System.Diagnostics.Stopwatch.Frequency; stageAt=now; }
        internal void Begin(EventCluster cluster,FxPlan fx,FxSettings settings,int debrisBudget,bool light,bool allowVolume,bool allowDistortion,bool allowFireVolume=true)
        {
            stageAt=System.Diagnostics.Stopwatch.GetTimestamp();
            Clear(); active=true; plan=fx; kind=cluster.Representative.Kind;
            TrailFire=cluster.Representative.Part.Name=="fire trail";
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
            // A warhead or part that blew up already inside the ground (a fast bomb or missile deeper than the
            // buried check reaches, or under the terrain's visible surface) was drawn under it: lift the blast to the surface.
            // A nuclear burst within a few metres of the surface is a ground (or sea) burst.
            if(body!=null&&body.pqsController!=null&&kind!=EventKind.WaterImpact)
            {
                double terrainAt=body.TerrainAltitude(body.GetLatitude(anchor),body.GetLongitude(anchor),false), altAt=body.GetAltitude(anchor);
                bool nuclear=plan.Warhead==WarheadKind.Nuke;
                double margin=nuclear?Math.Max(3,0.08*VolumeEvolution.FireballRadius(plan)):-1;
                if(!double.IsNaN(terrainAt)&&altAt<terrainAt+margin)
                {
                    anchor+=(frame*Vector3.up)*(float)(terrainAt-altAt);
                    Root.transform.position=anchor; DebrisRoot.transform.position=anchor;
                    if(body.ocean&&terrainAt<=0.01) { if(nuclear&&settings.Water) water=true; }
                    else if(settings.Ground) ground=true;
                    surface=ground||water||kind!=EventKind.Destruction;
                    if(nuclear) Debug.Log("[VEFX] Nuclear burst "+(altAt-terrainAt).ToString("0")+" m from the surface: drawn on it");
                }
            }
            if(body!=null && surface)
            {
                latitude=body.GetLatitude(anchor); longitude=body.GetLongitude(anchor);
                surfaceAltitude=body.GetAltitude(anchor);
            }
            float scale=(float)Math.Min(1.8,1+Math.Log(1+cluster.Members)*0.15);
            plan.Radius*=scale; // bounded visual clustering gain
            Quaternion toLocal=Quaternion.Inverse(frame);
            Vector3 localVelocity=toLocal*SnapshotReader.Vector(s.SurfaceVelocity);
            // A warhead going off on the ground or water (a missile or bomb part dying on impact) keeps none of its dive:
            // the fireball and its debris went on into the ground with the weapon's speed.
            if(fx.Warhead!=WarheadKind.None&&(ground||water)) localVelocity=new Vector3(localVelocity.x*0.05f,0,localVelocity.z*0.05f);
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
            bool reentry=Reentry.Applies(plan,surface,plan.ImpactSpeed);
            LookKind=settings.Looks.Choose(LookProfile.Select(plan,surface,reentry,cluster.Members));
            plan.Look=settings.Looks.For(LookKind);
            Chemistry.Apply(plan);
            Color soil;
            if(ground&&settings.Ground&&SnapshotReader.GroundColour(anchor,frame*Vector3.up,20,out soil)) Chemistry.GroundDust(plan,soil.r,soil.g,soil.b);
            uint salt=FxPool.NextSalt();
            Chemistry.Vary(plan,salt^s.PersistentId);
            if(plan.Warhead==WarheadKind.Nuke&&!ground&&!water&&body!=null)
            {
                // Height above the ground (or the sea) under the burst.
                double terrain=body.TerrainAltitude(body.GetLatitude(anchor),body.GetLongitude(anchor),false);
                plan.BurstHeight=Math.Max(0,body.GetAltitude(anchor)-terrain);
            }
            layout=BlastLayout.Create(plan,new Vec3(localVelocity.x,localVelocity.y,localVelocity.z),ground,water,(uint)cluster.Id*2654435761u^s.PersistentId^salt,sources);
            groundNext=plan.Warhead==WarheadKind.Nuke&&plan.Look!=null&&plan.Look.Modern&&body!=null&&air>0.3f?0:-1;
            if(plan.Warhead==WarheadKind.Nuke)
                Debug.Log("[VEFX] Nuclear burst: "+(plan.WarheadTnt/1e6).ToString("0.##")+" kt, "+layout.Nuke+(ground?" on the ground":water?" on the sea":", "+plan.BurstHeight.ToString("0")+" m above the ground"));
            double gasTime=Reentry.GasTime(air,layout.RadiusMeters);
            gasRate=double.IsInfinity(gasTime)?0:1/gasTime;
            Stage(0);
            residue=settings.Residue&&settings.Explosions&&(ground||water)?Residue.Plan(plan,ground,water,settings.ResidueSeconds):default(ResiduePlan);
            if(poolFire==null) residue.FireSeconds=0;
            if(residue.Patchy>0&&residue.HasFire)
            {
                // No two spills alike: size, height and start vary per explosion.
                var vary=new SplitRandom((uint)cluster.Id*40503u^s.PersistentId^0xF1A3u^salt);
                residue.FireRadius*=vary.Range(0.75,1.3); residue.FlameHeight*=vary.Range(0.6,1.25); residue.Delay*=vary.Range(0.8,1.4);
            }
            uint seed=(uint)(cluster.Id*7919)^s.PersistentId^salt;
            Vec3 breeze=Aftermath.Wind(seed,air,plan.Look!=null&&plan.Look.Modern); Vector3 wind=new Vector3((float)breeze.X,0,(float)breeze.Z);
            flickerPhase=(seed%1000)*0.01f;
            bool fire3d=residue.HasFire&&fireVolume!=null&&settings.Volumetric&&allowFireVolume;
            residueEmitter.Volume=fire3d;
            if(fire3d) fireVolume.Begin(residue.FireRadius,residue.FlameHeight,wind,s.Environment.Gravity,seed,residue.Patchy);
            else if(fireVolume!=null) fireVolume.Clear();
            // A water impact's splash as a volume, on the sea under the blast (the part died a little above or
            // below it).
            float seaLevel=body!=null&&body.ocean?(float)Numbers.Clamp(-surfaceAltitude,-40,40):0;
            bool splash3d=splashVolume!=null&&water&&settings.Volumetric&&
                splashVolume.Begin(plan,true,(float)s.Environment.Gravity,localVelocity,wind,seed,seaLevel,body);
            if(!splash3d&&splashVolume!=null) splashVolume.Clear();
            if(residue.HasFire)
            {
                residueEmitter.Begin(residue,plan,(uint)cluster.Id*747796405u^s.PersistentId^salt,0,wind);
                residueEmitter.Creep((float)Aftermath.CreepSpeed(plan));
                SmokeParticles.Bend(column,wind,0.12f); poolFire.Play(); column.Play(); tongues.Play();
            }
            else residueEmitter.Stop();
            nukeShadow=plan.Warhead==WarheadKind.Nuke&&plan.Look!=null&&plan.Look.Modern&&nukeShade!=null&&settings.Lighting&&settings.Volumetric&&air>0.3f&&body!=null;
            scorchSeed=(uint)cluster.Id*2891336453u^s.PersistentId^salt;
            boltRandom=new SplitRandom(scorchSeed^0xB0175u); nextBolt=0;
            popNext=0;
            popCount=(ground||water)&&settings.Explosions?Aftermath.Pops(plan,scorchSeed,Math.Max(residue.FireRadius,0.5*layout.RadiusMeters),pops):0;
            Array.Sort(pops,0,popCount,PopOrder);
            Vec4 markLook=Residue.ScorchLook(plan,localVelocity.x,localVelocity.y,localVelocity.z);
            scorchLook=new Vector4((float)markLook.X,(float)markLook.Y,(float)markLook.Z,(float)markLook.W);
            hasScorch=scorch!=null&&residue.ScorchRadius>0&&BuildScorch((float)residue.ScorchRadius);
            Stage(4);
            groundFireEnd=!residue.HasFire&&ground&&settings.Explosions&&plan.FireFraction>0.05&&air>0.1&&plan.Warhead!=WarheadKind.Nuke?0.4f+2.5f+3*(float)plan.FireFraction:0;
            if(volume!=null) { volume.Begin(allowVolume,allowDistortion,(int)((uint)cluster.Id^salt)&0x7fffffff,settings.VolumeSteps,ground,water,body,layout); volume.Tick(plan,0,settings,0,1); }
            dome=volume!=null&&volume.Reserved;
            Stage(2);
            // (Not a nuclear cloud: its own renderer does not follow the lobes the shadow is traced through.)
            hasShadow=shadow!=null&&dome&&ground&&plan.Look!=null&&plan.Look.Modern&&settings.Lighting&&settings.Volumetric&&plan.Warhead!=WarheadKind.Nuke&&
                BuildShadowDrape();
            waveReach=VolumeEvolution.GroundWaveReach(plan,ground,water);
            hasWave=wave!=null&&dome&&settings.Shockwave&&waveReach>0&&BuildWave();
            if(wave!=null) wave.enabled=false;
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
            Stage(1);
            particles.Begin(new BlastContext {
                Plan=plan, Layout=layout, Settings=settings, Kind=kind, Ground=ground, Water=water, Surface=surface, Dome=dome,
                Air=air, DomeRadius=domeRadius, Gravity=(float)s.Environment.Gravity,
                ImpactSpeed=(float)Math.Abs(Vec3.Dot(s.SurfaceVelocity,s.Environment.Up)), Velocity=localVelocity,
                Floor=floor, FloorWet=wet, Sunlit=(float)FxPool.Starlight(anchor), DebrisBudget=debrisBudget, Seed=seed, GasRate=gasRate, SplashVolume=splash3d });
            Stage(3);
            bool burning=settings.Explosions&&plan.FireFraction>0.05;
            lit=light&&settings.Lighting&&(dome?VolumeEvolution.LightIntensity(volume.State)>0.05:burning);
            lightScale=(float)VolumeEvolution.Look(plan).LightIntensity;
            lightAllowed=false; flash.enabled=false;
            flash.color=Color.Lerp(new Color(0.92f,0.94f,1),new Color(1,0.84f,0.62f),air); flash.transform.localPosition=Vector3.zero;
            flash.range=Mathf.Clamp((float)Math.Max(layout.RadiusMeters*2.5,plan.Radius*5),10,120);
            // A nuclear flash lights the land for kilometres around.
            if(plan.Warhead==WarheadKind.Nuke&&plan.Look!=null&&plan.Look.Modern) flash.range=Mathf.Clamp((float)layout.RadiusMeters*10,800,12000);
            flash.intensity=dome?(float)VolumeEvolution.LightIntensity(volume.State):3*(float)plan.FireFraction*lightScale;
            LightWanted=lit?flash.intensity:0;
        }
        // Runs the particle start-up once, out of sight, while the flight scene loads: Unity sets up its particle
        // systems on their first use, which otherwise stalls the frame of the first explosion (~10 ms).
        internal void WarmUp(FxSettings settings)
        {
            foreach(bool solid in new[]{false,true})
            {
                var plan=new FxPlan { PartKind=solid?PartKind.SolidBooster:PartKind.LiquidTank, Kind=EventKind.GroundImpact, Atmosphere=1, ImpactSpeed=60,
                    Radius=6, FireFraction=0.99, SmokeFraction=0.99, VisualEnergyScore=4.7, BurnableKg=solid?7000:1500, Soot=solid?0:0.6, WhiteFlame=solid?1:0,
                    ExpansionSpeed=6, Lifetime=6, DebrisCount=Math.Min(40,settings.ParticlesPerEvent), PuffCount=30, Look=LookProfile.Default(solid?LookKind.Solid:LookKind.Tank) };
                var blast=BlastLayout.Create(plan,new Vec3(0,-60,0),true,false,1,null);
                Root.SetActive(true); DebrisRoot.SetActive(true);
                particles.Begin(new BlastContext { Plan=plan, Layout=blast, Settings=settings, Kind=EventKind.GroundImpact, Ground=true, Surface=true, Dome=true,
                    Air=1, DomeRadius=(float)VolumeEvolution.DomeRadius(plan), Gravity=9.81f, ImpactSpeed=60, Velocity=new Vector3(0,-60,0), Floor=0f,
                    Sunlit=1, DebrisBudget=0, Seed=1 });
                particles.Tick(0.02f,0.02f,0,false,default(VolumeState));
                particles.Clear();
            }
            Root.SetActive(false); DebrisRoot.SetActive(false);
        }
        // Secondary explosions in the burning wreck, handed out as they fall due: where (world) and how big (m).
        readonly Aftermath.Pop[] pops=new Aftermath.Pop[5];
        int popCount, popNext;
        static readonly System.Collections.Generic.IComparer<Aftermath.Pop> PopOrder=System.Collections.Generic.Comparer<Aftermath.Pop>.Create((a,b)=>a.At.CompareTo(b.At));
        internal bool Pop(out Vector3 at,out float size)
        {
            at=Vector3.zero; size=0;
            if(!active||popNext>=popCount||pops[popNext].At>age) return false;
            Aftermath.Pop p=pops[popNext++];
            at=Root.transform.TransformPoint(new Vector3((float)p.X,0.4f,(float)p.Z)); size=(float)p.Size;
            return true;
        }
        // A lightning stroke due in this blast's cloud (very large blasts only): its ends in the world and its width.
        float nextBolt;
        internal bool Bolt(float dt,out Vector3 a,out Vector3 b,out float width)
        {
            a=b=Vector3.zero; width=0;
            if(!active||volume==null||!volume.Active||plan==null) return false;
            double rate=Aftermath.LightningRate(plan,age);
            if(rate<=0) return false;
            nextBolt-=dt;
            if(nextBolt>0) return false;
            nextBolt=(float)(-Math.Log(Math.Max(1-boltRandom.Next(),1e-4))/rate);
            VolumeState s=volume.State;
            float box=(float)s.BoxRadius, r=(float)Math.Max(s.FireRadius*s.BoxRadius,2);
            Vector3 up=Root.transform.up, centre=Root.transform.position+up*(float)(s.FireRise*s.BoxRadius);
            if(Nuclear&&volume.Nuke.Ball.Y>0)
            {
                // In a nuclear cloud: in its cap and down its stem (it has its own shape, not the fireball's lobes).
                NukeFrame f=volume.Nuke;
                r=(float)Math.Max(f.Ball.Y*box*0.8,2); centre=Root.transform.position+up*(float)((f.Ball.X-0.2*f.Ball.Z)*box);
            }
            // Strokes show where the cloud does not hide them: along its underside and rim, and down the column to the
            // ground (one in four). The simulated gas spans its bounding sphere from the ground up: the cap's underside is
            // about a fifth of the radius above the centre.
            Vector3 side=Vector3.ProjectOnPlane(Dir(),up).normalized, side2=Vector3.ProjectOnPlane(Dir(),up).normalized;
            Vector3 underside=centre+up*r*0.2f;
            a=underside+side*r*(0.1f+0.3f*(float)boltRandom.Next());
            if(boltRandom.Next()<0.25&&surface) b=Root.transform.position+side2*r*0.08f;
            else b=underside+side2*r*(0.2f+0.3f*(float)boltRandom.Next())-up*r*(float)(0.05+0.15*boltRandom.Next());
            width=Mathf.Clamp(r*0.025f,0.4f,10f);
            return box>0;
        }
        Vector3 Dir() { double z=boltRandom.Next()*2-1, t=boltRandom.Next()*Math.PI*2, k=Math.Sqrt(1-z*z); return new Vector3((float)(k*Math.Cos(t)),(float)z,(float)(k*Math.Sin(t))); }
        SplitRandom boltRandom=new SplitRandom(0xB0175u);
        internal bool AddSecondary(Vector3 worldPosition,double weight)
        {
            if(!active||volume==null) return false;
            return volume.AddSecondary(worldPosition,age,weight);
        }
        internal float Coverage(Camera cam) { return active?(volume!=null?volume.Coverage(cam):0)+(fireVolume!=null?fireVolume.Coverage(cam):0)+(splashVolume!=null?splashVolume.Coverage(cam):0):0; }
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
            // Once the fire is out, the hot wreck keeps the air trembling for about a minute (distortion only).
            float fireOut=residue.HasFire?(float)residue.FireSeconds:groundFireEnd;
            float shimmer=(float)Aftermath.Afterheat(fireOut,age);
            if(volume!=null) volume.Tick(plan,age,settings,Mathf.Max(groundFire,shimmer),stepScale);
            if(residue.HasFire)
            {
                // Under the blast's rising cloud the fire's smoke is drawn up into it: fast enough to reach its
                // base in about six seconds, while the cloud is there.
                float draft=0;
                if(HasVolume&&volume.State.Sim&&surface&&plan.Look!=null&&plan.Look.Modern&&plan.Warhead!=WarheadKind.Nuke)
                {
                    VolumeState vs=volume.State;
                    float baseY=(float)((vs.Lobe0.Center.Y-0.5*vs.Lobe0.Radius)*vs.BoxRadius);
                    draft=Mathf.Clamp(baseY/6,0,10)*(float)Numbers.Clamp(vs.Fade*(1-vs.Dissolve),0,1);
                }
                residueEmitter.Updraft=draft;
                residueEmitter.Tick(age,dt,poolFire,column,tongues);
            }
            if(residueEmitter.Volume&&fireVolume!=null)
            {
                float level=residue.HasFire?(float)Residue.FireLevel(residue,age):0;
                float flame=(float)residue.FlameHeight*(0.55f+0.45f*Mathf.Sqrt(level));
                float wobble=1+0.08f*Mathf.Sin(age*9.1f+flickerPhase);
                fireVolume.Advance(dt);
                fireVolume.Tick(level,flame,residue.PuffHz,Mathf.Max(24,64*Mathf.Clamp(stepScale,0.4f,1)),wobble);
                fireTarget=CameraRig.Pick(fireVolume.Center,fireVolume.Reach);
            }
            particles.Tick(age,dt,residue.HasFire?0:groundFire,HasVolume,volume!=null?volume.State:default(VolumeState));
            if(hasWave)
            {
                Vec4 w=VolumeEvolution.GroundWave(plan,volume.State,waveReach);
                bool on=w.Z>0.003;
                wave.enabled=on;
                if(on)
                {
                    VolumeState vs=volume.State;
                    waveProperties.SetVector(WaveId,new Vector4((float)w.X,(float)w.Y,(float)w.Z,(float)w.W));
                    Vec4 t=VolumeEvolution.GroundWaveTrail(plan,vs);
                    waveProperties.SetVector(WaveTrailId,new Vector4((float)t.X,(float)t.Y,(float)t.Z,(float)t.W));
                    waveProperties.SetVector(WaveGroundId,new Vector4((float)waveReach,scorchSeed%997,age,plan.Warhead==WarheadKind.Nuke?1:0));
                    waveProperties.SetVector(WaveDustId,new Vector4((float)vs.DustR,(float)vs.DustG,(float)vs.DustB,0));
                    wave.SetPropertyBlock(waveProperties);
                }
                else if(age>2&&!volume.Active) hasWave=false;
            }
            if(hasShadow)
            {
                Vec4 sh=VolumeEvolution.Shadow(volume.State,scorchSeed);
                bool on=volume.Active&&sh.Y>0.005;
                shadow.enabled=on; ShadowWeight=on?(float)(sh.Y*sh.Z):0;
                if(on)
                {
                    shadowProperties.SetVector(SunId,volume.Sun);
                    shadowProperties.SetMatrix(ShadowFrameId,Root.transform.worldToLocalMatrix); shadowProperties.SetVector(ShadowAreaId,shadowArea);
                    shadowProperties.SetVector(ShadowId,new Vector4((float)sh.X,(float)sh.Y,(float)sh.Z,(float)sh.W));
                    bool sim=volume.State.Sim;
                    shadowProperties.SetVector(SimOnId,new Vector4(sim?1:0,volume.State.Ground?1:0,0,0));
                    Vec4 shape=volume.State.SimShape; shadowProperties.SetVector(SimShapeId,new Vector4((float)shape.X,(float)shape.Y,(float)shape.Z,(float)shape.W));
                    shadowProperties.SetFloat(SimVariantId,volume.State.SimRun); shadowProperties.SetFloat(SimStemId,(float)volume.State.SimStem);
                    Vec4 nr=volume.State.NukeRing; shadowProperties.SetVector(NukeRingId,new Vector4((float)nr.X,(float)nr.Y,(float)nr.Z,(float)nr.W));
                    for(int i=0;i<4;i++)
                    {
                        Vec4 l=VolumeEvolution.ShadowLobe(volume.State,i), a=new Vec4(), d=new Vec4();
                        if(sim) VolumeEvolution.PackLobe(volume.State,i,out l,out a,out d);
                        shadowProperties.SetVector(ShadowLobeIds[i],new Vector4((float)l.X,(float)l.Y,(float)l.Z,(float)l.W));
                        shadowProperties.SetVector(ShadowAxisIds[i],new Vector4((float)a.X,(float)a.Y,(float)a.Z,(float)a.W));
                        shadowProperties.SetVector(ShadowOriginIds[i],new Vector4((float)d.X,(float)d.Y,(float)d.Z,(float)d.W));
                    }
                    shadow.SetPropertyBlock(shadowProperties);
                }
            }
            UpdateStrip(dt);
            if(hasScorch)
            {
                scorchProperties.SetVector(ScorchId,new Vector4((float)Residue.ScorchLevel(residue,age),
                    plan.Warhead==WarheadKind.Nuke?0:Mathf.Clamp01(1-age/14)*(float)plan.FireFraction*(air>0.1f?1:0),scorchSeed%997,air<0.1f?1:0));   // (no embers under a nuclear cloud)
                scorchProperties.SetVector(ScorchLookId,scorchLook); scorchProperties.SetVector(ScorchSoilId,Soil());
                scorch.SetPropertyBlock(scorchProperties);
            }
            float flicker=(float)ExplosionLight.Flicker(age,flickerPhase);
            float want;
            if(HasVolume&&plan.Warhead==WarheadKind.Nuke&&plan.Look!=null&&plan.Look.Modern)
            {
                // A nuclear burst lights the land for kilometres: a white flash outshining the sun, then the fireball's
                // warm glow from where it hangs.
                double tf=VolumeEvolution.FireballDuration(plan), rf=VolumeEvolution.FireballRadius(plan);
                double night=1-FxPool.Daylight;
                want=(float)(NukeCloud.LightLevel(age,tf,night)*volume.State.LightScale*volume.State.Fade);
                Vec3 c=NukeCloud.LightColour(age,tf); flash.color=new Color((float)c.X,(float)c.Y,(float)c.Z);
                flash.range=(float)NukeCloud.LightRange(age,tf,rf,night,Math.Max(volume.Nuke.Ball.X,0.02)*volume.State.BoxRadius);
                flash.transform.localPosition=Vector3.up*(float)(Math.Max(volume.Nuke.Ball.X,0.02)*volume.State.BoxRadius);
                // (32 points a frame, the inner rings first: the dust wall reaches each ring seconds after it is
                // filled; 128 a frame cost 6-8 ms per frame for the first frames of every nuclear burst)
                if(groundNext>=0) FillGround(32);
                if(nukeShadow) UpdateNukeShadow();
            }
            else if(HasVolume)
            {
                // Colour and reach follow the explosion: white flash, then the fireball cooling to orange.
                want=(float)VolumeEvolution.LightIntensity(volume.State)*flicker;
                Vec3 c=ExplosionLight.Colour(volume.State); flash.color=new Color((float)c.X,(float)c.Y,(float)c.Z);
                flash.range=(float)ExplosionLight.Range(volume.State,layout!=null?layout.RadiusMeters:0,domeRadius);
                flash.transform.localPosition=Vector3.up*(float)(VolumeEvolution.LightHeight(volume.State)*volume.State.BoxRadius);
            }
            else if(groundFire>0)
            {
                want=0.8f*groundFire*flicker*lightScale; flash.color=new Color(1,0.55f,0.22f);
                flash.range=residue.HasFire?Mathf.Max(domeRadius,(float)residue.FireRadius*8):domeRadius;
                flash.transform.localPosition=Vector3.up*(residue.HasFire?(float)residue.FlameHeight*0.35f:1);
            }
            else want=(float)ExplosionLight.FallbackFlash(plan.FireFraction,age)*lightScale;
            // By day a fireball tints the sunlit ground around it only a little (it lit it up like a lamp); at night fully.
            if(plan.Look!=null&&plan.Look.Modern&&plan.Warhead!=WarheadKind.Nuke) want*=1-0.4f*FxPool.Daylight;   // (a nuclear flash outshines the sun)
            flash.intensity=want;
            if(splashVolume!=null)
            {
                // (the fire of the blast lights the spray: orange over the water at night)
                float fi=want>0.03f?want*0.12f*(1-0.75f*FxPool.Daylight):0; Color fc=flash.color;
                splashVolume.Tick(age,new Vector4(fc.r*fi,fc.g*fi,fc.b*fi,flash.range*0.3f),flash.transform.localPosition,stepScale);
            }
            LightWanted=lit&&want>0.03f?want:0;
            if(LightWanted<=0) flash.enabled=false;
            float glow=HasVolume?(float)VolumeEvolution.LightIntensity(volume.State):0, pool=groundFire*1.4f;
            FireGlow=Mathf.Max(glow,pool)*flicker;
            FirePosition=glow>=pool?flash.transform.position:Root.transform.TransformPoint(Vector3.up*(residue.HasFire?(float)residue.FlameHeight*0.35f:1));
            FireRange=glow>=pool?Mathf.Max(flash.range,10):Mathf.Max((float)residue.FireRadius*8+5,10);
            bool alive=particles.Alive||(poolFire!=null&&(poolFire.IsAlive()||column.IsAlive()||tongues.IsAlive()))||FireShown||HasSplash;
            bool refracting=volume!=null&&volume.Distorting;
            double residueEnd=Math.Max(residue.HasFire?residue.SmokeSeconds:0,hasScorch?residue.ScorchSeconds:0);
            bool lingering=age<residueEnd;
            double limit=Math.Max(Math.Max(VolumeEvolution.VolumeDuration(plan),groundFireEnd)+4,Math.Max(residueEnd+14,particles.Horizon));
            if(splashVolume!=null) limit=Math.Max(limit,splashVolume.Duration+1);
            if(age>limit||(!HasVolume&&!refracting&&!alive&&!particles.Pending&&groundFire<=0&&!lingering))
            { Clear(); return false; }
            return true;
        }
        // Called by FxPool after every slot has ticked; primary lights are rendered per pixel.
        internal void AllowLight(bool allow,bool primary)
        {
            lightAllowed=allow&&active;
            flash.enabled=lightAllowed&&LightWanted>0;
            flash.renderMode=primary?LightRenderMode.ForcePixel:LightRenderMode.Auto;
        }
        internal void Shift(Vector3 offset)
        {
            if(!active) return;
            Root.transform.position-=offset; DebrisRoot.transform.position-=offset;
        }
        // The burning strip along a crash path: smoke and embers all along it, and a burnt scar that follows the fire.
        void UpdateStrip(float dt)
        {
            if(fireVolume==null||!residue.HasFire) { particles.FireEmbers(dt,0,0,Vector3.zero,0); return; }
            Vector2 d2=fireVolume.StripDirection; Vector3 dir=new Vector3(d2.x,0,d2.y);
            float length=fireVolume.StripLength, target=fireVolume.StripTarget;
            float level=(float)Residue.FireLevel(residue,age);
            residueEmitter.Strip(dir,length);
            residueEmitter.PoolsLit=fireVolume.Visible?fireVolume.PoolsLit(residueEmitter.Pools):0;
            particles.FireEmbers(dt,level,(float)residue.FireRadius,dir,length);
            if(scar==null||target<=1) return;
            if(target>scarBuilt+4)
            {
                float halfWidth=(float)residue.FireRadius*1.3f+fireVolume.PoolReach;
                scar.transform.localRotation=Quaternion.Euler(0,Mathf.Atan2(-d2.y,d2.x)*Mathf.Rad2Deg,0);
                Transform frame=scar.transform; int mask=SnapshotReader.SurfaceMask(); float reach=halfWidth+target;
                bool laid=ScorchMesh.Strip(scarMesh,halfWidth,target,Mathf.Clamp((int)(target/4),6,40),6,(Vector3 local,out Vector3 point,out Vector3 normal)=>
                {
                    Vector3 world=frame.TransformPoint(local), up=frame.up; RaycastHit hit;
                    if(SnapshotReader.GroundRay(world+up*60,-up,160,mask,out hit))
                    { point=frame.InverseTransformPoint(hit.point); normal=frame.InverseTransformDirection(hit.normal); return true; }
                    point=local; normal=Vector3.up; return false;
                });
                scarBuilt=target; scar.enabled=laid;
                scarProperties.SetVector(StripId,new Vector4(target/Mathf.Max(halfWidth,0.1f),0,halfWidth,0));
            }
            if(!scar.enabled) return;
            float halfW=(float)residue.FireRadius*1.3f+fireVolume.PoolReach;
            scarProperties.SetVectorArray(BlobsId,fireVolume.Pools);
            scarProperties.SetVector(ScorchId,new Vector4(Mathf.Max((float)Residue.ScorchLevel(residue,age),0.55f*Mathf.Clamp01(age/3)),
                Mathf.Clamp01(1-age/14)*(float)plan.FireFraction*(air>0.1f?1:0),(scorchSeed+17)%997,air<0.1f?1:0));
            scarProperties.SetVector(StripId,new Vector4(scarBuilt/Mathf.Max(halfW,0.1f),Mathf.Clamp01(length/Mathf.Max(scarBuilt,0.1f)),halfW,length));
            scarProperties.SetVector(ScorchLookId,new Vector4(scorchLook.x,0,0,0)); scarProperties.SetVector(ScorchSoilId,Soil());   // (burns under the pools)
            scar.SetPropertyBlock(scarProperties);
        }
        // The ground's colour round the blast (read from the screen when it can be), and 1 for snow, 2 for hard ground.
        Vector4 Soil() { return new Vector4((float)plan.DustR,(float)plan.DustG,(float)plan.DustB,plan.Surface==GroundKind.Snow?1:plan.Surface==GroundKind.Rock?2:0); }
        bool BuildScorch(float radius)
        {
            bool built=BuildDrape(scorchMesh,0,0,radius,radius>200?32:16);
            scorch.enabled=built;
            return built;
        }
        // A mesh laid on the terrain round the blast (parts excluded), in the blast frame.
        bool BuildShadowDrape()
        {
            Vector4 sun=volume.Sun; double x,z,radius;
            VolumeEvolution.ShadowDrape(volume.State.BoxRadius,sun.x,sun.y,sun.z,out x,out z,out radius);
            float box=(float)volume.State.BoxRadius;
            Transform t=shadow.transform;
            t.localPosition=new Vector3((float)x,0.5f*box,(float)z); t.localRotation=Quaternion.identity;
            t.localScale=new Vector3(2*(float)radius,4*box,2*(float)radius);
            shadowArea=new Vector4((float)x,(float)z,(float)radius,2.5f*box);
            return true;
        }
        bool BuildWave()
        {
            Transform frame=Root.transform; int mask=SnapshotReader.SurfaceMask(); float reach=(float)waveReach;
            float drop=plan!=null&&plan.Warhead==WarheadKind.Nuke?(float)plan.BurstHeight:0;
            return ScorchMesh.Polar(waveMesh,reach,30,72,(Vector3 local,out Vector3 point,out Vector3 normal)=>
            {
                Vector3 world=frame.TransformPoint(local+Vector3.down*drop), up=frame.up; RaycastHit hit;   // (on the ground under an air burst)
                if(SnapshotReader.GroundRay(world+up*(reach*0.3f+20),-up,reach*0.6f+60,mask,out hit))
                { point=frame.InverseTransformPoint(hit.point); normal=frame.InverseTransformDirection(hit.normal); return true; }
                point=local; normal=Vector3.up; return false;
            });
        }
        bool BuildDrape(Mesh mesh,float x,float z,float radius,int cells)
        {
            Transform frame=Root.transform; int mask=SnapshotReader.SurfaceMask();
            return ScorchMesh.Build(mesh,x,z,radius,cells,(Vector3 local,out Vector3 point,out Vector3 normal)=>
            {
                Vector3 world=frame.TransformPoint(local), up=frame.up; RaycastHit hit;
                if(SnapshotReader.GroundRay(world+up*(radius+5),-up,2*radius+10,mask,out hit))
                { point=frame.InverseTransformPoint(hit.point); normal=frame.InverseTransformDirection(hit.normal); return true; }
                point=local; normal=Vector3.up; return false;
            });
        }
        internal void Clear()
        {
            if(volume!=null) volume.Clear();
            particles.Clear();
            if(poolFire!=null) { poolFire.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); column.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); tongues.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
            residueEmitter.Stop(); residue=default(ResiduePlan); FireGlow=0; nukeShadow=false; if(nukeShade!=null) nukeShade.enabled=false;
            if(fireVolume!=null) fireVolume.Clear(); fireTarget=null;
            if(splashVolume!=null) splashVolume.Clear();
            if(scorch!=null) scorch.enabled=false; hasScorch=false;
            if(scar!=null) scar.enabled=false; scarBuilt=0;
            if(shadow!=null) shadow.enabled=false; hasShadow=false;
            if(wave!=null) wave.enabled=false; hasWave=false;
            active=false; flash.enabled=false; lightAllowed=false; LightWanted=0; layout=null;
            Root.SetActive(false); DebrisRoot.SetActive(false);
        }
        public void Dispose() { UnityEngine.Object.Destroy(Root); UnityEngine.Object.Destroy(DebrisRoot); }
    }
}
