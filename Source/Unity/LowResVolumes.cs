using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace VolumetricExplosionFX.Rendering
{
    // Draws ray-marched volumes into a reduced-resolution image (half width and height = a quarter
    // of the pixels to march) and lays it over the camera image after the transparent pass.
    // Two command buffers per camera, refilled in that camera's pre-cull. No KSP types.
    // Before the transparent pass: the volumes are marched; the 1.1 simulated clouds also write their opaque front into
    // the depth buffer, so transparent things behind a cloud are hidden by it.
    // After the transparent pass: the volumes are laid over the image (after Scatterer's atmosphere, which otherwise
    // hazed the clouds with the distance of the ground behind them), and the "overlays" (engine flames: Waterfall) that
    // are in front of a cloud are drawn again over it (they were hidden behind the cloud).
    internal sealed class LowResVolumes : IDisposable
    {
        static readonly int SpreadId=Shader.PropertyToID("_VefxCompositeSpread");
        static readonly int FrontId=Shader.PropertyToID("_VefxLowResFront");
        static readonly int OverlayId=Shader.PropertyToID("_VefxOverlay"), OverlayOnId=Shader.PropertyToID("_VefxOverlayOn"),
            OverlayZId=Shader.PropertyToID("_VefxOverlayZ");
        // 1.1: clouds another mod composites in the transparent pass (EVE: overlay queue) were covered by the
        // volumes laid over the image after it. Where a cloud layer (altitudes, the caller's) lies between the eye and a
        // volume's front and the clouds covered the pixel, the volume is hidden. (what the clouds covered is the
        // image just before the overlay queue, grabbed by a probe drawn at Overlay-1, against the image after the
        // transparent pass: against the image before the whole pass, the atmosphere's haze and our own glare counted as
        // clouds and the explosions faded over the land.)
        static readonly int AfterId=Shader.PropertyToID("_VefxAfterAlpha"),
            CloudOccId=Shader.PropertyToID("_VefxCloudOcc"), CloudBandsId=Shader.PropertyToID("_VefxCloudBands"),
            PlanetId=Shader.PropertyToID("_VefxPlanet"), EyeId=Shader.PropertyToID("_VefxEye"), FrustumId=Shader.PropertyToID("_VefxFrustum");
        internal bool CloudOcclusion; internal Vector4 CloudBands, Planet;   // two layers' lowest and highest altitudes (m); planet centre and radius
        readonly Vector3[] corners=new Vector3[4]; Matrix4x4 frustum;
        internal Material ProbeMaterial;   // VolumetricExplosionFX/CloudProbe (none: no hiding behind clouds)
        GameObject probe; Mesh probeMesh; Renderer probeRenderer;
        // The probe is drawn only by the camera whose volumes are hidden behind clouds (the grab is once a frame).
        internal void ShowProbe(Camera cam,bool on)
        {
            if(ProbeMaterial==null) return;
            if(probe==null)
            {
                if(!on) return;
                probe=new GameObject("VEFX cloud probe"); probe.layer=EffectLayer.Value;
                probeMesh=new Mesh { name="VEFX cloud probe" };
                probeMesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up}; probeMesh.triangles=new[]{0,1,2};
                probeMesh.bounds=new Bounds(Vector3.zero,Vector3.one*2e7f);   // never culled
                probe.AddComponent<MeshFilter>().sharedMesh=probeMesh;
                probeRenderer=probe.AddComponent<MeshRenderer>(); probeRenderer.sharedMaterial=ProbeMaterial;
                probeRenderer.shadowCastingMode=ShadowCastingMode.Off; probeRenderer.receiveShadows=false;
            }
            probeRenderer.enabled=on;
            if(on&&cam!=null) probe.transform.position=cam.transform.position;
        }
        const CameraEvent MarchEvent=CameraEvent.BeforeForwardAlpha, CompositeEvent=CameraEvent.AfterForwardAlpha;
        readonly Material composite;
        readonly Camera[] cameras=new Camera[4];
        readonly CommandBuffer[] marches=new CommandBuffer[4], composites=new CommandBuffer[4];
        CommandBuffer current, after;
        // The marched image and the clouds' fronts live from one buffer to the other (temporary textures do not), one pair
        // per camera, resized with it.
        readonly RenderTexture[] images=new RenderTexture[4], frontImages=new RenderTexture[4];
        RenderTexture image, frontImage;
        readonly RenderTargetIdentifier[] mrt=new RenderTargetIdentifier[2];
        int count, divisor=2;
        Vector3 eye, eyeForward;
        // Volumes collected for this camera, drawn farthest first so nearer explosions cover farther ones.
        const int Capacity=64;
        readonly Mesh[] meshes=new Mesh[Capacity];
        readonly Matrix4x4[] matrices=new Matrix4x4[Capacity];
        readonly Material[] materials=new Material[Capacity];
        readonly MaterialPropertyBlock[] blocks=new MaterialPropertyBlock[Capacity];
        readonly float[] depths=new float[Capacity];
        readonly bool[] fronts=new bool[Capacity];   // the volume's shader writes its front depth (1.1 simulated fireball)
        readonly int[] order=new int[Capacity];
        // Transparent renderers to draw again over the clouds where they are in front of them (set by the caller).
        internal IList<Renderer> Overlays;
        internal Mesh OverlayBox;   // a unit cube: each flame's bounding box (Composite pass 3)
        readonly MaterialPropertyBlock overlayBlock=new MaterialPropertyBlock();
        static readonly int OverlayCentreId=Shader.PropertyToID("_VefxOverlayCentre");
        readonly List<Material> overlayMaterials=new List<Material>();   // (reused: sharedMaterials made a new array per plume per frame)
        internal LowResVolumes(Material compositeMaterial) { composite=compositeMaterial; }
        internal bool Available { get { return composite!=null; } }
        // Starts filling the buffers of the camera about to render (divisor 2 = half resolution;
        // 1 = full resolution, used only to smooth the ray-march dither of the 1.1 fireball).
        internal void Begin(Camera cam,int resolutionDivisor)
        {
            current=null; after=null; count=0; divisor=Mathf.Clamp(resolutionDivisor,1,4);
            if(composite==null||cam==null) return;
            eye=cam.transform.position; eyeForward=cam.transform.forward;
            int slot=-1, free=-1;
            for(int i=0;i<cameras.Length;i++)
            {
                if(cameras[i]==cam) { slot=i; break; }
                if(free<0&&(cameras[i]==null||marches[i]==null)) free=i;
            }
            if(slot<0)
            {
                if(free<0) return;
                slot=free; Detach(slot);
                cameras[slot]=cam;
                marches[slot]=new CommandBuffer { name="VEFX reduced-resolution volumes" };
                composites[slot]=new CommandBuffer { name="VEFX volumes composite" };
                cam.AddCommandBuffer(MarchEvent,marches[slot]);
                cam.AddCommandBuffer(CompositeEvent,composites[slot]);
            }
            current=marches[slot]; after=composites[slot]; current.Clear(); after.Clear();
            int w=Mathf.Max(1,(cam.pixelWidth+divisor-1)/divisor), h=Mathf.Max(1,(cam.pixelHeight+divisor-1)/divisor);
            image=Sized(ref images[slot],w,h,FilterMode.Bilinear); frontImage=Sized(ref frontImages[slot],w,h,FilterMode.Point);
            occluding=CloudOcclusion&&ProbeMaterial!=null;
            if(occluding)
            {
                // World rays through the screen's corners, one unit of eye depth long (bottom-left, top-left, top-right, bottom-right).
                cam.CalculateFrustumCorners(new Rect(0,0,1,1),1,Camera.MonoOrStereoscopicEye.Mono,corners);
                for(int k=0;k<4;k++) { Vector3 v=cam.transform.TransformVector(corners[k]); frustum.SetRow(k,new Vector4(v.x,v.y,v.z,0)); }
            }
        }
        static void Free(RenderTexture rt)
        {
            rt.Release();
            if(Application.isPlaying) UnityEngine.Object.Destroy(rt); else UnityEngine.Object.DestroyImmediate(rt);   // (the bench runs in edit mode)
        }
        static RenderTexture Sized(ref RenderTexture rt,int w,int h,FilterMode filter)
        {
            if(rt!=null&&(rt.width!=w||rt.height!=h||!rt.IsCreated())) { Free(rt); rt=null; }
            if(rt==null)
            {
                rt=new RenderTexture(w,h,0,RenderTextureFormat.ARGBHalf) { filterMode=filter, wrapMode=TextureWrapMode.Clamp, name="VEFX volumes" };
                rt.Create();
            }
            return rt;
        }
        internal void Add(Mesh mesh,Matrix4x4 matrix,Material material,MaterialPropertyBlock properties)
        {
            if(current==null||mesh==null||material==null||count>=Capacity) return;
            meshes[count]=mesh; matrices[count]=matrix; materials[count]=material; blocks[count]=properties;
            depths[count]=(new Vector3(matrix.m03,matrix.m13,matrix.m23)-eye).sqrMagnitude;
            fronts[count]=material.IsKeywordEnabled("VEFX_SIM");
            count++;
        }
        internal void End()
        {
            if(current!=null&&after!=null&&count>0)
            {
                for(int i=0;i<count;i++)
                {
                    int k=i;   // insertion sort, farthest first (few volumes, no allocation)
                    while(k>0&&depths[order[k-1]]<depths[i]) { order[k]=order[k-1]; k--; }
                    order[k]=i;
                }
                bool any=false;
                for(int i=0;i<count;i++) any|=fronts[i];
                bool occlude=any&&occluding;
                // ---- before the transparent pass: march, and the clouds' front into the depth buffer ----
                mrt[0]=image; mrt[1]=frontImage;
                if(any) { current.SetRenderTarget(frontImage); current.ClearRenderTarget(false,true,Color.clear); }
                current.SetRenderTarget(image);
                current.ClearRenderTarget(false,true,Color.clear);
                bool both=false;
                for(int i=0;i<count;i++)
                {
                    int j=order[i];
                    // Shaders that write a front depth draw into both targets; the others only into the colour.
                    if(fronts[j]!=both) { both=fronts[j]; if(both) current.SetRenderTarget(mrt,image); else current.SetRenderTarget(image); }
                    current.DrawMesh(meshes[j],matrices[j],materials[j],0,0,blocks[j]);
                }
                if(any)
                {
                    current.SetGlobalTexture(FrontId,frontImage);
                    current.Blit(image,BuiltinRenderTextureType.CameraTarget,composite,1);
                }
                // ---- after the transparent pass: engine flames in front of the clouds, then the composite ----
                if(occlude)
                {
                    // (full size, as the probe's grab: at another size the land's fine detail differed and read as cloud)
                    after.GetTemporaryRT(AfterId,-1,-1,0,FilterMode.Bilinear,RenderTextureFormat.ARGBHalf);
                    after.Blit(BuiltinRenderTextureType.CameraTarget,AfterId);
                    after.SetGlobalVector(CloudBandsId,CloudBands); after.SetGlobalVector(PlanetId,Planet);
                    after.SetGlobalVector(EyeId,eye); after.SetGlobalMatrix(FrustumId,frustum);
                }
                after.SetGlobalFloat(CloudOccId,occlude?1:0);
                bool overlay=false;
                if(any&&Overlays!=null)
                    for(int i=0;i<Overlays.Count;i++)
                    {
                        Renderer r=Overlays[i];
                        if(r==null||!r.enabled||!r.gameObject.activeInHierarchy) continue;
                        if(!overlay)
                        {
                            overlay=true;
                            // The flames' colour, and their own eye depth (Composite nearest kept): the composite
                            // shows them only where they are nearer than the cloud's front (their depth test did not hold).
                            after.GetTemporaryRT(OverlayId,-1,-1,0,FilterMode.Bilinear,RenderTextureFormat.ARGBHalf);
                            after.GetTemporaryRT(OverlayZId,-1,-1,0,FilterMode.Point,RenderTextureFormat.RFloat);
                            after.SetRenderTarget(OverlayZId); after.ClearRenderTarget(false,true,new Color(1e7f,0,0,0));
                            after.SetRenderTarget(OverlayId); after.ClearRenderTarget(false,true,Color.clear);
                        }
                        r.GetSharedMaterials(overlayMaterials);
                        int subs=overlayMaterials.Count;
                        after.SetRenderTarget(OverlayId);
                        for(int m=0;m<subs;m++) if(overlayMaterials[m]!=null) after.DrawRenderer(r,overlayMaterials[m],m,0);
                        after.SetRenderTarget(OverlayZId);
                        for(int m=0;m<Mathf.Max(subs,1);m++) after.DrawRenderer(r,composite,m,2);
                        if(OverlayBox!=null)
                        {
                            Bounds b=r.bounds;
                            overlayBlock.SetFloat(OverlayCentreId,Mathf.Max(Vector3.Dot(b.center-eye,eyeForward),0.01f));
                            after.DrawMesh(OverlayBox,Matrix4x4.TRS(b.center,Quaternion.identity,b.extents),composite,0,3,overlayBlock);   // (a cube of half-size 1)
                        }
                    }
                after.SetGlobalFloat(OverlayOnId,overlay?1:0);
                if(overlay) { after.SetGlobalTexture(OverlayId,OverlayId); after.SetGlobalTexture(OverlayZId,OverlayZId); }
                after.SetGlobalFloat(SpreadId,divisor==1?1.25f:0.6f);
                after.Blit(image,BuiltinRenderTextureType.CameraTarget,composite,0);
                if(overlay) { after.ReleaseTemporaryRT(OverlayId); after.ReleaseTemporaryRT(OverlayZId); }
                if(occlude) after.ReleaseTemporaryRT(AfterId);
            }
            for(int i=0;i<count;i++) { meshes[i]=null; materials[i]=null; blocks[i]=null; }
            overlayMaterials.Clear();
            current=null; after=null; count=0;
        }
        void Detach(int i)
        {
            if(marches[i]!=null)
            {
                if(cameras[i]!=null) cameras[i].RemoveCommandBuffer(MarchEvent,marches[i]);
                marches[i].Release(); marches[i]=null;
            }
            if(composites[i]!=null)
            {
                if(cameras[i]!=null) cameras[i].RemoveCommandBuffer(CompositeEvent,composites[i]);
                composites[i].Release(); composites[i]=null;
            }
            if(images[i]!=null) { Free(images[i]); images[i]=null; }
            if(frontImages[i]!=null) { Free(frontImages[i]); frontImages[i]=null; }

            cameras[i]=null;
        }
        bool occluding;
        public void Dispose()
        {
            for(int i=0;i<cameras.Length;i++) Detach(i); current=null; after=null;
            if(probe!=null) { if(Application.isPlaying) UnityEngine.Object.Destroy(probe); else UnityEngine.Object.DestroyImmediate(probe); probe=null; }
            if(probeMesh!=null) { if(Application.isPlaying) UnityEngine.Object.Destroy(probeMesh); else UnityEngine.Object.DestroyImmediate(probeMesh); probeMesh=null; }
        }
    }
}
