using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class FireVolumeRenderer
    {
        static readonly int BlobsId=Shader.PropertyToID("_Blobs");
        static readonly int BoxId=Shader.PropertyToID("_Box"), FireId=Shader.PropertyToID("_Fire"), LeanId=Shader.PropertyToID("_Lean"), PatchId=Shader.PropertyToID("_Patchy"),StreakId=Shader.PropertyToID("_Streak");
        readonly Transform transform;
        readonly MeshRenderer renderer;
        readonly Mesh mesh;
        readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        float radius, height, halfWidth, halfHeight, halfLength, rise, seed, patchy;
        Vector2 lean, localLean;
        // 1.1: the pool can stretch into a strip of burning fuel along a crash path (direction in the blast frame).
        Vector2 streakDir=Vector2.right; float streakLength, streakTarget, streakSpeed;
        // 1.1: along a crash path, pools of burning fuel scattered at random (a straight, even band of fire read
        // as fake): where along the path (share of its length), how far to the side and how big (pool radii), how tall
        // the flames. The first is the pool itself. Drawn in the strip frame: along (m), across (m), radius (m), height.
        internal const int PoolCount=8;
        readonly float[] poolAlong=new float[PoolCount], poolAcross=new float[PoolCount], poolSize=new float[PoolCount], poolTall=new float[PoolCount];
        readonly Vector4[] pools=new Vector4[PoolCount];
        float poolReach;   // furthest a pool reaches to the side of the path (m)
        internal float PoolReach { get { return poolReach; } }
        internal Vector4[] Pools { get { return pools; } }
        void ScatterPools(uint randomSeed)
        {
            uint h=randomSeed*2654435761u+0x9E37u;
            Func<float> next=()=>{ h^=h<<13; h^=h>>17; h^=h<<5; return (h&0xFFFFFF)/16777216f; };
            poolAlong[0]=0; poolAcross[0]=0; poolSize[0]=1; poolTall[0]=1;
            float across=0;
            for(int k=1;k<PoolCount;k++)
            {
                poolAlong[k]=Mathf.Clamp01((k-0.5f+0.8f*(next()-0.5f))/(PoolCount-1.5f));
                across=Mathf.Clamp(across+(next()*2-1)*1.1f,-2.4f,2.4f);   // the path wanders
                poolAcross[k]=across; poolSize[k]=0.4f+0.8f*next(); poolTall[k]=0.3f+0.85f*next();
            }
        }
        void PlacePools()
        {
            poolReach=0;
            for(int k=0;k<PoolCount;k++)
            {
                float size=radius*poolSize[k];
                // (no pool beyond the spill: those past its end stay out)
                pools[k]=new Vector4(poolAlong[k]*streakTarget,poolAcross[k]*radius,k>0&&streakTarget<=0.01f?0:size,poolTall[k]);
                poolReach=Mathf.Max(poolReach,Mathf.Abs(poolAcross[k]*radius)+size-radius);
            }
        }
        bool visible;
        internal bool Visible { get { return visible; } }
        internal Vector3 Center { get { return transform.position; } }
        internal float Reach { get { return Mathf.Sqrt(halfLength*halfLength+halfWidth*halfWidth+halfHeight*halfHeight); } }
        internal FireVolumeRenderer(Transform parent,Mesh cube,Material material,int layer)
        {
            var go=new GameObject("Original volumetric pool fire"); go.layer=layer; go.transform.SetParent(parent,false);
            transform=go.transform; go.AddComponent<MeshFilter>().sharedMesh=cube; mesh=cube;
            renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off; renderer.enabled=false;
        }
        internal void Begin(double poolRadius,double flameHeight,Vector3 wind,double gravity,uint randomSeed,double patches=0)
        {
            patchy=(float)patches;
            radius=(float)Math.Max(poolRadius,0.3); height=(float)Math.Max(flameHeight,0.5);
            float buoyant=1.4f*Mathf.Sqrt((float)Math.Max(gravity,0.5)*height);
            lean=new Vector2(wind.x,wind.z)/Mathf.Max(buoyant,1);
            if(lean.magnitude>0.6f) lean=lean.normalized*0.6f;
            rise=Mathf.Clamp(0.8f*buoyant/height,0.25f,3);
            seed=(randomSeed%997)*0.137f;
            streakDir=Vector2.right; streakLength=0; streakTarget=0; streakSpeed=0;
            ScatterPools(randomSeed); PlacePools();
            Layout();
            visible=false; renderer.enabled=false;
        }
        void Layout()
        {
            halfHeight=height*0.9f;
            halfWidth=radius*1.25f+lean.magnitude*height*1.7f+0.5f+(streakTarget>0.01f?poolReach:0);
            float yaw=Mathf.Atan2(-streakDir.y,streakDir.x)*Mathf.Rad2Deg;   // turns local +x onto the strip's direction
            Quaternion turn=streakLength>0.01f?Quaternion.Euler(0,yaw,0):Quaternion.identity;
            halfLength=halfWidth+streakLength*0.5f;
            transform.localRotation=turn;
            transform.localPosition=turn*Vector3.right*(streakLength*0.5f)+Vector3.up*halfHeight;
            transform.localScale=new Vector3(halfLength,halfHeight,halfWidth);
            Vector3 l=Quaternion.Inverse(turn)*new Vector3(lean.x,0,lean.y); localLean=new Vector2(l.x,l.z);
        }
        // Burning fuel thrown along the path: the strip reaches toward a point (blast frame, horizontal), growing at `speed`.
        internal bool Extend(Vector2 direction,float distance,float speed)
        {
            if(streakTarget>0&&Vector2.Dot(direction,streakDir)<0.8f) return false;   // only along the same path
            if(streakTarget<=0) streakDir=direction;
            streakTarget=Mathf.Min(Mathf.Max(streakTarget,distance*Vector2.Dot(direction,streakDir)),180);
            streakSpeed=Mathf.Max(speed,12);
            PlacePools(); Layout();
            return true;
        }
        // The strip so far (blast frame): direction, length burnt, length it will reach.
        internal Vector2 StripDirection { get { return streakDir; } }
        internal float StripLength { get { return streakLength; } }
        internal float StripTarget { get { return streakTarget; } }
        internal float PoolRadius { get { return radius; } }
        internal void Advance(float dt)
        {
            if(streakLength>=streakTarget) return;
            streakLength=Mathf.Min(streakTarget,streakLength+streakSpeed*dt);
            Layout();
        }
        // A pool fire fed by another spill nearby grows in place (radius and flame height), keeping its lean and flicker.
        internal void Resize(double poolRadius,double flameHeight)
        {
            radius=(float)Math.Max(poolRadius,0.3); height=(float)Math.Max(flameHeight,0.5);
            PlacePools(); Layout();
        }
        // The pools burning so far, in the blast frame (horizontal offset from the first pool's centre, radius in w); for
        // the smoke rising over them.
        internal int PoolsLit(Vector4[] into)
        {
            int n=0;
            Vector2 side=new Vector2(-streakDir.y,streakDir.x);   // the strip frame's +z in the blast frame
            for(int k=0;k<PoolCount&&n<into.Length;k++)
            {
                Vector4 p=pools[k];
                if(p.z<=0||(k>0&&p.x>streakLength)) continue;
                Vector2 at=streakDir*p.x+side*p.y;
                into[n++]=new Vector4(at.x,0,at.y,p.z);
            }
            return n;
        }
        internal void Tick(float level,float flame,double puffHz,float steps,float flicker)
        {
            visible=level>0.01f;
            if(!visible) { renderer.enabled=false; return; }
            block.SetVector(BoxId,new Vector4(halfLength,halfHeight,steps,seed));
            block.SetVector(StreakId,new Vector4(streakLength,streakLength*0.5f,halfWidth,0));
            block.SetVector(FireId,new Vector4(radius,Mathf.Min(flame,2*halfHeight/1.75f),Mathf.Clamp01(level),(float)puffHz));
            block.SetVector(LeanId,new Vector4(localLean.x,localLean.y,rise,flicker));
            block.SetFloat(PatchId,patchy);
            block.SetVectorArray(BlobsId,pools);
            renderer.SetPropertyBlock(block);
        }
        internal void Show(bool on,LowResVolumes lowRes=null)
        {
            renderer.enabled=visible&&on&&lowRes==null;
            if(visible&&on&&lowRes!=null) lowRes.Add(mesh,transform.localToWorldMatrix,renderer.sharedMaterial,block);
        }
        internal void Clear() { visible=false; renderer.enabled=false; }
        internal float Coverage(Camera cam)
        {
            if(!visible||cam==null) return 0;
            float r=Reach, distance=Vector3.Distance(cam.transform.position,Center);
            if(distance<=r) return 1;
            float projected=r/(distance*Mathf.Tan(cam.fieldOfView*0.5f*Mathf.Deg2Rad));
            return Mathf.Clamp01(Mathf.PI*projected*projected/(4*Mathf.Max(cam.aspect,0.1f)));
        }
    }
}
