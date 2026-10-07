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
        internal const string Version="1.0.0";
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
                if(settings.Debug)
                {
                    string path=Path.Combine(KSPUtil.ApplicationRootPath,"GameData/VolumetricExplosionFX/PluginData");
                    Directory.CreateDirectory(path);
                    log=new StreamWriter(Path.Combine(path,"visual-events.log"),false); log.AutoFlush=true;
                }
                GameEvents.onPartWillDie.Add(OnWillDie); GameEvents.onPartDie.Add(OnDie);
                GameEvents.onCrash.Add(OnCrash); GameEvents.onCrashSplashdown.Add(OnSplash);
                GameEvents.onFloatingOriginShift.Add(OnOriginShift); subscribed=true;
                nextSummary=Time.unscaledTime+5;
                Debug.Log("[VEFX] Volumetric Explosion FX "+Version+" loaded in flight.");
                foreach(var a in AppDomain.CurrentDomain.GetAssemblies())
                    if(a.GetName().Name=="ProjectDestructionFX") Debug.LogWarning("[VEFX] An older copy of this mod (GameData/ProjectDestructionFX) is still installed: remove that folder, both together double every effect.");
                Debug.Log("[VEFX] Supported shader selection: "+pool.ShaderSummary);
                Debug.Log("[VEFX] Stock replacement: "+settings.ReplaceStockVisuals+" / "+stock.Status);
            }
            catch(Exception ex) { Fail(ex); OnDestroy(); }
        }
        void OnWillDie(Part part)
        {
            willDieReports++; Capture(part,EventKind.Destruction,null);
            if(pool!=null) { try { pool.PrepareDebris(part); } catch(Exception ex) { Fail(ex); } }
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
                FxInstance host=pool.Covering(where,plan.VisualEnergyScore);
                if(host!=null)
                {
                    host.AddSecondary(SnapshotReader.Vector(e.Origin),plan.VisualEnergyScore/Math.Max(host.Score,1e-3));
                    owner=VisualReplacementWindows.Covered; absorbed++;
                }
                else
                {
                    owner=clusters.Add(e,plan.VisualEnergyScore);
                    if(clusters.LastEvicted>0) stock.Release(clusters.LastEvicted);
                }
                if(owner!=0&&ReplaceStock&&pool.InRange(where))
                    stock.Claim(s.Position,now,Numbers.Clamp(s.Dimensions.Length*3,10,45),owner);
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
            if(!settings.Enabled) { stock.SetEnabled(false); clusters.Clear(); pool.Clear(); return; }
            if(settings.Structure!=structure)
            {
                if(restructureAt<0) restructureAt=Time.unscaledTime+0.6f;
                else if(Time.unscaledTime>=restructureAt) { Rebuild(); restructureAt=-1; }
            }
            else restructureAt=-1;
            stock.SetEnabled(ReplaceStock&&!MapView.MapIsEnabled);
            if(MapView.MapIsEnabled || TimeWarp.CurrentRate>4 || (FlightGlobals.ActiveVessel!=null&&FlightGlobals.ActiveVessel.packed))
            { clusters.Clear(); pool.Clear(); return; }
            try
            {
                probe.Begin();
                for(int i=pendingBlasts.Count-1;i>=0;i--)
                    if(Time.time>=pendingBlasts[i].At) { PendingBlast b=pendingBlasts[i]; pendingBlasts.RemoveAt(i); Blast(b.Kind,b.Point); }
                EventCluster c;
                while((c=clusters.TakeReady(Time.time))!=null)
                {
                    FxPlan plan=VisualEnergy.Calculate(c.Representative,classifier,settings);
                    if(!pool.Spawn(c,plan)) stock.Release(c.Id);
                }
                pool.Tick(Time.deltaTime);
                probe.End();
                probe.Frame(pool.Load,settings.Quality.ToString());
                if(Time.unscaledTime>=nextSummary)
                {
                    nextSummary=Time.unscaledTime+5;
                    if(confirmed>0||testCount>0) ReportSummary();
                }
            }
            catch(Exception ex) { Fail(ex); clusters.Clear(); pool.Clear(); }
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
                int[] kinds={1,0,0,2,2,1,0,0};
                float[] offsets={0,12,-10,25,-24,40,-38,55}, delays={0,0.25f,0.45f,0.7f,0.9f,1.2f,1.5f,1.8f};
                pendingBlasts.Clear();
                for(int i=0;i<kinds.Length;i++)
                    pendingBlasts.Add(new PendingBlast { Kind=kinds[i], Point=centre+along*offsets[i], At=Time.time+delays[i] });
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
        void OnOriginShift(Vector3d shift,Vector3d nonFrame)
        {
            Vec3 offset=SnapshotReader.Value(shift);
            for(int i=0;i<pendingBlasts.Count;i++) { PendingBlast b=pendingBlasts[i]; b.Point-=(Vector3)shift; pendingBlasts[i]=b; }
            if(clusters!=null) clusters.Shift(offset);
            cache.Shift(offset);
            if(pool!=null) pool.Shift((Vector3)shift);
            if(stock!=null) stock.Shift(offset);
        }
        bool ReplaceStock { get { return settings!=null&&settings.Enabled&&settings.ReplaceStockVisuals&&
            (settings.Explosions||settings.Burst||settings.Ground||settings.Water); } }
        void OnGUI()
        {
            if(!overlay || pool==null) return;
            GUILayout.BeginArea(new Rect(20,80,580,400),GUI.skin.box);
            GUILayout.Label("Volumetric Explosion FX (VEFX) "+Version);
            GUILayout.Label("Alt+F8 closes this panel. Detailed log requires debug=true in Settings.cfg.");
            GUILayout.Label("Confirmed: "+confirmed+" | active FX: "+pool.Active+" | debris: "+pool.Debris+
                " | lights: "+pool.Lights+" | culled/budget drops: "+pool.Dropped+" | queue drops: "+clusters.Dropped+" | errors: "+failures);
            GUILayout.Label("Spawned slots: "+pool.Spawned+" | live particles: "+pool.Particles+
                " | camera/distance/budget drops: "+pool.NoCamera+"/"+pool.OutOfRange+"/"+pool.BudgetDrops);
            GUILayout.Label(pool.ShaderSummary);
            GUILayout.Label("Fireball volumes: "+pool.Volumes+" / "+settings.MaxVolumes+" | fire volumes: "+pool.FireVolumes);
            GUILayout.Label("Part wreckage: "+pool.Wreckage);
            GUILayout.Label("Absorbed into active domes: "+absorbed+" | stock renderers hidden/restored: "+
                (stock!=null?stock.Suppressed+"/"+stock.Restored:"0/0"));
            GUILayout.Label(lastEvent);
            settings.Explosions=GUILayout.Toggle(settings.Explosions,"Enhanced explosions");
            settings.Debris=GUILayout.Toggle(settings.Debris,"Micro debris");
            settings.PartDebris=GUILayout.Toggle(settings.PartDebris,"Wreckage cut from the destroyed parts");
            settings.Residue=GUILayout.Toggle(settings.Residue,"Residue: pool fires, smoke columns, scorch");
            settings.ReplaceStockVisuals=GUILayout.Toggle(settings.ReplaceStockVisuals,"Replace matched stock destruction visuals");
            settings.Shockwave=GUILayout.Toggle(settings.Shockwave,"Shock front and heat shimmer (refraction)");
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
                GameEvents.onFloatingOriginShift.Remove(OnOriginShift); subscribed=false;
            }
            string summary=null;
            if(pool!=null) { try { summary=Summary(false); probe.Flush(settings.Quality.ToString()); } catch(Exception ex) { Fail(ex); } }
            if(stock!=null) { try { stock.Dispose(); } catch(Exception ex) { Fail(ex); } stock=null; }
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
