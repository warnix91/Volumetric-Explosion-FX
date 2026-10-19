using System;
using System.Collections.Generic;
using UnityEngine;
namespace VolumetricExplosionFX.Rendering
{
    // Lightning in the cloud of a very large explosion (charged dust and ash, as over volcanoes and nuclear clouds): jagged
    // strokes that flicker for a tenth of a second, mostly inside the cloud, sometimes down to the ground. A few shared line
    // renderers under a root that follows the floating origin.
    internal sealed class Lightning : IDisposable
    {
        sealed class Stroke { public LineRenderer Line, Halo; public float Age, Life; public bool Active; public Color Tint; public float Width; }
        readonly GameObject root;
        readonly Material material; readonly Texture2D profile;
        readonly Stroke[] strokes=new Stroke[8];
        readonly List<Vector3> points=new List<Vector3>();
        VisualRandom random=new VisualRandom(0xB017u);
        internal int Count { get; private set; }
        internal Lightning(Material glow)
        {
            root=new GameObject("VolumetricExplosionFX lightning"); root.layer=EffectLayer.Value;
            // The glow material with a profile across the stroke (bright core, soft edge) instead of its round sprite.
            profile=new Texture2D(4,32,TextureFormat.RGBA32,false); profile.wrapMode=TextureWrapMode.Clamp; profile.name="VEFX lightning profile";
            for(int y=0;y<32;y++) { float v=(y+0.5f)/32-0.5f, a=Mathf.Exp(-v*v/0.018f); for(int x=0;x<4;x++) profile.SetPixel(x,y,new Color(1,1,1,a)); }
            profile.Apply(false,true);
            material=new Material(glow); material.mainTexture=profile;
            for(int i=0;i<strokes.Length;i++)
                strokes[i]=new Stroke { Line=Line("stroke "+i), Halo=Line("glow "+i) };
        }
        LineRenderer Line(string name)
        {
            var go=new GameObject(name); go.layer=EffectLayer.Value; go.transform.SetParent(root.transform,false);
            var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=material; line.useWorldSpace=false; line.enabled=false;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows=false;
            line.numCapVertices=2; line.textureMode=LineTextureMode.Stretch; line.alignment=LineAlignment.View;
            return line;
        }
        // A stroke from a to b (world), width in metres: a jagged main channel with a branch or two.
        internal void Strike(Vector3 a,Vector3 b,float width)
        {
            Stroke s=null;
            for(int i=0;i<strokes.Length&&s==null;i++) if(!strokes[i].Active) s=strokes[i];
            if(s==null) return;
            Count++;
            points.Clear();
            Vector3 la=root.transform.InverseTransformPoint(a), lb=root.transform.InverseTransformPoint(b);
            float length=Vector3.Distance(la,lb);
            int n=10+(int)(random.Next()*6);
            for(int k=0;k<=n;k++)
            {
                float t=(float)k/n, kink=(k==0||k==n)?0:length*0.09f*(0.4f+random.Next());
                points.Add(Vector3.Lerp(la,lb,t)+random.Direction()*kink);
            }
            // A branch off the middle, back along the main channel and then away.
            int from=n/3+(int)(random.Next()*(n/3));
            Vector3 dir=(lb-la).normalized+random.Direction()*0.9f, at=points[from];
            int keep=points.Count;
            for(int k=keep-1;k>from;k--) points.Add(points[k]);
            for(int k=1;k<=4;k++) { at+=dir.normalized*length*0.08f+random.Direction()*length*0.03f; points.Add(at); }
            Vector3[] path=points.ToArray();
            s.Line.positionCount=path.Length; s.Line.SetPositions(path);
            s.Halo.positionCount=path.Length; s.Halo.SetPositions(path);
            s.Width=width; s.Age=0; s.Life=0.12f+0.1f*random.Next(); s.Active=true; s.Line.enabled=true; s.Halo.enabled=true;
            s.Tint=new Color(0.75f,0.82f,1f,1);
        }
        // Flicker: two or three pulses over the stroke's short life.
        internal void Tick(float dt)
        {
            for(int i=0;i<strokes.Length;i++)
            {
                Stroke s=strokes[i]; if(!s.Active) continue;
                s.Age+=dt;
                if(s.Age>=s.Life) { s.Active=false; s.Line.enabled=false; s.Halo.enabled=false; continue; }
                float t=s.Age/s.Life, pulse=0.55f+0.45f*Mathf.Cos(t*Mathf.PI*5);
                float a=(1-t)*pulse;
                Color c=new Color(s.Tint.r,s.Tint.g,s.Tint.b,a);
                s.Line.startColor=c; s.Line.endColor=c;
                s.Line.startWidth=s.Width*(0.7f+0.3f*pulse); s.Line.endWidth=s.Width*0.5f;
                // The cloud lit up around the channel.
                Color h=new Color(s.Tint.r*0.8f,s.Tint.g*0.85f,s.Tint.b,a*0.22f);
                s.Halo.startColor=h; s.Halo.endColor=h; s.Halo.startWidth=s.Width*9; s.Halo.endWidth=s.Width*6;
            }
        }
        // The brightest stroke now: where its flash lights the cloud (world), how bright, and how far it reaches.
        internal bool Flash(out Vector3 at,out float strength,out float range)
        {
            at=Vector3.zero; strength=0; range=0;
            for(int i=0;i<strokes.Length;i++)
            {
                Stroke s=strokes[i]; if(!s.Active) continue;
                float t=s.Age/s.Life, a=(1-t)*(0.55f+0.45f*Mathf.Cos(t*Mathf.PI*5));
                if(a<=strength) continue;
                strength=a; at=root.transform.TransformPoint(s.Line.GetPosition(s.Line.positionCount/3)); range=s.Width*28;
            }
            return strength>0;
        }
        internal void Shift(Vector3 offset) { if(root!=null) root.transform.position-=offset; }
        internal void Clear() { for(int i=0;i<strokes.Length;i++) { strokes[i].Active=false; strokes[i].Line.enabled=false; strokes[i].Halo.enabled=false; } }
        public void Dispose()
        {
            if(root!=null) UnityEngine.Object.Destroy(root);
            if(material!=null) UnityEngine.Object.Destroy(material); if(profile!=null) UnityEngine.Object.Destroy(profile);
        }
    }
}
