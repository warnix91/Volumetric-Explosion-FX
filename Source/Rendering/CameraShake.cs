using System;
using System.Collections.Generic;
using UnityEngine;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    // Camera shake from nearby explosions (1.1 option): a small rotation added to the view matrix of the flight cameras just
    // before they render and removed right after. The cameras' transforms (and so KSP's camera logic, vessels and physics)
    // are never touched.
    internal sealed class CameraShake : IDisposable
    {
        // A blast's jolt rides its front: it starts when the front (spreading from the burst at Shake.Speed) reaches
        // the camera where it is now, as hard as that distance makes it. The burst is kept in the body's frame (KSP moves
        // its world origin).
        struct Jolt { public double At, Amplitude, Duration, Born, Score, Air, Dome, Speed; public bool Nuclear, Started; public Vector3 Local; public Transform Frame; public float Phase; }
        readonly List<Jolt> jolts=new List<Jolt>();
        readonly List<Camera> shaken=new List<Camera>();
        Quaternion current=Quaternion.identity; bool active; int frame=-1;
        internal bool Enabled;
        internal int Count { get; private set; }
        internal CameraShake() { Camera.onPreCull+=Before; Camera.onPostRender+=After; }
        internal void Add(Vector3 at,double score,double air,double dome,bool nuclear=false)
        {
            if(!Enabled||Shake.Amplitude(score,0,air,dome,nuclear)<=0) return;
            Transform frame=FlightGlobals.currentMainBody!=null?FlightGlobals.currentMainBody.transform:null;
            if(jolts.Count>=12) jolts.RemoveAt(0);
            jolts.Add(new Jolt { Born=Time.time, Score=score, Air=air, Dome=dome, Nuclear=nuclear, Speed=Shake.Speed(air), Duration=Shake.Duration(score,nuclear),
                Local=frame!=null?frame.InverseTransformPoint(at):at, Frame=frame, Phase=(float)(at.x*0.37+at.z*0.11)%6.28f });
            Count++;
        }
        // Rumble: a few incommensurate sines per axis (no repeating pattern), mostly pitch and yaw.
        Quaternion Compute()
        {
            float pitch=0, yaw=0, roll=0, now=Time.time;
            Camera cam=FlightCamera.fetch!=null?FlightCamera.fetch.mainCamera:Camera.main;
            for(int i=jolts.Count-1;i>=0;i--)
            {
                Jolt j=jolts[i];
                if(!j.Started)
                {
                    if(cam==null) continue;
                    Vector3 at=j.Frame!=null?j.Frame.TransformPoint(j.Local):j.Local;
                    double distance=Vector3.Distance(cam.transform.position,at), front=(now-j.Born)*j.Speed;
                    if(front<distance)
                    {
                        // Still on its way, unless it has died out before reaching the camera.
                        if(Shake.Amplitude(j.Score,front,j.Air,j.Dome,j.Nuclear)<=0) jolts.RemoveAt(i);
                        continue;
                    }
                    j.Started=true; j.At=now; j.Amplitude=Shake.Amplitude(j.Score,distance,j.Air,j.Dome,j.Nuclear);
                    if(j.Amplitude<=0) { jolts.RemoveAt(i); continue; }
                    jolts[i]=j;
                }
                double t=now-j.At;
                if(t>j.Duration) { jolts.RemoveAt(i); continue; }
                float e=(float)(j.Amplitude*Shake.Envelope(t,j.Duration)); if(e<=0) continue;
                float u=(float)t, p=j.Phase;
                pitch+=e*(0.6f*Mathf.Sin(u*71.3f+p)+0.4f*Mathf.Sin(u*113.7f+2.1f*p));
                yaw+=e*(0.6f*Mathf.Sin(u*83.9f+1.3f*p)+0.4f*Mathf.Sin(u*131.1f+0.7f*p));
                roll+=e*0.3f*Mathf.Sin(u*97.1f+3.1f*p);
            }
            return Quaternion.Euler(pitch,yaw,roll);
        }
        // Which cameras shake, cached by instance (reading a camera's name allocates a string every time).
        readonly Dictionary<int,bool> backdrop=new Dictionary<int,bool>();
        bool Shakes(Camera c)
        {
            if(c==null||c.orthographic||c.targetTexture!=null) return false;
            if(CameraRig.IsFlight(c)) return true;
            int id=c.GetInstanceID(); bool yes;
            if(!backdrop.TryGetValue(id,out yes)) { string n=c.name; yes=n=="Camera ScaledSpace"||n=="GalaxyCamera"; backdrop[id]=yes; }   // the sky and planet backdrop move with the near scene
            return yes;
        }
        void Before(Camera c)
        {
            if(!Enabled||jolts.Count==0||Time.timeScale<=0||!Shakes(c)) return;
            if(Time.frameCount!=frame) { frame=Time.frameCount; current=jolts.Count>0?Compute():Quaternion.identity; active=Quaternion.Angle(current,Quaternion.identity)>0.001f; }
            if(!active) return;
            c.worldToCameraMatrix=Matrix4x4.Rotate(current)*c.worldToCameraMatrix;
            shaken.Add(c);
        }
        void After(Camera c)
        {
            int i=shaken.IndexOf(c); if(i<0) return;
            shaken.RemoveAt(i); c.ResetWorldToCameraMatrix();
        }
        internal void Clear() { jolts.Clear(); }
        public void Dispose()
        {
            Camera.onPreCull-=Before; Camera.onPostRender-=After;
            for(int i=0;i<shaken.Count;i++) if(shaken[i]!=null) shaken[i].ResetWorldToCameraMatrix();
            shaken.Clear(); jolts.Clear();
        }
    }
}
