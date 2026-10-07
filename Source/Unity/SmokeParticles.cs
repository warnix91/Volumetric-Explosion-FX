using System;
using System.Collections.Generic;
using UnityEngine;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    internal static class SmokeParticles
    {
        static readonly List<ParticleSystemVertexStream> Streams=new List<ParticleSystemVertexStream>{
            ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,ParticleSystemVertexStream.UV2,ParticleSystemVertexStream.AnimBlend,ParticleSystemVertexStream.Tangent,
            ParticleSystemVertexStream.Center,ParticleSystemVertexStream.SizeX};
        internal static ParticleSystem Create(Transform parent,string name,Material material,int capacity,float firstFrame,float lastFrame)
        {
            var go=new GameObject(name); go.layer=EffectLayer.Value; go.transform.SetParent(parent,false);
            ParticleSystem p=go.AddComponent<ParticleSystem>(); p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main=p.main; main.playOnAwake=false; main.loop=false; main.duration=60;
            main.simulationSpace=ParticleSystemSimulationSpace.Local; main.maxParticles=capacity;
            main.startSpeed=0; main.startLifetime=4; main.gravityModifier=0; main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            p.useAutoRandomSeed=false; p.randomSeed=1;
            ParticleSystem.EmissionModule emission=p.emission; emission.enabled=false;
            ParticleSystem.ShapeModule shape=p.shape; shape.enabled=false;
            ParticleSystem.SizeOverLifetimeModule size=p.sizeOverLifetime; size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.5f),new Keyframe(0.25f,1),new Keyframe(1,2.6f)));
            ParticleSystem.ColorOverLifetimeModule fade=p.colorOverLifetime; fade.enabled=true;
            var g=new Gradient(); g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.06f),new GradientAlphaKey(0.75f,0.6f),new GradientAlphaKey(0,1)});
            fade.color=g;
            ParticleSystem.RotationOverLifetimeModule roll=p.rotationOverLifetime; roll.enabled=true;
            roll.z=new ParticleSystem.MinMaxCurve(-0.35f,0.35f);
            ParticleSystem.NoiseModule noise=p.noise; noise.enabled=true; noise.strength=0.35f; noise.frequency=0.25f; noise.scrollSpeed=0.2f;
            ParticleSystem.TextureSheetAnimationModule sheet=p.textureSheetAnimation; sheet.enabled=true;
            sheet.numTilesX=8; sheet.numTilesY=8; sheet.animation=ParticleSystemAnimationType.SingleRow;
            sheet.rowMode=ParticleSystemAnimationRowMode.Random; sheet.cycleCount=1;
            sheet.frameOverTime=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,firstFrame,1,lastFrame));
            var r=p.GetComponent<ParticleSystemRenderer>();
            r.renderMode=ParticleSystemRenderMode.Billboard; r.sharedMaterial=material; r.sortMode=ParticleSystemSortMode.Distance;
            r.normalDirection=1; r.SetActiveVertexStreams(Streams);
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
            r.maxParticleSize=2;
            return p;
        }
        static readonly List<ParticleSystemVertexStream> FlameStreams=new List<ParticleSystemVertexStream>{
            ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.Center,ParticleSystemVertexStream.SizeX,
            ParticleSystemVertexStream.UV,ParticleSystemVertexStream.AgePercent,ParticleSystemVertexStream.StableRandomX};
        internal static ParticleSystem CreateFlameSheets(Transform parent,string name,Material flame,int capacity)
        {
            var go=new GameObject(name); go.layer=EffectLayer.Value; go.transform.SetParent(parent,false);
            ParticleSystem p=go.AddComponent<ParticleSystem>(); p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main=p.main; main.playOnAwake=false; main.loop=false; main.duration=60;
            main.simulationSpace=ParticleSystemSimulationSpace.Local; main.maxParticles=capacity;
            main.startSpeed=0; main.startLifetime=1; main.gravityModifier=0; main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            p.useAutoRandomSeed=false; p.randomSeed=7;
            ParticleSystem.EmissionModule emission=p.emission; emission.enabled=false;
            ParticleSystem.ShapeModule shape=p.shape; shape.enabled=false;
            ParticleSystem.SizeOverLifetimeModule size=p.sizeOverLifetime; size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.7f),new Keyframe(0.3f,1),new Keyframe(1,0.85f)));
            var r=p.GetComponent<ParticleSystemRenderer>();
            r.renderMode=ParticleSystemRenderMode.Billboard; r.sharedMaterial=flame; r.sortMode=ParticleSystemSortMode.Distance;
            r.SetActiveVertexStreams(FlameStreams);
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
            r.maxParticleSize=4;
            return p;
        }
        internal static void Bend(ParticleSystem p,Vector3 wind,float rate)
        {
            ParticleSystem.ForceOverLifetimeModule push=p.forceOverLifetime; push.enabled=wind.sqrMagnitude>1e-4f;
            push.space=ParticleSystemSimulationSpace.Local; push.x=wind.x*rate; push.y=0; push.z=wind.z*rate;
        }
        internal static ParticleSystem CreateFlames(Transform parent,string name,Material glow,int capacity)
        {
            var go=new GameObject(name); go.layer=EffectLayer.Value; go.transform.SetParent(parent,false);
            ParticleSystem p=go.AddComponent<ParticleSystem>(); p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main=p.main; main.playOnAwake=false; main.loop=false; main.duration=60;
            main.simulationSpace=ParticleSystemSimulationSpace.Local; main.maxParticles=capacity;
            main.startSpeed=0; main.startLifetime=1; main.gravityModifier=0; main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            p.useAutoRandomSeed=false; p.randomSeed=5;
            ParticleSystem.EmissionModule emission=p.emission; emission.enabled=false;
            ParticleSystem.ShapeModule shape=p.shape; shape.enabled=false;
            ParticleSystem.SizeOverLifetimeModule size=p.sizeOverLifetime; size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.5f),new Keyframe(0.3f,1),new Keyframe(1,0.25f)));
            ParticleSystem.ColorOverLifetimeModule fade=p.colorOverLifetime; fade.enabled=true;
            var g=new Gradient(); g.SetKeys(new[]{new GradientColorKey(new Color(1,0.85f,0.55f),0),new GradientColorKey(new Color(1,0.5f,0.15f),0.5f),new GradientColorKey(new Color(0.8f,0.2f,0.05f),1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.15f),new GradientAlphaKey(0.6f,0.6f),new GradientAlphaKey(0,1)});
            fade.color=g;
            ParticleSystem.NoiseModule noise=p.noise; noise.enabled=true; noise.strength=0.6f; noise.frequency=0.5f; noise.scrollSpeed=0.6f;
            var r=p.GetComponent<ParticleSystemRenderer>();
            r.renderMode=ParticleSystemRenderMode.Stretch; r.lengthScale=1.6f; r.velocityScale=0.08f; r.sharedMaterial=glow;
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false; r.maxParticleSize=1;
            return p;
        }
        internal static void Emit(ParticleSystem p,Vector3 position,Vector3 velocity,float size,float lifetime,Color color,float rotation)
        {
            var ep=new ParticleSystem.EmitParams(); ep.position=position; ep.velocity=velocity; ep.startSize=size;
            ep.startLifetime=lifetime; ep.startColor=color; ep.rotation=rotation; p.Emit(ep,1);
        }
    }
    internal sealed class ResidueEmitter
    {
        ResiduePlan plan; SplitRandom rnd; float flames, smoke, phase, smokeStart; Color tint; Vector3 drift;
        internal bool Active { get; private set; }
        internal ResiduePlan Plan { get { return plan; } }
        internal bool Sheets;
        internal bool Volume;
        internal void Begin(ResiduePlan residue,FxPlan fx,uint seed,float groundY,Vector3 wind)
        {
            plan=residue; rnd=new SplitRandom(seed^0x5EEDu); flames=0; smoke=0; floor=groundY;
            Active=residue.HasFire;
            double soot=Numbers.Clamp(fx.Soot,0,1);
            tint=new Color((float)Numbers.Clamp(fx.SmokeR*(1.1-0.25*soot),0.1,1),(float)Numbers.Clamp(fx.SmokeG*(1.1-0.25*soot),0.1,1),(float)Numbers.Clamp(fx.SmokeB*(1.1-0.25*soot),0.1,1),1);
            drift=wind; phase=(float)(rnd.Next()*6.283);
            smokeStart=(float)Residue.SmokeStart(fx);
        }
        float floor;
        internal void Stop() { Active=false; }
        internal void Tick(float age,float dt,ParticleSystem fire,ParticleSystem column,ParticleSystem tongues=null)
        {
            if(!Active) return;
            if(age>plan.SmokeSeconds) { Active=false; return; }
            float level=(float)Residue.FireLevel(plan,age);
            float radius=(float)plan.FireRadius, D=2*radius;
            float height=(float)plan.FlameHeight*(0.55f+0.45f*Mathf.Sqrt(level));
            float surge=1+0.55f*Mathf.Sin(age*6.2831853f*(float)plan.PuffHz+phase);
            flames+=Volume?0:dt*(float)plan.FlameRate*level*surge*(Sheets&&tongues!=null?0.12f:1);
            while(flames>=1)
            {
                flames-=1;
                if(Sheets&&tongues!=null)
                {
                    {
                        double ka=rnd.Next()*Math.PI*2, kr=Math.Sqrt(rnd.Next())*radius*0.75;
                        float middle=1-0.4f*(float)(kr*kr/Math.Max(radius*radius,1e-3));
                        float tall=height*(0.6f+0.45f*(float)rnd.Next())*middle;
                        var spot=new Vector3((float)(Math.Cos(ka)*kr),floor,(float)(Math.Sin(ka)*kr));
                        var lean=new Vector3(-spot.x,0,-spot.z)*0.05f+drift*0.1f;
                        SmokeParticles.Emit(tongues,spot,lean,tall,1.6f+1.2f*(float)rnd.Next(),new Color(1,1,1,0.75f+0.25f*level),0);
                    }
                    continue;
                }
                double a=rnd.Next()*Math.PI*2, r=Math.Sqrt(rnd.Next())*radius*0.85;
                float size=D*(0.5f+0.35f*(float)rnd.Next())*(0.55f+0.45f*Mathf.Sqrt(level));
                float life=1.1f+0.7f*(float)rnd.Next();
                var at=new Vector3((float)(Math.Cos(a)*r),floor+(Sheets&&tongues!=null?height*(0.2f+0.25f*(float)rnd.Next()):size*0.2f),(float)(Math.Sin(a)*r));
                var inward=new Vector3(-at.x,0,-at.z)*(0.35f/life);
                float rise=height/(life*0.55f)*(0.7f+0.5f*(float)rnd.Next())*surge*0.7f;
                var up=new Vector3((float)(rnd.Next()-0.5)*0.08f*rise,rise,(float)(rnd.Next()-0.5)*0.08f*rise)+inward+drift*0.3f;
                SmokeParticles.Emit(fire,at,up,size,life,new Color(tint.r,tint.g,tint.b,0.92f),(float)(rnd.Next()*360));
                for(int k=0;tongues!=null&&!Sheets&&k<(rnd.Next()<0.6?2:1);k++)
                {
                    float tl=0.5f+0.45f*(float)rnd.Next();
                    var offset=new Vector3((float)(rnd.Next()-0.5),0,(float)(rnd.Next()-0.5))*size*0.4f;
                    var tv=new Vector3((float)(rnd.Next()-0.5)*0.1f*rise,height/tl*(0.6f+0.5f*(float)rnd.Next())*surge*0.55f,(float)(rnd.Next()-0.5)*0.1f*rise)+inward*0.5f+drift*0.2f;
                    SmokeParticles.Emit(tongues,at+offset+Vector3.up*size*0.1f,tv,D*(0.28f+0.22f*(float)rnd.Next())*(0.6f+0.4f*level),tl,
                        new Color(1,0.6f+0.28f*(float)rnd.Next(),0.24f,0.65f+0.3f*level),(float)(rnd.Next()*360));
                }
            }
            float after=age<plan.FireSeconds?1:Mathf.Clamp01(1-(float)((age-plan.FireSeconds)/Math.Max(plan.SmokeSeconds-plan.FireSeconds,0.1)));
            float build=Mathf.Clamp01((age-smokeStart)/4f); build=build*build*(3-2*build);
            float strength=Mathf.Max(level,0.35f*after)*build;
            smoke+=dt*(float)plan.SmokeRate*strength;
            while(smoke>=1)
            {
                smoke-=1;
                double a=rnd.Next()*Math.PI*2, r=Math.Sqrt(rnd.Next())*radius*0.5;
                float size=D*(0.7f+0.45f*(float)rnd.Next());
                var at=new Vector3((float)(Math.Cos(a)*r),floor+height*(0.75f+0.3f*(float)rnd.Next()),(float)(Math.Sin(a)*r));
                var up=Vector3.up*(float)plan.PlumeSpeed*(0.7f+0.5f*(float)rnd.Next())+drift;
                float shade=0.9f+0.18f*(float)rnd.Next();
                SmokeParticles.Emit(column,at,up,size,8+5*(float)rnd.Next(),new Color(Mathf.Min(1,tint.r*shade),Mathf.Min(1,tint.g*shade),Mathf.Min(1,tint.b*shade),0.3f+0.18f*strength),(float)(rnd.Next()*360));
            }
        }
    }
    internal sealed class DebrisTrails
    {
        FragmentMotion[] motion; int[] burning; float[] acc; int count; double gravity; SplitRandom rnd; Color tint; float smoulder;
        bool[] landed; Color dust; bool wet; float firstTick=-1;
        internal int Count { get { return count; } }
        internal bool FlyingPuffs=true;
        internal void Begin(FragmentMotion[] pieces,double g,int maximum,FxPlan fx,uint seed,float residueSeconds,bool water=false)
        {
            if(landed==null||landed.Length<pieces.Length) landed=new bool[pieces.Length];
            for(int f=0;f<landed.Length;f++) landed[f]=false;
            wet=water; firstTick=-1;
            dust=water?new Color(0.9f,0.93f,0.97f,1):new Color((float)Math.Min(1,fx.DustR*1.1),(float)Math.Min(1,fx.DustG*1.1),(float)Math.Min(1,fx.DustB*1.1),1);
            motion=pieces; gravity=g; rnd=new SplitRandom(seed^0x7A11u); count=0;
            if(burning==null||burning.Length<maximum) { burning=new int[maximum]; acc=new float[maximum]; }
            for(int f=0;f<pieces.Length&&count<maximum;f++) if(pieces[f].Burning) { burning[count]=f; acc[count]=(float)rnd.Next(); count++; }
            double soot=Numbers.Clamp(fx.Soot,0,1);
            tint=new Color((float)Numbers.Clamp(fx.SmokeR*(1.15-0.2*soot),0.12,1),(float)Numbers.Clamp(fx.SmokeG*(1.15-0.2*soot),0.12,1),(float)Numbers.Clamp(fx.SmokeB*(1.15-0.2*soot),0.12,1),1);
            smoulder=Mathf.Min(residueSeconds*0.5f,20);
        }
        internal void Stop() { count=0; motion=null; }
        internal void Tick(float age,float dt,ParticleSystem fire,ParticleSystem smoke,ParticleSystem flames=null)
        {
            if(motion!=null) Landings(age,smoke);
            for(int k=0;k<count;k++)
            {
                FragmentMotion m=motion[burning[k]];
                bool flying=age<m.LandTime;
                float burn=(float)m.BurnTime;
                float rate;
                if(flying) rate=FlyingPuffs&&age<burn?30:0;
                else rate=smoulder>0&&age-(float)m.LandTime<smoulder?3.5f*(1-(age-(float)m.LandTime)/smoulder)*Mathf.Clamp01((age-(float)m.LandTime)/2.5f):0;
                bool flaming=!flying&&smoulder>0&&age-(float)m.LandTime<smoulder*0.4f;
                if(flaming) rate+=flames!=null?1.6f:4;
                if(rate<=0) continue;
                acc[k]+=dt*rate;
                if(acc[k]<1) continue;
                acc[k]-=1;
                Vec3 at=DebrisPlanner.Where(m,gravity,age);
                var p=new Vector3((float)at.X,(float)at.Y,(float)at.Z);
                float size=(float)Math.Max(1,Math.Min(5,m.Heat*3+0.8));
                if(flying)
                {
                    SmokeParticles.Emit(fire,p,Vector3.up*0.6f,size*0.7f,0.5f+0.3f*(float)rnd.Next(),new Color(tint.r,tint.g,tint.b,0.85f),(float)(rnd.Next()*360));
                    SmokeParticles.Emit(smoke,p,Vector3.up*0.4f,size*1.3f,5+3*(float)rnd.Next(),new Color(tint.r,tint.g,tint.b,0.42f),(float)(rnd.Next()*360));
                }
                else if(flaming&&(flames!=null||rnd.Next()<0.55))
                {
                    if(flames!=null)
                    {
                        float left=1-(age-(float)m.LandTime)/(smoulder*0.4f);
                        var spot=p+new Vector3((float)(rnd.Next()-0.5),0,(float)(rnd.Next()-0.5))*size*0.3f;
                        SmokeParticles.Emit(flames,spot,Vector3.zero,size*(1.0f+0.6f*(float)rnd.Next())*(0.5f+0.5f*left),1.4f+0.8f*(float)rnd.Next(),
                            new Color(1,1,1,0.65f+0.35f*left),0);
                    }
                    else SmokeParticles.Emit(fire,p+Vector3.up*size*0.3f,Vector3.up*(0.8f+(float)rnd.Next()),size*0.8f,0.6f+0.5f*(float)rnd.Next(),new Color(tint.r,tint.g,tint.b,0.85f),(float)(rnd.Next()*360));
                }
                else
                    SmokeParticles.Emit(smoke,p+Vector3.up*size*0.4f,Vector3.up*(1.2f+(float)rnd.Next()),size*1.4f,5+3*(float)rnd.Next(),new Color(tint.r,tint.g,tint.b,0.4f),(float)(rnd.Next()*360));
            }
        }
        void Landings(float age,ParticleSystem smoke)
        {
            if(firstTick<0) firstTick=age;
            for(int f=0;f<motion.Length&&f<landed.Length;f++)
            {
                if(landed[f]) continue;
                double land=motion[f].LandTime;
                if(double.IsInfinity(land)||age<land) continue;
                landed[f]=true;
                if(land<firstTick-0.2) continue;
                Vec3 at=DebrisPlanner.Where(motion[f],gravity,land);
                float size=(wet?1.6f:1.1f)+1.1f*(float)rnd.Next();
                var drift=new Vector3((float)(rnd.Next()-0.5),0,(float)(rnd.Next()-0.5))*2.5f;
                SmokeParticles.Emit(smoke,new Vector3((float)at.X,(float)at.Y+size*0.25f,(float)at.Z),Vector3.up*(0.3f+0.4f*(float)rnd.Next())+drift,size,
                    1.4f+1.2f*(float)rnd.Next(),new Color(dust.r,dust.g,dust.b,wet?0.32f:0.3f),(float)(rnd.Next()*360));
            }
        }
    }
}
