using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Ksp
{
    // Hide stock renderers and lights only; simulation and sound keep running.
    internal sealed class StockVisualFilter : IDisposable
    {
        struct Tracked { public GameObject Effect; public int Owner; public double Since; }
        const int Tentative=-2;
        const double TentativeGrace=0.35;
        readonly VisualReplacementWindows windows=new VisualReplacementWindows(96);
        readonly Tracked[] tracked=new Tracked[96];
        // Restore only state changed by this filter.
        readonly Suppression<Renderer>[] hiddenRenderers=new Suppression<Renderer>[96];
        readonly Suppression<Light>[] hiddenLights=new Suppression<Light>[96];
        readonly int[] rejected=new int[64], logged=new int[32];
        readonly string[] described=new string[4];
        readonly List<Renderer> renderers=new List<Renderer>(32);
        readonly List<Light> lights=new List<Light>(4);
        static readonly FieldInfo Fetch=typeof(FXMonger).GetField("fetch",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
        static readonly FieldInfo Objects=typeof(FXMonger).GetField("explosionObjects",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        readonly Func<Vector3,bool> insideOurEffect, inRange;
        int rejectedCursor, loggedCursor, lastFrame=-1, trackedCount, diagnostics;
        bool enabled, subscribed, failed;
        internal int Suppressed { get; private set; }
        internal int Restored { get; private set; }
        internal int Missed { get; private set; }
        internal string Status
        {
            get
            {
                if(!subscribed) return "stock filter API unavailable; stock visuals unchanged";
                return failed?"stock filter stopped after an error; stock visuals restored":"stock explosion renderers/lights where our effects are shown";
            }
        }
        // Only the active flight filter may hook camera callbacks.
        static StockVisualFilter current;
        int errors;
        internal StockVisualFilter(Func<Vector3,bool> covered,Func<Vector3,bool> withinRange)
        {
            insideOurEffect=covered; inRange=withinRange;
            for(int i=0;i<tracked.Length;i++) { hiddenRenderers[i]=new Suppression<Renderer>(8); hiddenLights[i]=new Suppression<Light>(2); }
            if(current!=null) { try { current.Dispose(); } catch { } }
            current=this;
            if(Fetch==null||Objects==null) return;
            Camera.onPreCull+=PreCull; subscribed=true;
        }
        internal void SetEnabled(bool on)
        {
            if(enabled&&!on) { RestoreAll(); windows.Clear(); }
            enabled=on;
        }
        internal void Claim(Vec3 position,double now,double radius,int owner)
        { if(subscribed&&enabled&&!failed) windows.Claim(position,now,radius,owner); }
        internal void Release(int owner)
        {
            if(owner==0) return;
            windows.Release(owner);
            for(int i=0;i<tracked.Length;i++) if(tracked[i].Effect!=null&&tracked[i].Owner==owner) Untrack(i,true);
        }
        internal void Shift(Vec3 offset) { windows.Shift(offset); }
        void PreCull(Camera camera)
        {
            if(Time.frameCount==lastFrame) return;
            lastFrame=Time.frameCount;
            if(!enabled||failed) return;
            try { Scan(Time.time); Hide(); errors=0; }
            catch(Exception ex)
            {
                if(++errors<10) { if(errors==1) Debug.LogWarning("[VEFX] Stock visual filter frame skipped: "+ex); return; }
                failed=true; RestoreAll();
                Debug.LogWarning("[VEFX] Stock visual filter stopped after repeated errors: "+ex.GetType().Name);
            }
        }
        void Scan(double now)
        {
            FXMonger manager=Fetch.GetValue(null) as FXMonger;
            if(manager==null) return;
            var live=Objects.GetValue(manager) as List<FXObject>;
            if(live==null||live.Count==0) return;
            bool claims=windows.Any(now);
            Confirm(claims,now);
            for(int i=Math.Max(0,live.Count-128);i<live.Count;i++)
            {
                FXObject fx=live[i]; GameObject effect=fx!=null?fx.effectObj:null;
                if(effect==null||!effect.activeInHierarchy||!Matches(effect,manager.explosions)) continue;
                int id=effect.GetInstanceID();
                if(IsTracked(effect)||Contains(rejected,id)) continue;
                Vector3 where=effect.transform.position;
                int owner=claims?windows.OwnerAt(SnapshotReader.Value(where),now):0;
                if(owner==0&&insideOurEffect!=null&&insideOurEffect(where)) owner=VisualReplacementWindows.Covered;
                if(owner==0)
                {
                    if(inRange==null||!inRange(where)) { Diagnose(effect,"unclaimed (out of range)",-1); continue; }
                    owner=Tentative;
                }
                if(effect.GetComponentInParent<Part>()!=null)
                { rejected[rejectedCursor]=id; rejectedCursor=(rejectedCursor+1)%rejected.Length; Diagnose(effect,"attached to a part",0); continue; }
                Describe(effect);
                Track(effect,owner,now);
                Diagnose(effect,owner==Tentative?"hidden (provisional)":owner==VisualReplacementWindows.Covered?"hidden (inside our effect)":"hidden (claimed)",0);
            }
        }
        void Hide()
        {
            if(trackedCount==0) return;
            for(int i=0;i<tracked.Length;i++)
            {
                if(tracked[i].Owner==0) continue;
                GameObject effect=tracked[i].Effect;
                if(effect==null) { Forget(i); continue; } // stock destroyed it
                effect.GetComponentsInChildren(true,renderers);
                for(int r=0;r<renderers.Count;r++)
                {
                    Renderer renderer=renderers[r];
                    if(renderer!=null&&hiddenRenderers[i].Hide(renderer,!renderer.forceRenderingOff)) { renderer.forceRenderingOff=true; Suppressed++; }
                }
                effect.GetComponentsInChildren(true,lights);
                for(int l=0;l<lights.Count;l++)
                {
                    Light light=lights[l];
                    if(light!=null&&hiddenLights[i].Hide(light,light.enabled)) light.enabled=false;
                }
            }
            renderers.Clear(); lights.Clear();
        }
        void Confirm(bool claims,double now)
        {
            if(trackedCount==0) return;
            for(int i=0;i<tracked.Length;i++)
            {
                if(tracked[i].Owner!=Tentative||tracked[i].Effect==null) continue;
                Vector3 where=tracked[i].Effect.transform.position;
                int owner=claims?windows.OwnerAt(SnapshotReader.Value(where),now):0;
                if(owner==0&&insideOurEffect!=null&&insideOurEffect(where)) owner=VisualReplacementWindows.Covered;
                if(owner!=0) { tracked[i].Owner=owner; Diagnose(tracked[i].Effect,"provisional confirmed",0); continue; }
                if(now-tracked[i].Since<TentativeGrace) continue;
                GameObject effect=tracked[i].Effect;
                rejected[rejectedCursor]=effect.GetInstanceID(); rejectedCursor=(rejectedCursor+1)%rejected.Length;
                Diagnose(effect,"unclaimed",windows.NearestDistance(SnapshotReader.Value(where),now));
                Untrack(i,true); Unconfirmed++;
            }
        }
        internal int Unconfirmed { get; private set; }
        void Track(GameObject effect,int owner,double now)
        {
            for(int i=0;i<tracked.Length;i++) if(tracked[i].Owner==0)
            {
                tracked[i]=new Tracked { Effect=effect, Owner=owner, Since=now }; trackedCount++;
                hiddenRenderers[i].Clear(); hiddenLights[i].Clear();
                return;
            }
            Missed++; // bounded table full: this stock effect stays visible
        }
        bool IsTracked(GameObject effect)
        {
            if(trackedCount==0) return false;
            for(int i=0;i<tracked.Length;i++) if(tracked[i].Owner!=0&&tracked[i].Effect==effect) return true;
            return false;
        }
        void Untrack(int i,bool restore)
        {
            if(restore&&tracked[i].Effect!=null)
            {
                Restored+=hiddenRenderers[i].Restore(r=>{ if(r!=null&&r.forceRenderingOff) r.forceRenderingOff=false; });
                hiddenLights[i].Restore(l=>{ if(l!=null&&!l.enabled) l.enabled=true; });
            }
            Forget(i);
        }
        void Forget(int i)
        {
            if(tracked[i].Owner!=0) trackedCount--;
            tracked[i]=new Tracked(); hiddenRenderers[i].Clear(); hiddenLights[i].Clear();
        }
        static bool Matches(GameObject effect,GameObject[] prefabs)
        {
            if(prefabs==null) return false;
            string name=effect.name;
            for(int i=0;i<prefabs.Length;i++) if(prefabs[i]!=null&&StockNames.IsInstanceOf(name,prefabs[i].name)) return true;
            return false;
        }
        void Describe(GameObject effect)
        {
            string name=effect.name;
            for(int i=0;i<described.Length;i++)
            {
                if(described[i]==name) return;
                if(described[i]!=null) continue;
                described[i]=name;
                effect.GetComponentsInChildren(true,renderers); effect.GetComponentsInChildren(true,lights);
                int on=0, lit=0;
                for(int r=0;r<renderers.Count;r++) if(renderers[r]!=null&&!renderers[r].forceRenderingOff) on++;
                for(int l=0;l<lights.Count;l++) if(lights[l]!=null&&lights[l].enabled) lit++;
                Debug.Log("[VEFX] Stock FX "+name+" holds "+renderers.Count+" renderer(s) ("+on+" visible) and "+lights.Count+" light(s) ("+lit+" on)");
                renderers.Clear(); lights.Clear();
                return;
            }
        }
        static bool Contains(int[] ids,int id) { for(int i=0;i<ids.Length;i++) if(ids[i]==id) return true; return false; }
        void Diagnose(GameObject effect,string decision,double nearestClaim)
        {
            if(diagnostics>=24) return;
            int id=effect.GetInstanceID();
            int key=decision.StartsWith("unclaimed",StringComparison.Ordinal)?id:decision=="provisional confirmed"?id^0x55555555:~id;
            if(Contains(logged,key)) return;
            logged[loggedCursor]=key; loggedCursor=(loggedCursor+1)%logged.Length; diagnostics++;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[VEFX] Stock FX {0}: {1}{2}",effect.name,decision,
                decision=="unclaimed"?(nearestClaim<0?" after grace, restored (no open claim)":string.Format(System.Globalization.CultureInfo.InvariantCulture," after grace, restored (nearest claim {0:F1} m)",nearestClaim)):""));
        }
        void RestoreAll() { for(int i=0;i<tracked.Length;i++) if(tracked[i].Owner!=0) Untrack(i,true); }
        public void Dispose()
        {
            if(current==this) current=null;
            if(subscribed) { Camera.onPreCull-=PreCull; subscribed=false; }
            RestoreAll(); windows.Clear();
        }
    }
}
