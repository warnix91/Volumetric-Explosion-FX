using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VolumetricExplosionFX.Core;
using VolumetricExplosionFX.Rendering;
namespace VolumetricExplosionFX.Ksp
{
    [KSPAddon(KSPAddon.Startup.Flight,false)]
    public sealed class FlightController : MonoBehaviour
    {
        internal const string Version="1.1.0";
        static FlightController active;
        readonly SnapshotCache cache=new SnapshotCache();
        readonly RecentEvents recent=new RecentEvents();
        readonly ResourceClassifier classifier=new ResourceClassifier();
        EventClusterer clusters;
        FxSettings settings;
        FxPool pool;
        StockVisualFilter stock;
        StreamWriter log;
        int failures, confirmed, absorbed, anchored, willDieReports, groundReports, waterReports, missingSnapshots;
        float nextSummary;
        bool subscribed, overlay;
        string lastEvent="Awaiting confirmed destruction";
        string structure; float restructureAt=-1;
        uint testCount;
        readonly PerfProbe probe=new PerfProbe();
        struct PendingBlast { public Vector3 Point; public int Kind; public double At; }
        readonly List<PendingBlast> pendingBlasts=new List<PendingBlast>();
        // 1.1 fire trail: secondary fireballs thrown ahead of a vehicle that hit the ground with sideways speed.
        struct PendingTrail { public PartSnapshot Source; public Vector3 Point, Ahead; public double At, Fuel, Speed; }
        readonly List<PendingTrail> pendingTrails=new List<PendingTrail>();
        readonly Dictionary<Guid,double> lastTrail=new Dictionary<Guid,double>();
        readonly FireTrailStep[] trailSteps=new FireTrailStep[FireTrail.MaxSteps];
        uint trailCount; double anyTrail=-10;
        // Name of another installed mod that also replaces explosions (null: none); the warning is shown once per game.
        internal static string OtherExplosionMod;
        static bool conflictShown;
        void Start()
        {
            active=this;
            try
            {
                settings=SettingsStore.Settings;
                SettingsReader.Resources(classifier);
                if(settings.Enabled) Init();
            }
            catch(Exception ex) { Fail(ex); OnDestroy(); }
        }
        void Init()
        {
            try
            {
                structure=settings.Structure;
                clusters=new EventClusterer(settings.MaxEvents);
                pool=new FxPool(settings);
                stock=new StockVisualFilter(p=>pool!=null&&pool.Covers(p,0),p=>pool!=null&&pool.InRange(p)); stock.SetEnabled(ReplaceStock);
                combat=new CombatBridge(); combat.Hide=ReplaceStock;
                if(settings.Debug)
                {
                    string path=Path.Combine(KSPUtil.ApplicationRootPath,"GameData/VolumetricExplosionFX/PluginData");
                    Directory.CreateDirectory(path);
                    log=new StreamWriter(Path.Combine(path,"visual-events.log"),false); log.AutoFlush=true;
                }
                GameEvents.onPartWillDie.Add(OnWillDie); GameEvents.onPartDie.Add(OnDie);
                GameEvents.onCrash.Add(OnCrash); GameEvents.onCrashSplashdown.Add(OnSplash);
                GameEvents.onFloatingOriginShift.Add(OnOriginShift); GameEvents.OnKSCStructureCollapsing.Add(OnCollapse); subscribed=true;
                Aftermath.SceneWind=Aftermath.NewSceneWind((uint)Environment.TickCount);   // one breeze for this flight
                settings.PhotoMode=0;   // a frozen photo mode left on does not carry over to the next flight
                Debug.Log("[VEFX] Scene wind "+Aftermath.SceneWind.Length.ToString("0.0")+" m/s");
                nextSummary=Time.unscaledTime+5;
                Debug.Log("[VEFX] Volumetric Explosion FX "+Version+" loaded in flight.");
                var names=new List<string>();
                foreach(var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    names.Add(a.GetName().Name);
                    if(a.GetName().Name=="ProjectDestructionFX") Debug.LogWarning("[VEFX] An older copy of this mod (GameData/ProjectDestructionFX) is still installed: remove that folder, both together double every effect.");
                }
                var folders=new List<string>();
                try { foreach(string d in Directory.GetDirectories(Path.Combine(KSPUtil.ApplicationRootPath,"GameData"))) folders.Add(Path.GetFileName(d)); }
                catch(Exception) { }
                OtherExplosionMod=Conflicts.Find(names,folders);
                var visual=new List<string>();
                // By assembly name, any case and any suffix (Scatterer ships as "scatterer", Parallax as "ParallaxContinued").
                foreach(string mod in new[]{"Scatterer","EVEManager","Parallax","Deferred","TUFX","Firefly","Waterfall","Kopernicus"})
                    foreach(string name in names)
                        if(name!=null&&name.StartsWith(mod,StringComparison.OrdinalIgnoreCase)) { visual.Add(name); break; }
                Debug.Log("[VEFX] Visual mods: "+(visual.Count>0?string.Join(", ",visual.ToArray()):"none"));
                stackLogAt=Time.unscaledTime+20;
                if(OtherExplosionMod!=null)
                {
                    Debug.LogWarning("[VEFX] Another explosion mod is installed ("+OtherExplosionMod+"): both replace the game's explosions, so every blast is drawn twice. Keep only one of them.");
                    if(!conflictShown)
                    {
                        conflictShown=true;
                        ScreenMessages.PostScreenMessage("VEFX: "+GameText.Current.Format("OtherMod","{0} is also installed: both replace the game's explosions, so every blast is drawn twice. Keep only one of them.",OtherExplosionMod),12f,ScreenMessageStyle.UPPER_CENTER);
                    }
                }
                Debug.Log("[VEFX] Supported shader selection: "+pool.ShaderSummary);
                Debug.Log("[VEFX] Stock replacement: "+settings.ReplaceStockVisuals+" / "+stock.Status);
                Debug.Log("[VEFX] Combat mods: "+combat.Status+(combat.Available?(combat.Hide?" (their explosion visuals replaced)":" (drawn alongside)"):""));
            }
            catch(Exception ex) { Fail(ex); OnDestroy(); }
        }
        // Hitch diagnosis: time spent in destruction callbacks and in each stage of Update.
        readonly System.Diagnostics.Stopwatch hitchWatch=new System.Diagnostics.Stopwatch();
        double callbackMs; int callbackCount; float hitchLogAt;
        void OnWillDie(Part part)
        {
            long start=hitchWatch.ElapsedTicks; hitchWatch.Start();
            willDieReports++; Capture(part,EventKind.Destruction,null);
            if(pool!=null) { try { pool.PrepareDebris(part); } catch(Exception ex) { Fail(ex); } }
            callbackMs+=(hitchWatch.ElapsedTicks-start)*1000.0/System.Diagnostics.Stopwatch.Frequency; callbackCount++;
        }
        void OnCrash(EventReport report)
        {
            if(report==null || report.origin==null) return;
            groundReports++;
            try
            {
                PartSnapshot s=SnapshotReader.Read(report.origin); Vector3 point;
                if(s!=null&&SnapshotReader.TryGround(report.origin,s,out point)) Capture(report.origin,EventKind.GroundImpact,SnapshotReader.Value(point));
            }
            catch(Exception ex) { Fail(ex); }
        }
        void OnSplash(EventReport report)
        {
            if(report==null || report.origin==null) return;
            waterReports++;
            try
            {
                Part p=report.origin; CelestialBody b=p.vessel!=null?p.vessel.mainBody:null;
                if(b==null || !b.ocean) return;
                Vector3d r=(Vector3d)p.transform.position-b.position;
                Capture(p,EventKind.WaterImpact,SnapshotReader.Value(b.position+r.normalized*b.Radius));
            }
            catch(Exception ex) { Fail(ex); }
        }
        void Capture(Part part,EventKind kind,Vec3? point)
        {
            if(pool==null || part==null) return;
            try
            {
                PartSnapshot s=SnapshotReader.Read(part);
                if(s!=null) cache.Capture(s,kind,point??s.Position,Time.time);
            }
            catch(Exception ex) { Fail(ex); }
        }
        void OnDie(Part part)
        {
            if(pool==null || part==null) return;
            long start=hitchWatch.ElapsedTicks; hitchWatch.Start();
            try { Die(part); }
            finally { callbackMs+=(hitchWatch.ElapsedTicks-start)*1000.0/System.Diagnostics.Stopwatch.Frequency; callbackCount++; }
        }
        void Die(Part part)
        {
            try
            {
                double now=Time.time; int id=part.GetInstanceID();
                if(!recent.Accept(id,now)) return;
                DestructionEvent e=cache.Confirm(id,now,null);
                if(e==null) e=cache.Confirm(id,now,SnapshotReader.Read(part));
                if(e==null) { missingSnapshots++;return; }
                PartSnapshot s=e.Part;
                FxPlan plan=VisualEnergy.Calculate(e,classifier,settings);
                pool.CaptureDebris(part,e,plan);
                Vector3 below;
                if(e.Kind==EventKind.Destruction&&!s.Environment.Splashed&&settings.Ground&&
                    SnapshotReader.GroundBelow(SnapshotReader.Vector(s.Position),SnapshotReader.Vector(s.Environment.Up),
                        (float)Numbers.Clamp(VolumeEvolution.DomeRadius(plan)*0.5,3,25),out below))
                {
                    e=new DestructionEvent(s.At(SnapshotReader.Value(below)),EventKind.GroundImpact,e.Time,s.Position);
                    s=e.Part; plan=VisualEnergy.Calculate(e,classifier,settings); anchored++;
                }
                Vector3 where=SnapshotReader.Vector(s.Position);
                int owner;
                // A bomb or missile destroyed as a part (it hit the ground unarmed, or was shot down) still carries its
                // charge: it gets the warhead's explosion, spawned at once, whether or not the combat mod detonates it.
                WarheadInfo warhead=default(WarheadInfo); string weapon;
                bool armed=combat!=null&&combat.WarheadOf(part,out warhead,out weapon)&&!Warhead.Small(warhead);
                if(armed) { Warhead.Apply(plan,warhead); warheadParts++; }
                bool nuclear=armed&&warhead.Kind==WarheadKind.Nuke;
                if(nuclear&&(pool.NuclearNear(where,(float)Warhead.SameNuke(VolumeEvolution.FireballRadius(plan)),10)||PendingNuke(where,Warhead.SameNuke(VolumeEvolution.FireballRadius(plan)))))
                {
                    owner=VisualReplacementWindows.Covered; absorbed++;   // (its detonation was drawn already)
                }
                else if(pool.Absorb(where,SnapshotReader.Vector(e.Origin),plan.VisualEnergyScore,false,nuclear)||armed&&pool.Covers(where,plan.VisualEnergyScore,true,nuclear))
                {
                    owner=VisualReplacementWindows.Covered; absorbed++;   // (a charge already drawn by its detonation: one explosion)
                }
                else if(armed)
                {
                    warheadCount++;
                    owner=0x40000000+(int)(warheadCount&0xFFFFFF);
                    // Started in Update within the frame's spawn budget, not inside KSP's part callback (a bomb's set-up
                    // costs milliseconds: four of them made a 25 ms callback).
                    pendingCharges.Add(new PendingCharge { Cluster=EventCluster.Single(owner,e,plan.VisualEnergyScore), Plan=plan, Where=where,
                        Up=SnapshotReader.Vector(s.Environment.Up), Burst=Mathf.Max((float)Warhead.BurstSize(warhead.TntKg,0),2), Kind=e.Kind });
                }
                else
                {
                    owner=clusters.Add(e,plan.VisualEnergyScore);
                    if(clusters.LastEvicted>0) stock.Release(clusters.LastEvicted);
                }
                if(owner!=0&&ReplaceStock&&pool.InRange(where))
                    stock.Claim(s.Position,now,Numbers.Clamp(s.Dimensions.Length*3,10,45),owner);
                ScheduleTrail(e,plan,now);
                confirmed++;
                lastEvent=string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} | {1} | {2} | {3:F0} kg | {4:F1} m/s | score {5:F2} | rho {6:F4} | p {7:F2} kPa",
                    e.Kind,s.Name,plan.PartKind,s.MassKg,s.SurfaceVelocity.Length,plan.VisualEnergyScore,s.Environment.Density,s.Environment.PressureKpa);
                if(log!=null) log.WriteLine(lastEvent);
            }
            catch(Exception ex) { Fail(ex); }
        }
        void Update()
        {
            if(pool==null)
            {
                if(settings!=null&&settings.Enabled&&!subscribed&&failures==0) Init();
                if(pool==null) return;
            }
            if(Input.GetKey(KeyCode.LeftAlt)&&Input.GetKeyDown(KeyCode.F8)) overlay=!overlay;
            if(Input.GetKey(KeyCode.LeftAlt)&&Input.GetKeyDown(KeyCode.F7))
            {
                // Photo mode: the explosions slow down or freeze (the game keeps running) for screenshots.
                settings.PhotoMode=(settings.PhotoMode+1)%3;
                var text=GameText.Current;
                string mode=settings.PhotoMode==1?text.Get("PhotoSlow","Slow motion"):settings.PhotoMode==2?text.Get("PhotoFrozen","Frozen"):text.Get("PhotoOff","Off");
                ScreenMessages.PostScreenMessage(text.Format("PhotoModeMessage","VEFX photo mode: {0}",mode),2f,ScreenMessageStyle.UPPER_CENTER);
            }
            // The combat bridge only hides BDArmory's visuals while VEFX is drawing (effects on, flight view, no time warp):
            // otherwise BDArmory keeps its own explosions and nothing waits in the queue.
            bool drawing=settings.Enabled&&!(MapView.MapIsEnabled||TimeWarp.CurrentRate>4||(FlightGlobals.ActiveVessel!=null&&FlightGlobals.ActiveVessel.packed));
            if(combat!=null) combat.SetActive(drawing,ReplaceStock&&settings.Explosions);
            if(!settings.Enabled) { stock.SetEnabled(false); clusters.Clear(); pool.Clear(); pendingCharges.Clear(); return; }
            if(settings.Structure!=structure)
            {
                if(restructureAt<0) restructureAt=Time.unscaledTime+0.6f;
                // Rebuilding clears every effect: wait until none is young (at most 25 s), so switching a quick setup
                // during a crash does not make its explosions vanish.
                else if(Time.unscaledTime>=restructureAt&&(pool.Quiet||Time.unscaledTime>=restructureAt+25)) { Rebuild(); restructureAt=-1; }
            }
            else restructureAt=-1;
            stock.SetEnabled(ReplaceStock&&!MapView.MapIsEnabled);
            if(TimeWarp.CurrentRate>4) { clusters.Clear(); pool.Clear(); pendingCharges.Clear(); return; }
            bool map=MapView.MapIsEnabled, packed=FlightGlobals.ActiveVessel!=null&&FlightGlobals.ActiveVessel.packed;
            if(map||packed)
            {
                // The map, or the active vessel briefly on rails (KSP switching vessels after a crash): the effects already
                // burning go on (they were cleared, so a crash's explosions vanished at the switch); nothing new starts.
                // On the map the waiting blasts are dropped; a switch only holds them back for the frames it takes.
                if(map) { clusters.Clear(); pendingCharges.Clear(); }
                try { pool.Tick(Time.deltaTime); } catch(Exception ex) { Fail(ex); pool.Clear(); }
                return;
            }
            try
            {
                probe.Begin();
                long t0=System.Diagnostics.Stopwatch.GetTimestamp(); int spawned=0;
                // Setting up an effect costs 1-3 ms (ground drapes for the shadow and the scorch, the volume): past 6 ms in a
                // frame the rest waits for the next frame (ten blasts at once made a 16 ms spawn).
                long spawnBudget=System.Diagnostics.Stopwatch.Frequency*6/1000;
                for(int i=pendingBlasts.Count-1;i>=0;i--)
                    if(Time.time>=pendingBlasts[i].At) { PendingBlast b=pendingBlasts[i]; pendingBlasts.RemoveAt(i); Blast(b.Kind,b.Point); }
                for(int i=pendingCollapses.Count-1;i>=0;i--)
                    if(Time.time>=pendingCollapses[i].At)
                    {
                        if(System.Diagnostics.Stopwatch.GetTimestamp()-t0>spawnBudget) break;
                        PendingCollapse due=pendingCollapses[i]; pendingCollapses.RemoveAt(i); try { Collapse(due); } catch(Exception ex) { Fail(ex); }
                    }
                HideCollapseFx();
                if(stackLogAt>0&&Time.unscaledTime>=stackLogAt) { stackLogAt=-1; try { Debug.Log(CameraRig.RenderStack()); } catch(Exception ex) { Fail(ex); } }
                int started=0;
                while(started<pendingCharges.Count&&(started==0||System.Diagnostics.Stopwatch.GetTimestamp()-t0<=spawnBudget))
                {
                    PendingCharge pc=pendingCharges[started++];
                    if(pc.Plan.Warhead==WarheadKind.Nuke&&pool.NuclearNear(pc.Where,(float)Warhead.SameNuke(VolumeEvolution.FireballRadius(pc.Plan)),10)) continue;
                    // No room for a full effect: still ours, as a large burst (the stock explosion stays hidden).
                    try { if(!pool.Spawn(pc.Cluster,pc.Plan)) pool.Hit(pc.Where,pc.Up,pc.Burst,true,pc.Kind==EventKind.GroundImpact,pc.Kind==EventKind.WaterImpact); }
                    catch(Exception ex) { Fail(ex); }
                }
                pendingCharges.RemoveRange(0,started);
                CombatBridge.Detonation d;
                while(combat!=null&&System.Diagnostics.Stopwatch.GetTimestamp()-t0<=spawnBudget&&combat.Next(out d)) Detonate(d);
                for(int i=pendingTrails.Count-1;i>=0;i--)
                    if(Time.time>=pendingTrails[i].At) { PendingTrail t=pendingTrails[i]; pendingTrails.RemoveAt(i); Trail(t); }
                EventCluster c;
                while((spawned==0||System.Diagnostics.Stopwatch.GetTimestamp()-t0<=spawnBudget)&&(c=clusters.TakeReady(Time.time))!=null)
                {
                    FxPlan plan=VisualEnergy.Calculate(c.Representative,classifier,settings);
                    if(!pool.Spawn(c,plan)) stock.Release(c.Id);
                    spawned++;
                }
                long t1=System.Diagnostics.Stopwatch.GetTimestamp();
                if(settings.Looks.Forced>=0&&Time.time>forcedLookUntil) settings.Looks.Forced=-1;
                pool.Tick(Time.deltaTime);
                probe.End();
                ReportHitch(Ms(t0,t1),Ms(t1,System.Diagnostics.Stopwatch.GetTimestamp()),spawned);
                probe.Frame(pool.Load,settings.Quality.ToString());
                if(Time.unscaledTime>=nextSummary)
                {
                    nextSummary=Time.unscaledTime+5;
                    if(confirmed>0||testCount>0) ReportSummary();
                }
            }
            catch(Exception ex) { Fail(ex); clusters.Clear(); pool.Clear(); pendingCharges.Clear(); }
        }
        static double Ms(long a,long b) { return (b-a)*1000.0/System.Diagnostics.Stopwatch.Frequency; }
        // One line per slow frame (at most two per second), so a stutter can be traced to its stage.
        void ReportHitch(double spawnMs,double tickMs,int spawned)
        {
            double total=spawnMs+tickMs+callbackMs;
            if(total>20&&Time.unscaledTime>=hitchLogAt)
            {
                hitchLogAt=Time.unscaledTime+0.5f;
                Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[VEFX] Slow frame: {0:0.0} ms in VEFX | destruction callbacks {1:0.0} ms ({2}) | spawn {3:0.0} ms ({4} blasts) | lighting {9:0.0} ms | debris {5:0.0} ms (ground fit {10:0.0}, meshes {11:0.0}) | effects {6:0.0} ms | lights {7:0.0} ms | frame {8:0.0} ms",
                    total,callbackMs,callbackCount,spawnMs,spawned,pool.DebrisMs,pool.EffectsMs,pool.LightsMs,Time.unscaledDeltaTime*1000,pool.LightingMs,pool.DebrisFitMs,pool.DebrisMeshMs)+
                    (spawned>0?string.Format(System.Globalization.CultureInfo.InvariantCulture," | spawn stages: set-up {0:0.0}, residue+scorch {1:0.0}, volume {2:0.0}, shadow {3:0.0}, particles {4:0.0} ms",
                        FxInstance.BeginMs[0],FxInstance.BeginMs[4],FxInstance.BeginMs[2],FxInstance.BeginMs[1],FxInstance.BeginMs[3]):""));
            }
            callbackMs=0; callbackCount=0;
            for(int i=0;i<FxInstance.BeginMs.Length;i++) FxInstance.BeginMs[i]=0;
        }
        void Rebuild()
        {
            try
            {
                structure=settings.Structure;
                clusters.Clear(); pool.Dispose(); pool=null;
                clusters=new EventClusterer(settings.MaxEvents);
                pool=new FxPool(settings);
                Debug.Log("[VEFX] Effect pool rebuilt for the new settings ("+structure+").");
            }
            catch(Exception ex) { Fail(ex); }
        }
        internal static bool CanTest { get { return active!=null&&active.pool!=null&&active.settings.Enabled&&!MapView.MapIsEnabled&&
            TimeWarp.CurrentRate<=4&&(FlightGlobals.ActiveVessel==null||!FlightGlobals.ActiveVessel.packed); } }
        internal static bool Test(int kind) { return active!=null&&active.TestBlast(kind); }
        // Look editor: a test blast of a matching kind, forced to render with the profile being tuned.
        internal static bool TestLook(LookKind look)
        {
            if(active==null) return false;
            int kind=look==LookKind.Solid?2:look==LookKind.Vessel?1:look==LookKind.Tank?0:3;
            active.settings.Looks.Forced=(int)look; active.forcedLookUntil=Time.time+3;
            bool ok=active.TestBlast(kind);
            if(!ok) active.settings.Looks.Forced=-1;
            else Debug.Log("[VEFX] Test: look profile "+look);
            return ok;
        }
        float forcedLookUntil;
        // One trail per vehicle per crash: the first ground impact with sideways speed throws the burning fuel ahead.
        void ScheduleTrail(DestructionEvent e,FxPlan plan,double now)
        {
            if(e.Kind!=EventKind.GroundImpact||settings.Looks.UseClassic||!settings.Explosions||e.Part.Name=="fire trail"||e.Part.Name=="warhead") return;
            PartSnapshot s=e.Part;
            Vector3 up=SnapshotReader.Vector(s.Environment.Up), v=SnapshotReader.Vector(s.SurfaceVelocity);
            Vector3 flat=Vector3.ProjectOnPlane(v,up);
            double last;
            if(lastTrail.TryGetValue(s.VesselId,out last)&&now-last<3||now-anyTrail<1) return;   // the pieces of one crash share one trail
            int n=FireTrail.Plan(flat.magnitude,VolumeEvolution.FireballRadius(plan),plan.BurnableKg,trailSteps,s.PersistentId^(uint)(now*1000));
            if(n==0||pendingTrails.Count>12) return;
            lastTrail[s.VesselId]=now; anyTrail=now;
            Vector3 ahead=flat.normalized, start=SnapshotReader.Vector(s.Position), aside=Vector3.Cross(up,ahead).normalized;
            for(int k=0;k<n;k++)
                pendingTrails.Add(new PendingTrail { Source=s, Point=start+ahead*(float)trailSteps[k].Distance+aside*(float)trailSteps[k].Side, Ahead=ahead,
                    At=Time.time+trailSteps[k].Delay, Fuel=trailSteps[k].Fuel, Speed=trailSteps[k].Speed });
        }
        void Trail(PendingTrail t)
        {
            try
            {
                if(pool==null) return;
                PartSnapshot s=t.Source;
                Vector3 up=SnapshotReader.Vector(s.Environment.Up), below;
                if(!SnapshotReader.GroundBelow(t.Point+up*60,up,160,out below)) return;   // thrown over a cliff or into the sea: nothing lands
                var fuel=new ResourceSnapshot[s.Resources.Length];
                for(int i=0;i<fuel.Length;i++) { ResourceSnapshot r=s.Resources[i]; fuel[i]=new ResourceSnapshot(r.Name,r.Amount*t.Fuel,r.Capacity,r.MassKg*t.Fuel); }
                Vector3 velocity=t.Ahead*(float)t.Speed-up*6;
                trailCount++;
                var snapshot=new PartSnapshot(s.PersistentId^(0x7A110000u+trailCount),-(int)(2000000+trailCount),s.VesselId,"fire trail",SnapshotReader.Value(below),
                    SnapshotReader.Value(velocity),s.InertialVelocity,new Vec3(),s.Dimensions*0.6,s.MassKg*t.Fuel,s.Temperature,new string[0],fuel,s.Environment);
                var e=new DestructionEvent(snapshot,EventKind.GroundImpact,Time.time);
                FxPlan plan=VisualEnergy.Calculate(e,classifier,settings);
                if(pool.Absorb(below,below,plan.VisualEnergyScore,false)) absorbed++;
                else clusters.Add(e,plan.VisualEnergyScore);
            }
            catch(Exception ex) { Fail(ex); }
        }
        // A combat mod's explosion drawn by VEFX: an event at the blast point (on the ground when it is close to it), with the
        // warhead's look, spawned at once. Whenever VEFX does not draw it (out of range, no room...), the combat mod's own
        // explosion visuals are given back the same frame: every detonation shows, one way or the other.
        CombatBridge combat;
        uint warheadCount;
        int combatDrawn, combatGivenBack, combatHits, warheadParts;
        void Detonate(CombatBridge.Detonation d)
        {
            string outcome=null; bool drawn=false;
            try { drawn=Draw(d,out outcome); }
            catch(Exception ex) { Fail(ex); outcome="error "+ex.GetType().Name; }
            if(drawn)
            {
                combatDrawn++;
                if(d.Hit) return;   // bullet impacts are counted, not listed
            }
            else { combatGivenBack++; if(combat!=null) combat.GiveBack(d.Handle); outcome=(outcome??"not drawn")+", BDArmory's own visuals shown"; }
            Note(d,outcome);
        }
        bool Draw(CombatBridge.Detonation d,out string outcome)
        {
            outcome=null;
            {
                if(pool==null||!settings.Explosions) { outcome=pool==null?"no pool":"explosions off"; return false; }
                CelestialBody body=FlightGlobals.currentMainBody;
                if(body==null) { outcome="no body"; return false; }
                Vector3d radial=(Vector3d)d.Position-body.position;
                Vector3 up=(Vector3)radial.normalized, below, point=d.Position;
                double altitude=radial.magnitude-body.Radius;
                bool small=d.Hit||Warhead.Small(d.Info);
                float reach=small?2.5f:(float)Numbers.Clamp(d.Info.RangeM*0.25,2,40);
                bool ground=SnapshotReader.GroundBelow(d.Position+up*2,up,reach+2,out below);
                bool water=!ground&&body.ocean&&altitude<Math.Max(3,reach*0.5);
                // A fast weapon detonates a frame or two after it passed the surface: a surface burst, not a deep one.
                if(water&&altitude<0) altitude=Math.Min(0,altitude+0.04*Math.Max(-Vector3.Dot(d.Velocity,up),0));
                if(ground) point=below;
                else if(water) point=d.Position-up*(float)altitude;
                float burst=(float)Warhead.BurstSize(d.Info.TntKg,d.Caliber);
                bool explosive=!d.Hit&&d.Info.Kind!=WarheadKind.Kinetic&&d.Info.TntKg>0;
                // Small hits (and anything beyond the effect distance, seen as a speck) get the light burst.
                if(small||!pool.InRange(d.Position,d.Info.Kind==WarheadKind.Nuke))
                {
                    pool.Hit(ground||water?point:d.Position,up,small?burst:Mathf.Max(burst,2),explosive,ground,water);
                    Camera eye=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:Camera.main;
                    outcome=small?"small hit":"far ("+(eye!=null?(Vector3.Distance(eye.transform.position,d.Position)/1000).ToString("0.0"):"?")+" km): light burst"; combatHits++; return true;
                }
                double pressure=body.atmosphere?body.GetPressure(Math.Max(altitude,0)):0, density=0;
                if(pressure>0) density=body.GetDensity(pressure,body.GetTemperature(Math.Max(altitude,0)));
                double gravity=body.gravParameter/Math.Max(1,radial.sqrMagnitude);
                // The blast keeps only a little of the missile's speed: the gas is stopped by the air within metres.
                Vector3 velocity=d.Velocity.sqrMagnitude>0.01f?d.Velocity*0.15f:-up*5;
                // On the ground or water the blast is stopped dead: none of the weapon's dive (it drove the fireball into the ground).
                if(ground||water) velocity=Vector3.ProjectOnPlane(d.Velocity,up)*0.05f;
                double size=Numbers.Clamp(Math.Pow(Math.Max(d.Info.TntKg,0.01),1.0/3)*0.6,0.2,6);
                var env=new EnvironmentContext(body.bodyName,density,pressure,gravity,altitude,body.ocean,ground,water,SnapshotReader.Value(up));
                warheadCount++;
                uint id=0x0BDA0000u+warheadCount;
                var snapshot=new PartSnapshot(id,-(int)(3000000+warheadCount),Guid.Empty,"warhead",SnapshotReader.Value(point),
                    SnapshotReader.Value(velocity),SnapshotReader.Value(velocity),new Vec3(),new Vec3(size,size,size),Math.Max(d.Info.TntKg*1.6,1),300,new string[0],new ResourceSnapshot[0],env);
                var e=new DestructionEvent(snapshot,ground?EventKind.GroundImpact:water?EventKind.WaterImpact:EventKind.Destruction,Time.time);
                FxPlan plan=VisualEnergy.Calculate(e,classifier,settings); Warhead.Apply(plan,d.Info);
                // The same charge already drawn (its part died first): one explosion, not two, even when no lobe is added.
                bool nuclear=d.Info.Kind==WarheadKind.Nuke;
                if(nuclear)
                {
                    // The same warhead seen twice: its part died first (it showed two mushrooms side by side). Drawn already:
                    // nothing more; still waiting: the detonation (at its real burst point) takes its place.
                    double same=Warhead.SameNuke(VolumeEvolution.FireballRadius(plan));
                    if(pool.NuclearNear(point,(float)same,10)) { absorbed++; outcome="the same nuclear blast, already drawn"; return true; }
                    for(int i=pendingCharges.Count-1;i>=0;i--)
                        if(pendingCharges[i].Plan.Warhead==WarheadKind.Nuke&&Vector3.Distance(pendingCharges[i].Where,point)<=same) pendingCharges.RemoveAt(i);
                }
                if(pool.Absorb(point,point,plan.VisualEnergyScore,true,nuclear)||pool.Covers(point,plan.VisualEnergyScore,true,nuclear)) { absorbed++; outcome="inside a blast already burning"; return true; }
                outcome=(ground?"ground":water?"water":"air")+" burst, score "+plan.VisualEnergyScore.ToString("0.0")+", radius "+plan.Radius.ToString("0.0")+" m";
                if(pool.Spawn(EventCluster.Single(0x40000000+(int)(warheadCount&0xFFFFFF),e,plan.VisualEnergyScore),plan)) return true;
                // No room at all for a full effect: still ours, as a large burst.
                pool.Hit(point,up,Mathf.Max(burst,2),explosive,ground,water); combatHits++;
                outcome+=" (no room: light burst)";
                return true;
            }
        }
        // The first combat explosions of a flight are written to the log with what became of them.
        int combatNotes;
        void Note(CombatBridge.Detonation d,string outcome)
        {
            if(combatNotes++>=12) return;
            Debug.Log("[VEFX] Combat explosion: "+d.Info.Kind+" "+d.Info.TntKg.ToString("0.###")+" kg TNT, range "+d.Info.RangeM.ToString("0.#")+" m, weapon '"+(d.Weapon??"?")+"' -> "+outcome);
        }
        bool TestBlast(int kind)
        {
            if(!CanTest) return false;
            if(kind!=4) return Blast(kind,null);
            try
            {
                Camera cam=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:null;
                CelestialBody body=FlightGlobals.currentMainBody;
                if(cam==null||body==null) return false;
                Vector3 centre; RaycastHit hit;
                if(Physics.Raycast(cam.transform.position,cam.transform.forward,out hit,1500,SnapshotReader.SurfaceMask(),QueryTriggerInteraction.Ignore)&&
                    hit.collider!=null&&hit.collider.GetComponentInParent<Part>()==null) centre=hit.point;
                else centre=cam.transform.position+cam.transform.forward*160;
                Vector3 up=(Vector3)(((Vector3d)centre-body.position).normalized);
                Vector3 along=Vector3.ProjectOnPlane(cam.transform.right,up).normalized;
                Vector3 ahead=Vector3.Cross(along,up).normalized;
                int[] kinds={1,0,0,2,2,1,0,0};
                float[] delays={0,0.25f,0.45f,0.7f,0.9f,1.2f,1.5f,1.8f};
                // On a ring of 200 m: neighbours are further apart than a young blast covers (a closer
                // blast would be absorbed into it, and the test would measure a single explosion), and
                // some blasts are near the camera while others are far.
                pendingBlasts.Clear();
                for(int i=0;i<kinds.Length;i++)
                {
                    float angle=i*Mathf.PI*2/kinds.Length;
                    pendingBlasts.Add(new PendingBlast { Kind=kinds[i], Point=centre+(along*Mathf.Cos(angle)+ahead*Mathf.Sin(angle))*200f, At=Time.time+delays[i] });
                }
                probe.Note("heavy crash test");
                Debug.Log("[VEFX] Test: heavy crash, "+kinds.Length+" blasts over 1.8 s with wreckage and residue");
                return true;
            }
            catch(Exception ex) { Fail(ex); return false; }
        }
        bool Blast(int kind,Vector3? at)
        {
            try
            {
                Camera cam=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:null;
                CelestialBody body=FlightGlobals.currentMainBody;
                if(cam==null||body==null||pool==null) return false;
                bool flight=kind==3, ground=false;
                Vector3 point;
                RaycastHit hit;
                if(at.HasValue)
                {
                    Vector3 vertical=(Vector3)(((Vector3d)at.Value-body.position).normalized), below;
                    if(!flight&&SnapshotReader.GroundBelow(at.Value+vertical*40,vertical,120,out below)) { point=below; ground=true; }
                    else point=at.Value;
                }
                else if(!flight&&Physics.Raycast(cam.transform.position,cam.transform.forward,out hit,1500,SnapshotReader.SurfaceMask(),QueryTriggerInteraction.Ignore)&&
                    hit.collider!=null&&hit.collider.GetComponentInParent<Part>()==null) { point=hit.point; ground=true; }
                else point=cam.transform.position+cam.transform.forward*(flight?140:90);
                Vector3d radial=(Vector3d)point-body.position;
                Vector3 up=(Vector3)radial.normalized;
                double altitude=radial.magnitude-body.Radius;
                double pressure=body.atmosphere?body.GetPressure(altitude):0, density=0;
                if(pressure>0) density=body.GetDensity(pressure,body.GetTemperature(altitude));
                double gravity=body.gravParameter/Math.Max(1,radial.sqrMagnitude);
                Vector3d air=body.inverseRotation?Vector3d.zero:body.getRFrmVel(point);
                Vector3 across=Vector3.ProjectOnPlane(cam.transform.forward,up).normalized;
                Vector3 surface;
                if(flight)
                {
                    Vessel v=FlightGlobals.ActiveVessel;
                    Vector3 sv=v!=null?(Vector3)v.srf_velocity:Vector3.zero;
                    surface=sv.magnitude>60?sv:cam.transform.forward*250;
                }
                else surface=-up*(kind==1?70f:kind==2?14f:40f)+across*(kind==2?2f:9f);
                ResourceSnapshot[] resources; double kg; Vec3 size; string[] models;
                switch(kind)
                {
                    case 1: resources=new[]{new ResourceSnapshot("LiquidFuel",2880,2880,14400),new ResourceSnapshot("Oxidizer",3520,3520,17600)};
                        kg=36000; size=new Vec3(3.75,14,3.75); models=new[]{"Rockomax64.BW","Rockomax64_BW","fuelTank4-2","fuelTank"}; break;
                    case 2: resources=new[]{new ResourceSnapshot("SolidFuel",2600,2600,19500)};
                        kg=22000; size=new Vec3(1.9,10,1.9); models=new[]{"MassiveBooster","solidBooster1-1","solidBooster"}; break;
                    default: resources=new[]{new ResourceSnapshot("LiquidFuel",180,180,900),new ResourceSnapshot("Oxidizer",220,220,1100)};
                        kg=2250; size=new Vec3(1.25,3.8,1.25); models=new[]{"fuelTank","fuelTankSmall"}; break;
                }
                testCount++;
                var env=new EnvironmentContext(body.bodyName,density,pressure,gravity,altitude,body.ocean,ground,false,SnapshotReader.Value(up));
                var snapshot=new PartSnapshot(0x7E570000u+testCount,-(int)testCount,Guid.Empty,"test blast",SnapshotReader.Value(point),
                    SnapshotReader.Value(surface),SnapshotReader.Value((Vector3d)surface+air),new Vec3(),size,kg,300,new string[0],resources,env);
                var e=new DestructionEvent(snapshot,ground?EventKind.GroundImpact:EventKind.Destruction,Time.time);
                FxPlan plan=VisualEnergy.Calculate(e,classifier,settings);
                foreach(string name in models)
                {
                    AvailablePart info=PartLoader.getPartInfoByName(name);
                    if(info==null||info.partPrefab==null) continue;
                    Vector3 tilt=flight?across:Quaternion.AngleAxis(25,Vector3.Cross(up,across))*up;
                    CapturedPart model=PartCapture.ReadPrefab(info.partPrefab,point,flight?across:tilt,settings.DebrisTriangles);
                    if(model!=null) { pool.CaptureTestDebris(model,e,plan,body); break; }
                }
                clusters.Add(e,plan.VisualEnergyScore);
                return true;
            }
            catch(Exception ex) { Fail(ex); return false; }
        }
        // KSC buildings (and other destructible structures) coming down: VEFX draws the collapse (a burst of dust and rubble at
        // the base, then two more along the footprint as the walls fall) and hides the stock demolition effects meanwhile.
        // Charge-carrying parts destroyed this frame, started in Update (see Die).
        struct PendingCharge { public EventCluster Cluster; public FxPlan Plan; public Vector3 Where, Up; public float Burst; public EventKind Kind; }
        bool PendingNuke(Vector3 where,double within)
        {
            for(int i=0;i<pendingCharges.Count;i++) if(pendingCharges[i].Plan.Warhead==WarheadKind.Nuke&&Vector3.Distance(pendingCharges[i].Where,where)<=within) return true;
            return false;
        }
        readonly List<PendingCharge> pendingCharges=new List<PendingCharge>();
        struct PendingCollapse { public Vector3 Point; public double At; public float Width, Height, FuelKg; public bool Follow; }
        struct CollapseZone { public Vector3 Point; public float Reach; public double Until; public int Born; }
        readonly List<PendingCollapse> pendingCollapses=new List<PendingCollapse>();
        readonly List<CollapseZone> collapseZones=new List<CollapseZone>();
        readonly HashSet<int> collapseHidden=new HashSet<int>();
        readonly List<double> recentCollapses=new List<double>();
        int collapses;
        float stackLogAt=-1;
        double lastCollapseAt=-1;
        void OnCollapse(DestructibleBuilding b)
        {
            try
            {
                if(pool==null||b==null||!settings.Enabled||!settings.Explosions) return;
                CelestialBody body=FlightGlobals.currentMainBody;
                Vector3 foot; float width, height;
                if(!Footprint(b,body,out foot,out width,out height)) return;
                Vector3 up=body!=null?((Vector3)((Vector3d)foot-body.position)).normalized:Vector3.up;
                Vector3 ground;
                if(SnapshotReader.GroundBelow(foot+up*3,up,8,out ground)) foot=ground;
                string name=((b.id??"")+" "+b.name).ToLowerInvariant();
                float fuel=name.Contains("fuel")||name.Contains("tank")?Mathf.Clamp(width*width*height*0.4f,2000,60000):0;
                // When the whole centre comes down at once, each building gets one burst (no follow-ups) so the effect budget
                // goes round; the tallest clouds are kept for the large buildings.
                recentCollapses.RemoveAll(t=>Time.time-t>2); recentCollapses.Add(Time.time);
                bool crowd=recentCollapses.Count>4;
                if(crowd) pendingCollapses.RemoveAll(c=>c.Follow);   // the follow-ups planned before the crowd are dropped too
                // Buildings coming down together start one after the other (~1 per frame at 60 fps): setting up a cloud
                // costs a few milliseconds, and nine in one frame made a 38 ms hitch.
                double at=Math.Max(Time.time,lastCollapseAt+0.07); lastCollapseAt=at;
                pendingCollapses.Add(new PendingCollapse { Point=foot, At=at, Width=Mathf.Min(width,crowd?90:120), Height=height, FuelKg=fuel });
                if(!crowd&&width>12)
                {
                    Vector3 side=Vector3.Cross(up,Vector3.forward); if(side.sqrMagnitude<0.01f) side=Vector3.Cross(up,Vector3.right); side.Normalize();
                    pendingCollapses.Add(new PendingCollapse { Point=foot+side*width*0.3f, At=at+0.5, Width=width*0.55f, Height=height*0.6f, FuelKg=fuel*0.3f, Follow=true });
                    pendingCollapses.Add(new PendingCollapse { Point=foot-side*width*0.32f, At=at+1.1, Width=width*0.5f, Height=height*0.5f, FuelKg=fuel*0.3f, Follow=true });
                }
                collapseZones.Add(new CollapseZone { Point=foot, Reach=width*1.2f+40, Until=Time.time+8, Born=Time.frameCount });
                collapses++;
                Debug.Log("[VEFX] Building collapse: "+(b.id??b.name)+", "+width.ToString("0")+" m wide, "+height.ToString("0")+" m high"+(fuel>0?", fuel store":"")+(crowd?" (one burst: many at once)":""));
            }
            catch(Exception ex) { Fail(ex); }
        }
        // Size of what falls, measured on the meshes of the building's collapsing parts along the local vertical (world-axis
        // boxes made long, low buildings look hundreds of metres tall): footprint centre at ground level, width and height.
        static bool Footprint(DestructibleBuilding b,CelestialBody body,out Vector3 foot,out float width,out float height)
        {
            foot=Vector3.zero; width=height=0;
            var roots=new List<GameObject>();
            if(b.CollapsibleObjects!=null) foreach(var c in b.CollapsibleObjects) if(c!=null&&c.collapseObject!=null) roots.Add(c.collapseObject);
            if(roots.Count==0) roots.Add(b.gameObject);
            Vector3 up=body!=null?((Vector3)((Vector3d)b.transform.position-body.position)).normalized:Vector3.up;
            Vector3 e1=Vector3.Cross(up,Mathf.Abs(up.y)<0.9f?Vector3.up:Vector3.right).normalized, e2=Vector3.Cross(up,e1);
            float hMin=float.MaxValue, hMax=float.MinValue, xMin=float.MaxValue, xMax=float.MinValue, yMin=float.MaxValue, yMax=float.MinValue;
            bool any=false;
            foreach(GameObject root in roots)
                foreach(MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    if(mf==null||mf.sharedMesh==null) continue;
                    var mr=mf.GetComponent<MeshRenderer>(); if(mr==null||!mr.enabled) continue;
                    Bounds lb=mf.sharedMesh.bounds;
                    for(int k=0;k<8;k++)
                    {
                        Vector3 corner=lb.center+Vector3.Scale(lb.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1));
                        Vector3 w=mf.transform.TransformPoint(corner);
                        float h=Vector3.Dot(w,up), x=Vector3.Dot(w,e1), y=Vector3.Dot(w,e2);
                        hMin=Mathf.Min(hMin,h); hMax=Mathf.Max(hMax,h); xMin=Mathf.Min(xMin,x); xMax=Mathf.Max(xMax,x); yMin=Mathf.Min(yMin,y); yMax=Mathf.Max(yMax,y);
                        any=true;
                    }
                }
            if(!any) return false;
            height=Mathf.Clamp(hMax-hMin,2,150); width=Mathf.Clamp(Mathf.Max(xMax-xMin,yMax-yMin),3,200);
            foot=e1*((xMin+xMax)*0.5f)+e2*((yMin+yMax)*0.5f)+up*hMin;
            return true;
        }
        void Collapse(PendingCollapse p)
        {
            CelestialBody body=FlightGlobals.currentMainBody;
            if(pool==null||body==null||!pool.InRange(p.Point)) return;
            Vector3d radial=(Vector3d)p.Point-body.position;
            Vector3 up=(Vector3)radial.normalized;
            double altitude=radial.magnitude-body.Radius;
            double pressure=body.atmosphere?body.GetPressure(Math.Max(altitude,0)):0, density=0;
            if(pressure>0) density=body.GetDensity(pressure,body.GetTemperature(Math.Max(altitude,0)));
            double gravity=body.gravParameter/Math.Max(1,radial.sqrMagnitude);
            var env=new EnvironmentContext(body.bodyName,density,pressure,gravity,altitude,body.ocean,true,false,SnapshotReader.Value(up));
            warheadCount++;
            uint id=0x0BDB0000u+warheadCount;
            double size=Math.Max(p.Width,p.Height)*0.5;
            var snapshot=new PartSnapshot(id,-(int)(4000000+warheadCount),Guid.Empty,"building",SnapshotReader.Value(p.Point),
                SnapshotReader.Value(-up*3),SnapshotReader.Value(-up*3),new Vec3(),new Vec3(size,size,size),p.Width*p.Width*p.Height*20,300,new string[0],new ResourceSnapshot[0],env);
            var e=new DestructionEvent(snapshot,EventKind.GroundImpact,Time.time);
            FxPlan plan=VisualEnergy.Calculate(e,classifier,settings); Warhead.Collapse(plan,p.Width,p.Height,p.FuelKg);
            if(!pool.Spawn(EventCluster.Single(0x40000000+(int)(warheadCount&0xFFFFFF),e,plan.VisualEnergyScore),plan))
                pool.Hit(p.Point,up,Mathf.Clamp(p.Width*0.15f,2,6),p.FuelKg>0,true,false);
        }
        // The stock demolition and debris effects of a collapse are hidden while ours shows (renderers and lights only),
        // around every building that came down in the last seconds.
        // Scanning the scene for effects costs milliseconds: the sequencers near a ruin are looked for six times a second,
        // and those found are kept hidden every other frame (a sequencer adds its effects in steps).
        readonly List<VFXSequencer> collapseSequencers=new List<VFXSequencer>();
        readonly List<Renderer> collapseRenderers=new List<Renderer>();
        readonly List<Light> collapseLights=new List<Light>();
        int nextSequencerScan; float nextOtherScan;
        double collapseFxMs, collapseFxWorstMs;
        void HideCollapseFx()
        {
            for(int i=collapseZones.Count-1;i>=0;i--) if(Time.time>collapseZones[i].Until) collapseZones.RemoveAt(i);
            if(collapseZones.Count==0||!ReplaceStock) { collapseSequencers.Clear(); return; }
            long t0=System.Diagnostics.Stopwatch.GetTimestamp();
            // Scanning the whole scene costs ~10 ms: only a few times after each collapse (the demolition effects are created
            // at once and in a couple of later steps), then the ones found are kept hidden.
            bool scan=false, survey=false;
            for(int i=0;i<collapseZones.Count;i++) { int a=Time.frameCount-collapseZones[i].Born; scan|=a==1||a==12||a==45||a==120; survey|=a==60; }
            if(scan&&Time.frameCount!=nextSequencerScan)
            {
                nextSequencerScan=Time.frameCount;
                foreach(VFXSequencer v in UnityEngine.Object.FindObjectsOfType<VFXSequencer>())
                {
                    if(v==null||!NearCollapse(v.transform.position)||collapseSequencers.Contains(v)) continue;
                    collapseSequencers.Add(v);
                    if(collapseHidden.Add(v.GetInstanceID())) collapseNames.Add(v.name);
                }
            }
            if((Time.frameCount&1)==0)
                for(int i=collapseSequencers.Count-1;i>=0;i--)
                {
                    VFXSequencer v=collapseSequencers[i];
                    if(v==null) { collapseSequencers.RemoveAt(i); continue; }
                    v.GetComponentsInChildren(true,collapseRenderers);
                    for(int k=0;k<collapseRenderers.Count;k++) if(collapseRenderers[k].enabled) collapseRenderers[k].enabled=false;
                    v.GetComponentsInChildren(true,collapseLights);
                    for(int k=0;k<collapseLights.Count;k++) if(collapseLights[k].enabled) collapseLights[k].enabled=false;
                }
            // Other particle effects near a fallen building, outside any vessel and outside VEFX: listed in the log (not
            // touched) so a stock effect still showing can be identified. Twice a second at most.
            if(survey&&Time.time>=nextOtherScan&&otherNames.Count<12)
            {
                nextOtherScan=Time.time+1;
                foreach(ParticleSystem ps in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
                {
                    if(ps==null||ps.gameObject.layer==EffectLayer.Value||!ps.isPlaying||!NearCollapse(ps.transform.position)) continue;
                    var shown=ps.GetComponent<ParticleSystemRenderer>(); if(shown==null||!shown.enabled) continue;   // hidden already
                    if(ps.GetComponentInParent<Part>()!=null||ps.GetComponentInParent<VFXSequencer>()!=null) continue;
                    if(otherNames.Count<12) otherNames.Add(ps.transform.root.name+"/"+ps.name);
                }
            }
            double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-t0)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            collapseFxMs+=ms; collapseFxWorstMs=Math.Max(collapseFxWorstMs,ms);
        }
        readonly HashSet<string> collapseNames=new HashSet<string>(), otherNames=new HashSet<string>();
        bool NearCollapse(Vector3 at)
        {
            for(int i=0;i<collapseZones.Count;i++) if(Vector3.Distance(at,collapseZones[i].Point)<=collapseZones[i].Reach) return true;
            return false;
        }
        void OnOriginShift(Vector3d shift,Vector3d nonFrame)
        {
            Vec3 offset=SnapshotReader.Value(shift);
            for(int i=0;i<pendingBlasts.Count;i++) { PendingBlast b=pendingBlasts[i]; b.Point-=(Vector3)shift; pendingBlasts[i]=b; }
            for(int i=0;i<pendingTrails.Count;i++) { PendingTrail t=pendingTrails[i]; t.Point-=(Vector3)shift; pendingTrails[i]=t; }
            for(int i=0;i<pendingCollapses.Count;i++) { PendingCollapse c=pendingCollapses[i]; c.Point-=(Vector3)shift; pendingCollapses[i]=c; }
            for(int i=0;i<collapseZones.Count;i++) { CollapseZone z=collapseZones[i]; z.Point-=(Vector3)shift; collapseZones[i]=z; }
            for(int i=0;i<pendingCharges.Count;i++) { PendingCharge c=pendingCharges[i]; c.Where-=(Vector3)shift; c.Cluster.Shift(offset); pendingCharges[i]=c; }
            if(combat!=null) combat.Shift((Vector3)shift);
            if(clusters!=null) clusters.Shift(offset);
            cache.Shift(offset);
            if(pool!=null) pool.Shift((Vector3)shift);
            if(stock!=null) stock.Shift(offset);
        }
        bool ReplaceStock { get { return settings!=null&&settings.Enabled&&settings.ReplaceStockVisuals&&
            (settings.Explosions||settings.Burst||settings.Ground||settings.Water); } }
        void OnGUI()
        {
            // A nuclear flash whites out the view for a moment (the eye and the camera are blinded), fading into a glare.
            if(pool!=null&&pool.ScreenFlash>0.004f&&Event.current.type==EventType.Repaint)
            {
                float w=pool.ScreenWarmth;
                Color was=GUI.color; GUI.color=new Color(1,Mathf.Lerp(0.98f,0.66f,w),Mathf.Lerp(0.94f,0.36f,w),pool.ScreenFlash);
                GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);
                GUI.color=was;
            }
            if(!overlay || pool==null) return;
            var text=GameText.Current;
            GUILayout.BeginArea(new Rect(20,80,680,440),GUI.skin.box);
            GUILayout.Label("Volumetric Explosion FX (VEFX) "+Version);
            GUILayout.Label(text.Get("DiagnosticsHelp","Alt+F8 closes this panel. Detailed logging: debug=true in Settings.cfg."));
            GUILayout.Label(text.Format("Counters","Confirmed: {0} | active FX: {1} | debris: {2} | lights: {3} | culled: {4} | queue drops: {5} | errors: {6}",
                confirmed,pool.Active,pool.Debris,pool.Lights,pool.Dropped,clusters.Dropped,failures));
            GUILayout.Label(text.Format("ParticleCounters","Spawned: {0} | particles: {1} | camera/distance/budget drops: {2}/{3}/{4}",
                pool.Spawned,pool.Particles,pool.NoCamera,pool.OutOfRange,pool.BudgetDrops));
            GUILayout.Label(text.Format("Shaders","Shaders: {0}",pool.ShaderSummary));
            GUILayout.Label(text.Format("VolumeCounters","Fireball volumes: {0} / {1} | fire volumes: {2}",pool.Volumes,settings.MaxVolumes,pool.FireVolumes));
            GUILayout.Label(text.Format("WreckageCount","Part wreckage: {0}",pool.Wreckage));
            GUILayout.Label(text.Format("StockCounters","Absorbed: {0} | stock visuals hidden/restored: {1}",absorbed,
                stock!=null?stock.Suppressed+"/"+stock.Restored:"0/0"));
            GUILayout.Label(lastEvent=="Awaiting confirmed destruction"?text.Get("AwaitingEvent","Awaiting confirmed destruction"):
                text.Format("LastEvent","Last event: {0}",lastEvent));
            settings.Explosions=GUILayout.Toggle(settings.Explosions,text.Get("Explosions","Explosions"));
            settings.Debris=GUILayout.Toggle(settings.Debris,text.Get("MicroDebris","Micro debris"));
            settings.PartDebris=GUILayout.Toggle(settings.PartDebris,text.Get("PartFragments","Part fragments"));
            settings.Residue=GUILayout.Toggle(settings.Residue,text.Get("Residue","Ground fire and smoke"));
            settings.ReplaceStockVisuals=GUILayout.Toggle(settings.ReplaceStockVisuals,text.Get("ReplaceStock","Replace KSP explosions"));
            settings.Shockwave=GUILayout.Toggle(settings.Shockwave,text.Get("Shockwave","Shockwave and distortion"));
            GUILayout.EndArea();
        }
        void Fail(Exception ex)
        {
            failures++;
            if(failures==1) Debug.LogError("[VEFX] "+ex);
            else if(failures<=3) Debug.LogError("[VEFX] "+ex.GetType().Name+": "+ex.Message);
        }
        void ReportSummary() { Debug.Log(Summary(true)); }
        string Summary(bool live)
        {
            return ("[VEFX] Flight counters: willDie="+willDieReports+" ground="+groundReports+
                " water="+waterReports+" confirmed="+confirmed+" missingSnapshots="+missingSnapshots+
                " spawned="+pool.Spawned+(live?" liveParticles="+pool.Particles+" volumes="+pool.Volumes:" (final)")+" noCamera="+pool.NoCamera+
                " outOfRange="+pool.OutOfRange+" budgetDrops="+pool.BudgetDrops+" queueDrops="+clusters.Dropped+
                " queueReplaced="+clusters.Replaced+" absorbed="+absorbed+"/"+pool.Absorbed+" groundAnchored="+anchored+
                " stockHidden="+(stock!=null?stock.Suppressed:0)+" stockRestored="+(stock!=null?stock.Restored:0)+
                " stockMissed="+(stock!=null?stock.Missed:0)+" stockGivenBack="+(stock!=null?stock.Unconfirmed:0)+" errors="+failures+
                " wreckage="+pool.Wreckage);
        }
        void OnDestroy()
        {
            if(active==this) active=null;
            if(subscribed)
            {
                GameEvents.onPartWillDie.Remove(OnWillDie); GameEvents.onPartDie.Remove(OnDie);
                GameEvents.onCrash.Remove(OnCrash); GameEvents.onCrashSplashdown.Remove(OnSplash);
                GameEvents.onFloatingOriginShift.Remove(OnOriginShift); GameEvents.OnKSCStructureCollapsing.Remove(OnCollapse); subscribed=false;
            }
            string summary=null;
            if(pool!=null) { try { summary=Summary(false); probe.Flush(settings.Quality.ToString()); } catch(Exception ex) { Fail(ex); } }
            if(stock!=null) { try { stock.Dispose(); } catch(Exception ex) { Fail(ex); } stock=null; }
            if(combat!=null) { try { if(combat.Available) Debug.Log("[VEFX] Combat explosions: "+combat.Detected+" detonations and "+combat.BulletHits+" bullet hits seen, "+combatDrawn+" drawn by VEFX ("+combatHits+" as light bursts), "+combatGivenBack+" left to BDArmory's visuals; "+warheadParts+" bombs/missiles destroyed as parts drawn as warheads; BDArmory generic effects: "+combat.Emitters);
                if(collapses>0) Debug.Log("[VEFX] Building collapses drawn: "+collapses+", stock demolition effects hidden: "+collapseHidden.Count+" ("+string.Join(", ",new List<string>(collapseNames).ToArray())+"), hiding cost "+collapseFxMs.ToString("0")+" ms in all (worst frame "+collapseFxWorstMs.ToString("0.0")+" ms); other particle effects near them: "+(otherNames.Count>0?string.Join(", ",new List<string>(otherNames).ToArray()):"none")); combat.Dispose(); } catch(Exception ex) { Fail(ex); } combat=null; }
            if(pool!=null)
            {
                if(summary!=null) Debug.Log(summary);
                try { pool.Dispose(); } catch(Exception ex) { Fail(ex); }
                pool=null;
            }
            if(log!=null) { log.Dispose(); log=null; }
        }
    }
}
