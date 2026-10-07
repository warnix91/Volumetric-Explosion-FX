using System;
using System.Collections.Generic;
using UnityEngine;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class TrailShower
    {
        struct Head
        {
            internal Vec3 Start, Velocity; internal double Drag, Burn, Active, Land;
            internal float Size; internal bool Landed; internal Vector3 Last;
            internal double Fade, Flare, Warm;
        }
        const float TrailShare=0.8f;
        internal readonly ParticleSystem System;
        ParticleSystem.Particle[] buffer;
        Head[] heads=new Head[0];
        int count;
        double gravity;
        bool traced;
        Color headColor;
        ParticleSystem landing;
        float rise;
        // Compensate for parent motion so trail heads remain in the air frame.
        Vec3 frameVelocity; double frameRate; bool moving;
        bool streaming;
        Color coolColor;
        internal TrailShower(Transform parent,string name,Material head,Material trail,int capacity)
        {
            var go=new GameObject(name); go.layer=EffectLayer.Value; go.transform.SetParent(parent,false);
            System=go.AddComponent<ParticleSystem>(); System.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main=System.main; main.playOnAwake=false; main.loop=false; main.duration=60;
            main.simulationSpace=ParticleSystemSimulationSpace.Local; main.maxParticles=capacity;
            main.startSpeed=0; main.startLifetime=4; main.gravityModifier=0; main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            System.useAutoRandomSeed=false; System.randomSeed=1;
            ParticleSystem.EmissionModule emission=System.emission; emission.enabled=false;
            ParticleSystem.ShapeModule shape=System.shape; shape.enabled=false;
            ParticleSystem.SizeOverLifetimeModule size=System.sizeOverLifetime; size.enabled=false;
            ParticleSystem.ColorOverLifetimeModule fade=System.colorOverLifetime; fade.enabled=false;
            ParticleSystem.TrailModule trails=System.trails; trails.enabled=true;
            trails.mode=ParticleSystemTrailMode.PerParticle; trails.ratio=1;
            trails.lifetime=new ParticleSystem.MinMaxCurve(TrailShare); trails.minVertexDistance=1.5f;
            trails.textureMode=ParticleSystemTrailTextureMode.Stretch; trails.worldSpace=false; trails.dieWithParticles=false;
            trails.inheritParticleColor=false; trails.sizeAffectsWidth=false; trails.sizeAffectsLifetime=false;
            var r=System.GetComponent<ParticleSystemRenderer>();
            r.renderMode=ParticleSystemRenderMode.Billboard; r.sharedMaterial=head; r.trailMaterial=trail;
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
        }
        internal bool Alive { get { return System!=null&&System.IsAlive(); } }
        internal int Count { get { return System!=null?System.particleCount:0; } }
        internal static void ConfigureLandingFire(ParticleSystem p,Color tint,bool sheets=false)
        {
            ParticleSystem.MainModule main=p.main;
            if(sheets)
            {
                main.startLifetime=new ParticleSystem.MinMaxCurve(2,3.5f); main.startSize=new ParticleSystem.MinMaxCurve(1.3f,2.6f);
                main.startSpeed=0; main.startRotation=0; main.startColor=new Color(1,1,1,0.9f);
                return;
            }
            main.startLifetime=new ParticleSystem.MinMaxCurve(2,4); main.startSize=new ParticleSystem.MinMaxCurve(0.9f,1.8f);
            main.startSpeed=0; main.startRotation=new ParticleSystem.MinMaxCurve(0,6.28f);
            main.startColor=new Color(tint.r,tint.g,tint.b,0.75f);
        }
        internal static ParticleSystem Ember(Transform parent,string name,Material glow)
        {
            var go=new GameObject(name); go.layer=EffectLayer.Value; go.transform.SetParent(parent,false);
            var p=go.AddComponent<ParticleSystem>(); p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main=p.main; main.playOnAwake=false; main.loop=false; main.duration=60;
            main.simulationSpace=ParticleSystemSimulationSpace.Local; main.maxParticles=32; main.gravityModifier=0;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            p.useAutoRandomSeed=false; p.randomSeed=3;
            ParticleSystem.EmissionModule emission=p.emission; emission.enabled=false;
            ParticleSystem.ShapeModule shape=p.shape; shape.enabled=false;
            ParticleSystem.ColorOverLifetimeModule fade=p.colorOverLifetime; fade.enabled=true;
            var g=new Gradient(); g.SetKeys(new[]{new GradientColorKey(new Color(1,0.85f,0.6f),0),new GradientColorKey(new Color(1,0.45f,0.2f),1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0.75f,0.6f),new GradientAlphaKey(0,1)});
            fade.color=g;
            ParticleSystem.SizeOverLifetimeModule size=p.sizeOverLifetime; size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(1,0.5f)));
            var r=p.GetComponent<ParticleSystemRenderer>(); r.renderMode=ParticleSystemRenderMode.Billboard; r.sharedMaterial=glow;
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
            return p;
        }
        internal static void ConfigureEmbers(ParticleSystem ember,ParticleSystem flames,ParticleSystem smoke,Color tint,float minLife,float maxLife,bool sheets=false)
        {
            ParticleSystem.MainModule main=ember.main;
            main.startLifetime=new ParticleSystem.MinMaxCurve(minLife,maxLife); main.startSize=new ParticleSystem.MinMaxCurve(1.2f,2.4f);
            main.startSpeed=0; main.startColor=new Color(1,0.7f,0.4f,0.9f);
            ParticleSystem.SubEmittersModule sub=ember.subEmitters; sub.enabled=true;
            if(sub.subEmittersCount==0)
            {
                sub.AddSubEmitter(flames,ParticleSystemSubEmitterType.Birth,ParticleSystemSubEmitterProperties.InheritNothing);
                sub.AddSubEmitter(smoke,ParticleSystemSubEmitterType.Birth,ParticleSystemSubEmitterProperties.InheritNothing);
            }
            if(sheets) Feed(flames,1.1f,1.5f,2.3f,1.6f,3.2f,0.02f,0.08f,new Color(1,1,1,0.85f));
            else Feed(flames,2.6f,0.8f,1.4f,1.2f,2.6f,0.8f,1.8f,new Color(tint.r,tint.g,tint.b,0.85f));
            Feed(smoke,1.1f,6,9,2,3.8f,1.4f,2.6f,new Color(tint.r,tint.g,tint.b,0.5f));
        }
        static void Feed(ParticleSystem p,float rate,float minLife,float maxLife,float minSize,float maxSize,float minSpeed,float maxSpeed,Color color)
        {
            ParticleSystem.MainModule main=p.main;
            main.startLifetime=new ParticleSystem.MinMaxCurve(minLife,maxLife); main.startSize=new ParticleSystem.MinMaxCurve(minSize,maxSize);
            main.startSpeed=new ParticleSystem.MinMaxCurve(minSpeed,maxSpeed); main.startRotation=new ParticleSystem.MinMaxCurve(0,6.28f);
            main.startColor=color;
            ParticleSystem.EmissionModule emission=p.emission; emission.enabled=true; emission.rateOverTime=rate;
            ParticleSystem.ShapeModule shape=p.shape; shape.enabled=true; shape.shapeType=ParticleSystemShapeType.Cone;
            shape.angle=15; shape.radius=0.4f; shape.rotation=new Vector3(-90,0,0);
        }
        enum Shape { Spread, Plume, Taper, Dust }
        void Look(Color smoke,double width,bool trailsOn,double step,Shape shape)
        {
            ParticleSystem.TrailModule trails=System.trails; trails.enabled=trailsOn; trails.inheritParticleColor=false;
            trails.lifetime=new ParticleSystem.MinMaxCurve(TrailShare);
            trails.minVertexDistance=(float)Math.Max(0.5,step);
            AnimationCurve w; GradientAlphaKey[] a;
            switch(shape)
            {
                case Shape.Plume:
                    w=new AnimationCurve(new Keyframe(0,0.3f),new Keyframe(0.06f,0.75f),new Keyframe(0.4f,1.25f),new Keyframe(1,2.4f));
                    a=new[]{new GradientAlphaKey(0.9f,0),new GradientAlphaKey(1,0.05f),new GradientAlphaKey(0.6f,0.4f),new GradientAlphaKey(0.25f,0.8f),new GradientAlphaKey(0,1)};
                    break;
                case Shape.Dust:
                    w=new AnimationCurve(new Keyframe(0,0.5f),new Keyframe(0.3f,1),new Keyframe(1,2.2f));
                    a=new[]{new GradientAlphaKey(0.85f,0),new GradientAlphaKey(0.55f,0.25f),new GradientAlphaKey(0,1)};
                    break;
                case Shape.Taper:
                    w=new AnimationCurve(new Keyframe(0,0.7f),new Keyframe(0.2f,1),new Keyframe(1,1.5f));
                    a=new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0.6f,0.25f),new GradientAlphaKey(0.25f,0.6f),new GradientAlphaKey(0,1)};
                    break;
                default:
                    w=new AnimationCurve(new Keyframe(0,0.12f),new Keyframe(0.15f,0.45f),new Keyframe(0.5f,1.1f),new Keyframe(1,2.2f));
                    a=new[]{new GradientAlphaKey(0.5f,0),new GradientAlphaKey(0.85f,0.04f),new GradientAlphaKey(0.42f,0.35f),new GradientAlphaKey(0.18f,0.7f),new GradientAlphaKey(0,1)};
                    break;
            }
            trails.widthOverTrail=new ParticleSystem.MinMaxCurve((float)width,w);
            var g=new Gradient();
            if(shape==Shape.Taper) g.SetKeys(new[]{new GradientColorKey(smoke,0),new GradientColorKey(new Color(smoke.r,smoke.g*0.62f,smoke.b*0.35f),0.35f),
                new GradientColorKey(new Color(smoke.r*0.85f,smoke.g*0.3f,smoke.b*0.1f),1)},a);
            else g.SetKeys(new[]{new GradientColorKey(smoke,0),new GradientColorKey(smoke,1)},a);
            trails.colorOverTrail=new ParticleSystem.MinMaxGradient(g);
        }
        void Ensure(int n) { if(heads.Length<n) heads=new Head[Math.Max(n,8)]; count=n; }
        static Vector3 V(Vec3 v) { return new Vector3((float)v.X,(float)v.Y,(float)v.Z); }
        internal void Burst(ShowerPlan plan,Color smoke,Color head,Vector3 origin,float spread,Vector3 inherited,uint seed,float? floor,ParticleSystem onLanding,float rise)
        {
            if(plan.Kind==ShowerKind.None||plan.Count<=0) return;
            Look(smoke,plan.TrailWidth,plan.Trails,plan.TrailStep>0?plan.TrailStep:1.5,
                plan.Kind==ShowerKind.Dirt?Shape.Dust:Shape.Spread);
            ParticleSystem.ForceOverLifetimeModule force=System.forceOverLifetime; force.enabled=false;
            ParticleSystem.LimitVelocityOverLifetimeModule drag=System.limitVelocityOverLifetime; drag.enabled=false;
            traced=false; streaming=false; gravity=plan.Gravity; headColor=head; landing=onLanding; this.rise=rise;
            Ensure(plan.Count);
            var rnd=new SplitRandom(seed);
            double persistence=plan.TrailLife>0?plan.TrailLife:1.6;
            System.Play();
            if(landing!=null) landing.Play();
            int clumps=Math.Max(0,Math.Min(plan.Clumps,12));
            var bundle=new Vec3[Math.Max(clumps,1)];
            for(int k=0;k<clumps;k++) bundle[k]=Direction(plan,ref rnd);
            for(int i=0;i<plan.Count;i++)
            {
                Vec3 dir=Direction(plan,ref rnd);
                if(clumps>0&&rnd.Next()<plan.ClumpShare) dir=Climb(plan,(bundle[Math.Min((int)(rnd.Next()*clumps),clumps-1)]+rnd.Direction()*0.22).Normalized(dir));
                double speed=plan.SpeedMin+(plan.SpeedMax-plan.SpeedMin)*Math.Pow(rnd.Next(),0.7);
                Vec3 p0=new Vec3(origin.x,origin.y,origin.z)+dir*(spread*rnd.Next());
                Vec3 v0=dir*speed+new Vec3(inherited.x,inherited.y,inherited.z);
                double k=plan.Drag*rnd.Range(0.75,1.3);
                double burn=plan.LifeMin+(plan.LifeMax-plan.LifeMin)*rnd.Next();
                double land=floor.HasValue?Ballistics.LandingTime(p0.Y,v0.Y,k,gravity,floor.Value,burn+1):double.PositiveInfinity;
                if(land<=0) land=double.PositiveInfinity;
                double active=Math.Min(burn,land);
                float size=(float)(plan.HeadSize*(0.6+0.8*rnd.Next()));
                heads[i]=new Head { Start=p0, Velocity=v0, Drag=k, Burn=burn, Active=active, Land=land, Size=size, Last=V(p0), Flare=-1 };
                var ep=new ParticleSystem.EmitParams();
                ep.position=V(p0); ep.velocity=Vector3.zero; ep.startSize=size; ep.startColor=head;
                ep.startLifetime=(float)Math.Max(persistence*burn/TrailShare,active+0.1);
                ep.randomSeed=(uint)i;
                System.Emit(ep,1);
            }
        }
        internal void SetFrame(Vector3 velocity,double rate)
        {
            frameVelocity=new Vec3(velocity.x,velocity.y,velocity.z); frameRate=Math.Max(rate,0);
            moving=velocity.sqrMagnitude>1e-8f;
            if(System!=null) System.transform.localPosition=Vector3.zero;
        }
        Vec3 FrameOffset(double t) { t=Math.Max(t,0); return frameVelocity*(t*Ballistics.E1(frameRate*t)); }
        internal void Stream(ReentryPlan plan,Vector3 airVelocity,double frameRate,double g,Color hot,Color cool,uint seed,bool smoke,Color smokeColor)
        {
            if(!plan.Active||plan.Count<=0) return;
            int n=smoke?Math.Min(plan.SmokeTrails,plan.Count):plan.Count;
            if(n<=0) return;
            double speed=Math.Max(airVelocity.magnitude,1);
            double trailSeconds=smoke?Numbers.Clamp(plan.GlowSeconds*0.6,2,5):Numbers.Clamp(plan.WakeLength/speed,0.08,1);
            double longest=plan.GlowSeconds*1.25, life=(longest+trailSeconds+0.3)/0.75;
            Look(smoke?smokeColor:Color.white,smoke?plan.TrailWidth*3:plan.TrailWidth,true,Math.Max(3,speed*0.025),smoke?Shape.Spread:Shape.Taper);
            ParticleSystem.TrailModule trails=System.trails; trails.inheritParticleColor=!smoke;
            trails.lifetime=new ParticleSystem.MinMaxCurve((float)(trailSeconds/life));
            ParticleSystem.ForceOverLifetimeModule force=System.forceOverLifetime; force.enabled=false;
            ParticleSystem.LimitVelocityOverLifetimeModule drag=System.limitVelocityOverLifetime; drag.enabled=false;
            SetFrame(airVelocity,frameRate);
            traced=false; streaming=true; landing=null; gravity=g; headColor=hot; coolColor=cool;
            Ensure(plan.Count);
            var rnd=new SplitRandom(seed);
            System.Play();
            double share=(double)n/plan.Count;
            int m=0;
            for(int i=0;i<plan.Count&&m<n;i++)
            {
                Vec3 d=rnd.Direction();
                Vec3 push=d*(plan.Spread*Math.Sqrt(rnd.Next()));
                double u=rnd.Next();
                double k=plan.DragMin*Math.Pow(plan.DragMax/Math.Max(plan.DragMin,1e-6),u);
                double heavy=Math.Sqrt(plan.DragMin/Math.Max(k,1e-6));
                double burn=Math.Min(plan.GlowSeconds*(0.3+0.7*heavy)*(0.8+0.4*rnd.Next()),longest);
                Vec3 p0=d*(plan.FlashSize*0.08*rnd.Next());
                float size=(float)(plan.HeadSize*(0.45+0.9*Math.Pow(heavy,0.6))*(0.8+0.4*rnd.Next()));
                double flareDraw=rnd.Next(), flareAt=rnd.Range(0.15,0.75)*burn;
                if(smoke&&u>=share) continue;
                heads[m]=new Head { Start=p0, Velocity=frameVelocity+push, Drag=k, Burn=burn, Active=burn, Land=double.PositiveInfinity,
                    Size=smoke?0.05f:size, Last=V(p0), Fade=0.6*k+0.35/Math.Max(burn,0.1), Flare=!smoke&&flareDraw<plan.FlareShare?flareAt:-1,
                    Warm=0.35*(1-heavy) };
                var ep=new ParticleSystem.EmitParams();
                ep.position=V(p0); ep.velocity=Vector3.zero; ep.startSize=heads[m].Size;
                ep.startColor=smoke?new Color(smokeColor.r,smokeColor.g,smokeColor.b,1):hot;
                ep.startLifetime=(float)(life*(0.5+0.75*heavy));
                ep.randomSeed=(uint)m;
                System.Emit(ep,1);
                m++;
            }
            count=m;
        }
        static Vec3 Direction(ShowerPlan plan,ref SplitRandom rnd)
        {
            Vec3 d=rnd.Direction();
            if(plan.Cone>0) return Climb(plan,(plan.Axis+d*(plan.Cone*rnd.Next())).Normalized(plan.Axis));
            double up=Math.Abs(d.Y)*plan.UpBias+d.Y*(1-plan.UpBias);
            return Climb(plan,new Vec3(d.X,up,d.Z).Normalized(new Vec3(0,1,0)));
        }
        static Vec3 Climb(ShowerPlan plan,Vec3 dir)
        {
            double floor=Math.Max(plan.MinUp,plan.UpBias>0.5?0.05:-1);
            return dir.Y>=floor?dir:new Vec3(dir.X,floor,dir.Z).Normalized(new Vec3(0,1,0));
        }
        internal void Trace(int n,Color smoke,double width,float[] active,Vector3[] start,Color head,float size,double persistence=1.4,bool plume=false)
        {
            Look(smoke,width,true,plume?2.5:1.5,plume?Shape.Plume:Shape.Spread);
            ParticleSystem.ForceOverLifetimeModule force=System.forceOverLifetime; force.enabled=false;
            ParticleSystem.LimitVelocityOverLifetimeModule drag=System.limitVelocityOverLifetime; drag.enabled=false;
            traced=true; streaming=false; headColor=head; landing=null; gravity=0;
            Ensure(n);
            System.Play();
            for(int i=0;i<n;i++)
            {
                heads[i]=new Head { Burn=active[i], Active=active[i], Land=double.PositiveInfinity, Size=size, Last=start[i] };
                if(active[i]<=0.05f) continue;
                var ep=new ParticleSystem.EmitParams();
                ep.position=start[i]; ep.velocity=Vector3.zero; ep.startSize=size; ep.startColor=head;
                ep.startLifetime=(float)Math.Max(persistence*active[i]/TrailShare,active[i]+0.1);
                ep.randomSeed=(uint)i;
                System.Emit(ep,1);
            }
        }
        internal void Update(float age,IList<Vector3> positions=null,IList<float> sizes=null)
        {
            if(System==null) return;
            if(moving) System.transform.localPosition=-V(FrameOffset(age));
            int n=System.particleCount;
            if(n==0) return;
            if(buffer==null||buffer.Length<n) buffer=new ParticleSystem.Particle[Math.Max(n,8)];
            n=System.GetParticles(buffer,n);
            for(int i=0;i<n;i++)
            {
                int k=(int)buffer[i].randomSeed;
                if(k<0||k>=count) continue;
                bool burning=age<heads[k].Active;
                if(streaming)
                {
                    heads[k].Last=V(Ballistics.Position(heads[k].Start,heads[k].Velocity,heads[k].Drag,gravity,Math.Min(age,heads[k].Active)));
                    buffer[i].position=heads[k].Last; buffer[i].velocity=Vector3.zero;
                    float burnt=Mathf.Clamp01((float)(age/Math.Max(heads[k].Burn,0.05)));
                    float cooled=Mathf.Max(1-(1-(float)heads[k].Warm)*Mathf.Exp(-(float)(heads[k].Fade*age)),burnt*burnt);
                    float flare=heads[k].Flare>=0?2.4f*Mathf.Exp(-Mathf.Pow((float)((age-heads[k].Flare)/0.08),2)):0;
                    Color glow=Color.Lerp(headColor,coolColor,cooled);
                    glow.a=burning?Mathf.Clamp01((1-0.7f*cooled)*Mathf.Clamp01((1-burnt)*3)*(1+flare)):0;
                    buffer[i].startColor=glow;
                    buffer[i].startSize=burning?heads[k].Size*(0.5f+0.5f*(1-cooled))*(1+flare):0;
                    continue;
                }
                if(traced) { if(burning&&positions!=null&&k<positions.Count) heads[k].Last=positions[k]; }
                else heads[k].Last=V(Ballistics.Position(heads[k].Start,heads[k].Velocity,heads[k].Drag,gravity,Math.Min(age,heads[k].Active)));
                buffer[i].position=heads[k].Last; buffer[i].velocity=Vector3.zero;
                float u=Mathf.Clamp01((float)(age/Math.Max(heads[k].Burn,0.05)));
                buffer[i].startSize=!burning?0:sizes!=null&&k<sizes.Count?sizes[k]:heads[k].Size*Shrink(u);
                Color c=Color.Lerp(headColor,new Color(headColor.r,headColor.g*0.72f,headColor.b*0.55f),u);
                c.a=headColor.a*Mathf.Clamp01((1-u)*4);
                buffer[i].startColor=c;
                if(!heads[k].Landed&&heads[k].Land<=heads[k].Burn&&age>=heads[k].Land)
                {
                    heads[k].Landed=true;
                    if(landing!=null)
                    {
                        var ep=new ParticleSystem.EmitParams(); ep.position=heads[k].Last;
                        ep.velocity=new Vector3(0,rise*(0.5f+(float)((k*0.618034)%1.0)),0);
                        landing.Emit(ep,1);
                    }
                }
            }
            System.SetParticles(buffer,n);
        }
        static float Shrink(float u) { return u<0.7f?1-0.15f*u/0.7f:Mathf.Max(0,0.85f*(1-(u-0.7f)/0.3f)); }
        internal void Clear()
        {
            if(System!=null) { System.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); System.transform.localPosition=Vector3.zero; }
            count=0; landing=null; streaming=false; moving=false; frameVelocity=new Vec3(); frameRate=0;
        }
    }
}
