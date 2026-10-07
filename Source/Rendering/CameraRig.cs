using System;
using System.Text;
using UnityEngine;
namespace VolumetricExplosionFX.Rendering
{
    internal static class CameraRig
    {
        internal static Camera Near, Far;
        static bool addedNear, addedFar;
        static float lastNear, lastFar;
        internal static void Setup()
        {
            Refresh();
            int near=Near!=null?Near.cullingMask:~0, far=Far!=null?Far.cullingMask:~0, both=near&far;
            EffectLayer.Value=(both&(1<<1))!=0?1:(both&1)!=0?0:1;
            var text=new StringBuilder("[VEFX] Flight cameras:");
            FlightCamera fc=FlightCamera.fetch;
            if(fc!=null&&fc.cameras!=null)
                foreach(Camera c in fc.cameras)
                    if(c!=null) text.Append(" | "+c.name+" near "+c.nearClipPlane.ToString("F2")+" far "+c.farClipPlane.ToString("F0")+
                        " mask 0x"+c.cullingMask.ToString("X8")+" depth "+c.depthTextureMode+(c==Near?" (near)":c==Far?" (far)":""));
            text.Append(" | effect layer "+EffectLayer.Value);
            Debug.Log(text.ToString());
        }
        internal static void Refresh()
        {
            FlightCamera fc=FlightCamera.fetch;
            Near=fc!=null?fc.mainCamera:Camera.main; Far=null;
            if(fc==null||fc.cameras==null) return;
            foreach(Camera c in fc.cameras)
                if(c!=null&&c!=Near&&(Far==null||c.farClipPlane>Far.farClipPlane)) Far=c;
            if(Far!=null&&Near!=null&&Far.farClipPlane<=Near.farClipPlane) Far=null;
        }
        internal static Camera Pick(Vector3 center,float reach)
        {
            if(Near==null||Far==null) return null;
            float d=Vector3.Distance(Near.transform.position,center);
            return d+reach<Near.farClipPlane*0.98f?Near:Far;
        }
        internal static bool IsFlight(Camera c) { return c!=null&&(c==Near||c==Far); }
        internal static void RequestDepth(bool near,bool far)
        {
            float now=Time.unscaledTime;
            if(near) lastNear=now; if(far) lastFar=now;
            Want(Near,near||now-lastNear<3,ref addedNear);
            Want(Far,far||now-lastFar<3,ref addedFar);
        }
        static void Want(Camera c,bool want,ref bool added)
        {
            if(c==null) { added=false; return; }
            bool has=(c.depthTextureMode&DepthTextureMode.Depth)!=0;
            if(want&&!has) { c.depthTextureMode|=DepthTextureMode.Depth; added=true; }
            else if(!want&&has&&added) { c.depthTextureMode&=~DepthTextureMode.Depth; added=false; }
        }
        internal static void Release()
        {
            Want(Near,false,ref addedNear); Want(Far,false,ref addedFar);
            Near=null; Far=null;
        }
    }
}
