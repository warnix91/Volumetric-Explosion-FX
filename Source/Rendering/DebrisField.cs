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
        internal int Captured { get; private set; }
        internal int Missed { get; private set; }
        internal int Dropped { get; private set; }
        internal int Failed { get; private set; }
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
            if(Time.frameCount!=captureFrame) { captureFrame=Time.frameCount; capturedThisFrame=0; Prune(); }
            if(capturedThisFrame>=Math.Max(6,batches.Length)) { Dropped++; return; }
            capturedThisFrame++;
            Vector3 up=((Vector3d)part.transform.position-part.vessel.mainBody.position).normalized;
            CapturedPart captured=PartCapture.Read(part,up,settings.DebrisTriangles);
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
            if(Physics.Raycast(origin+up*lift,-up,out hit,1500+lift,SnapshotReader.SurfaceMask(),QueryTriggerInteraction.Ignore)&&
                hit.collider!=null&&hit.collider.GetComponentInParent<Part>()==null)
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
            while(true)
            {
                Job job=null;
                lock(finished) if(finished.Count>0) job=finished.Dequeue();
                if(job==null) break;
                inFlight--; running.Remove(job);
                if(job.Generation!=generation) continue;
                if(job.Cut==null||job.Motion==null) { Failed++; continue; }
                try { Apply(job); }
                catch(Exception ex) { Failed++; if(Failed<=2) Debug.LogWarning("[VEFX] Part debris skipped: "+ex.GetType().Name+": "+ex.Message); }
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
            Vector3 origin;
            if(job.Inertial) origin=job.Origin+(Vector3)((job.InertialVelocity-Krakensbane.GetFrameVelocity())*age);
            else origin=(Vector3)job.Body.GetWorldSurfacePosition(job.Latitude,job.Longitude,job.Altitude);
            if(job.Landing) Refine(job,origin);
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
            b.Tracers.Trace(b.Traced.Count,smoke,1.2+2.2*job.Air,lifetimes,starts,new Color(1,0.66f,0.32f,1),0.9f);
        }
        void Refine(Job job,Vector3 origin)
        {
            Vector3 up=job.Frame*Vector3.up;
            int mask=SnapshotReader.SurfaceMask();
            for(int f=0;f<job.Motion.Length;f++)
            {
                FragmentMotion m=job.Motion[f];
                if(double.IsInfinity(m.LandTime)) continue;
                Vec3 at=Ballistics.Position(m.Pivot,m.Velocity,m.Drag,job.Gravity,m.LandTime);
                Vector3 world=origin+job.Frame*new Vector3((float)at.X,(float)job.Floor,(float)at.Z);
                double floor=job.Floor; bool wet=job.Water;
                RaycastHit hit;
                if(Physics.Raycast(world+up*80,-up,out hit,200,mask,QueryTriggerInteraction.Ignore)&&hit.collider!=null&&hit.collider.GetComponentInParent<Part>()==null)
                {
                    double ground=Vector3.Dot(hit.point-origin,up);
                    wet=job.Sea>ground; floor=wet?job.Sea:ground;
                }
                if(Math.Abs(floor-job.Floor)>0.25||wet!=job.Water) DebrisPlanner.Land(job.Cut,f,Identity,ref job.Motion[f],floor,job.Gravity,job.Horizon,wet);
                if(wet&&!double.IsInfinity(job.Motion[f].LandTime)) job.Motion[f].RestHeight-=1.5;
            }
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
                b.Smoke=SmokeParticles.Create(b.Root.transform,"burning piece smoke",assets.SmokeCloud,192,0.35f,1);
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
        internal void Shift(Vector3 offset)
        {
            for(int i=0;i<batches.Length;i++) if(batches[i]!=null&&batches[i].Active&&batches[i].Inertial) batches[i].Root.transform.position-=offset;
            foreach(Job job in waiting) job.Origin-=offset;
            for(int i=0;i<running.Count;i++) running[i].Origin-=offset;
        }
        internal void Clear()
        {
            generation++; waiting.Clear(); prepared.Clear();
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
