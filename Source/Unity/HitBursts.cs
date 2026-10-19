using System;
using UnityEngine;
namespace VolumetricExplosionFX.Rendering
{
    // Small combat hits (cannon shells, kinetic strikes, bullet impacts): a short flash, a spray of sparks, a puff of smoke and,
    // on the ground, a kick of dust. One shared set of particle systems for every hit (local space under a root that follows
    // the floating origin), so the dozens of hits a second of a gun fight never take the place of a real explosion.
    internal sealed class HitBursts : IDisposable
    {
        readonly GameObject root;
        readonly ParticleSystem flash, sparks, smoke, dust, spout;
        readonly ParticleSystem[] all;
        VisualRandom random=new VisualRandom(0x5EED41u);
        internal int Count { get; private set; }
        internal HitBursts(ProceduralAssets assets)
        {
            root=new GameObject("VolumetricExplosionFX small hits"); root.layer=EffectLayer.Value;
            flash=BlastParticles.System(root.transform,"hit flash",assets.Glow,256);
            sparks=BlastParticles.System(root.transform,"hit sparks",assets.Glow,1536);
            smoke=BlastParticles.System(root.transform,"hit smoke",assets.Cloud,512);
            dust=BlastParticles.System(root.transform,"hit dust",assets.Cloud,384);
            spout=BlastParticles.System(root.transform,"hit spout",assets.Cloud,1024);
            BlastParticles.Render(spout,ParticleSystemRenderMode.Stretch,0.07f); BlastParticles.Brake(spout,4f);
            BlastParticles.Render(sparks,ParticleSystemRenderMode.Stretch,0.02f);
            BlastParticles.Brake(sparks,1.5f); BlastParticles.Brake(smoke,1.2f); BlastParticles.Brake(dust,2.2f);
            ParticleSystem.NoiseModule still=flash.noise; still.enabled=false;
            ParticleSystem.SizeOverLifetimeModule pop=flash.sizeOverLifetime;
            pop.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.5f),new Keyframe(0.25f,1),new Keyframe(1,1.15f)));
            ParticleSystem.SizeOverLifetimeModule shrink=sparks.sizeOverLifetime;
            shrink.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(1,0.3f)));
            all=new[]{flash,sparks,smoke,dust,spout};
            foreach(ParticleSystem p in all) { ParticleSystem.MainModule main=p.main; main.loop=true; p.Play(); }   // always running, fed by Emit
        }
        // size: radius of the burst (m); up: away from the surface hit (or the local vertical in the air); explosive: a shell
        // with a charge (flash and dark smoke) rather than a solid slug (sparks, pale dust); ground/water: what was hit below.
        internal void Hit(Vector3 world,Vector3 up,float size,bool explosive,bool ground,bool water)
        {
            Count++;
            Vector3 at=root.transform.InverseTransformPoint(world);
            up=up.sqrMagnitude>1e-6f?up.normalized:Vector3.up;
            size=Mathf.Clamp(size,0.15f,6);
            float hot=explosive?1:0.55f;
            // Brutal pass: a bigger, longer flash, more and faster sparks, a harder spout of earth.
            Emit(flash,at+up*size*0.3f,Vector3.zero,size*(explosive?3.2f:1.6f),explosive?0.17f:0.09f,new Color(1,0.86f,0.62f,0.95f*hot));
            if(explosive) Emit(flash,at+up*size*0.45f,up*size*2.5f,size*2f,0.35f,new Color(1,0.62f,0.28f,0.65f));
            int n=explosive?12+(int)(size*8):6+(int)(size*5);
            for(int i=0;i<Mathf.Min(n,40);i++)
            {
                Vector3 d=(random.Direction()+up*0.8f).normalized;
                Emit(sparks,at+up*0.05f,d*(24+52*random.Next())*Mathf.Sqrt(size),Mathf.Max(0.035f,size*(0.05f+0.06f*random.Next())),0.18f+0.35f*random.Next(),
                    new Color(1,0.7f+0.2f*random.Next(),0.35f,0.9f));
            }
            int puffs=explosive?2+(int)Mathf.Min(size*2,4):1;
            for(int i=0;i<puffs;i++)
            {
                // The burst's smoke lifts off and spreads instead of sitting on the ground as a dark ball.
                Vector3 d=(random.Direction()*0.5f+up).normalized; float puff=size*(explosive?2.2f:1.4f)*(0.7f+0.6f*random.Next());
                float grey=explosive?0.2f+0.08f*random.Next():0.5f+0.1f*random.Next();
                Emit(smoke,at+up*puff*0.45f+d*size*0.3f,d*size*(1.2f+1.5f*random.Next())+up*(0.6f+0.6f*random.Next()),puff,2.5f+2*random.Next(),new Color(grey,grey*0.95f,grey*0.9f,explosive?0.7f:0.45f));
            }
            if(ground||water)
            {
                // Earth (or spray) thrown up from the hit: pale, quick to settle.
                Color c=water?new Color(0.86f,0.9f,0.94f,0.6f):new Color(0.42f,0.36f,0.28f,0.6f);   // churned earth, darker than the surface
                // A spout of earth or spray kicked up in a narrow cone, in streaks...
                int jets=explosive?14+(int)(size*10):6+(int)(size*8);
                for(int i=0;i<Mathf.Min(jets,40);i++)
                {
                    Vector3 d=(random.Direction()*0.35f+up).normalized;
                    Emit(spout,at+up*0.05f,d*(5+8*random.Next())*Mathf.Sqrt(size+0.3f),size*(0.14f+0.14f*random.Next()),0.35f+0.45f*random.Next(),
                        new Color(c.r*0.85f,c.g*0.85f,c.b*0.85f,0.7f));
                }
                // ...then a pale cloud of dust that rises and drifts.
                for(int i=0;i<(explosive?4:2);i++)
                {
                    Vector3 d=(random.Direction()*0.6f+up).normalized; float puff=size*(1.8f+1.2f*random.Next());
                    Emit(dust,at+up*puff*0.4f,d*size*(1.5f+2*random.Next())+up*(0.4f+0.6f*random.Next()),puff,2.5f+2*random.Next(),c);
                }
            }
        }
        static void Emit(ParticleSystem p,Vector3 position,Vector3 velocity,float size,float lifetime,Color color)
        {
            var ep=new ParticleSystem.EmitParams(); ep.position=position; ep.velocity=velocity;
            ep.startSize=size; ep.startLifetime=lifetime; ep.startColor=color; p.Emit(ep,1);
        }
        // Look-development bench only (the editor does not run particle systems by itself).
        internal void Simulate(float dt) { foreach(ParticleSystem p in all) p.Simulate(dt,true,false,false); }
        internal void Shift(Vector3 offset) { if(root!=null) root.transform.position-=offset; }
        internal void SetSpeed(float speed) { foreach(ParticleSystem p in all) { ParticleSystem.MainModule main=p.main; main.simulationSpeed=speed; } }
        internal void Clear()
        {
            foreach(ParticleSystem p in all) p.Clear();
        }
        public void Dispose() { if(root!=null) UnityEngine.Object.Destroy(root); }
    }
}
