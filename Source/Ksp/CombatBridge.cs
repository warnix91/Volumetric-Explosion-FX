using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Ksp
{
    // Reads the explosions of BDArmory (and the weapon packs built on it) through its public members, by reflection: no
    // compile-time dependency, nothing written to it. Its explosion objects are pooled; a new explosion is an active pooled
    // object whose start time changed. Optionally hides their renderers and light (visual only) while ours is shown, and
    // gives them back as soon as the object returns to the pool. Damage, sound and physics stay entirely with BDArmory.
    internal sealed class CombatBridge : IDisposable
    {
        internal struct Detonation { public Vector3 Position, Velocity; public WarheadInfo Info; public string Weapon; public int Handle; public bool Hit; public float Caliber; }
        sealed class Hidden { public GameObject Owner; public int Handle; public List<Renderer> Renderers=new List<Renderer>(); public Light Light; }
        // One kind of pooled explosion object: ordinary explosions (ExplosionFx) and nuclear ones (NukeFX, whose power is a
        // yield in kilotons and whose reach is the thermal radius).
        sealed class Source
        {
            public Type Type; public FieldInfo Pools, Warhead;
            public MethodInfo Start, Position, Power, Range, Origin, Weapon, Velocity, Light;
            public bool Nuclear;
            public readonly Dictionary<int,Component> Components=new Dictionary<int,Component>();
        }
        readonly FieldInfo poolList;
        readonly List<Source> sources=new List<Source>();
        readonly Dictionary<int,float> seen=new Dictionary<int,float>();
        readonly List<Hidden> hidden=new List<Hidden>();
        readonly List<Renderer> scratch=new List<Renderer>();
        readonly Queue<Detonation> found=new Queue<Detonation>();
        int lastFrame=-1, errors, nextHandle;
        bool subscribed;
        internal bool Available { get; private set; }
        internal bool Hide;
        internal int Detected { get; private set; }
        internal int Replaced { get; private set; }
        internal string Status { get; private set; }
        internal CombatBridge()
        {
            Status="BDArmory not installed";
            try
            {
                Assembly bda=null;
                foreach(AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies) if(a.name=="BDArmory") { bda=a.assembly; break; }
                if(bda==null) return;
                Type poolType=bda.GetType("BDArmory.Utils.ObjectPool");
                poolList=poolType!=null?poolType.GetField("pool",BindingFlags.Instance|BindingFlags.Public):null;
                Source blast=Read(bda,"BDArmory.FX.ExplosionFx","explosionFXPools","Power","Range","SourceWeaponName",false);
                Source nuke=Read(bda,"BDArmory.FX.NukeFX","nukePool","yield","thermalRadius","ReportingName",true);
                if(blast!=null) sources.Add(blast);
                if(nuke!=null) sources.Add(nuke);
                // Bullet impacts (sparks of a slug hitting a part or the ground), pooled in static ObjectPool fields.
                hitType=bda.GetType("BDArmory.FX.BulletHitFX");
                if(hitType!=null)
                {
                    foreach(string f in new[]{"bulletHitFXPool","penetrationFXPool"}) { FieldInfo pf=hitType.GetField(f,BindingFlags.Static|BindingFlags.Public); if(pf!=null) hitPools.Add(pf); }
                    caliberField=hitType.GetField("caliber",BindingFlags.Instance|BindingFlags.Public);
                }
                // Generic effects (FXEmitter): only counted for now, by model, to learn what they are.
                // Charges carried by parts (a bomb or missile destroyed without detonating still shows its warhead).
                explosiveType=bda.GetType("BDArmory.Weapons.BDExplosivePart"); nukeModuleType=bda.GetType("BDArmory.Weapons.BDModuleNuke");
                const BindingFlags field=BindingFlags.Instance|BindingFlags.Public;
                if(explosiveType!=null) { tntField=explosiveType.GetField("tntMass",field); radiusField=explosiveType.GetField("blastRadius",field); typeField=explosiveType.GetField("warheadType",field); nameField=explosiveType.GetField("warheadReportingName",field); }
                if(nukeModuleType!=null) { yieldField=nukeModuleType.GetField("yield",field); thermalField=nukeModuleType.GetField("thermalRadius",field); }
                Type emitter=bda.GetType("BDArmory.FX.FXEmitter");
                if(emitter!=null) { emitterType=emitter; emitterPools=emitter.GetField("FXPools",BindingFlags.Static|BindingFlags.Public); emitterStart=Getter(emitter,"StartTime"); }
                if(poolList==null||sources.Count==0) { Status="BDArmory found, explosion API not recognised"; return; }
                Available=true; Status="BDArmory "+(blast!=null?"explosions":"")+(blast!=null&&nuke!=null?" and ":"")+(nuke!=null?"nuclear blasts":"");
                Camera.onPreCull+=PreCull; subscribed=true;
            }
            catch(Exception ex) { Available=false; Status="BDArmory bridge off: "+ex.GetType().Name; }
        }
        static Source Read(Assembly bda,string type,string pools,string power,string range,string weapon,bool nuclear)
        {
            var s=new Source { Type=bda.GetType(type), Nuclear=nuclear };
            if(s.Type==null) return null;
            const BindingFlags inst=BindingFlags.Instance|BindingFlags.Public;
            s.Pools=s.Type.GetField(pools,BindingFlags.Static|BindingFlags.Public);
            s.Warhead=s.Type.GetField("warheadType",inst);
            s.Start=Getter(s.Type,"StartTime"); s.Position=Getter(s.Type,"Position"); s.Power=Getter(s.Type,power); s.Range=Getter(s.Type,range);
            s.Origin=Getter(s.Type,"ExplosionSource"); s.Weapon=Getter(s.Type,weapon); s.Velocity=Getter(s.Type,"Velocity"); s.Light=Getter(s.Type,"LightFx");
            if(s.Pools==null||s.Start==null||s.Position==null||s.Power==null||s.Range==null) return null;
            return s;
        }
        static MethodInfo Getter(Type t,string name)
        {
            PropertyInfo p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public);
            return p!=null?p.GetGetMethod():null;
        }
        // Whether VEFX draws now (effects on, flight view, no time warp) and hides the combat mod's visuals meanwhile.
        bool paused;
        internal void SetActive(bool drawing,bool hide)
        {
            paused=!drawing;
            bool wanted=drawing&&hide;
            if(Hide&&!wanted) RestoreAll();
            Hide=wanted;
            if(paused) found.Clear();
        }
        // Floating-origin shift of the detonations waiting to be drawn.
        internal void Shift(Vector3 offset)
        {
            for(int n=found.Count;n>0;n--) { Detonation d=found.Dequeue(); d.Position-=offset; found.Enqueue(d); }
        }
        internal bool Next(out Detonation d)
        {
            if(found.Count>0) { d=found.Dequeue(); return true; }
            d=default(Detonation); return false;
        }
        // Once per frame, before anything is drawn: new explosions are queued (and hidden) before they can show.
        void PreCull(Camera cam)
        {
            if(Time.frameCount==lastFrame) return;
            lastFrame=Time.frameCount;
            if(!Available) return;
            // Paused: explosions are only noted as seen (none drawn late when the view comes back).
            try { Restore(); for(int i=0;i<sources.Count;i++) Scan(sources[i]); ScanHits(); if(!paused) WatchEmitters(); errors=0; }
            catch(Exception ex)
            {
                if(++errors>=10) { Available=false; RestoreAll(); Status="BDArmory bridge stopped after errors: "+ex.GetType().Name; Debug.LogWarning("[VEFX] "+Status); }
            }
        }
        void Scan(Source s)
        {
            var dictionary=s.Pools.GetValue(null) as IDictionary;
            if(dictionary==null) return;
            foreach(DictionaryEntry entry in dictionary)
            {
                var list=entry.Value!=null?poolList.GetValue(entry.Value) as IList:null;
                if(list==null) continue;
                for(int i=0;i<list.Count;i++)
                {
                    var go=list[i] as GameObject;
                    if(go==null||!go.activeInHierarchy) continue;
                    int id=go.GetInstanceID();
                    Component fx;
                    if(!s.Components.TryGetValue(id,out fx)) { fx=go.GetComponent(s.Type); s.Components[id]=fx; }
                    if(fx==null) continue;
                    float t=Convert.ToSingle(s.Start.Invoke(fx,null)), last;
                    if(seen.TryGetValue(id,out last)&&last==t) continue;
                    seen[id]=t;
                    if(paused) continue;
                    Detected++;
                    string kind=s.Warhead!=null?Convert.ToString(s.Warhead.GetValue(fx)):"", from=s.Origin!=null?Convert.ToString(s.Origin.Invoke(fx,null)):"";
                    if(s.Nuclear) kind="Nuke";   // isEMP means the nuke also sends a pulse: it still blows up
                    string name=s.Weapon!=null?s.Weapon.Invoke(fx,null) as string:null;
                    double power=Convert.ToDouble(s.Power.Invoke(fx,null)), blast=Convert.ToDouble(s.Range.Invoke(fx,null));
                    double tnt=s.Nuclear?power*1e6:power;   // a nuclear yield is in kilotons of TNT
                    var info=new WarheadInfo { Kind=Warhead.Classify(kind,from,name,tnt), TntKg=tnt, RangeM=blast };
                    if(!Warhead.Replaces(info)) continue;
                    Replaced++;
                    int handle=Hide?HideVisuals(go,fx,s):0;
                    found.Enqueue(new Detonation { Position=(Vector3)s.Position.Invoke(fx,null), Velocity=s.Velocity!=null?(Vector3)s.Velocity.Invoke(fx,null):Vector3.zero, Info=info, Weapon=name, Handle=handle });
                }
            }
        }
        // Bullet impacts: a pooled object that turns active is a new hit.
        readonly List<FieldInfo> hitPools=new List<FieldInfo>();
        readonly Dictionary<int,int> hitSeen=new Dictionary<int,int>();
        readonly Dictionary<int,Component> hitComponents=new Dictionary<int,Component>();
        Type hitType; FieldInfo caliberField;
        int lastHitScan=-1;
        internal int BulletHits { get; private set; }
        void ScanHits()
        {
            int previous=lastHitScan; lastHitScan=Time.frameCount;
            for(int k=0;k<hitPools.Count;k++)
            {
                object pool=hitPools[k].GetValue(null);
                var list=pool!=null?poolList.GetValue(pool) as IList:null;
                if(list==null) continue;
                for(int i=0;i<list.Count;i++)
                {
                    var go=list[i] as GameObject;
                    if(go==null||!go.activeInHierarchy) continue;
                    int id=go.GetInstanceID(), last;
                    bool fresh=!hitSeen.TryGetValue(id,out last)||last!=previous;
                    hitSeen[id]=lastHitScan;
                    if(!fresh||paused) continue;
                    Component fx;
                    if(!hitComponents.TryGetValue(id,out fx)) { fx=go.GetComponent(hitType); hitComponents[id]=fx; }
                    float caliber=fx!=null&&caliberField!=null?Convert.ToSingle(caliberField.GetValue(fx)):20;
                    BulletHits++;
                    int handle=Hide?HideVisuals(go,null,null):0;
                    found.Enqueue(new Detonation { Position=go.transform.position, Hit=true, Caliber=caliber, Handle=handle,
                        Info=new WarheadInfo { Kind=WarheadKind.Kinetic, TntKg=0, RangeM=0 } });
                }
            }
        }
        // Generic effects (FXEmitter): counted by model so the log says what they are.
        Type emitterType; FieldInfo emitterPools; MethodInfo emitterStart;
        readonly Dictionary<int,float> emitterSeen=new Dictionary<int,float>();
        readonly Dictionary<int,Component> emitterComponents=new Dictionary<int,Component>();
        readonly Dictionary<string,int> emitterCounts=new Dictionary<string,int>();
        void WatchEmitters()
        {
            var dictionary=emitterPools!=null?emitterPools.GetValue(null) as IDictionary:null;
            if(dictionary==null||emitterStart==null) return;
            foreach(DictionaryEntry entry in dictionary)
            {
                var list=entry.Value!=null?poolList.GetValue(entry.Value) as IList:null;
                if(list==null) continue;
                for(int i=0;i<list.Count;i++)
                {
                    var go=list[i] as GameObject;
                    if(go==null||!go.activeInHierarchy) continue;
                    int id=go.GetInstanceID();
                    Component fx;
                    if(!emitterComponents.TryGetValue(id,out fx)) { fx=go.GetComponent(emitterType); emitterComponents[id]=fx; }
                    if(fx==null) continue;
                    float t=Convert.ToSingle(emitterStart.Invoke(fx,null)), last;
                    if(emitterSeen.TryGetValue(id,out last)&&last==t) continue;
                    emitterSeen[id]=t;
                    string key=Convert.ToString(entry.Key); int n; emitterCounts.TryGetValue(key,out n); emitterCounts[key]=n+1;
                    // Parts of an explosion (a nuke's flash, shock, blast and mushroom models): ours draws all of it.
                    if(Hide&&key.IndexOf("/explosion/",StringComparison.OrdinalIgnoreCase)>=0) HideVisuals(go,null,null);
                }
            }
        }
        internal string Emitters
        {
            get
            {
                if(emitterCounts.Count==0) return "none";
                var parts=new List<string>(); foreach(var kv in emitterCounts) parts.Add(kv.Key+" x"+kv.Value);
                return string.Join(", ",parts.ToArray());
            }
        }
        Type explosiveType, nukeModuleType; FieldInfo tntField, radiusField, typeField, nameField, yieldField, thermalField;
        // The warhead a part carries (BDExplosivePart or BDModuleNuke), read from the combat mod's part modules.
        internal bool WarheadOf(Part part,out WarheadInfo info,out string weapon)
        {
            info=default(WarheadInfo); weapon=null;
            if(!Available||part==null||part.Modules==null) return false;
            try
            {
                for(int i=0;i<part.Modules.Count;i++)
                {
                    PartModule m=part.Modules[i]; if(m==null) continue;
                    Type t=m.GetType();
                    weapon=part.partInfo!=null?part.partInfo.title:part.name;
                    if(nukeModuleType!=null&&nukeModuleType.IsAssignableFrom(t)&&yieldField!=null)
                    {
                        double kt=Convert.ToDouble(yieldField.GetValue(m)), reach=thermalField!=null?Convert.ToDouble(thermalField.GetValue(m)):900;
                        info=new WarheadInfo { Kind=WarheadKind.Nuke, TntKg=Math.Max(kt,0.001)*1e6, RangeM=reach };
                        return true;
                    }
                    if(explosiveType!=null&&explosiveType.IsAssignableFrom(t)&&tntField!=null)
                    {
                        double tnt=Convert.ToDouble(tntField.GetValue(m));
                        double reach=radiusField!=null?Convert.ToDouble(radiusField.GetValue(m)):0;
                        if(reach<=0) reach=15*Math.Pow(Math.Max(tnt,0.01),1.0/3);
                        string kind=typeField!=null?Convert.ToString(typeField.GetValue(m)):"";
                        string name=nameField!=null?nameField.GetValue(m) as string:null;
                        if(!string.IsNullOrEmpty(name)) weapon=name+" "+weapon;
                        info=new WarheadInfo { Kind=Warhead.Classify(kind,"Missile",weapon,tnt), TntKg=tnt, RangeM=reach };
                        return tnt>0.01;
                    }
                }
            }
            catch(Exception) { }
            return false;
        }
        int HideVisuals(GameObject go,Component fx,Source s)
        {
            var h=new Hidden { Owner=go, Handle=++nextHandle };
            go.GetComponentsInChildren(true,scratch);
            for(int i=0;i<scratch.Count;i++) if(scratch[i]!=null&&scratch[i].enabled) { scratch[i].enabled=false; h.Renderers.Add(scratch[i]); }
            Light l=s!=null&&s.Light!=null?s.Light.Invoke(fx,null) as Light:null;
            if(l!=null&&l.enabled) { l.enabled=false; h.Light=l; }
            hidden.Add(h);
            return h.Handle;
        }
        // VEFX did not draw this one: the combat mod shows its own explosion (same frame, nothing missing).
        internal void GiveBack(int handle)
        {
            if(handle==0) return;
            for(int i=0;i<hidden.Count;i++) if(hidden[i].Handle==handle) { Give(hidden[i]); hidden.RemoveAt(i); return; }
        }
        // Give the combat mod its visuals back when the explosion object goes back to its pool.
        void Restore()
        {
            for(int i=hidden.Count-1;i>=0;i--)
            {
                Hidden h=hidden[i];
                if(h.Owner!=null&&h.Owner.activeInHierarchy) continue;
                Give(h); hidden.RemoveAt(i);
            }
        }
        static void Give(Hidden h)
        {
            for(int k=0;k<h.Renderers.Count;k++) if(h.Renderers[k]!=null) h.Renderers[k].enabled=true;
            if(h.Light!=null) h.Light.enabled=true;
        }
        internal void RestoreAll() { for(int i=0;i<hidden.Count;i++) Give(hidden[i]); hidden.Clear(); }
        public void Dispose()
        {
            if(subscribed) { Camera.onPreCull-=PreCull; subscribed=false; }
            RestoreAll(); found.Clear();
        }
    }
}
