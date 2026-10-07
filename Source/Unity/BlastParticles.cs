using System;
using System.Collections.Generic;
using UnityEngine;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class BlastMaterials
    {
        internal Material Glow, Cloud, Debris, Trail, SmokeFire, SmokeCloud, TrailSmoke, TrailGlow, Flame;
        internal Mesh Shard; internal bool HeatDebris;
    }
    internal struct BlastContext
    {
        internal FxPlan Plan; internal BlastLayout Layout; internal FxSettings Settings; internal EventKind Kind;
        internal bool Ground, Water, Surface, Dome;
        internal float Air, DomeRadius, Gravity, ImpactSpeed;
        internal Vector3 Velocity;      // the part's velocity relative to the air (surface velocity), effect frame
        internal float? Floor;          // ground under a blast in the air (effect frame); null when out of reach
        internal float Sunlit;          // 0 in the shadow of a body, 1 in full starlight
        internal double GasRate;        // in flight: rate (1/s) at which the gas gives the vehicle's velocity up to the air (0: never)
        internal bool FloorWet;         // that floor is the sea
        internal int DebrisBudget; internal uint Seed;
    }
    internal sealed class BlastParticles
    {
        readonly ParticleSystem fire, smoke, fragments, spray, embers, chunks, ring;
        readonly ParticleSystem burst, cloud;            // lit dust and spray; the cloud left by the fireball
        readonly TrailShower shower, heavy, runaway;     // burning fragments, heavy chunks, runaway booster
        readonly TrailShower plasma, plasmaSmoke;        // re-entry breakup: glowing fragments, smoke of the heaviest
        readonly ParticleSystem wake, sparks;            // re-entry breakup: glowing wake and sparks left in the air
        readonly TrailShower dirt;                       // clods thrown out of the crater, streaking dust
        readonly ParticleSystem splash;                  // white column thrown up by a water impact
        readonly Transform sparkGround;                  // plane the sparks bounce on
        Vector3 wind;                                    // breeze of this event (m/s), shared with the residue
        Color dust;                                      // colour of the body's dust
        readonly ParticleSystem landingFire, ember, emberFlames, emberSmoke;
        readonly ParticleSystem groundFlames;            // procedural flames of fuel spilled on the ground (no residue)
        readonly bool heatDebris, sheets;
        readonly AftermathPuff[] puffs=new AftermathPuff[48];
        readonly List<Vector3> runawayAt=new List<Vector3>();
        readonly List<float> runawaySize=new List<float>();
        RunawayPath[] paths;
        VisualRandom random;
        BlastContext c;
        float nextPuff, nextFlame, cloudStart, afterStart;
        int remainingPuffs, afterLeft;
        bool active, cloudDone, reentry;
        internal int ReservedDebris { get; private set; }
        internal float Horizon { get; private set; }
        internal bool Pending { get { return active&&(remainingPuffs>0||!cloudDone&&!float.IsInfinity(cloudStart)); } }
        internal BlastParticles(Transform gas,Transform solid,BlastMaterials m,FxSettings settings)
        {
            heatDebris=m.HeatDebris;
            fire=System(gas,"hot lobes and ground fire",m.Glow,settings.ParticlesPerEvent);
            smoke=System(gas,"cooling cloud",m.Cloud,settings.ParticlesPerEvent);
            spray=System(gas,"ejecta and spray",m.Cloud,settings.ParticlesPerEvent);
            embers=System(gas,"embers",m.Glow,settings.ParticlesPerEvent);
            ring=System(gas,"shock dust ring",m.Cloud,96);
            fragments=System(solid,"visual debris",m.Debris,settings.ParticlesPerEvent);
            chunks=System(solid,"burning chunks",m.Glow,16);
            ParticleSystemRenderer renderer=fragments.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode=ParticleSystemRenderMode.Mesh; renderer.mesh=m.Shard;
            ParticleSystem.RotationOverLifetimeModule spin=fragments.rotationOverLifetime; spin.enabled=true; spin.separateAxes=true;
            spin.x=new ParticleSystem.MinMaxCurve(-6,6); spin.y=new ParticleSystem.MinMaxCurve(-6,6); spin.z=new ParticleSystem.MinMaxCurve(-6,6);
            ParticleSystem.SizeOverLifetimeModule rigid=fragments.sizeOverLifetime; rigid.enabled=true;
            rigid.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(0.88f,1),new Keyframe(1,0)));
            ParticleSystem.MainModule shape3D=fragments.main; shape3D.startSize3D=true; // varied silhouettes from one mesh
            ParticleSystem.ColorOverLifetimeModule solidFade=fragments.colorOverLifetime; Gradient g=new Gradient();
            g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                heatDebris?new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0.35f,0.25f),new GradientAlphaKey(0,0.55f)}:
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,0.85f),new GradientAlphaKey(0,1)}); solidFade.color=g;
            ParticleSystem.SizeOverLifetimeModule emberSize=embers.sizeOverLifetime;
            emberSize.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(1,0.4f)));
            ParticleSystem.NoiseModule wander=embers.noise; wander.strength=0.6f; wander.frequency=0.4f;
            ParticleSystem.SizeOverLifetimeModule chunkSize=chunks.sizeOverLifetime;
            chunkSize.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(0.8f,0.8f),new Keyframe(1,0.2f)));
            ParticleSystem.NoiseModule chunkNoise=chunks.noise; chunkNoise.enabled=false;
            ParticleSystem.ColorOverLifetimeModule chunkFade=chunks.colorOverLifetime;
            Gradient chunkGradient=new Gradient(); chunkGradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,0.55f,0.3f),1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,0.7f),new GradientAlphaKey(0,1)});
            chunkFade.color=chunkGradient;
            ParticleSystem.TrailModule trail=chunks.trails;
            trail.enabled=false; trail.mode=ParticleSystemTrailMode.PerParticle; trail.ratio=1;
            trail.lifetime=new ParticleSystem.MinMaxCurve(0.45f); trail.minVertexDistance=0.35f;
            trail.textureMode=ParticleSystemTrailTextureMode.Tile; trail.worldSpace=false; trail.dieWithParticles=false;
            trail.inheritParticleColor=false; trail.sizeAffectsWidth=true;
            trail.widthOverTrail=new ParticleSystem.MinMaxCurve(3.2f,new AnimationCurve(new Keyframe(0,0.45f),new Keyframe(1,1.6f)));
            chunks.GetComponent<ParticleSystemRenderer>().trailMaterial=m.Trail;
            ParticleSystem.NoiseModule ringNoise=ring.noise; ringNoise.strength=0.4f; ringNoise.frequency=0.5f;
            ParticleSystem.SizeOverLifetimeModule ringSize=ring.sizeOverLifetime;
            ringSize.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.4f),new Keyframe(0.35f,1),new Keyframe(1,1.7f)));
            ParticleSystem.ColorOverLifetimeModule ringFade=ring.colorOverLifetime;
            Gradient ringGradient=new Gradient(); ringGradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.06f),new GradientAlphaKey(0.55f,0.45f),new GradientAlphaKey(0,1)});
            ringFade.color=ringGradient;
            if(m.Flame!=null) groundFlames=SmokeParticles.CreateFlameSheets(gas,"ground flames",m.Flame,64);
            sheets=m.Flame!=null;
            var plane=new GameObject("spark ground"); plane.layer=EffectLayer.Value; plane.transform.SetParent(embers.transform,false); sparkGround=plane.transform;
            if(m.SmokeCloud!=null)
            {
                splash=SmokeParticles.Create(gas,"splash column",m.SmokeCloud,48,0.3f,1);
                burst=SmokeParticles.Create(gas,"dust and spray",m.SmokeCloud,Math.Min(160,settings.ParticlesPerEvent+32),0.3f,1);
                ParticleSystem.SizeOverLifetimeModule spread=burst.sizeOverLifetime;
                spread.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.6f),new Keyframe(0.3f,1),new Keyframe(1,1.7f)));
                cloud=SmokeParticles.Create(gas,"lingering cloud",m.SmokeCloud,puffs.Length,0.35f,0.9f);
                ParticleSystem.SizeOverLifetimeModule grow=cloud.sizeOverLifetime;
                grow.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.9f),new Keyframe(0.3f,1.3f),new Keyframe(1,2.1f)));
                ParticleSystem.ColorOverLifetimeModule fade=cloud.colorOverLifetime;
                var cg=new Gradient(); cg.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                    new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.09f),new GradientAlphaKey(0.7f,0.5f),new GradientAlphaKey(0,1)});
                fade.color=cg;
                ParticleSystem.NoiseModule drift=cloud.noise; drift.strength=0.25f; drift.frequency=0.08f;
            }
            if(m.TrailSmoke!=null)
            {
                shower=new TrailShower(gas,"burning fragments",m.Glow,m.TrailSmoke,256);
                heavy=new TrailShower(gas,"heavy burning chunks",m.Glow,m.TrailSmoke,24);
                runaway=new TrailShower(gas,"runaway booster",m.Glow,m.TrailSmoke,4);
                if(m.TrailGlow!=null)
                {
                    plasma=new TrailShower(gas,"glowing fragments",m.Glow,m.TrailGlow,96);
                    plasma.System.GetComponent<ParticleSystemRenderer>().minParticleSize=0.0025f;
                    wake=System(plasma.System.transform,"plasma wake",m.Glow,32);
                    sparks=System(plasma.System.transform,"re-entry sparks",m.Glow,160);
                    Brake(wake,3); Brake(sparks,1.6f);
                    Render(sparks,ParticleSystemRenderMode.Stretch,0.015f);
                    sparks.GetComponent<ParticleSystemRenderer>().minParticleSize=0.001f;
                    ParticleSystem.NoiseModule still=sparks.noise; still.enabled=false;
                    ParticleSystem.SizeOverLifetimeModule shrink=sparks.sizeOverLifetime;
                    shrink.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(1,0.3f)));
                }
                plasmaSmoke=new TrailShower(gas,"re-entry smoke trails",m.Glow,m.TrailSmoke,24);
                dirt=new TrailShower(gas,"dirt jets",m.Glow,m.TrailSmoke,48);
                if(m.SmokeFire!=null&&m.SmokeCloud!=null)
                {
                    landingFire=m.Flame!=null?SmokeParticles.CreateFlameSheets(shower.System.transform,"landing fires",m.Flame,64):
                        SmokeParticles.Create(shower.System.transform,"landing fires",m.SmokeFire,96,0,0.85f);
                    ember=TrailShower.Ember(heavy.System.transform,"burning chunks on the ground",m.Glow);
                    emberFlames=m.Flame!=null?SmokeParticles.CreateFlameSheets(ember.transform,"chunk flames",m.Flame,128):
                        SmokeParticles.Create(ember.transform,"chunk flames",m.SmokeFire,160,0,0.8f);
                    emberSmoke=SmokeParticles.Create(ember.transform,"chunk smoke",m.SmokeCloud,200,0.35f,1);
                }
            }
        }
        static void Render(ParticleSystem p,ParticleSystemRenderMode mode,float velocityScale)
        {
            ParticleSystemRenderer r=p.GetComponent<ParticleSystemRenderer>();
            r.renderMode=mode; r.lengthScale=1; r.velocityScale=velocityScale;
        }
        static void Brake(ParticleSystem p,float drag)
        {
            ParticleSystem.LimitVelocityOverLifetimeModule b=p.limitVelocityOverLifetime; b.enabled=true;
            b.limit=1e5f; b.dampen=0; b.drag=drag; b.multiplyDragByParticleSize=false; b.multiplyDragByParticleVelocity=false;
        }
        static ParticleSystem System(Transform parent,string name,Material material,int capacity)
        {
            GameObject go=new GameObject(name); go.layer=EffectLayer.Value; go.transform.SetParent(parent,false);
            ParticleSystem p=go.AddComponent<ParticleSystem>(); p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main=p.main; main.playOnAwake=false; main.loop=false; main.duration=15;
            main.simulationSpace=ParticleSystemSimulationSpace.Local; main.maxParticles=capacity;
            main.startSpeed=0; main.startLifetime=1; main.gravityModifier=0;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            p.useAutoRandomSeed=false; p.randomSeed=1;
            ParticleSystem.EmissionModule emission=p.emission; emission.enabled=false;
            ParticleSystem.ShapeModule shape=p.shape; shape.enabled=false;
            ParticleSystem.CollisionModule collision=p.collision; collision.enabled=false;
            ParticleSystem.NoiseModule noise=p.noise; noise.enabled=true; noise.strength=0.2f; noise.frequency=0.6f;
            ParticleSystem.ColorOverLifetimeModule fade=p.colorOverLifetime; fade.enabled=true;
            Gradient gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.04f),new GradientAlphaKey(0.65f,0.5f),new GradientAlphaKey(0,1)});
            fade.color=gradient;
            ParticleSystem.SizeOverLifetimeModule size=p.sizeOverLifetime; size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.6f),new Keyframe(0.7f,1.4f),new Keyframe(1,1.8f)));
            ParticleSystemRenderer r=p.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial=material;
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
            return p;
        }
        IEnumerable<ParticleSystem> Roots()
        {
            yield return fire; yield return smoke; yield return fragments; yield return spray; yield return embers; yield return chunks; yield return ring;
            if(burst!=null) yield return burst;
            if(cloud!=null) yield return cloud;
            if(shower!=null) { yield return shower.System; yield return heavy.System; yield return runaway.System; }
            if(plasma!=null) yield return plasma.System;
            if(plasmaSmoke!=null) yield return plasmaSmoke.System;
            if(dirt!=null) yield return dirt.System;
            if(splash!=null) yield return splash;
            if(groundFlames!=null) yield return groundFlames;
        }
        internal void Begin(BlastContext context)
        {
            Clear(); c=context; active=true;
            random=new VisualRandom(c.Seed);
            uint s=c.Seed;
            fire.randomSeed=s+1; smoke.randomSeed=s+2; fragments.randomSeed=s+3; spray.randomSeed=s+4;
            embers.randomSeed=s+5; chunks.randomSeed=s+6; ring.randomSeed=s+7;
            foreach(ParticleSystem p in Roots()) if(p!=shower?.System&&p!=heavy?.System&&p!=runaway?.System&&p!=plasma?.System&&p!=plasmaSmoke?.System&&p!=dirt?.System) p.Play();
            Vec3 breeze=Aftermath.Wind(c.Seed,c.Air); wind=new Vector3((float)breeze.X,0,(float)breeze.Z);
            dust=new Color((float)c.Plan.DustR,(float)c.Plan.DustG,(float)c.Plan.DustB,1);
            FxPlan plan=c.Plan; FxSettings settings=c.Settings;
            float radius=(float)plan.Radius, speed=(float)plan.ExpansionSpeed;
            bool burning=settings.Explosions&&plan.FireFraction>0.05;
            nextPuff=0.12f; nextFlame=0.35f; remainingPuffs=plan.PuffCount;
            double handover=Aftermath.Start(plan,c.Layout!=null?c.Layout.Tempo:1);
            cloudStart=c.Dome&&settings.Explosions&&cloud!=null&&!double.IsInfinity(handover)?(float)handover:float.PositiveInfinity;
            afterStart=!c.Dome&&cloud!=null&&c.Air>0.05f&&settings.Explosions?1.1f+0.6f*random.Next():float.PositiveInfinity;
            afterLeft=(int)Mathf.Clamp(3+(float)plan.VisualEnergyScore,3,9);
            Horizon=6;
            reentry=Reentry.Applies(plan,c.Surface,plan.ImpactSpeed);
            if(reentry) { EmitReentry(); afterStart=float.PositiveInfinity; remainingPuffs=0; }
            else if(!c.Dome)
            {
                float flashSize=radius*(burning?1.6f:0.7f);
                Emit(fire,Lift(Vector3.zero,flashSize),Vector3.zero,flashSize,0.22f,new Color(1,0.9f,0.72f,burning?0.9f:0.45f));
                if(burning) for(int i=0;i<3;i++)
                    Emit(fire,random.Direction()*radius*0.12f,random.Direction()*speed*0.6f,radius*(0.2f+random.Next()*0.25f),
                        0.2f+random.Next()*0.3f,new Color(1,0.82f,0.55f,(float)plan.FireFraction));
                int count=3+(int)Math.Min(5,plan.VisualEnergyScore);
                for(int i=0;i<count;i++)
                {
                    Vector3 d=Upward(random.Direction()); float puff=radius*(0.35f+random.Next()*0.35f);
                    Emit(smoke,Lift(d*radius*0.15f,puff),d*speed*0.25f+Vector3.up*0.8f,puff,1.2f+random.Next(),
                        new Color(0.68f,0.66f,0.62f,0.32f));
                }
            }
            if(settings.Burst&&plan.ElectricFraction>0.01)
                for(int i=0;i<6;i++) Emit(fire,Vector3.zero,random.Direction()*5,0.035f,0.12f+random.Next()*0.15f,
                    new Color(0.65f,0.8f,1,(float)plan.ElectricFraction));
            if(!reentry) EmitEmbers(burning,radius,speed);
            EmitDebris(radius,speed);
            if(!reentry) { if(shower!=null) EmitShower(); else EmitChunks(burning); }
            if(c.Kind==EventKind.GroundImpact&&settings.Ground || c.Kind==EventKind.WaterImpact&&settings.Water)
            {
                EmitSurface(radius,speed);
                if(burst!=null) EmitBurst();
            }
            if(settings.Shockwave&&(c.Ground||c.Water)&&c.Air>0.15f) EmitShockRing();
            if(!reentry&&!c.Surface&&c.Air<0.02f&&burst!=null) EmitVacuum(radius,speed);
        }
        Color Dust(float light,float alpha) { return new Color(Mathf.Min(1,dust.r*light),Mathf.Min(1,dust.g*light),Mathf.Min(1,dust.b*light),alpha); }
        void EmitVacuum(float radius,float speed)
        {
            FxPlan plan=c.Plan;
            ParticleSystem.ForceOverLifetimeModule fall=burst.forceOverLifetime; fall.enabled=false;
            ParticleSystem.LimitVelocityOverLifetimeModule brake=burst.limitVelocityOverLifetime; brake.enabled=false;
            float reach=c.DomeRadius;
            if(plan.BurnableKg>30||plan.VaporFraction>0.05)
            {
                int n=(int)Mathf.Clamp(8+(float)plan.VisualEnergyScore*0.5f,8,12);
                float lit=Mathf.Clamp01(c.Sunlit);
                for(int i=0;i<n;i++)
                {
                    Vector3 d=random.Direction();
                    Emit(smoke,d*reach*0.08f,d*reach*(0.55f+0.45f*random.Next()),reach*(1.5f+0.8f*random.Next()),2.5f+1.5f*random.Next(),
                        new Color(0.92f*lit,0.95f*lit,lit,0.2f*lit));
                }
            }
            int specks=(int)Mathf.Clamp(40+(float)plan.VisualEnergyScore*10,40,110);
            for(int i=0;i<specks;i++)
            {
                Vector3 d=random.Direction();
                SmokeParticles.Emit(burst,d*radius*0.1f,d*(speed*(0.5f+1.5f*random.Next())+5),0.25f+0.35f*random.Next(),3+3*random.Next(),
                    new Color(0.82f,0.8f,0.78f,0.9f),random.Next()*360);
            }
            Horizon=Mathf.Max(Horizon,8);
        }
        void EmitSplashColumn(float reach,float strength)
        {
            float height=(float)Splash.ColumnHeight(c.Plan), g=Mathf.Max(c.Gravity,1)*0.75f;
            if(height<=0) return;
            ParticleSystem.ForceOverLifetimeModule fall=splash.forceOverLifetime; fall.enabled=true;
            fall.space=ParticleSystemSimulationSpace.Local; fall.x=wind.x*0.3f; fall.y=-g; fall.z=wind.z*0.3f;
            ParticleSystem.LimitVelocityOverLifetimeModule brake=splash.limitVelocityOverLifetime; brake.enabled=true;
            brake.limit=1e5f; brake.dampen=0; brake.drag=0.15f; brake.multiplyDragByParticleSize=false; brake.multiplyDragByParticleVelocity=false;
            splash.Play();
            Render(splash,ParticleSystemRenderMode.Stretch,0.14f);
            int n=(int)Mathf.Clamp(30+12*strength,30,44);
            float width=Mathf.Clamp(height*0.15f,2,Mathf.Max(2,reach*0.6f));
            for(int i=0;i<n;i++)
            {
                float h=height*(0.12f+0.88f*Mathf.Sqrt(random.Next()));
                float v=Mathf.Sqrt(2*g*h)*1.25f;   // the drag takes the rest
                Vector3 radial=random.Direction(); radial.y=0;
                float size=width*(1.2f+0.6f*random.Next())*(0.75f+0.5f*h/height);
                SmokeParticles.Emit(splash,Lift(radial*width*0.15f,size),Vector3.up*v+radial*width*(0.05f+0.2f*h/height),size,Mathf.Min(2*v/g*0.85f+1,9),
                    new Color(0.95f,0.97f,1,0.5f),random.Next()*360);
            }
            Horizon=Mathf.Max(Horizon,11);
        }
        void EmitReentry()
        {
            FxPlan plan=c.Plan;
            ReentryPlan rp=Reentry.Plan(plan,c.Surface,plan.ImpactSpeed,c.Settings.ParticlesPerEvent);
            if(!rp.Active) return;
            float airSpeed=(float)plan.ImpactSpeed;
            Vector3 dir=c.Velocity.sqrMagnitude>1e-6f?c.Velocity.normalized:Vector3.forward;
            float flash=(float)rp.FlashSize, flashLife=(float)rp.FlashSeconds;
            Emit(fire,Vector3.zero,Vector3.zero,flash,flashLife,new Color(1,0.97f,0.9f,1));
            Emit(fire,Vector3.zero,Vector3.zero,flash*2.2f,flashLife*1.8f,new Color(1,0.62f,0.4f,0.45f));
            Horizon=Mathf.Max(Horizon,flashLife*1.8f+1);
            if(plasma==null) return;
            uint seed=(uint)(random.Next()*16777216f)^0x9A5Au;
            plasma.Stream(rp,c.Velocity,c.GasRate,c.Gravity,new Color(1,0.97f,0.9f,1),new Color(0.9f,0.25f,0.06f,1),seed,false,default(Color));
            if(rp.SmokeTrails>0&&plasmaSmoke!=null)
                plasmaSmoke.Stream(rp,c.Velocity,c.GasRate,c.Gravity,Color.white,Color.white,seed,true,new Color(0.72f,0.71f,0.69f,1));
            wake.Play(); sparks.Play();
            for(int i=0;i<16;i++)
            {
                Vector3 d=random.Direction();
                Emit(wake,d*flash*0.25f*random.Next(),dir*airSpeed*(0.05f+0.25f*random.Next())+d*(float)rp.Spread*0.3f,
                    flash*(0.3f+0.4f*random.Next()),0.6f+1.2f*random.Next(),new Color(1,0.5f+0.25f*random.Next(),0.3f+0.15f*random.Next(),0.45f));
            }
            int count=(int)Mathf.Clamp(50+(float)plan.VisualEnergyScore*10,50,Mathf.Min(150,c.Settings.ParticlesPerEvent));
            for(int i=0;i<count;i++)
            {
                Vector3 d=random.Direction();
                Emit(sparks,d*flash*0.1f*random.Next(),dir*airSpeed*(0.3f+0.65f*random.Next())+d*(float)rp.Spread*(0.4f+0.8f*random.Next()),
                    0.15f+0.3f*random.Next(),0.6f+1.4f*random.Next(),new Color(1,0.6f+0.35f*random.Next(),0.3f+0.2f*random.Next(),1));
            }
            Horizon=Mathf.Max(Horizon,(float)(rp.GlowSeconds*1.25+(rp.SmokeTrails>0?6.5:1.5)+1));
        }
        void EmitDirt()
        {
            ShowerPlan dp=Shower.Dirt(c.Plan,c.Gravity,c.Settings.ParticlesPerEvent,new Vec3(c.Velocity.x,c.Velocity.y,c.Velocity.z));
            if(dp.Kind==ShowerKind.None) return;
            dirt.Burst(dp,Dust(c.Air>0.05f?0.7f:0.85f,1),Dust(0.5f,1),Vector3.up*0.5f,(float)Math.Max(0.5,c.Plan.Radius*0.25),Vector3.zero,
                (uint)(random.Next()*16777216f)^0xD127u,0f,null,0);
            Horizon=Mathf.Max(Horizon,(float)(dp.LifeMax*dp.TrailLife/0.8)+2);
        }
        void EmitBurst()
        {
            bool water=c.Water;
            float reach=c.DomeRadius, strength=(float)Numbers.Clamp(c.Plan.VisualEnergyScore/6,0.25,1.3);
            if(c.Air<0.05f&&!water) return;
            ParticleSystem.ForceOverLifetimeModule fall=burst.forceOverLifetime; fall.enabled=true;
            fall.space=ParticleSystemSimulationSpace.Local; fall.x=0; fall.z=0;
            fall.y=water?-0.6f:-0.15f;   // mist and fine dust barely settle within their life
            float drag=water?1.2f:1.4f;
            fall.x=wind.x*drag; fall.z=wind.z*drag;
            ParticleSystem.LimitVelocityOverLifetimeModule brake=burst.limitVelocityOverLifetime; brake.enabled=true;
            brake.limit=1e5f; brake.dampen=0; brake.drag=drag;
            brake.multiplyDragByParticleSize=false; brake.multiplyDragByParticleVelocity=false;
            var tint=water?new Color(0.9f,0.93f,0.97f,1):Dust(1.2f,1);
            float opacity=Mathf.Min(1,strength+0.35f), linger=water?1:0.8f+0.8f*Mathf.Min(1,strength);
            int ringCount=(int)Mathf.Clamp(14+8*strength,14,22);
            for(int i=0;i<ringCount;i++)
            {
                float a=(i+random.Next()*0.8f)/ringCount*Mathf.PI*2;
                var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                float size=reach*(water?1.1f+0.5f*random.Next():0.9f+0.5f*random.Next());
                Vector3 velocity=radial*reach*(water?0.15f+0.2f*random.Next():0.25f+0.25f*random.Next())+Vector3.up*(0.3f+0.7f*random.Next());
                SmokeParticles.Emit(burst,Lift(radial*reach*(0.1f+0.2f*random.Next()),size),velocity,size,((water?5:6)+4*random.Next())*linger,
                    new Color(tint.r,tint.g,tint.b,(water?0.16f:0.2f)*opacity),random.Next()*360);
            }
            if(water&&splash!=null) EmitSplashColumn(reach,strength);
            int plumeCount=water&&splash!=null?0:(int)Mathf.Clamp(3+3*strength,3,6);
            for(int i=0;i<plumeCount;i++)
            {
                Vector3 d=random.Direction(); d.y=Mathf.Abs(d.y)*2.5f+1; d.Normalize();
                float size=reach*(0.8f+0.4f*random.Next());
                float up=water?Mathf.Sqrt(c.ImpactSpeed+20)*(0.5f+0.6f*random.Next()):(2+4*strength)*(0.4f+0.8f*random.Next());
                SmokeParticles.Emit(burst,Lift(d*reach*0.1f,size),d*up,size,((water?5:8)+3*random.Next())*linger,
                    new Color(tint.r,tint.g,tint.b,0.3f*opacity),random.Next()*360);
            }
            Horizon=Mathf.Max(Horizon,(water?16:11)*linger+2);
        }
        void EmitEjecta(float reach,float strength)
        {
            ParticleSystem.ForceOverLifetimeModule fall=burst.forceOverLifetime; fall.enabled=true;
            fall.space=ParticleSystemSimulationSpace.Local; fall.x=0; fall.z=0; fall.y=-c.Gravity;
            ParticleSystem.LimitVelocityOverLifetimeModule brake=burst.limitVelocityOverLifetime; brake.enabled=false;
            int n=(int)Mathf.Clamp(40+30*strength,40,80);
            float launch=c.ImpactSpeed*0.25f+8;
            for(int i=0;i<n;i++)
            {
                float a=random.Next()*Mathf.PI*2, elevation=(35+30*random.Next())*Mathf.Deg2Rad, v=launch*(0.5f+random.Next());
                var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                float size=reach*(0.08f+0.08f*random.Next());
                SmokeParticles.Emit(burst,Lift(radial*reach*0.1f,size),radial*v*Mathf.Cos(elevation)+Vector3.up*v*Mathf.Sin(elevation),size,
                    2.5f+2.5f*random.Next(),Dust(1.15f+0.25f*random.Next(),0.45f*Mathf.Min(1,strength+0.35f)),random.Next()*360);
            }
            Horizon=Mathf.Max(Horizon,7);
        }
        void EmitEmbers(bool burning,float radius,float speed)
        {
            FxPlan plan=c.Plan; float air=c.Air;
            ParticleSystem.LimitVelocityOverLifetimeModule drag=embers.limitVelocityOverLifetime;
            drag.enabled=air>0.05f; drag.limit=8; drag.dampen=0.08f;
            Render(embers,air>0.05f?ParticleSystemRenderMode.Stretch:ParticleSystemRenderMode.Billboard,0.05f);
            ParticleSystem.ForceOverLifetimeModule sink=embers.forceOverLifetime;
            sink.enabled=true; sink.space=ParticleSystemSimulationSpace.Local; sink.x=0; sink.z=0;
            sink.y=c.Surface||air>0.02f?-c.Gravity*(air>0.05f?0.7f:1):0;
            float? floor=c.Surface?0f:(c.Floor.HasValue&&air>0.3f?c.Floor:null);
            ParticleSystem.CollisionModule bounce=embers.collision; bounce.enabled=floor.HasValue;
            if(floor.HasValue)
            {
                sparkGround.localPosition=new Vector3(0,floor.Value,0); sparkGround.localRotation=Quaternion.identity;
                bounce.type=ParticleSystemCollisionType.Planes; bounce.SetPlane(0,sparkGround);
                bounce.bounce=0.35f; bounce.dampen=0.45f; bounce.lifetimeLoss=0.25f; bounce.radiusScale=0.5f;
            }
            int count=(int)Numbers.Clamp(burning||c.Dome?20+plan.VisualEnergyScore*14:4+plan.VisualEnergyScore*3,4,Math.Min(140,c.Settings.ParticlesPerEvent));
            float size=Mathf.Clamp(radius*0.025f,0.05f,0.3f);
            int sheaves=3+(int)(random.Next()*4);
            var sheaf=new Vector3[6];
            for(int k=0;k<sheaves;k++) { sheaf[k]=random.Direction(); if(c.Surface) sheaf[k].y=Mathf.Abs(sheaf[k].y)*0.8f+0.3f; }
            for(int i=0;i<count;i++)
            {
                Vector3 d=random.Direction();
                if(random.Next()<0.6f) d=(sheaf[Mathf.Min((int)(random.Next()*sheaves),sheaves-1)].normalized+random.Direction()*0.3f).normalized;
                if(c.Surface&&d.y<0) d.y=-d.y*0.3f;
                float life=air>0.05f?1+random.Next()*2:0.8f+random.Next()*0.8f;
                Emit(embers,d*radius*0.15f,d*speed*(0.4f+random.Next())+Vector3.up*speed*0.25f*air,size*(0.6f+random.Next()),
                    life,new Color(1,0.55f+random.Next()*0.3f,0.22f,1));
            }
        }
        void EmitDebris(float radius,float speed)
        {
            FxPlan plan=c.Plan;
            ReservedDebris=c.Settings.Debris?Math.Min(plan.DebrisCount,c.DebrisBudget):0;
            float chip=Mathf.Clamp(radius*0.03f,0.05f,0.4f);
            for(int i=0;i<ReservedDebris;i++)
            {
                Vector3 d=random.Direction(); if(c.Surface&&d.y<0) d.y=-d.y;
                bool plate=i<ReservedDebris/12;
                float size=chip*(plate?2+random.Next()*1.5f:0.5f+random.Next()*1.2f);
                ParticleSystem.EmitParams ep=new ParticleSystem.EmitParams();
                ep.position=d*radius*0.08f; ep.velocity=d*speed*(0.5f+random.Next())+Vector3.up*speed*0.3f;
                ep.startSize3D=new Vector3(size*(0.5f+random.Next()*1.1f),size*(0.6f+random.Next()*0.6f),size*(0.3f+random.Next()*1.1f));
                ep.startLifetime=1.5f+random.Next()*3;
                float shade=random.Next();
                Color baseColor=shade<0.2f?new Color(0.88f,0.86f,0.8f):Color.Lerp(new Color(0.7f,0.7f,0.68f),new Color(0.3f,0.29f,0.27f),shade);
                float heat=heatDebris&&random.Next()<0.25f+0.6f*(float)plan.FireFraction?0.6f+0.4f*random.Next():0;
                baseColor.a=heatDebris?heat:1;
                ep.startColor=baseColor;
                fragments.Emit(ep,1);
            }
            ParticleSystem.ForceOverLifetimeModule force=fragments.forceOverLifetime;
            force.enabled=true; force.space=ParticleSystemSimulationSpace.Local; force.y=-c.Gravity;
        }
        void EmitShower()
        {
            FxPlan plan=c.Plan; FxSettings settings=c.Settings;
            if(!settings.Debris||!settings.Explosions) return;
            float g=c.Surface||c.Air>0.02f?c.Gravity:0;
            ShowerPlan sp=Shower.Plan(plan,c.Surface,g,settings.ParticlesPerEvent);
            if(sp.Kind==ShowerKind.None) return;
            bool solid=sp.Kind==ShowerKind.Solid;
            float dim=solid?1:0.55f;
            var smokeColor=new Color((float)plan.SmokeR*dim,(float)plan.SmokeG*dim,(float)plan.SmokeB*dim,1);
            var head=solid?new Color(1,0.93f,0.78f,1):new Color(1,0.62f,0.28f,1);
            var tint=new Color((float)plan.SmokeR,(float)plan.SmokeG,(float)plan.SmokeB,1);
            bool airborne=!c.Surface&&c.GasRate>0;
            Vector3 inherited=airborne?c.Velocity:Vector3.zero;
            shower.SetFrame(inherited,c.GasRate); heavy.SetFrame(inherited,c.GasRate);
            float lift=c.Surface?(float)Math.Max(1,c.Layout.RadiusMeters*0.2):0;
            float? floor=c.Surface?0f:(c.Floor.HasValue&&c.Air>0.3f?c.Floor:null);
            bool land=floor.HasValue&&!c.Water&&!(!c.Surface&&c.FloorWet)&&c.Air>0.1f;
            if(landingFire!=null) TrailShower.ConfigureLandingFire(landingFire,tint,sheets);
            float spread=(float)Math.Max(0.5,plan.Radius*0.3);
            shower.Burst(sp,smokeColor,head,Vector3.up*lift,spread,inherited,(uint)(random.Next()*16777216f)^0x51A5u,floor,land?landingFire:null,sheets?0:1.2f);
            Horizon=Mathf.Max(Horizon,(float)(sp.LifeMax*Math.Max(1,sp.TrailLife)/0.8)+2);
            ShowerPlan hp=Shower.Heavy(plan,c.Surface,g,settings.ParticlesPerEvent);
            if(hp.Kind!=ShowerKind.None)
            {
                if(ember!=null) TrailShower.ConfigureEmbers(ember,emberFlames,emberSmoke,tint,8,16,sheets);
                heavy.Burst(hp,smokeColor,head,Vector3.up*lift,spread,inherited,(uint)(random.Next()*16777216f)^0x4EA7u,floor,land?ember:null,0);
                Horizon=Mathf.Max(Horizon,(float)(hp.LifeMax*Math.Max(1,hp.TrailLife)/0.8)+2);
                if(land) Horizon=Mathf.Max(Horizon,(float)hp.LifeMax+16+9+1);
            }
            int runaways=Shower.Runaways(plan,c.Surface);
            if(runaways>0) StartRunaways(runaways,smokeColor,floor);
        }
        void StartRunaways(int n,Color smokeColor,float? floor)
        {
            bool airborne=!c.Surface&&c.GasRate>0;
            Vector3 v=airborne?c.Velocity:Vector3.zero;
            runaway.SetFrame(v,c.GasRate);
            paths=new RunawayPath[n];
            var life=new float[n]; var start=new Vector3[n];
            runawayAt.Clear(); runawaySize.Clear();
            for(int i=0;i<n;i++)
            {
                paths[i]=RunawayPath.Create(c.Seed*31u+(uint)i*977u,new Vec3(v.x,v.y,v.z),c.Gravity,c.Air,
                    floor.HasValue?floor.Value:double.NegativeInfinity,12);
                life[i]=(float)paths[i].ThrustTime+1.2f; start[i]=Vector3.zero;
                runawayAt.Add(Vector3.zero); runawaySize.Add(5);
            }
            var plume=new Color(Mathf.Min(1,smokeColor.r*1.05f),Mathf.Min(1,smokeColor.g*1.05f),Mathf.Min(1,smokeColor.b*1.05f),1);
            runaway.Trace(n,plume,4+8*c.Air,life,start,new Color(1,0.9f,0.72f,1),5,2.4,true);
            Horizon=Mathf.Max(Horizon,(float)(8.2*2.4/0.8)+2);
        }
        void EmitChunks(bool burning)
        {
            FxPlan plan=c.Plan; float air=c.Air;
            double R=c.Layout.RadiusMeters;
            bool solid=plan.PartKind==PartKind.SolidBooster;
            if(!c.Settings.Debris||!burning||R<4&&!solid) return;
            int count=Math.Min(solid?12:8,(solid?5:2)+(int)(random.Next()*(solid?6:5)));
            ParticleSystem.TrailModule trail=chunks.trails;
            trail.enabled=air>0.1f;
            Color head=solid?new Color(0.92f,0.9f,0.86f):new Color(0.3f,0.28f,0.26f);
            Gradient smokeTrail=new Gradient();
            smokeTrail.SetKeys(new[]{new GradientColorKey(head,0),new GradientColorKey(Color.Lerp(head,new Color(0.6f,0.58f,0.55f),0.6f),1)},
                new[]{new GradientAlphaKey(0.85f,0),new GradientAlphaKey(0.45f,0.5f),new GradientAlphaKey(0,1)});
            trail.colorOverTrail=new ParticleSystem.MinMaxGradient(smokeTrail);
            ParticleSystem.ForceOverLifetimeModule force=chunks.forceOverLifetime;
            force.enabled=true; force.space=ParticleSystemSimulationSpace.Local; force.y=-c.Gravity;
            ParticleSystem.LimitVelocityOverLifetimeModule drag=chunks.limitVelocityOverLifetime;
            drag.enabled=air>0.05f; drag.limit=60; drag.dampen=0.02f*air;
            float launch=Mathf.Clamp(Mathf.Sqrt((float)R)*7,12,55);
            for(int i=0;i<count;i++)
            {
                Vector3 d=random.Direction(); if(c.Surface) d.y=Mathf.Abs(d.y)*0.8f+0.35f;
                d.Normalize();
                Emit(chunks,d*(float)R*0.1f,d*launch*(0.6f+random.Next()*0.6f),0.3f+random.Next()*0.35f,
                    solid?3+random.Next()*2:2+random.Next()*1.5f,new Color(1,0.62f+random.Next()*0.2f,0.25f,1));
            }
        }
        void EmitSurface(float radius,float speed)
        {
            FxPlan plan=c.Plan; bool water=c.Water; float air=c.Air, domeRadius=c.DomeRadius;
            Vector3 tangent=c.Velocity; tangent.y=0; tangent=Vector3.ClampMagnitude(tangent,30)*0.12f;
            float rise=Mathf.Clamp(Mathf.Sqrt(c.ImpactSpeed)*radius,1,40);
            bool airless=air<0.1f&&!water;
            Render(spray,ParticleSystemRenderMode.Stretch,airless?0.08f:0.035f);
            ParticleSystem.NoiseModule turbulence=spray.noise; turbulence.enabled=!airless;
            int clumps=airless?Math.Min(c.Settings.ParticlesPerEvent,plan.PuffCount*3):water?Math.Min(c.Settings.ParticlesPerEvent,plan.PuffCount*2):
                c.Dome?Math.Max(3,plan.PuffCount/4):plan.PuffCount;
            if(burst!=null&&!airless) clumps=Math.Max(3,clumps/3);
            float fireball=(float)c.Layout.RadiusMeters;
            for(int i=0;i<clumps;i++)
            {
                Vector3 radial=random.Direction(); radial.y=0; radial.Normalize();
                Vector3 start=radial*0.2f, direction; float size, life; Color color;
                if(water)
                {
                    int part=i%10;
                    if(part<3)
                    {
                        start=radial*(fireball+radius)*0.05f*random.Next();
                        direction=radial*speed*0.08f+Vector3.up*rise*(1.8f+random.Next()*0.8f)+tangent*0.5f;
                        size=radius*(0.06f+random.Next()*0.05f); life=2.2f+random.Next()*1.2f; color=new Color(0.9f,0.95f,1,0.45f);
                    }
                    else if(part<7)
                    {
                        start=radial*domeRadius*(0.15f+random.Next()*0.25f);
                        direction=radial*speed*(0.3f+random.Next()*0.3f)+Vector3.up*rise*(1.0f+random.Next()*0.6f)+tangent;
                        size=radius*(0.07f+random.Next()*0.06f); life=1.8f+random.Next()*1.2f; color=new Color(0.86f,0.93f,1,0.5f);
                    }
                    else
                    {
                        direction=radial*speed*(0.7f+random.Next()*0.5f)+Vector3.up*rise*(0.3f+random.Next()*0.4f)+tangent;
                        size=radius*(0.06f+random.Next()*0.06f); life=1.2f+random.Next(); color=new Color(0.85f,0.94f,1,0.5f);
                    }
                }
                else if(airless)
                {
                    float elevation=0.5f+random.Next()*0.6f, v=speed*(0.6f+random.Next()*0.9f)+rise*0.4f;
                    direction=radial*v*Mathf.Cos(elevation)+Vector3.up*v*Mathf.Sin(elevation)+tangent;
                    size=Mathf.Clamp(radius*(0.02f+random.Next()*0.03f),0.15f,0.6f); life=1.8f+random.Next()*2.2f;
                    color=Dust(1.1f+0.35f*random.Next(),0.95f);
                }
                else
                {
                    direction=radial*speed+Vector3.up*rise*(0.4f+random.Next())+tangent;
                    if(c.Dome) direction*=1.6f;
                    size=radius*(c.Dome?0.03f+random.Next()*0.05f:0.08f+random.Next()*0.12f); life=1+random.Next()*2;
                    color=Dust(0.9f,0.5f);
                }
                Emit(spray,Lift(start,size),direction,size,life,color);
            }
            if(dirt!=null&&!water) EmitDirt();
            ParticleSystem.ForceOverLifetimeModule fall=spray.forceOverLifetime;
            fall.enabled=true; fall.space=ParticleSystemSimulationSpace.Local; fall.y=-c.Gravity;
            if(water)
                for(int i=0;i<8;i++)
                {
                    Vector3 d=random.Direction(); d.y=Mathf.Abs(d.y)*0.5f; float puff=radius*(0.4f+random.Next()*0.4f);
                    Emit(smoke,Lift(d*domeRadius*0.25f,puff),d*speed*0.12f+Vector3.up*(1.5f+random.Next()*2),puff,2.5f+random.Next()*1.5f,
                        new Color(0.88f,0.92f,0.96f,0.22f));
                }
        }
        void EmitShockRing()
        {
            double v0,tau,reach;
            VolumeEvolution.ShockFront(c.Plan,c.Layout,out v0,out tau,out reach);
            float strength=(float)Numbers.Clamp(c.Plan.VisualEnergyScore/6,0.2,1);
            float life=Mathf.Clamp((float)tau*4,0.8f,2.2f);
            ParticleSystem.LimitVelocityOverLifetimeModule brake=ring.limitVelocityOverLifetime;
            brake.enabled=true; brake.dampen=1;
            var keys=new Keyframe[6];
            for(int k=0;k<keys.Length;k++) { float u=k/(keys.Length-1f); keys[k]=new Keyframe(u,Mathf.Exp(-u*life/(float)tau)); }
            brake.limit=new ParticleSystem.MinMaxCurve((float)v0,new AnimationCurve(keys));
            int count=(int)(56+64*strength);
            float puff=Mathf.Clamp((float)reach*0.042f,1.8f,12);
            Color color=c.Water?new Color(0.88f,0.93f,0.97f,0.5f*strength):Dust(1.1f,0.55f*strength);
            for(int i=0;i<count;i++)
            {
                float a=(i+random.Next()*0.6f)/count*Mathf.PI*2;
                var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                Emit(ring,radial*(float)reach*0.03f+Vector3.up*puff*0.45f,radial*(float)v0*(0.92f+random.Next()*0.16f)+Vector3.up*random.Next()*2,
                    puff*(0.7f+random.Next()*0.6f),life*(0.85f+random.Next()*0.3f),color);
            }
        }
        internal void Tick(float age,float dt,float groundFire,bool volume,in VolumeState state)
        {
            if(!active) return;
            FxPlan plan=c.Plan; FxSettings settings=c.Settings;
            if(groundFire>0&&age>=nextFlame)
            {
                nextFlame=age+(groundFlames!=null?0.3f:0.07f);
                float spread=c.DomeRadius*0.16f, radius=(float)plan.Radius;
                if(groundFlames!=null)
                {
                    Vector2 spot=new Vector2(random.Next()*2-1,random.Next()*2-1)*spread;
                    float tall=radius*(0.6f+0.6f*random.Next())*(0.5f+0.5f*groundFire);
                    Emit(groundFlames,new Vector3(spot.x,0,spot.y),wind*0.08f,tall,1.2f+0.6f*random.Next(),new Color(1,1,1,0.6f+0.4f*groundFire));
                }
                else for(int i=0;i<2;i++)
                {
                    Vector2 at=new Vector2(random.Next()*2-1,random.Next()*2-1)*spread;
                    float flame=radius*(0.15f+random.Next()*0.2f);
                    Emit(fire,Lift(new Vector3(at.x,0.1f,at.y),flame),new Vector3(0,1.5f+random.Next()*2,0),flame,
                        0.5f+random.Next()*0.4f,new Color(1,0.5f+random.Next()*0.2f,0.15f,0.85f*groundFire));
                }
                if(random.Next()<0.35f)
                    Emit(smoke,new Vector3(random.Next()-0.5f,1,random.Next()-0.5f)*spread,new Vector3(0,2.5f,0),radius*0.5f,2.5f,
                        new Color(0.5f,0.48f,0.45f,0.18f*groundFire));
            }
            if(age>=nextPuff&&remainingPuffs>0)
            {
                int batch=Math.Min(4,remainingPuffs); remainingPuffs-=batch; nextPuff+=0.12f;
                for(int i=0;i<batch;i++)
                {
                    float hot=Mathf.Clamp01(1-age/0.8f)*(float)plan.FireFraction;
                    Vector3 d=Upward(random.Direction());
                    if(!c.Dome&&settings.Explosions&&hot>0.02f)
                        Emit(fire,d*(float)plan.Radius*age,d*(float)plan.ExpansionSpeed*0.4f,
                            (float)plan.Radius*(0.15f+random.Next()*0.25f),0.3f+random.Next()*0.4f,
                            new Color(1,0.55f+hot*0.35f,0.2f,hot*0.6f));
                    float haze=(float)((settings.Explosions?plan.SmokeFraction:0)+(settings.Burst?plan.VaporFraction:0));
                    float lifetime=Mathf.Min((float)plan.Lifetime,3.5f);
                    if(plan.SmokeFraction<0.01) lifetime=Mathf.Min(lifetime,1.2f);
                    if(!c.Dome&&haze>0.01f)
                    {
                        bool white=settings.Burst&&plan.VaporFraction>plan.SmokeFraction;
                        Color color=white?new Color(0.85f,0.9f,0.94f,haze*0.35f):new Color(0.5f,0.48f,0.45f,haze*0.28f);
                        Emit(smoke,d*(float)plan.Radius*(0.2f+age*0.3f),d*(float)plan.ExpansionSpeed*0.1f+Vector3.up*0.5f,
                            (float)plan.Radius*(0.2f+random.Next()*0.35f),lifetime,color);
                    }
                }
            }
            if(paths!=null)
            {
                for(int i=0;i<paths.Length;i++)
                {
                    Vec3 at=paths[i].At(age);
                    runawayAt[i]=new Vector3((float)at.X,(float)at.Y,(float)at.Z);
                    runawaySize[i]=age<paths[i].ThrustTime?4.2f+1.6f*random.Next():1.2f;
                }
                runaway.Update(age,runawayAt,runawaySize);
            }
            if(shower!=null) { shower.Update(age); heavy.Update(age); }
            if(plasma!=null) plasma.Update(age);
            if(plasmaSmoke!=null) plasmaSmoke.Update(age);
            if(dirt!=null) dirt.Update(age);
            if(!cloudDone&&age>=cloudStart)
            {
                cloudDone=true;
                if(volume) EmitCloud(age,state);
            }
            if(age>=afterStart)
            {
                EmitAfterSmoke(age);
                afterStart=--afterLeft>0?age+0.25f+0.25f*random.Next():float.PositiveInfinity;
            }
        }
        void EmitCloud(float age,in VolumeState state)
        {
            FxPlan plan=c.Plan;
            Vec3 wind=Aftermath.Wind(c.Seed,c.Air);
            int n=Aftermath.Build(state,plan,c.Seed,wind,c.Surface,puffs);
            if(n==0) return;
            double soot=Numbers.Clamp(plan.Soot,0,1);
            var tint=new Color((float)Numbers.Clamp(plan.SmokeR*(1.1-0.3*soot),0.08,1),(float)Numbers.Clamp(plan.SmokeG*(1.1-0.3*soot),0.08,1),
                (float)Numbers.Clamp(plan.SmokeB*(1.1-0.3*soot),0.08,1),1);
            cloud.Play();
            float longest=0;
            for(int i=0;i<n;i++)
            {
                AftermathPuff p=puffs[i];
                float shade=0.9f+0.16f*random.Next();
                SmokeParticles.Emit(cloud,V(p.Position),V(p.Velocity),(float)p.Size,(float)p.Life,
                    new Color(Mathf.Min(1,tint.r*shade),Mathf.Min(1,tint.g*shade),Mathf.Min(1,tint.b*shade),(float)Numbers.Clamp(p.Alpha,0,1)),random.Next()*360);
                longest=Mathf.Max(longest,(float)p.Life);
            }
            Horizon=Mathf.Max(Horizon,age+longest+1);
        }
        void EmitAfterSmoke(float age)
        {
            FxPlan plan=c.Plan;
            bool fuel=plan.FireFraction>0.05;
            float radius=(float)plan.Radius;
            double soot=Numbers.Clamp(plan.Soot,0,1);
            Color tint=fuel?new Color((float)Numbers.Clamp(plan.SmokeR*(1.1-0.3*soot),0.08,1),(float)Numbers.Clamp(plan.SmokeG*(1.1-0.3*soot),0.08,1),
                (float)Numbers.Clamp(plan.SmokeB*(1.1-0.3*soot),0.08,1),1):Dust(1.15f,1);
            cloud.Play();
            {
                Vector3 d=Upward(random.Direction());
                float size=radius*(1.3f+0.9f*random.Next());
                Vector3 velocity=Vector3.up*(0.6f+1.4f*random.Next())+wind*(0.8f+0.4f*random.Next())+d*0.6f;
                float shade=0.9f+0.16f*random.Next();
                SmokeParticles.Emit(cloud,Lift(d*radius*0.45f,size),velocity,size,9+9*random.Next(),
                    new Color(Mathf.Min(1,tint.r*shade),Mathf.Min(1,tint.g*shade),Mathf.Min(1,tint.b*shade),fuel?0.5f:0.32f),random.Next()*360);
            }
            Horizon=Mathf.Max(Horizon,age+20);
        }
        static Vector3 V(Vec3 v) { return new Vector3((float)v.X,(float)v.Y,(float)v.Z); }
        // Lift sprites by half their height to avoid clipping against the ground.
        Vector3 Lift(Vector3 local,float size) { if(c.Surface) local.y=Mathf.Max(local.y,0)+size*0.5f; return local; }
        Vector3 Upward(Vector3 d) { if(c.Surface&&d.y<0) d.y=-d.y; return d; }
        static void Emit(ParticleSystem p,Vector3 position,Vector3 velocity,float size,float lifetime,Color color)
        {
            ParticleSystem.EmitParams ep=new ParticleSystem.EmitParams(); ep.position=position; ep.velocity=velocity;
            ep.startSize=size; ep.startLifetime=lifetime; ep.startColor=color; p.Emit(ep,1);
        }
        internal bool Alive
        {
            get
            {
                if(!active) return false;
                foreach(ParticleSystem p in Roots()) if(p!=null&&p.IsAlive()) return true;
                return false;
            }
        }
        // Unity objects may already be gone during scene teardown.
        internal int Count
        {
            get
            {
                if(!active) return 0;
                int n=0;
                foreach(ParticleSystem p in Roots()) if(p!=null) n+=p.particleCount;
                if(landingFire!=null) n+=landingFire.particleCount;
                if(ember!=null) n+=ember.particleCount+emberFlames.particleCount+emberSmoke.particleCount;
                if(wake!=null) n+=wake.particleCount+sparks.particleCount;
                return n;
            }
        }
        internal void Simulate(float dt) { foreach(ParticleSystem p in Roots()) if(p!=null) p.Simulate(dt,true,false,false); }
        internal void Clear()
        {
            foreach(ParticleSystem p in Roots()) if(p!=null) p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach(TrailShower t in new[]{shower,heavy,runaway,plasma,plasmaSmoke,dirt}) if(t!=null) t.Clear();
            active=false; ReservedDebris=0; paths=null; cloudDone=false; remainingPuffs=0; Horizon=0; afterStart=float.PositiveInfinity;
        }
    }
}
