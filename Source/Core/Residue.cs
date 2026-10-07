using System;
namespace VolumetricExplosionFX.Core
{
    public struct ResiduePlan
    {
        public double FireSeconds, FireRadius, SmokeSeconds, PlumeSpeed, FlameRate, SmokeRate;
        public double FlameHeight, PuffHz;
        public double ScorchRadius, ScorchSeconds, ScorchDarkness;
        public bool HasFire { get { return FireSeconds>0; } }
    }
    public static class Residue
    {
        public static ResiduePlan Plan(FxPlan plan,bool ground,bool water,double maxSeconds)
        {
            var r=new ResiduePlan();
            maxSeconds=Numbers.Clamp(maxSeconds,0,180);
            if(maxSeconds<=0||!(ground||water)) return r;
            double air=Numbers.Clamp(plan.Atmosphere,0,1), fire=Numbers.Clamp(plan.FireFraction,0,1);
            double R=Math.Max(VolumeEvolution.FireballRadius(plan),0);
            double burnable=Numbers.Clamp(plan.BurnableKg,0,1e7);
            if(air>0.1&&fire>0.08&&burnable>5)
            {
                double seconds=(4+9*Math.Log10(1+burnable/200))*(water?0.5:1)*(0.4+0.6*air);
                r.FireSeconds=Numbers.Clamp(seconds,3,maxSeconds);
                r.FireRadius=Numbers.Clamp(0.35*R,1.2,25);
                r.SmokeSeconds=Math.Min(r.FireSeconds+8,maxSeconds+8);
                double D=2*r.FireRadius, Q=1700*Math.PI*r.FireRadius*r.FireRadius*fire*(water?0.5:1);
                r.FlameHeight=Numbers.Clamp(0.235*Math.Pow(Q,0.4)-1.02*D,0.8*D,45);
                r.PuffHz=Numbers.Clamp(1.5/Math.Sqrt(Math.Max(D,0.5)),0.2,2);
                r.PlumeSpeed=Numbers.Clamp(2.5+0.35*r.FireRadius,2.5,10);
                r.FlameRate=Numbers.Clamp(8+3*r.FireRadius*fire,6,40);
                r.SmokeRate=Numbers.Clamp(4+1.6*r.FireRadius,4,28);
            }
            bool fuel=fire>0.15&&burnable>20, hard=plan.ImpactSpeed>60&&plan.VisualEnergyScore>3.5;
            if(ground&&(fuel||hard))
            {
                double radius=Math.Max(0.7*R,0.6*plan.Radius);
                r.ScorchRadius=Numbers.Clamp(radius,2,45);
                r.ScorchDarkness=fuel?Numbers.Clamp(0.2+0.45*fire,0.2,0.65):0.3;
                if(air<0.1) r.ScorchDarkness=Math.Min(r.ScorchDarkness,0.35);
                r.ScorchSeconds=maxSeconds;
            }
            return r;
        }
        public static double FireLevel(ResiduePlan r,double age)
        {
            if(!r.HasFire||age<0||age>=r.FireSeconds) return 0;
            double u=age/r.FireSeconds, spread=Math.Min(1,age/1.8);
            return spread*spread*(3-2*spread)*Math.Sqrt(1-u);
        }
        public static double SmokeStart(FxPlan plan) { return Math.Max(1.2,0.7*VolumeEvolution.FireballDuration(plan)); }
        public static double ScorchLevel(ResiduePlan r,double age)
        {
            if(r.ScorchRadius<=0||age<0||age>=r.ScorchSeconds) return 0;
            double fadeIn=Math.Min(1,age/0.8), fadeOut=Math.Min(1,(r.ScorchSeconds-age)/Math.Min(6,r.ScorchSeconds*0.3+1e-3));
            return fadeIn*fadeOut*r.ScorchDarkness;
        }
    }
}
