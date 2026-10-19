using System;
using System.Collections.Generic;
using UnityEngine;
namespace VolumetricExplosionFX.Rendering
{
    // The engine-flame renderers of the loaded vessels (Waterfall: their shaders are named "Waterfall/..."), drawn again
    // over the explosion clouds where they are in front of them (LowResVolumes). Read only: found by shader name, never
    // changed. Vessels are scanned one per frame, each again every few seconds (flames are added when engines start).
    internal sealed class PlumeOverlays
    {
        readonly List<Renderer> found=new List<Renderer>();
        // Lines and trails (other mods' contrails and ribbons) and every see-through effect on the vessels: the
        // clouds covered them even in front (only Waterfall's flames were drawn again). Lines are found every few seconds.
        readonly List<Renderer> lines=new List<Renderer>(); float linesAt=-10;
        readonly Dictionary<Guid,List<Renderer>> byVessel=new Dictionary<Guid,List<Renderer>>();
        readonly Dictionary<Guid,float> scannedAt=new Dictionary<Guid,float>();
        readonly List<Renderer> scratch=new List<Renderer>();
        readonly List<Guid> gone=new List<Guid>();
        int next;
        const float Rescan=4;
        // Whether a material's shader is Waterfall's, remembered by material (reading a shader's name makes a new
        // string: a vessel of hundreds of renderers made hundreds every few seconds, feeding the garbage collector).
        readonly Dictionary<int,bool> waterfall=new Dictionary<int,bool>();
        bool Waterfall(Material m)
        {
            int id=m.GetInstanceID(); bool yes;
            if(waterfall.TryGetValue(id,out yes)) return yes;
            if(waterfall.Count>4096) waterfall.Clear();
            yes=m.shader.name.StartsWith("Waterfall",StringComparison.OrdinalIgnoreCase);
            waterfall[id]=yes; return yes;
        }
        internal IList<Renderer> Renderers { get { return found; } }
        internal void Update()
        {
            var vessels=FlightGlobals.VesselsLoaded;
            if(vessels==null) return;
            // Forget vessels no longer loaded.
            gone.Clear();
            foreach(Guid id in byVessel.Keys)
            {
                bool loaded=false;
                for(int i=0;i<vessels.Count;i++) if(vessels[i]!=null&&vessels[i].id==id) { loaded=true; break; }
                if(!loaded) gone.Add(id);
            }
            for(int i=0;i<gone.Count;i++) { byVessel.Remove(gone[i]); scannedAt.Remove(gone[i]); }
            if(vessels.Count>0)
            {
                next=(next+1)%vessels.Count;
                Vessel v=vessels[next];
                float at;
                if(v!=null&&(!scannedAt.TryGetValue(v.id,out at)||Time.unscaledTime-at>Rescan)) { Scan(v); scannedAt[v.id]=Time.unscaledTime; }
            }
            if(Time.unscaledTime-linesAt>3)
            {
                linesAt=Time.unscaledTime; lines.Clear();
                foreach(LineRenderer l in UnityEngine.Object.FindObjectsOfType<LineRenderer>()) if(l.gameObject.layer!=EffectLayer.Value) lines.Add(l);
                foreach(TrailRenderer t in UnityEngine.Object.FindObjectsOfType<TrailRenderer>()) if(t.gameObject.layer!=EffectLayer.Value) lines.Add(t);
            }
            found.Clear();
            foreach(var list in byVessel.Values) for(int i=0;i<list.Count;i++) if(list[i]!=null) found.Add(list[i]);
            for(int i=0;i<lines.Count;i++) if(lines[i]!=null&&!found.Contains(lines[i])) found.Add(lines[i]);
        }
        void Scan(Vessel v)
        {
            List<Renderer> list;
            if(!byVessel.TryGetValue(v.id,out list)) { list=new List<Renderer>(); byVessel[v.id]=list; }
            list.Clear();
            for(int p=0;p<v.parts.Count;p++)
            {
                Part part=v.parts[p];
                if(part==null) continue;
                scratch.Clear(); part.GetComponentsInChildren(true,scratch);
                for(int i=0;i<scratch.Count;i++)
                {
                    Renderer r=scratch[i];
                    if(r==null||r is ParticleSystemRenderer||r.gameObject.layer==EffectLayer.Value) continue;
                    Material m=r.sharedMaterial;
                    if(m==null||m.shader==null) continue;
                    if(m.renderQueue>2500||Waterfall(m)) list.Add(r);
                }
            }
        }
        internal void Clear() { found.Clear(); byVessel.Clear(); scannedAt.Clear(); waterfall.Clear(); }
    }
}
