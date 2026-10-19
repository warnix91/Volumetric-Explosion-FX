using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using VolumetricExplosionFX.Core;
using VolumetricExplosionFX.Ksp;
namespace VolumetricExplosionFX.Rendering
{
    // Fracture runs on copied arrays; Unity object access stays on the main thread.
    internal sealed class DebrisField : IDisposable
    {
        sealed class Job
        {
            // Worker input.
            internal PartMesh Mesh; internal FxPlan Plan; internal int Pieces, MaxTriangles; internal uint Seed;
            internal Vec3 Center, Velocity; internal bool Surface, Landing; internal double Floor, Gravity, Horizon;
            // Main thread only.
            internal SkinLook[] Looks; internal Quaternion Frame; internal Vector3 Origin; internal Vector3d InertialVelocity;
            internal CelestialBody Body; internal double Latitude, Longitude, Altitude, Sea;
            internal bool Inertial, Water; internal float Born, Air; internal int Generation;
            // Worker output; published through Finished.
            internal FractureResult Cut; internal FragmentMotion[] Motion; internal Exception Error;
            internal Queue<Job> Finished;
            // Main thread: next fragment to fit to the ground (placing is spread over frames).
            internal int RefineAt;
        }
        sealed class Batch
        {
            internal GameObject Root; internal MeshRenderer Renderer; internal Mesh Mesh;
            internal ParticleSystem Fire, Smoke, Flames;
            internal readonly DebrisTrails Trails=new DebrisTrails();
            internal TrailShower Tracers; internal float TraceStart;
            internal readonly List<int> Traced=new List<int>();
            internal readonly List<Vector3> TracerAt=new List<Vector3>();
            internal FragmentMotion[] Motion; internal double Gravity;
            internal readonly MaterialPropertyBlock Properties=new MaterialPropertyBlock();
            internal Material[] Skins;
            internal bool Active, Inertial; internal float Age, End;
            internal CelestialBody Body; internal double Latitude, Longitude, Altitude; internal Vector3d Velocity;
            internal int Pieces;
        }
        static readonly Quat Identity=new Quat(0,0,0,1);
        static readonly int AgeId=Shader.PropertyToID("_Age"), GravityId=Shader.PropertyToID("_Gravity"),
            LifeId=Shader.PropertyToID("_Life"), CoolId=Shader.PropertyToID("_Cool");
        const int MaxInFlight=2, MaxQueued=48;
        readonly VolumeAssets assets;
        readonly Material glow;
        readonly FxSettings settings;
        readonly Batch[] batches;
        readonly Queue<Job> waiting=new Queue<Job>();
        readonly Queue<Job> finished=new Queue<Job>();
        readonly List<Job> running=new List<Job>();
        readonly Dictionary<string,Material> skins=new Dictionary<string,Material>();
        sealed class Prepared { internal CapturedPart Part; internal int Frame; }
        readonly Dictionary<int,Prepared> prepared=new Dictionary<int,Prepared>();
        readonly List<int> stale=new List<int>();
        int inFlight, generation, captureFrame=-1, capturedThisFrame;
        Job placing;            // wreck being fitted to the ground over several frames
        long captureTicks;      // time spent reading part meshes this frame
        internal int Captured { get; private set; }
        internal int Missed { get; private set; }
        internal int Dropped { get; private set; }
        internal int Failed { get; private set; }
        internal int Skipped { get; private set; }
        // Last frame: time fitting wrecks to the ground and building their meshes (ms), for the slow-frame report.
        internal double FitMs, MeshMs;   // parts left without a torn wreck to keep the frame short
        internal int Active { get { int n=0; for(int i=0;i<batches.Length;i++) if(batches[i]!=null&&batches[i].Active) n++; return n; } }
        internal int Pieces { get { int n=0; for(int i=0;i<batches.Length;i++) if(batches[i]!=null&&batches[i].Active) n+=batches[i].Pieces; return n; } }
        internal int Pending { get { return waiting.Count+inFlight; } }
        internal bool Available { get { return assets.Fragment!=null&&assets.Noise!=null&&batches.Length>0; } }
        internal DebrisField(FxSettings s,VolumeAssets volumes,Material glowMaterial)
        {
            settings=s; assets=volumes; glow=glowMaterial; batches=new Batch[Math.Max(0,s.MaxDebrisParts)];
        }
        internal void Prepare(Part part)
        {
            if(!Available||!settings.PartDebris||part==null||part.vessel==null||part.vessel.mainBody==null) return;
            int id=part.GetInstanceID();
            if(prepared.ContainsKey(id)) return;
            if(Time.frameCount!=captureFrame) { captureFrame=Time.frameCount; capturedThisFrame=0; captureTicks=0; Prune(); }
            // Reading a part's mesh takes about a millisecond: a crash breaking thirty parts in one frame cost 48 ms. Past
            // 6 ms in a frame the remaining parts still explode, without a torn wreck.
            if(capturedThisFrame>=Math.Max(6,batches.Length)||captureTicks>System.Diagnostics.Stopwatch.Frequency*6/1000) { Dropped++; Skipped++; return; }
            capturedThisFrame++;
            long t0=System.Diagnostics.Stopwatch.GetTimestamp();
            Vector3 up=((Vector3d)part.transform.position-part.vessel.mainBody.position).normalized;
            CapturedPart captured=PartCapture.Read(part,up,settings.DebrisTriangles);
            captureTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-t0;
            if(captured!=null) prepared[id]=new Prepared { Part=captured, Frame=Time.frameCount };
        }
        void Prune()
        {
            if(prepared.Count==0) return;
            stale.Clear();
            foreach(var kv in prepared) if(Time.frameCount-kv.Value.Frame>30) stale.Add(kv.Key);
            for(int i=0;i<stale.Count;i++) prepared.Remove(stale[i]);
        }
        internal void Capture(Part part,DestructionEvent e,FxPlan plan)
        {
            if(!Available||!settings.PartDebris||part==null||part.vessel==null||part.vessel.mainBody==null) return;
            Prepared copy;
            if(!prepared.TryGetValue(part.GetInstanceID(),out copy)) { Missed++; return; }
            prepared.Remove(part.GetInstanceID());
            Enqueue(copy.Part,e,plan,part.vessel.mainBody);
        }
        internal void Capture(CapturedPart captured,DestructionEvent e,FxPlan plan,CelestialBody body)
        {
            if(!Available||!settings.PartDebris||captured==null||body==null) return;
            Enqueue(captured,e,plan,body);
        }
        void Enqueue(CapturedPart captured,DestructionEvent e,FxPlan plan,CelestialBody body)
        {
            PartSnapshot s=e.Part;
            Vector3 up=captured.Frame*Vector3.up;
            if(waiting.Count>=MaxQueued) { waiting.Dequeue(); Dropped++; }
            double air=Numbers.Clamp(plan.Atmosphere,0,1);
            var job=new Job { Mesh=captured.Mesh, Plan=plan, MaxTriangles=settings.DebrisTriangles, Looks=captured.Looks,
                Frame=captured.Frame, Origin=captured.Origin, Body=body, Born=Time.time, Air=(float)air, Generation=generation,
                Gravity=s.Environment.Gravity, Horizon=settings.ResidueSeconds+5, Center=captured.Center };
            job.Seed=s.PersistentId*2654435761u^(uint)(Time.frameCount*40503);
            job.Pieces=DebrisPlanner.Pieces(plan,captured.Size);
            double floor=Floor(body,captured.Origin,up,(float)captured.Size+3);
            job.Sea=body.ocean?-body.GetAltitude(captured.Origin):double.NegativeInfinity;
            if(job.Sea>floor) { floor=job.Sea; job.Water=true; }
            job.Floor=floor;
            Quaternion toLocal=Quaternion.Inverse(captured.Frame);
            job.Inertial=air<0.02&&floor<-2000;
            if(job.Inertial)
            {
                job.InertialVelocity=new Vector3d(s.InertialVelocity.X,s.InertialVelocity.Y,s.InertialVelocity.Z);
                job.Gravity=0; job.Landing=false; job.Velocity=new Vec3();
            }
            else
            {
                job.Latitude=body.GetLatitude(captured.Origin); job.Longitude=body.GetLongitude(captured.Origin); job.Altitude=body.GetAltitude(captured.Origin);
                Vector3 v=toLocal*SnapshotReader.Vector(s.SurfaceVelocity);
                job.Velocity=new Vec3(v.x,v.y,v.z);
                job.Landing=!double.IsNegativeInfinity(floor);
            }
            job.Surface=e.Kind!=EventKind.Destruction||floor>-(captured.Size+4);
            waiting.Enqueue(job); Captured++;
        }
        internal static double Floor(CelestialBody body,Vector3 origin,Vector3 up,float lift)
        {
            RaycastHit hit;
            if(SnapshotReader.GroundRay(origin+up*lift,-up,1500+lift,out hit))
                return Vector3.Dot(hit.point-origin,up);
            if(body.pqsController==null) return double.NegativeInfinity;
            double altitude=body.GetAltitude(origin);
            return body.TerrainAltitude(body.GetLatitude(origin),body.GetLongitude(origin),true)-altitude;
        }
        static void Work(object state)
        {
            var job=(Job)state;
            try
            {
                job.Cut=Fracture.Cut(job.Mesh,job.Pieces,job.Seed,job.MaxTriangles,1);
                if(job.Cut!=null)
                {
                    job.Motion=DebrisPlanner.Launch(job.Plan,job.Cut,Identity,new Vec3(),job.Center,job.Velocity,job.Surface,job.Seed);
                    if(job.Landing) for(int f=0;f<job.Motion.Length;f++)
                        DebrisPlanner.Land(job.Cut,f,Identity,ref job.Motion[f],job.Floor,job.Gravity,job.Horizon,job.Water);
                }
            }
            catch(Exception ex) { job.Error=ex; job.Cut=null; }
            try { Queue<Job> done=job.Finished; lock(done) done.Enqueue(job); }
            catch(Exception) { }
        }
        internal void Tick(float dt,Camera cam)
        {
            // Placing finished wreckage (ground probes, mesh upload) is spread over frames: at most ~4 ms a frame, the rest
            // waits in the queue (a crash breaking twenty parts at once otherwise cost one 30 ms frame).
            long start=System.Diagnostics.Stopwatch.GetTimestamp(), budget=System.Diagnostics.Stopwatch.Frequency*4/1000;
            FitMs=0; MeshMs=0;
            // A big wreck is fitted to the ground a few fragments at a time (each one probes the ground up to six times, and
            // where there is no collider the planet's terrain three times per probe): one wreck alone took 20-33 ms.
            while(System.Diagnostics.Stopwatch.GetTimestamp()-start<budget)
            {
                Job job=placing;
                if(job!=null&&job.Generation!=generation) { placing=null; continue; }
                if(job==null)
                {
                    lock(finished) if(finished.Count>0) job=finished.Dequeue();
                    if(job==null) break;
                    inFlight--; running.Remove(job);
                    if(job.Generation!=generation) continue;
                    if(job.Cut==null||job.Motion==null) { Failed++; continue; }
                    placing=job;
                }
                try
                {
                    long a=System.Diagnostics.Stopwatch.GetTimestamp();
                    bool fitted=!job.Landing||Refine(job,Origin(job),start+budget);
                    long b=System.Diagnostics.Stopwatch.GetTimestamp();
                    FitMs+=(b-a)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                    if(!fitted) break;   // the rest of this wreck next frame
                    placing=null;
                    Apply(job);
                    MeshMs+=(System.Diagnostics.Stopwatch.GetTimestamp()-b)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                }
                catch(Exception ex) { placing=null; Failed++; if(Failed<=2) Debug.LogWarning("[VEFX] Part debris skipped: "+ex.GetType().Name+": "+ex.Message); }
            }
            while(inFlight<MaxInFlight&&waiting.Count>0)
            {
                Job job=waiting.Dequeue(); job.Finished=finished; inFlight++; running.Add(job);
                if(!ThreadPool.QueueUserWorkItem(Work,job)) { inFlight--; running.Remove(job); Failed++; }
            }
            for(int i=0;i<batches.Length;i++)
            {
                Batch b=batches[i];
                if(b==null||!b.Active) continue;
                b.Age+=dt;
                if(b.Inertial) b.Root.transform.position+=(Vector3)((b.Velocity-Krakensbane.GetFrameVelocity())*dt);
                else if(b.Body!=null) b.Root.transform.position=(Vector3)b.Body.GetWorldSurfacePosition(b.Latitude,b.Longitude,b.Altitude);
                b.Properties.SetFloat(AgeId,b.Age); b.Renderer.SetPropertyBlock(b.Properties);
                if(b.Fire!=null) b.Trails.Tick(b.Age,dt,b.Fire,b.Smoke,b.Flames);
                if(b.Tracers!=null&&b.Traced.Count>0)
                {
                    for(int k=0;k<b.Traced.Count;k++)
                    { Vec3 at=DebrisPlanner.Where(b.Motion[b.Traced[k]],b.Gravity,b.Age); b.TracerAt[k]=new Vector3((float)at.X,(float)at.Y,(float)at.Z); }
                    b.Tracers.Update(b.Age-b.TraceStart,b.TracerAt);
                }
                if(b.Age>b.End) Release(b);
            }
        }
        void Apply(Job job)
        {
            Batch b=Take();
            if(b==null) { Dropped++; return; }
            float age=Mathf.Max(0,Time.time-job.Born);
            Vector3 origin=Origin(job);
            FragmentMeshBuilder.Build(b.Mesh,job.Cut,job.Motion,Quaternion.identity,Vector3.zero,job.Looks.Length,job.Gravity,job.Horizon,job.Seed);
            if(b.Skins==null||b.Skins.Length!=job.Looks.Length) b.Skins=new Material[job.Looks.Length];
            for(int i=0;i<job.Looks.Length;i++) b.Skins[i]=Skin(job.Looks[i]);
            b.Renderer.sharedMaterials=b.Skins;
            b.Root.transform.SetPositionAndRotation(origin,job.Frame);
            b.Inertial=job.Inertial; b.Velocity=job.InertialVelocity; b.Body=job.Body;
            b.Latitude=job.Latitude; b.Longitude=job.Longitude; b.Altitude=job.Altitude;
            float life=job.Inertial?Mathf.Min((float)settings.ResidueSeconds,30):(float)settings.ResidueSeconds;
            b.End=Mathf.Max(life,8); b.Age=age; b.Pieces=job.Cut.Count;
            b.Properties.Clear();
            b.Properties.SetFloat(AgeId,age); b.Properties.SetFloat(GravityId,(float)job.Gravity);
            b.Properties.SetVector(LifeId,new Vector4(b.End-4,b.End,0,0)); b.Properties.SetFloat(CoolId,job.Air>0.05f?1.8f:1.4f);
            b.Renderer.SetPropertyBlock(b.Properties);
            b.Root.SetActive(true); b.Renderer.enabled=true; b.Active=true;
            if(b.Fire!=null)
            {
                b.Trails.FlyingPuffs=b.Tracers==null;
                b.Trails.Begin(job.Motion,job.Gravity,6,job.Plan,job.Seed,job.Water?0:(float)settings.ResidueSeconds,job.Water);
                SmokeParticles.Wispy(b.Smoke,job.Plan.Look!=null&&job.Plan.Look.Modern);
                b.Fire.Play(); b.Smoke.Play(); if(b.Flames!=null) b.Flames.Play();
            }
            b.Motion=job.Motion; b.Gravity=job.Gravity; b.Traced.Clear(); b.TracerAt.Clear();
            if(b.Tracers!=null&&job.Air>0.15f) StartTracers(b,job,age);
        }
        void StartTracers(Batch b,Job job,float age)
        {
            var lifetimes=new float[6]; var starts=new Vector3[6];
            for(int f=0;f<job.Motion.Length&&b.Traced.Count<6;f++)
            {
                FragmentMotion m=job.Motion[f];
                if(!m.Burning) continue;
                float life=(float)Math.Min(m.BurnTime,m.LandTime)-age;
                if(life<=0.1f) continue;
                Vec3 at=DebrisPlanner.Where(m,job.Gravity,age);
                lifetimes[b.Traced.Count]=life; starts[b.Traced.Count]=new Vector3((float)at.X,(float)at.Y,(float)at.Z);
                b.Traced.Add(f); b.TracerAt.Add(starts[b.Traced.Count-1]);
            }
            if(b.Traced.Count==0) return;
            b.TraceStart=age;
            FxPlan p=job.Plan; float dim=(float)(1-0.5*Numbers.Clamp(p.Soot,0,1));
            var smoke=new Color((float)p.SmokeR*dim,(float)p.SmokeG*dim,(float)p.SmokeB*dim,1);
            // In the 1.1 look a burning piece leaves a thin, lighter wisp that is soon gone: thick dark trails
            // flying out low read as dark lines raying out over the ground ("encore pire pour les effets d'onde au sol").
            if(p.Look!=null&&p.Look.Modern)
                b.Tracers.Trace(b.Traced.Count,Color.Lerp(smoke,new Color(0.62f,0.6f,0.57f,1),0.4f),0.45+0.8*job.Air,lifetimes,starts,new Color(1,0.66f,0.32f,1),0.9f,0.6,false,0.55f);
            else b.Tracers.Trace(b.Traced.Count,smoke,1.2+2.2*job.Air,lifetimes,starts,new Color(1,0.66f,0.32f,1),0.9f);
        }
        static Vector3 Origin(Job job)
        {
            float age=Mathf.Max(0,Time.time-job.Born);
            if(job.Inertial) return job.Origin+(Vector3)((job.InertialVelocity-Krakensbane.GetFrameVelocity())*age);
            return (Vector3)job.Body.GetWorldSurfacePosition(job.Latitude,job.Longitude,job.Altitude);
        }
        // Fits the wreck's fragments to the ground from RefineAt on, until the deadline (Stopwatch ticks); true when done.
        bool Refine(Job job,Vector3 origin,long deadline)
        {
            Vector3 up=job.Frame*Vector3.up;
            Quaternion toLocal=Quaternion.Inverse(job.Frame);
            int mask=SnapshotReader.SurfaceMask();
            for(;job.RefineAt<job.Motion.Length;job.RefineAt++)
            {
                if(System.Diagnostics.Stopwatch.GetTimestamp()>=deadline) return false;
                int f=job.RefineAt;
                bool wet=job.Water;
                // Where a piece lands depends on the ground height there (downhill it flies further and lower): measure the
                // ground at the landing point, land again, and repeat until the two agree.
                for(int pass=0;pass<4;pass++)
                {
                    FragmentMotion m=job.Motion[f];
                    if(double.IsInfinity(m.LandTime)) break;
                    Vec3 at=Ballistics.Position(m.Pivot,m.Velocity,m.Drag,job.Gravity,m.LandTime);
                    double floor; Vector3 normal;
                    if(!Ground(job,origin,up,mask,new Vector3((float)at.X,(float)at.Y,(float)at.Z),out floor,out normal)) break;
                    wet=job.Sea>floor; if(wet) { floor=job.Sea; normal=up; }
                    Vector3 n=toLocal*normal;
                    double sx=n.y>0.3f?-n.x/n.y:0, sz=n.y>0.3f?-n.z/n.y:0;
                    bool settled=pass>0&&Math.Abs(job.Motion[f].SlopeX-sx)<0.05&&Math.Abs(job.Motion[f].SlopeZ-sz)<0.05&&Math.Abs(floor-lastFloor[f])<0.2;
                    job.Motion[f].SlopeX=sx; job.Motion[f].SlopeZ=sz; lastFloor[f]=floor;
                    DebrisPlanner.Land(job.Cut,f,Identity,ref job.Motion[f],floor,job.Gravity,job.Horizon,wet);
                    if(settled) break;
                }
                FitSkid(job,origin,up,mask,f,wet);
                if(wet&&!double.IsInfinity(job.Motion[f].LandTime)) job.Motion[f].RestHeight-=1.5;
            }
            return true;
        }
        // A skidding piece ends on the ground measured where it stops: the slope along the skid is taken from the two ends
        // (the hillside under the landing point alone could lift it into the air or bury it further down).
        void FitSkid(Job job,Vector3 origin,Vector3 up,int mask,int f,bool wet)
        {
            FragmentMotion m=job.Motion[f];
            double length=Math.Sqrt(m.Slide.X*m.Slide.X+m.Slide.Z*m.Slide.Z)*m.SlideTime*0.5;
            if(wet||double.IsInfinity(m.LandTime)||length<0.5) return;
            Vec3 at=Ballistics.Position(m.Pivot,m.Velocity,m.Drag,job.Gravity,m.LandTime);
            double sx=m.Slide.X/Math.Max(Math.Sqrt(m.Slide.X*m.Slide.X+m.Slide.Z*m.Slide.Z),1e-6), sz=m.Slide.Z/Math.Max(Math.Sqrt(m.Slide.X*m.Slide.X+m.Slide.Z*m.Slide.Z),1e-6);
            double start, end; Vector3 n;
            if(!Ground(job,origin,up,mask,new Vector3((float)at.X,0,(float)at.Z),out start,out n)) return;
            if(!Ground(job,origin,up,mask,new Vector3((float)(at.X+sx*length),0,(float)(at.Z+sz*length)),out end,out n)) return;
            double k=Numbers.Clamp((end-start)/length,-1.2,1.2);
            job.Motion[f].SlopeX=k*sx; job.Motion[f].SlopeZ=k*sz;
        }
        double[] lastFloor=new double[64];
        // Ground height (along the effect's up, from the origin) and normal under a point of the effect frame.
        bool Ground(Job job,Vector3 origin,Vector3 up,int mask,Vector3 local,out double floor,out Vector3 normal)
        {
            if(lastFloor.Length<job.Motion.Length) lastFloor=new double[job.Motion.Length];
            Vector3 world=origin+job.Frame*new Vector3(local.x,0,local.z);
            floor=job.Floor; normal=up;
            RaycastHit hit;
            if(SnapshotReader.GroundRay(world+up*200,-up,500,mask,out hit))
            { floor=Vector3.Dot(hit.point-origin,up); normal=hit.normal; return true; }
            if(job.Body==null||job.Body.pqsController==null) return false;
            // No collider there (terrain colliders exist only near the vessel): the terrain's own height and slope.
            double h0, hx, hz;
            if(!TerrainHeight(job.Body,world,origin,up,out h0)||!TerrainHeight(job.Body,world+job.Frame*Vector3.right*2,origin,up,out hx)||
                !TerrainHeight(job.Body,world+job.Frame*Vector3.forward*2,origin,up,out hz)) return false;
            floor=h0; normal=job.Frame*new Vector3(-(float)(hx-h0)/2,1,-(float)(hz-h0)/2).normalized;
            return true;
        }
        // Height of the terrain under `world` along the effect's up, relative to the effect origin.
        static bool TerrainHeight(CelestialBody body,Vector3 world,Vector3 origin,Vector3 up,out double height)
        {
            height=0;
            double lat=body.GetLatitude(world), lon=body.GetLongitude(world);
            double alt=body.TerrainAltitude(lat,lon,true);
            if(double.IsNaN(alt)||double.IsInfinity(alt)) return false;
            Vector3 ground=(Vector3)body.GetWorldSurfacePosition(lat,lon,alt);
            height=Vector3.Dot(ground-origin,up);
            return true;
        }
        Batch Take()
        {
            Batch oldest=null;
            for(int i=0;i<batches.Length;i++)
            {
                if(batches[i]==null) batches[i]=Create(i);
                if(!batches[i].Active) return batches[i];
                if(oldest==null||batches[i].Age>oldest.Age) oldest=batches[i];
            }
            if(oldest!=null) { Release(oldest); Dropped++; }
            return oldest;
        }
        Batch Create(int index)
        {
            var b=new Batch();
            b.Root=new GameObject("VolumetricExplosionFX part wreckage "+index); b.Root.layer=EffectLayer.Value;
            b.Mesh=new Mesh(); b.Mesh.name="Original pieces of a destroyed part";
            b.Root.AddComponent<MeshFilter>().sharedMesh=b.Mesh;
            b.Renderer=b.Root.AddComponent<MeshRenderer>();
            b.Renderer.shadowCastingMode=ShadowCastingMode.On; b.Renderer.receiveShadows=false;
            b.Renderer.lightProbeUsage=LightProbeUsage.Off; b.Renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            if(assets.SmokeFire!=null)
            {
                b.Fire=SmokeParticles.Create(b.Root.transform,"burning piece flames",assets.SmokeFire,96,0,0.7f);
                b.Smoke=SmokeParticles.Create(b.Root.transform,"burning piece smoke",assets.SmokeCloud,640,0.35f,1);
                if(assets.Flame!=null) b.Flames=SmokeParticles.CreateFlameSheets(b.Root.transform,"burning piece flames (procedural)",assets.Flame,64);
            }
            if(assets.TrailMaterial!=null&&glow!=null) b.Tracers=new TrailShower(b.Root.transform,"burning piece trails",glow,assets.TrailMaterial,8);
            b.Root.SetActive(false);
            return b;
        }
        Material Skin(SkinLook look)
        {
            string key=(look.Texture!=null?look.Texture.GetInstanceID():0)+"|"+look.Color+"|"+look.Scale+"|"+look.Offset;
            Material m;
            if(skins.TryGetValue(key,out m)&&m!=null) return m;
            if(skins.Count>=96) TrimSkins();
            m=new Material(assets.Fragment); m.name="Original part fragment skin";
            if(look.Texture!=null) m.SetTexture("_MainTex",look.Texture);
            m.SetTextureScale("_MainTex",look.Scale); m.SetTextureOffset("_MainTex",look.Offset);
            m.SetColor("_Color",look.Color); m.SetTexture("_NoiseTex",assets.Noise);
            skins[key]=m;
            return m;
        }
        void TrimSkins()
        {
            var used=new HashSet<Material>();
            for(int i=0;i<batches.Length;i++) if(batches[i]!=null&&batches[i].Active&&batches[i].Skins!=null) used.UnionWith(batches[i].Skins);
            var keys=new List<string>(skins.Keys);
            foreach(string k in keys) if(!used.Contains(skins[k])) { UnityEngine.Object.Destroy(skins[k]); skins.Remove(k); }
        }
        void Release(Batch b)
        {
            b.Active=false; b.Renderer.enabled=false; b.Pieces=0;
            if(b.Fire!=null) { b.Trails.Stop(); b.Fire.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); b.Smoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
            if(b.Flames!=null) b.Flames.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(b.Tracers!=null) b.Tracers.Clear(); b.Traced.Clear(); b.TracerAt.Clear();
            b.Mesh.Clear();
            b.Root.SetActive(false);
        }
        internal void SetSpeed(float speed)
        {
            for(int i=0;i<batches.Length;i++) if(batches[i]!=null) FxPool.Speed(batches[i].Root,speed);
        }
        internal void Shift(Vector3 offset)
        {
            for(int i=0;i<batches.Length;i++) if(batches[i]!=null&&batches[i].Active&&batches[i].Inertial) batches[i].Root.transform.position-=offset;
            foreach(Job job in waiting) job.Origin-=offset;
            for(int i=0;i<running.Count;i++) running[i].Origin-=offset;
            if(placing!=null) placing.Origin-=offset;
        }
        internal void Clear()
        {
            generation++; waiting.Clear(); prepared.Clear(); placing=null;
            for(int i=0;i<batches.Length;i++) if(batches[i]!=null&&batches[i].Active) Release(batches[i]);
        }
        public void Dispose()
        {
            generation++; waiting.Clear(); prepared.Clear();
            for(int i=0;i<batches.Length;i++) if(batches[i]!=null)
            {
                if(batches[i].Root!=null) UnityEngine.Object.Destroy(batches[i].Root);
                if(batches[i].Mesh!=null) UnityEngine.Object.Destroy(batches[i].Mesh);
            }
            foreach(Material m in skins.Values) if(m!=null) UnityEngine.Object.Destroy(m);
            skins.Clear();
        }
    }
}
