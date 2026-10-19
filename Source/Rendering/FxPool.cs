using System;
using System.Collections.Generic;
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
        internal int Splashes { get { int n=0;for(int i=0;i<slots.Length;i++) if(slots[i].HasSplash)n++;return n; } }
        internal EffectLoad Load
        {
            get
            {
                return new EffectLoad { Blasts=Active, FireballVolumes=Volumes, FireVolumes=FireVolumes, Particles=Particles,
                    Wrecks=debris.Available?debris.Active:0, WreckPieces=debris.Available?debris.Pieces:0, Lights=Lights };
            }
        }
        internal string Wreckage { get { return debris.Available?debris.Active+" wrecks / "+debris.Pieces+" pieces / "+debris.Pending+" cutting / "+
            debris.Captured+" captured / "+debris.Missed+" missed / "+debris.Dropped+" dropped ("+debris.Skipped+" to keep frames short) / "+debris.Failed+" failed":"part debris unavailable"; } }
        internal FxPool(FxSettings s)
        {
            settings=s;
            CameraRig.Setup();
            volumes=new VolumeAssets(s.Volumetric); assets=new ProceduralAssets(volumes.Particle,volumes.Debris); slots=new FxInstance[s.MaxEvents];
            lightPriority=new double[slots.Length]; lightChosen=new bool[slots.Length]; lightOrder=new int[slots.Length];
            debris=new DebrisField(s,volumes,assets.Glow);
            hits=new HitBursts(assets);
            lightning=new Lightning(assets.Glow);
            shake=new CameraShake();
            if(volumes.CompositeMaterial!=null) lowRes=new LowResVolumes(volumes.CompositeMaterial) { ProbeMaterial=volumes.CloudProbeMaterial };
            try { for(int i=0;i<slots.Length;i++) slots[i]=new FxInstance(i,assets,s,volumes); }
            catch { Dispose();throw; }
            if(slots.Length>0)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();
                try { slots[0].WarmUp(s); Warmup.Run(); Debug.Log("[VEFX] Particle and model warm-up in "+watch.ElapsedMilliseconds+" ms"); }
                catch(Exception ex) { slots[0].Clear(); Debug.LogWarning("[VEFX] Particle warm-up skipped: "+ex.GetType().Name); }
            }
            Camera.onPreCull+=OnPreCull; hooked=true;
        }
        bool hooked;
        LowResVolumes lowRes;
        readonly PlumeOverlays plumes=new PlumeOverlays();   // engine flames drawn again over the clouds in front of them
        void OnPreCull(Camera cam)
        {
            CameraRig.SetRenderRange(cam);
            LowResVolumes target=null;
            bool flight=CameraRig.IsFlight(cam);
            if(lowRes!=null&&flight)
            {
                // The 1.1 simulated fireball marches four times finer at one step lower resolution (same cost): its billows
                // need short steps (coarse steps showed as grain and banding), and a soft volume loses little at half size.
                bool sim=VolumeEvolution.UseSim&&!settings.Looks.UseClassic;
                lowRes.Overlays=plumes.Renderers; lowRes.OverlayBox=volumes.Cube;
                // Hidden behind EVE's cloud layers where they lie between the eye and the cloud.
                CelestialBody body=FlightGlobals.currentMainBody;
                CloudLayers.Refresh(body);
                lowRes.CloudOcclusion=CloudLayers.Known&&body!=null&&settings.Volumetric;
                if(lowRes.CloudOcclusion) { Vector3 c=body.position; lowRes.CloudBands=CloudLayers.Bands; lowRes.Planet=new Vector4(c.x,c.y,c.z,(float)body.Radius); }
                lowRes.ShowProbe(cam,lowRes.CloudOcclusion);
                lowRes.Begin(cam,VolumeEvolution.MarchDivisor(settings.VolumeDivisor,sim)); // also empties this camera's buffer at full resolution
                if(settings.VolumeDivisor>1||sim) target=lowRes;
            }
            if(lowRes!=null&&!flight) lowRes.ShowProbe(cam,false);   // (the grab is the flight camera's)
            for(int i=0;i<slots.Length;i++) if(slots[i]!=null) slots[i].ShowFor(cam,target);
            if(lowRes!=null&&flight) lowRes.End();
        }
        internal bool Spawn(EventCluster c,FxPlan plan)
        {
            Camera cam=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:Camera.main;
            Vector3 position=SnapshotReader.Vector(c.Position);
            if(cam==null) { NoCamera++;Dropped++;return false; }
            double reach=Reach(plan);
            if(Vector3.Distance(cam.transform.position,position)>reach) { OutOfRange++;Dropped++;return false; }
            CameraRig.Refresh();
            float distance=Vector3.Distance(cam.transform.position,position);
            Vector3 origin=position+SnapshotReader.Vector(c.Representative.Origin-c.Representative.Part.Position);
            // (A nuclear blast only joins another nuclear one: it was swallowed here by a missile's own explosion.)
            if(Absorb(position,origin,plan.VisualEnergyScore,plan.Warhead!=WarheadKind.None,plan.Warhead==WarheadKind.Nuke)) { Absorbed++; return true; }
            double lod=Numbers.Clamp(1-distance/reach,0.1,1);
            plan.DebrisCount=(int)(plan.DebrisCount*lod); plan.PuffCount=Math.Max(4,(int)(plan.PuffCount*lod));
            // 1.1: a fresh blast is worth more than an old one's lingering smoke; what is on screen more than what is not.
            int target=-1,weakest=-1,weakestVolume=-1;
            double weakestKeep=0, weakestVolumeKeep=0, fresh=plan.VisualEnergyScore+Freshness;
            for(int i=0;i<slots.Length;i++)
            {
                if(!slots[i].Active&&target<0) target=i;
                if(!slots[i].Active) continue;
                double keep=Keep(slots[i],cam);
                if(weakest<0||keep<weakestKeep) { weakest=i; weakestKeep=keep; }
                if(slots[i].ReservedVolume&&(weakestVolume<0||keep<weakestVolumeKeep)) { weakestVolume=i; weakestVolumeKeep=keep; }
            }
            if(target<0&&weakest>=0&&fresh>weakestKeep+0.25)
            { slots[weakest].Clear();target=weakest; }
            if(target<0&&plan.Warhead!=WarheadKind.None)
            {
                // In a battle every detonation must show: a new one takes the place of the oldest effect past its fireball.
                int oldest=-1;
                // (A nuclear cloud, which hangs for minutes, is not one of them: it gave its place to the next missile.)
                for(int i=0;i<slots.Length;i++) if(slots[i].Active&&slots[i].Age>3&&!slots[i].Nuclear&&(oldest<0||slots[i].Age>slots[oldest].Age)) oldest=i;
                if(oldest>=0) { slots[oldest].Clear(); target=oldest; }
            }
            if(target>=0)
            {
                bool ground=c.Ground&&settings.Ground, water=c.Representative.Kind==EventKind.WaterImpact&&settings.Water;
                bool candidate=!Reentry.Applies(plan,ground,plan.ImpactSpeed)&&volumes.Material!=null&&settings.Volumetric&&!cam.orthographic&&distance<reach&&
                    VolumeEvolution.Evaluate(plan,0.1,settings.Explosions,settings.Burst,ground,water).Visible;
                int volumeBudget=settings.Adaptive?governor.Volumes(settings.MaxVolumes):settings.MaxVolumes;
                // No volume left: the least valuable one fades out and hands its place to this blast (a whole explosion
                // drawn as flat sprites, or not at all, read as a bug).
                if(candidate&&ReservedVolumes>=volumeBudget&&weakestVolume>=0&&weakestVolume!=target&&fresh>weakestVolumeKeep+0.25)
                    slots[weakestVolume].ReleaseVolume();
                bool volume=candidate&&ReservedVolumes<volumeBudget;
                bool fireVolume=FireVolumes<volumeBudget;
                // (A nuclear blast wave is seen from tens of kilometres.)
                bool refraction=settings.Shockwave&&!cam.orthographic&&distance<(settings.Looks.UseClassic?2500:plan.Warhead==WarheadKind.Nuke?40000:8000);
                slots[target].Begin(c,plan,settings,Math.Max(0,settings.MaxDebris-Debris),distance<1500,volume,refraction,fireVolume);
                // A much stronger blast that swallows a young fireball takes over from it (it fades out instead of
                // being drawn inside the new one).
                if(slots[target].HasVolume&&plan.Warhead!=WarheadKind.Nuke)
                    for(int i=0;i<slots.Length;i++)
                        if(i!=target&&slots[i].HasVolume&&!slots[i].Nuclear&&slots[i].Age<6&&slots[i].Score<plan.VisualEnergyScore&&
                            Vector3.Distance(slots[i].Root.transform.position,position)<=slots[target].CoverRadius*0.8) slots[i].ReleaseVolume();
                if(shake!=null) shake.Add(position,plan.VisualEnergyScore,plan.Atmosphere,VolumeEvolution.DomeRadius(plan),plan.Warhead==WarheadKind.Nuke);
                MergePools(target);
                Spawned++;
                return true;
            }
            BudgetDrops++;Dropped++;
            return false;
        }
        // How much an effect is worth keeping when the budget is full: its strength, a bonus while its blast is young, less
        // as it ages, more the more of the screen it covers. A new blast counts as young.
        const double Freshness=3;
        static double Keep(FxInstance slot,Camera cam)
        {
            // A nuclear cloud lives for minutes: it loses its worth four times slower.
            return slot.Score+(slot.Age<4?Freshness:0)-(slot.Nuclear?0.01:0.04)*slot.Age+2*Mathf.Min(slot.Coverage(cam),1);
        }
        // No young effect left (only old smoke and fires): the pool can be rebuilt for new settings without anything
        // vanishing on screen.
        internal bool Quiet { get { for(int i=0;i<slots.Length;i++) if(slots[i].Active&&slots[i].Age<15) return false; return true; } }
        // 1.1: a new pool fire that lands in (or touching) one already burning feeds it instead of burning on top of it.
        void MergePools(int fresh)
        {
            if(!slots[fresh].PoolBurning) return;
            ResiduePlan pool=slots[fresh].Pool; Vector3 at=slots[fresh].PoolCenter;
            if(slots[fresh].TrailFire)
            {
                // Fuel thrown ahead along a crash path: the nearest fire on that path stretches into a burning strip.
                int best=-1; float near=float.MaxValue;
                for(int i=0;i<slots.Length;i++)
                {
                    if(i==fresh||!slots[i].PoolBurning) continue;
                    float d=Vector3.Distance(slots[i].PoolCenter,at);
                    if(d<near) { near=d; best=i; }
                }
                if(best>=0&&slots[best].Extend(at,pool,Mathf.Max(slots[fresh].TrailSpeed,15))) { slots[fresh].DropPool(); return; }
            }
            for(int i=0;i<slots.Length;i++)
            {
                if(i==fresh||!slots[i].PoolBurning) continue;
                if(Vector3.Distance(slots[i].PoolCenter,at)>(slots[i].Pool.FireRadius+pool.FireRadius)*1.1) continue;
                slots[i].Feed(pool); slots[fresh].DropPool();
                return;
            }
        }
        // nuclear: the blast is a nuclear detonation, which only another nuclear blast can hold (it was swallowed by the
        // missile's own explosion, a few metres away, and drawn as an ordinary blast).
        // A nuclear blast drawn near here lately: the same warhead seen twice (Warhead.SameNuke).
        internal bool NuclearNear(Vector3 position,double within,float maxAge)
        {
            for(int i=0;i<slots.Length;i++) if(slots[i].Active&&slots[i].Nuclear&&slots[i].Age<maxAge&&
                Vector3.Distance(slots[i].Root.transform.position,position)<=within) return true;
            return false;
        }
        internal bool Covers(Vector3 position,double score,bool tight=false,bool nuclear=false) { return Covering(position,score,tight,nuclear)!=null; }
        internal FxInstance Covering(Vector3 position,double score,bool tight=false,bool nuclear=false)
        {
            for(int i=0;i<slots.Length;i++) if(slots[i].Active&&slots[i].Age<(tight?2.5f:4.5f)&&score<=slots[i].Score+(tight?1.2:2.0)&&(!nuclear||slots[i].Nuclear)&&
                Vector3.Distance(slots[i].Root.transform.position,position)<=(tight?slots[i].CoreRadius:slots[i].CoverRadius)) return slots[i];
            return null;
        }
        // Joins a blast to one already burning around it, as a secondary lobe. False when none can take it (no host, or the
        // host has no room left): the blast then gets its own effect instead of vanishing. A warhead joins only a blast it
        // lands inside (each bomblet of a cluster bomb is its own detonation).
        internal bool Absorb(Vector3 position,Vector3 origin,double score,bool tight,bool nuclear=false)
        {
            FxInstance host=Covering(position,score,tight,nuclear);
            // A nuclear cloud takes what lands in it without growing a second fireball: the nuke's own detonation, seen
            // again after its warhead part died, made two mushrooms side by side.
            if(host!=null&&host.Nuclear) return true;
            if(host==null) return false;
            if(host.AddSecondary(origin,score/Math.Max(host.Score,1e-3))) return true;
            // No room for one more lobe: a blast no stronger than the fire it lands in is drawn by it (a second
            // fireball drawn inside it read as two nested explosions).
            return host.HasVolume&&score<=host.Score+0.3;
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
        // How far a blast is drawn: the effect distance; a nuclear cloud, seen from tens of kilometres, at least 60 km (beyond
        // the effect distance it was a light burst: no cloud at all).
        double Reach(FxPlan plan) { return plan!=null&&plan.Warhead==WarheadKind.Nuke?Math.Max(settings.Distance,60000):settings.Distance; }
        internal bool InRange(Vector3 position,bool nuclear)
        {
            Camera cam=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:Camera.main;
            return cam!=null&&Vector3.Distance(cam.transform.position,position)<=(nuclear?Math.Max(settings.Distance,60000):settings.Distance);
        }
        internal bool InRange(Vector3 position)
        {
            Camera cam=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:Camera.main;
            return cam!=null&&Vector3.Distance(cam.transform.position,position)<=settings.Distance;
        }
        static readonly int SunId=Shader.PropertyToID("_VefxSun"), AmbientId=Shader.PropertyToID("_VefxAmbient"),
            SkyId=Shader.PropertyToID("_VefxSky"), UpId=Shader.PropertyToID("_VefxUp"),
            Fire0Id=Shader.PropertyToID("_VefxFire0"), Fire1Id=Shader.PropertyToID("_VefxFire1"), FireRangeId=Shader.PropertyToID("_VefxFireRange"),
            LampId=Shader.PropertyToID("_VefxLamp"), LampColorId=Shader.PropertyToID("_VefxLampColor"), LampSpotId=Shader.PropertyToID("_VefxLampSpot");
        Light lamp; float lampSearchAt;
        // How much daylight the scene has (0 night, 1 full sun): an explosion lights its surroundings less against the sun.
        internal static float Daylight;
        static readonly int ModernId=Shader.PropertyToID("_VefxModern");
        // A fresh random seed for every explosion: the same part blowing up twice (or the same test replayed) never repeats.
        static readonly System.Random saltSource=new System.Random(Environment.TickCount^(int)DateTime.Now.Ticks);
        internal static uint NextSalt() { return (uint)saltSource.Next()^((uint)saltSource.Next()<<16); }
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
            Shader.SetGlobalFloat(ModernId,settings.Looks.UseClassic?0:1);
            float day=(float)strength*Mathf.Clamp01(Vector3.Dot((Vector3)direction,up)*4+0.5f);
            PublishLamp(day); Daylight=day;
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
        // The 1.1 smoke volumes are lit by the strongest lamp of the scene that reaches one of them (vessel floodlights
        // at night). Searched once a second, only while such a cloud exists; washed out by daylight.
        void PublishLamp(float day)
        {
            bool smoke=false;
            for(int i=0;i<slots.Length&&!smoke;i++) smoke=slots[i].Active&&slots[i].SimSmoke;
            if(!smoke) { lamp=null; Shader.SetGlobalVector(LampId,Vector4.zero); return; }
            if(Time.unscaledTime>=lampSearchAt) { lampSearchAt=Time.unscaledTime+1; lamp=FindLamp(); }
            if(lamp==null||!lamp.isActiveAndEnabled) { Shader.SetGlobalVector(LampId,Vector4.zero); return; }
            Vector3 p=lamp.transform.position, f=lamp.transform.forward; Color c=lamp.color;
            Shader.SetGlobalVector(LampId,new Vector4(p.x,p.y,p.z,lamp.intensity*(1-0.85f*day)));
            Shader.SetGlobalVector(LampColorId,new Vector4(c.r,c.g,c.b,lamp.range));
            Shader.SetGlobalVector(LampSpotId,new Vector4(f.x,f.y,f.z,lamp.type==LightType.Spot?Mathf.Cos(lamp.spotAngle*0.5f*Mathf.Deg2Rad):-2));
        }
        Light[] lights; float lightsAt; Vector3 lightsFrom;
        readonly List<Light> vesselLights=new List<Light>(), partLights=new List<Light>();
        Light FindLamp()
        {
            // Scanning the scene for lights costs tens of milliseconds with the KSC's hundreds: the fixed lights are listed
            // every 30 s, the lamps of the loaded vessels (the ones that move and switch on) every second, cheaply.
            Vector3 eye=Camera.main!=null?Camera.main.transform.position:Vector3.zero;
            if(lights==null||Time.unscaledTime>=lightsAt||(eye-lightsFrom).sqrMagnitude>3000f*3000f)
            { lightsAt=Time.unscaledTime+120; lightsFrom=eye; lights=UnityEngine.Object.FindObjectsOfType<Light>(); }
            vesselLights.Clear();
            var loaded=FlightGlobals.VesselsLoaded;
            if(loaded!=null) for(int v=0;v<loaded.Count;v++) if(loaded[v]!=null) { loaded[v].gameObject.GetComponentsInChildren(false,partLights); vesselLights.AddRange(partLights); }
            Light best=null; double bestScore=0;
            Score(lights,lights.Length,ref best,ref bestScore);
            Score(vesselLights,vesselLights.Count,ref best,ref bestScore);
            return best;
        }
        void Score(IList<Light> list,int count,ref Light best,ref double bestScore)
        {
            for(int k=0;k<count;k++)
            {
                Light l=list[k];
                if(l==null||!l.isActiveAndEnabled||l.type!=LightType.Point&&l.type!=LightType.Spot) continue;
                if(l.gameObject.layer==EffectLayer.Value&&l.transform.root.name.StartsWith("VolumetricExplosionFX",StringComparison.Ordinal)) continue;
                Vector3 lp=l.transform.position, axis=l.transform.forward;
                for(int i=0;i<slots.Length;i++)
                {
                    if(!slots[i].Active||!slots[i].SimSmoke) continue;
                    Vector3 to=slots[i].SmokeCenter-lp; float distance=to.magnitude;
                    double angle=distance>1e-3f?Vector3.Angle(axis,to)*Mathf.Deg2Rad:0;
                    double score=ExplosionLight.LampScore(l.intensity,l.range,distance,slots[i].SmokeSize,l.type==LightType.Spot,angle,l.spotAngle*0.5*Mathf.Deg2Rad);
                    if(score>bestScore) { bestScore=score; best=l; }
                }
            }
        }
        Vector4 Fire(int slot)
        {
            if(slot<0) return Vector4.zero;
            Vector3 p=slots[slot].FirePosition; return new Vector4(p.x,p.y,p.z,slots[slot].FireGlow);
        }
        // Where the smoke shadows of several blasts overlap only the strongest is laid: each darkens what is under
        // it, so they multiplied to black (over a vessel standing there too).
        void OneShadow()
        {
            for(int i=0;i<slots.Length;i++)
            {
                if(!slots[i].ShadowOn) continue;
                Vector3 a=slots[i].ShadowCentre; float ra=slots[i].ShadowRadius;
                for(int j=0;j<slots.Length;j++)
                {
                    if(j==i||!slots[j].ShadowOn) continue;
                    if(Vector3.Distance(a,slots[j].ShadowCentre)>0.6f*(ra+slots[j].ShadowRadius)) continue;
                    bool weaker=slots[i].ShadowWeight<slots[j].ShadowWeight||slots[i].ShadowWeight==slots[j].ShadowWeight&&i>j;
                    if(weaker) { slots[i].HideShadow(); break; }
                }
            }
        }
        internal void Tick(float dt)
        {
            photo=(float)FxSettings.PhotoSpeed(settings.PhotoMode);
            if(photo!=photoApplied||photo!=1&&Time.unscaledTime>photoCheck) { photoCheck=Time.unscaledTime+1; ApplyPhotoSpeed(); }
            dt*=photo;
            if(shake!=null) shake.Enabled=settings.CameraShake&&photo>0;
            Camera cam=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:Camera.main;
            if(lowRes!=null&&Volumes+Splashes>0) plumes.Update(); else plumes.Clear();   // only while a cloud (or a splash) is drawn
            long t0=System.Diagnostics.Stopwatch.GetTimestamp();
            if(Active>0||debris.Active>0) PublishLighting(cam);
            long tl=System.Diagnostics.Stopwatch.GetTimestamp();
            debris.Tick(Mathf.Clamp(dt,0,0.1f),cam);
            if(lightning!=null)
            {
                lightning.Tick(Mathf.Clamp(dt,0,0.1f));
                Vector3 flashAt; float flash, flashRange;
                bool lit=lightning.Flash(out flashAt,out flash,out flashRange);
                Shader.SetGlobalVector(BoltId,lit?new Vector4(flashAt.x,flashAt.y,flashAt.z,flash):Vector4.zero);
                Shader.SetGlobalFloat(BoltRangeId,lit?flashRange:1);
            }
            long t1=System.Diagnostics.Stopwatch.GetTimestamp();
            float coverage=0;
            for(int i=0;i<slots.Length;i++) coverage+=slots[i].Coverage(cam);
            if(settings.Adaptive) governor.Update(Time.unscaledDeltaTime*1000,Time.unscaledDeltaTime,Volumes+FireVolumes+Splashes>0); // only what it can act on
            float adapt=settings.Adaptive?(float)governor.Scale:1;
            if(adapt<0.95f&&Time.unscaledTime>adaptLogAt)
            { adaptLogAt=Time.unscaledTime+5; Debug.Log("[VEFX] Adaptive performance: volume detail x"+adapt.ToString("0.00")+", lights "+governor.Lights(settings.MaxLights)+"/"+settings.MaxLights+", frame budget "+governor.BudgetMs.ToString("0.0")+" ms"); }
            float stepScale=(coverage>1.2f?1.2f/coverage:1)*adapt;
            bool nearDepth=false, farDepth=false;
            for(int i=0;i<slots.Length;i++) if(slots[i].Active)
            {
                slots[i].Tick(Mathf.Clamp(dt,0,0.1f),settings,slots[i].Coverage(cam)>0.2f?stepScale:adapt);
                Vector3 popAt; float popSize;
                if(hits!=null&&slots[i].Pop(out popAt,out popSize)) hits.Hit(popAt,slots[i].Root.transform.up,popSize,true,true,false);
                Vector3 boltA,boltB; float boltWidth;
                if(lightning!=null&&slots[i].Bolt(Mathf.Clamp(dt,0,0.1f),out boltA,out boltB,out boltWidth)) lightning.Strike(boltA,boltB,boltWidth);
                nearDepth|=slots[i].NeedsNearDepth; farDepth|=slots[i].NeedsFarDepth;
            }
            CameraRig.RequestDepth(nearDepth,farDepth);
            OneShadow();
            long t2=System.Diagnostics.Stopwatch.GetTimestamp();
            AssignLights(cam);
            long t3=System.Diagnostics.Stopwatch.GetTimestamp();
            double f=1000.0/System.Diagnostics.Stopwatch.Frequency;
            LightingMs=(tl-t0)*f; DebrisMs=(t1-tl)*f; EffectsMs=(t2-t1)*f; LightsMs=(t3-t2)*f;
            // The white-out of the brightest nuclear flash in view (drawn over the screen by the flight controller).
            float whiteout=0, warmth=0;
            if(cam!=null&&!settings.Looks.UseClassic&&settings.Explosions)
                for(int i=0;i<slots.Length;i++) if(slots[i].Active&&slots[i].Nuclear&&slots[i].Age<5)
                {
                    float w=(float)NukeCloud.ScreenFlash(slots[i].Age,Vector3.Distance(cam.transform.position,slots[i].Root.transform.position),slots[i].FireballRadius);
                    if(w>whiteout) { whiteout=w; warmth=(float)NukeCloud.ScreenWarmth(slots[i].Age); }
                }
            ScreenFlash=whiteout; ScreenWarmth=warmth;
        }
        // Last frame's stage times (no allocation; formatted only when a slow frame is logged).
        internal double DebrisMs, EffectsMs, LightsMs, LightingMs;
        internal float ScreenFlash;   // 0..1: a nuclear flash whiting out the view
        internal float ScreenWarmth;  // 0 white .. 1 amber
        internal double DebrisFitMs { get { return debris.FitMs; } }
        internal double DebrisMeshMs { get { return debris.MeshMs; } }
        readonly FrameGovernor governor=new FrameGovernor();
        float adaptLogAt;
        internal double Adaptation { get { return governor.Scale; } }
        // Sized to the pool (it was a fixed 24: with more slots the last ones never got a light).
        readonly double[] lightPriority;
        readonly bool[] lightChosen;
        readonly int[] lightOrder;
        // Light budget re-ranked every frame: brightest, largest, closest lights first, so a big
        // new explosion takes a light from a fading one instead of going dark.
        void AssignLights(Camera cam)
        {
            int n=Math.Min(slots.Length,lightPriority.Length);
            Vector3 eye=cam!=null?cam.transform.position:Vector3.zero;
            for(int i=0;i<n;i++)
                lightPriority[i]=slots[i].Active&&cam!=null?ExplosionLight.Priority(slots[i].LightWanted,slots[i].LightRange,
                    Vector3.Distance(eye,slots[i].LightPosition)):0;
            int chosen=ExplosionLight.Select(lightPriority,n,settings.Lighting?(settings.Adaptive?governor.Lights(settings.MaxLights):settings.MaxLights):0,lightChosen,lightOrder);
            for(int i=0;i<n;i++) if(slots[i].Active) slots[i].AllowLight(lightChosen[i],chosen>0&&lightOrder[0]==i);
        }
        internal void Shift(Vector3 offset) { for(int i=0;i<slots.Length;i++) slots[i].Shift(offset); debris.Shift(offset); if(hits!=null) hits.Shift(offset); if(lightning!=null) lightning.Shift(offset); }
        internal void Clear() { for(int i=0;i<slots.Length;i++) slots[i].Clear(); debris.Clear(); if(hits!=null) hits.Clear(); if(lightning!=null) lightning.Clear(); if(shake!=null) shake.Clear(); }
        // Small combat hits: shared, cheap, never take a slot (see HitBursts).
        readonly HitBursts hits;
        readonly Lightning lightning;
        readonly CameraShake shake;
        internal int Jolts { get { return shake!=null?shake.Count:0; } }
        float photo=1, photoApplied=-1;
        // Photo mode: VEFX's own clock and its particle systems run slower or stop; the game itself keeps its pace.
        void ApplyPhotoSpeed()
        {
            photoApplied=photo;
            for(int i=0;i<slots.Length;i++) if(slots[i]!=null) { Speed(slots[i].Root,photo); Speed(slots[i].DebrisRoot,photo); }
            debris.SetSpeed(photo);
            if(hits!=null) hits.SetSpeed(photo);
        }
        internal static void Speed(GameObject root,float speed)
        {
            if(root==null) return;
            foreach(ParticleSystem p in root.GetComponentsInChildren<ParticleSystem>(true)) { ParticleSystem.MainModule main=p.main; main.simulationSpeed=speed; }
        }
        float photoCheck;
        static readonly int BoltId=Shader.PropertyToID("_VefxBolt"), BoltRangeId=Shader.PropertyToID("_VefxBoltRange");
        internal int Strokes { get { return lightning!=null?lightning.Count:0; } }
        internal int Hits { get { return hits!=null?hits.Count:0; } }
        internal bool Hit(Vector3 world,Vector3 up,float size,bool explosive,bool ground,bool water)
        {
            if(hits==null) return false;
            hits.Hit(world,up,size,explosive,ground,water); return true;
        }
        public void Dispose()
        {
            if(hooked) { Camera.onPreCull-=OnPreCull; hooked=false; }
            CameraRig.Release();
            if(lowRes!=null) { lowRes.Dispose(); lowRes=null; }
            for(int i=0;i<slots.Length;i++) if(slots[i]!=null) slots[i].Dispose();
            if(debris!=null) debris.Dispose(); if(hits!=null) hits.Dispose(); if(lightning!=null) lightning.Dispose(); if(shake!=null) shake.Dispose(); volumes.Dispose(); assets.Dispose();
        }
    }
}
