using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class FireVolumeRenderer
    {
        static readonly int BoxId=Shader.PropertyToID("_Box"), FireId=Shader.PropertyToID("_Fire"), LeanId=Shader.PropertyToID("_Lean");
        readonly Transform transform;
        readonly MeshRenderer renderer;
        readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        float radius, height, halfWidth, halfHeight, rise, seed;
        Vector2 lean;
        bool visible;
        internal bool Visible { get { return visible; } }
        internal Vector3 Center { get { return transform.position; } }
        internal float Reach { get { return Mathf.Sqrt(2*halfWidth*halfWidth+halfHeight*halfHeight); } }
        internal FireVolumeRenderer(Transform parent,Mesh cube,Material material,int layer)
        {
            var go=new GameObject("Original volumetric pool fire"); go.layer=layer; go.transform.SetParent(parent,false);
            transform=go.transform; go.AddComponent<MeshFilter>().sharedMesh=cube;
            renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off; renderer.enabled=false;
        }
        internal void Begin(double poolRadius,double flameHeight,Vector3 wind,double gravity,uint randomSeed)
        {
            radius=(float)Math.Max(poolRadius,0.3); height=(float)Math.Max(flameHeight,0.5);
            float buoyant=1.4f*Mathf.Sqrt((float)Math.Max(gravity,0.5)*height);
            lean=new Vector2(wind.x,wind.z)/Mathf.Max(buoyant,1);
            if(lean.magnitude>0.6f) lean=lean.normalized*0.6f;
            rise=Mathf.Clamp(0.8f*buoyant/height,0.25f,3);
            seed=(randomSeed%997)*0.137f;
            halfHeight=height*0.9f;
            halfWidth=radius*1.25f+lean.magnitude*height*1.7f+0.5f;
            transform.localPosition=new Vector3(0,halfHeight,0); transform.localRotation=Quaternion.identity;
            transform.localScale=new Vector3(halfWidth,halfHeight,halfWidth);
            visible=false; renderer.enabled=false;
        }
        internal void Tick(float level,float flame,double puffHz,float steps,float flicker)
        {
            visible=level>0.01f;
            if(!visible) { renderer.enabled=false; return; }
            block.SetVector(BoxId,new Vector4(halfWidth,halfHeight,steps,seed));
            block.SetVector(FireId,new Vector4(radius,Mathf.Min(flame,2*halfHeight/1.75f),Mathf.Clamp01(level),(float)puffHz));
            block.SetVector(LeanId,new Vector4(lean.x,lean.y,rise,flicker));
            renderer.SetPropertyBlock(block);
        }
        internal void Show(bool on) { renderer.enabled=visible&&on; }
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
