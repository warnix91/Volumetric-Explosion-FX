using System;
namespace VolumetricExplosionFX.Core
{
    public enum ShowerKind { None, Liquid, Solid, Dirt }
    public struct ShowerPlan
    {
        public ShowerKind Kind;
        public int Count;
        public double SpeedMin, SpeedMax;   // launch speed, m/s
        public double LifeMin, LifeMax;     // seconds a head burns
        public double Drag;                 // linear air drag, 1/s
        public double UpBias;               // 0 = all directions, 1 = upper hemisphere only
        public double HeadSize, TrailWidth; // metres
        public double Gravity;
        public bool Trails;                 // smoke trails need air
        public double TrailLife, TrailStep;
        public Vec3 Axis; public double Cone;
        public double MinUp;
        public int Clumps; public double ClumpShare;
    }
    public static class Shower
    {
        public static ShowerPlan Plan(FxPlan plan,bool surface,double gravity,int budget)
        {
            var s=new ShowerPlan { Kind=ShowerKind.None, Gravity=Math.Max(gravity,0) };
            double air=Numbers.Clamp(plan.Atmosphere,0,1), fire=Numbers.Clamp(plan.FireFraction,0,1);
            double violence=Violence(plan);
            budget=Math.Max(0,budget);
            if(budget<4) return s;
            double R=VolumeEvolution.FireballRadius(plan);
            if(Solid(plan))
            {
                s.Kind=ShowerKind.Solid;
                s.Count=(int)Math.Min(Math.Min(220,budget),30+plan.BurnableKg/40);
                s.SpeedMin=25*violence; s.SpeedMax=Numbers.Clamp(110*violence,60,160);
                s.LifeMin=2; s.LifeMax=6.5;
                s.HeadSize=1.4; s.TrailWidth=0.6+3.4*air;
                s.Drag=0.08+0.8*air;
                s.TrailLife=3.2; s.TrailStep=2.5;
                s.Clumps=7; s.ClumpShare=0.55;
            }
            else if(fire>0.3&&R>=4)
            {
                s.Kind=ShowerKind.Liquid;
                s.Count=(int)Math.Min(Math.Min(40,budget),6+R*1.2);
                s.SpeedMin=25*violence; s.SpeedMax=Numbers.Clamp(110*violence,50,160);
                s.LifeMin=1.2*(0.3+0.7*air); s.LifeMax=3.8*(0.3+0.7*air);
                s.HeadSize=1.6; s.TrailWidth=0.9+4.5*air;
                s.Drag=0.05+0.35*air;
                s.TrailLife=2.6; s.TrailStep=2;
                s.Clumps=4; s.ClumpShare=0.5;
            }
            else return s;
            s.UpBias=surface?(s.Kind==ShowerKind.Liquid?0.6:0.85):0.25;
            Thin(ref s,air);
            return s;
        }
        public static ShowerPlan Heavy(FxPlan plan,bool surface,double gravity,int budget)
        {
            var s=new ShowerPlan { Kind=ShowerKind.None, Gravity=Math.Max(gravity,0) };
            if(!Solid(plan)||plan.BurnableKg<400||budget<8) return s;
            double air=Numbers.Clamp(plan.Atmosphere,0,1), violence=Violence(plan);
            s.Kind=ShowerKind.Solid;
            s.Count=(int)Numbers.Clamp(4+plan.BurnableKg/700,4,Math.Min(18,budget/8));
            s.SpeedMin=12*violence; s.SpeedMax=Numbers.Clamp(45*violence,25,60);
            s.LifeMin=7; s.LifeMax=13;
            s.HeadSize=2.6; s.TrailWidth=1.2+4.8*air;
            s.Drag=0.02+0.1*air;
            s.UpBias=surface?0.92:0.3;
            s.TrailLife=2.2; s.TrailStep=2;
            Thin(ref s,air);
            return s;
        }
        public static ShowerPlan Dirt(FxPlan plan,double gravity,int budget,Vec3 velocity)
        {
            var s=new ShowerPlan { Kind=ShowerKind.None, Gravity=Math.Max(gravity,0) };
            if(plan.Kind!=EventKind.GroundImpact||budget<8||plan.ImpactSpeed<20&&plan.VisualEnergyScore<3.5) return s;
            double air=Numbers.Clamp(plan.Atmosphere,0,1), strength=Numbers.Clamp((plan.VisualEnergyScore-2)/4,0.2,1.2);
            s.Kind=ShowerKind.Dirt;
            s.Count=(int)Numbers.Clamp(8+12*strength+plan.ImpactSpeed/15,8,Math.Min(40,budget/3));
            s.SpeedMin=10+0.04*plan.ImpactSpeed; s.SpeedMax=Numbers.Clamp(20+0.15*plan.ImpactSpeed+10*strength,25,70);
            s.LifeMin=1; s.LifeMax=2.5+1.5*strength;
            s.HeadSize=0.05; s.TrailWidth=(1.1+2*strength)*(air>0.02?1:0.6);
            s.Drag=0.08+0.35*air;
            s.Trails=true; s.TrailLife=1.6; s.TrailStep=1.5;
            Vec3 horizontal=new Vec3(velocity.X,0,velocity.Z);
            double tilt=horizontal.Length/(horizontal.Length+Math.Abs(velocity.Y)+1e-6);
            s.Axis=(new Vec3(0,1,0)+horizontal.Normalized(new Vec3())*(0.8*tilt)).Normalized(new Vec3(0,1,0));
            s.Cone=1.1; s.MinUp=0.3;
            s.Clumps=5; s.ClumpShare=0.6;
            return s;
        }
        static void Thin(ref ShowerPlan s,double air)
        {
            s.Trails=air>0.15;
            double keep=Numbers.Clamp((air-0.1)/0.5,0.25,1);
            s.TrailLife*=keep; s.TrailWidth*=0.5+0.5*keep;
        }
        public static bool Solid(FxPlan plan) { return plan.PartKind==PartKind.SolidBooster&&plan.BurnableKg>20; }
        static double Violence(FxPlan plan) { return Numbers.Clamp(plan.VisualEnergyScore/6,0.3,1.4); }
        public static int Runaways(FxPlan plan,bool surface)
        {
            if(surface||!Solid(plan)||plan.BurnableKg<800||Numbers.Clamp(plan.Atmosphere,0,1)<0.2) return 0;
            return plan.BurnableKg>5000?2:1;
        }
    }
}
