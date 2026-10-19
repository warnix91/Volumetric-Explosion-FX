using System;
using System.Reflection;
using UnityEngine;
namespace VolumetricExplosionFX.Ksp
{
    // 1.1: the altitudes of the raymarched cloud layers of Environmental Visual Enhancements over the current body,
    // read through its public members by reflection (nothing of it is copied, and nothing is called but getters). EVE
    // composites its clouds in the overlay queue of the transparent pass, before VEFX lays its volumes over the image: a
    // nuclear cloud seen from above the cloud deck was drawn over it. LowResVolumes hides the volumes where one of these
    // layers lies between the eye and them and the transparent pass covered the pixel. Nothing without EVE.
    internal static class CloudLayers
    {
        internal static bool Known { get; private set; }
        internal static Vector4 Bands { get; private set; }   // lowest layer's lowest and highest altitude (m), then the next one's
        static Type cloudsObject; static FieldInfo bodyField, layerField; static PropertyInfo inner, outer, planet;
        static bool searched, failed; static float nextScan; static string scannedBody, logged;
        internal static void Refresh(CelestialBody body)
        {
            if(failed||body==null) { Known=false; return; }
            if(Time.realtimeSinceStartup<nextScan&&scannedBody==body.bodyName) return;
            nextScan=Time.realtimeSinceStartup+5; scannedBody=body.bodyName;
            try
            {
                if(!searched) { searched=true; Find(); }
                if(cloudsObject==null||bodyField==null||layerField==null) { Known=false; return; }
                UnityEngine.Object[] objects=UnityEngine.Object.FindObjectsOfType(cloudsObject);
                var layers=new System.Collections.Generic.List<Vector2>(); int skipped=0;
                for(int i=0;i<objects.Length;i++)
                {
                    if(bodyField.GetValue(objects[i]) as string!=body.bodyName) continue;
                    object layer=layerField.GetValue(objects[i]);
                    if(layer==null) continue;
                    float r=(float)planet.GetValue(layer,null), a=(float)inner.GetValue(layer,null)-r, b=(float)outer.GetValue(layer,null)-r;
                    // Real cloud decks only: the weather layers reaching the ground (rain, fog, snow: -20 to 2150 m
                    // over Kerbin) and very tall ones (one reached 80 km) made a band from the ground to space, so every
                    // explosion counted as under the clouds.
                    if(r>0&&b>a&&a>=300&&b-a<=20000&&b<=30000) layers.Add(new Vector2(a,b)); else skipped++;
                }
                // Layers that touch are one; two bands at most: the lowest, and everything above it (the cumulonimbus tops).
                layers.Sort((x,y)=>x.x.CompareTo(y.x));
                var merged=new System.Collections.Generic.List<Vector2>();
                foreach(Vector2 l in layers)
                {
                    if(merged.Count>0&&l.x<=merged[merged.Count-1].y+300) merged[merged.Count-1]=new Vector2(merged[merged.Count-1].x,Mathf.Max(merged[merged.Count-1].y,l.y));
                    else merged.Add(l);
                }
                Known=merged.Count>0;
                float lo0=Known?merged[0].x:0, hi0=Known?merged[0].y:0, lo1=float.MaxValue, hi1=0;
                for(int i=1;i<merged.Count;i++) { lo1=Mathf.Min(lo1,merged[i].x); hi1=Mathf.Max(hi1,merged[i].y); }
                Bands=Known?new Vector4(lo0,hi0,lo1<float.MaxValue?lo1:0,lo1<float.MaxValue?hi1:0):Vector4.zero;
                string text=Known?body.bodyName+" "+lo0.ToString("0")+"-"+hi0.ToString("0")+" m"+(lo1<float.MaxValue?", "+lo1.ToString("0")+"-"+hi1.ToString("0")+" m":""):null;
                if(text==null&&skipped>0) text=body.bodyName+" none used";
                if(text!=null) text+=skipped>0?" ("+skipped+" ground-level or very tall layer(s) not used)":"";
                if(text!=null&&text!=logged) { logged=text; Debug.Log("[VEFX] Cloud layers (EVE): "+text+": explosions are hidden behind a layer lying wholly between the eye and them"); }
            }
            catch(Exception ex)
            {
                failed=true; Known=false;
                Debug.LogWarning("[VEFX] Cloud layers unreadable ("+ex.GetType().Name+"): explosions are drawn over the clouds");
            }
        }
        static void Find()
        {
            foreach(AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
            {
                if(a.name!="Atmosphere") continue;
                cloudsObject=a.assembly.GetType("Atmosphere.CloudsObject");
                Type volume=a.assembly.GetType("Atmosphere.CloudsRaymarchedVolume");
                if(cloudsObject==null||volume==null) { cloudsObject=null; return; }
                const BindingFlags any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
                bodyField=cloudsObject.GetField("body",any); layerField=cloudsObject.GetField("layerRaymarchedVolumeV5",any);
                inner=volume.GetProperty("InnerSphereRadius",BindingFlags.Public|BindingFlags.Instance);
                outer=volume.GetProperty("OuterSphereRadius",BindingFlags.Public|BindingFlags.Instance);
                planet=volume.GetProperty("PlanetRadius",BindingFlags.Public|BindingFlags.Instance);
                if(inner==null||outer==null||planet==null) cloudsObject=null;
                return;
            }
        }
    }
}
