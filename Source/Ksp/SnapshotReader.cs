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
        // Surface queries exclude vessel parts.
        internal static bool GroundBelow(Vector3 position, Vector3 up, float reach, out Vector3 point)
        {
            RaycastHit hit;
            if(UnityEngine.Physics.Raycast(position+up,-up,out hit,reach+1,SurfaceMask(),QueryTriggerInteraction.Ignore)&&
                hit.collider!=null&&hit.collider.GetComponentInParent<Part>()==null)
            { point=hit.point; return true; }
            point=position; return false;
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
            if(UnityEngine.Physics.Raycast(pos+up*extent,-up,out hit,extent*2+1,mask,QueryTriggerInteraction.Ignore))
            { point=hit.point; return hit.collider!=null && hit.collider.GetComponentInParent<Part>()==null; }
            point=pos; return false;
        }
    }
}
