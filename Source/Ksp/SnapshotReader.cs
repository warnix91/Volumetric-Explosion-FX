using System;
using UnityEngine;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Ksp
{
    internal static class SnapshotReader
    {
        internal static Vec3 Value(Vector3d v) { return new Vec3(v.x,v.y,v.z); }
        internal static Vec3 Value(Vector3 v) { return new Vec3(v.x,v.y,v.z); }
        internal static Vector3 Vector(Vec3 v) { return new Vector3((float)v.X,(float)v.Y,(float)v.Z); }
        internal static PartSnapshot Read(Part p)
        {
            if(p==null || p.vessel==null || p.vessel.mainBody==null || p.packed) return null;
            Vessel v=p.vessel; CelestialBody body=v.mainBody;
            Vector3 pos=p.transform.position;
            Vector3d radial=(Vector3d)pos-body.position;
            Vector3 up=(Vector3)radial.normalized;
            double altitude=radial.magnitude-body.Radius;
            // World velocity includes the floating frame.
            Vector3d world=(p.rb!=null ? (Vector3d)p.rb.velocity : (Vector3d)v.rb_velocity)+Krakensbane.GetFrameVelocity();
            // The ground is stationary while KSP uses inverse rotation.
            Vector3d air=body.inverseRotation?Vector3d.zero:body.getRFrmVel(pos);
            Vector3d surface=world-air;
            ResourceSnapshot[] resources=new ResourceSnapshot[p.Resources.Count];
            for(int i=0;i<resources.Length;i++)
            {
                PartResource r=p.Resources[i];
                resources[i]=new ResourceSnapshot(r.resourceName,r.amount,r.maxAmount,r.amount*r.info.density*1000);
            }
            string[] modules=new string[p.Modules.Count];
            for(int i=0;i<modules.Length;i++) modules[i]=p.Modules[i].moduleName;
            Renderer[] renderers=p.GetComponentsInChildren<Renderer>();
            Bounds bounds=new Bounds(pos,Vector3.one*0.5f);
            for(int i=0;i<renderers.Length;i++) if(!(renderers[i] is ParticleSystemRenderer) && renderers[i].GetComponentInParent<Part>()==p) bounds.Encapsulate(renderers[i].bounds);
            double gravity=body.gravParameter/Math.Max(1,radial.sqrMagnitude);
            EnvironmentContext env=new EnvironmentContext(body.bodyName,p.atmDensity,p.staticPressureAtm*101.325,
                gravity,altitude,body.ocean,v.Landed,v.Splashed,Value(up));
            return new PartSnapshot(p.persistentId,p.GetInstanceID(),v.id,p.partInfo!=null?p.partInfo.name:"unknown",
                Value(pos),Value(surface),Value(world),p.rb!=null?Value(p.rb.angularVelocity):new Vec3(),
                Value(bounds.size),(p.mass+p.GetResourceMass())*1000,p.temperature,modules,resources,env,new Quat(p.transform.rotation.x,p.transform.rotation.y,p.transform.rotation.z,p.transform.rotation.w));
        }
        static readonly RaycastHit[] hits=new RaycastHit[16];
        // A ray down to the ground itself. ParallaxContinued gives its trees and rocks colliders: wreckage landed in the
        // canopy and blasts were anchored on tree tops (floating). A hit counts when it is on the terrain (a PQS quad), a
        // building or a launch site, or within a few metres of the terrain's height there (anything else stands on it).
        // Vessel parts never count. The nearest such hit from the origin.
        // Faster, same answer: the hits are taken nearest first (the first that counts is the answer); what each
        // collider is is remembered for the frame (a blast's drapes cast thousands of rays at the same terrain quads,
        // buildings and trees, and the component searches cost more than the rays); and a hit standing well above a terrain
        // or building hit further along the same ray (a tree) is known not to be ground without asking the terrain's
        // height (one query per tree hit made a nuclear blast's drape take 100 ms in a forest).
        internal static bool GroundRay(Vector3 origin,Vector3 direction,float distance,int mask,out RaycastHit ground)
        {
            ground=default(RaycastHit);
            int n=UnityEngine.Physics.RaycastNonAlloc(origin,direction,hits,distance,mask,QueryTriggerInteraction.Ignore);
            for(int i=1;i<n;i++) { RaycastHit h=hits[i]; int k=i; while(k>0&&hits[k-1].distance>h.distance) { hits[k]=hits[k-1]; k--; } hits[k]=h; }
            int solid=-1;   // the nearest terrain, building or launch site hit
            for(int i=0;i<n;i++) if(hits[i].collider!=null&&Kind(hits[i].collider)==1) { solid=i; break; }
            CelestialBody body=FlightGlobals.currentMainBody;
            for(int i=0;i<n;i++)
            {
                RaycastHit h=hits[i];
                if(h.collider==null) continue;
                byte kind=Kind(h.collider);
                if(kind==0) continue;
                if(kind==2)
                {
                    if(solid>i&&body!=null&&body.pqsController!=null)
                    {
                        Vector3 up=((Vector3)((Vector3d)h.point-body.position)).normalized;
                        if(Vector3.Dot(h.point-hits[solid].point,up)>6) continue;   // well above the ground under it
                    }
                    if(!OnTerrain(h.point)) continue;
                }
                ground=h; return true;
            }
            return false;
        }
        // What a collider is: 0 a vessel part (never ground), 1 ground (terrain, a building, a launch site), 2 something else
        // (ground only where it stands on the terrain). Remembered for the frame.
        static readonly System.Collections.Generic.Dictionary<int,byte> kinds=new System.Collections.Generic.Dictionary<int,byte>();
        static int kindsFrame=-1;
        static byte Kind(Collider c)
        {
            if(kindsFrame!=Time.frameCount) { kinds.Clear(); kindsFrame=Time.frameCount; }
            int id=c.GetInstanceID(); byte k;
            if(kinds.TryGetValue(id,out k)) return k;
            if(c.GetComponentInParent<Part>()!=null) k=0;
            else if(c.GetComponent<PQ>()!=null||c.GetComponentInParent<DestructibleBuilding>()!=null||c.GetComponentInParent<PQSCity>()!=null||
                c.GetComponentInParent<PQSCity2>()!=null) k=1;
            else k=2;
            kinds[id]=k; return k;
        }
        static bool OnTerrain(Vector3 point)
        {
            CelestialBody body=FlightGlobals.currentMainBody;
            if(body==null||body.pqsController==null) return true;
            double terrain=body.TerrainAltitude(body.GetLatitude(point),body.GetLongitude(point),true);
            return double.IsNaN(terrain)||body.GetAltitude(point)-terrain<3;
        }
        internal static bool GroundRay(Vector3 origin,Vector3 direction,float distance,out RaycastHit ground)
        { return GroundRay(origin,direction,distance,SurfaceMask(),out ground); }
        // Surface queries exclude vessel parts.
        internal static bool GroundBelow(Vector3 position, Vector3 up, float reach, out Vector3 point)
        {
            RaycastHit hit;
            if(GroundRay(position+up,-up,reach+1,out hit)) { point=hit.point; return true; }
            return Buried(position,up,out point);
        }
        // A part or weapon that hits the ground fast is already under the surface when it blows up (650 m/s is 13 m per
        // physics step): a ray from it finds nothing (terrain colliders are one-sided), so the blast was drawn in the air,
        // under the ground. Looked for from above: the surface over the point, at most this deep.
        const float BuriedDepth=60;
        internal static bool Buried(Vector3 position,Vector3 up,out Vector3 point)
        {
            RaycastHit hit;
            if(GroundRay(position+up*BuriedDepth,-up,BuriedDepth,out hit)&&Vector3.Dot(hit.point-position,up)>0)
            { point=hit.point; return true; }
            point=position; return false;
        }
        // Colour of the terrain under a point: the vertex colours of the terrain mesh it stands on (KSP's terrain is
        // coloured per vertex). False on meshes without colours (runway, buildings).
        sealed class TerrainColours { internal Color32[] Colours; internal int[] Triangles; internal int Vertices; }
        static readonly System.Collections.Generic.Dictionary<int,TerrainColours> terrainCache=new System.Collections.Generic.Dictionary<int,TerrainColours>();
        static float terrainCacheAt;
        internal static bool GroundColour(Vector3 position,Vector3 up,float reach,out Color colour)
        {
            colour=Color.gray;
            RaycastHit hit;
            if(!UnityEngine.Physics.Raycast(position+up*reach,-up,out hit,reach*2+1,SurfaceMask(),QueryTriggerInteraction.Ignore)) return false;
            var collider=hit.collider as MeshCollider;
            if(collider==null||collider.GetComponentInParent<Part>()!=null) return false;
            Mesh mesh=collider.sharedMesh;
            if(mesh==null||!mesh.isReadable||hit.triangleIndex<0) return false;
            // Terrain quads have a few hundred vertices; scenery meshes (KSC lawns) tens of thousands, and copying their
            // colours and triangles stalled a frame for 84 ms. Those keep the default dust.
            if(mesh.vertexCount>20000) return false;
            // Reading a terrain mesh's colours and triangles copies whole arrays: kept for the meshes hit recently (checked
            // against the vertex count, dropped every 30 s since the terrain rebuilds its quads).
            if(Time.unscaledTime>=terrainCacheAt||terrainCache.Count>16) { terrainCache.Clear(); terrainCacheAt=Time.unscaledTime+30; }
            TerrainColours cached;
            if(!terrainCache.TryGetValue(mesh.GetInstanceID(),out cached)||cached.Vertices!=mesh.vertexCount)
            { cached=new TerrainColours { Colours=mesh.colors32, Triangles=mesh.triangles, Vertices=mesh.vertexCount }; terrainCache[mesh.GetInstanceID()]=cached; }
            Color32[] colours=cached.Colours;
            int[] tris=cached.Triangles;
            int t=hit.triangleIndex*3;
            if(colours==null||colours.Length!=mesh.vertexCount||t+2>=tris.Length) return false;
            Color32 a=colours[tris[t]], b=colours[tris[t+1]], c=colours[tris[t+2]];
            colour=new Color((a.r+b.r+c.r)/765f,(a.g+b.g+c.g)/765f,(a.b+b.b+c.b)/765f,1);
            return colour.maxColorComponent>0.02f;
        }
        internal static int SurfaceMask()
        {
            int mask=(1<<15)|(1<<30);
            int terrain=LayerMask.NameToLayer("TerrainColliders"), scenery=LayerMask.NameToLayer("Local Scenery");
            if(terrain>=0) mask|=1<<terrain; if(scenery>=0) mask|=1<<scenery;
            return mask;
        }
        internal static bool TryGround(Part p, PartSnapshot s, out Vector3 point)
        {
            Vector3 up=Vector(s.Environment.Up), pos=p.transform.position;
            float extent=(float)Numbers.Clamp(s.Dimensions.Length*0.5,0.25,20);
            int mask=(1<<15)|(1<<30);
            int terrain=LayerMask.NameToLayer("TerrainColliders");
            int scenery=LayerMask.NameToLayer("Local Scenery");
            if(terrain>=0) mask|=1<<terrain; if(scenery>=0) mask|=1<<scenery;
            RaycastHit hit;
            if(GroundRay(pos+up*extent,-up,extent*2+1,mask,out hit)) { point=hit.point; return true; }
            return Buried(pos,up,out point);
        }
    }
}
