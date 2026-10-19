using System;
namespace VolumetricExplosionFX.Core
{
    // 1.1: how a burning wreck's smoke goes up. None keeps the 1.0 plume.
    public enum SmokeStyle { None, Column, Billow, Creep, Puffing }
    public static class SmokeStyles
    {
        // A tall narrow column, big billowing clouds, heavy smoke creeping low with the wind, or puffs as the fire surges.
        // Sooty fires favour the column and the billows; cleaner ones the creeping haze and the puffs.
        public static SmokeStyle Choose(FxPlan plan,double chance)
        {
            if(plan==null||plan.Look==null||!plan.Look.Modern) return SmokeStyle.None;
            double soot=Numbers.Clamp(plan.Soot,0,1), c=Numbers.Clamp(chance,0,0.999999);
            double column=0.2+0.15*soot, billow=column+0.25+0.1*soot, creep=billow+0.3-0.15*soot;
            return c<column?SmokeStyle.Column:c<billow?SmokeStyle.Billow:c<creep?SmokeStyle.Creep:SmokeStyle.Puffing;
        }
        // Multipliers of the column's rise speed, puff size and wind drift.
        public static void Shape(SmokeStyle style,out double rise,out double size,out double drift)
        {
            switch(style)
            {
                case SmokeStyle.Column: rise=1.45; size=0.7; drift=0.6; return;
                case SmokeStyle.Billow: rise=0.8; size=1.45; drift=0.9; return;
                case SmokeStyle.Creep: rise=0.45; size=1.25; drift=1.7; return;
                default: rise=1; size=1; drift=1; return;
            }
        }
        // Puffing: the smoke comes in bursts as the fire surges (0.35 Hz), with lulls between.
        public static double Pulse(SmokeStyle style,double age,double phase)
        {
            if(style!=SmokeStyle.Puffing) return 1;
            double w=Math.Max(0,Math.Sin(age*2*Math.PI*0.35+phase)); return 0.25+1.6*w*w;
        }
    }
    public struct ResiduePlan
    {
        public double FireSeconds, FireRadius, SmokeSeconds, PlumeSpeed, FlameRate, SmokeRate;
        public double FlameHeight, PuffHz;
        public double ScorchRadius, ScorchSeconds, ScorchDarkness;
        // 1.1: the pool fire takes hold after the fireball has lifted (Delay, s) and spreads over Ramp seconds (0 = 1.0 timing);
        // Patchy > 0 breaks it into several separate flames.
        public double Delay, Ramp, Patchy;
        public bool HasFire { get { return FireSeconds>0; } }
        // 1.1: a second spill that lands in a burning pool feeds it: the burning area adds up (radius from the summed
        // areas, at most 40 m), the flames get a little taller and the fire lasts longer. `age` is the pool's own age.
        // 1.1: a spill thrown further along a crash path joins this fire as a strip: it burns as long as the later spill.
        public ResiduePlan Joined(ResiduePlan other,double age)
        {
            ResiduePlan r=this;
            double tail=Math.Max(SmokeSeconds-FireSeconds,0);
            r.FireSeconds=Math.Max(FireSeconds,age+other.FireSeconds);
            r.SmokeSeconds=Math.Max(SmokeSeconds,r.FireSeconds+tail);
            r.FlameHeight=Math.Max(FlameHeight,other.FlameHeight);
            return r;
        }
        public ResiduePlan Fed(ResiduePlan other,double age)
        {
            ResiduePlan r=this;
            double tail=Math.Max(SmokeSeconds-FireSeconds,0);
            r.FireRadius=Math.Min(Math.Sqrt(FireRadius*FireRadius+0.7*other.FireRadius*other.FireRadius),40);
            r.FlameHeight=Math.Max(FlameHeight,other.FlameHeight)*1.08;
            r.FireSeconds=Math.Max(FireSeconds,age+other.FireSeconds)+0.3*other.FireSeconds;
            r.SmokeSeconds=Math.Max(SmokeSeconds,r.FireSeconds+tail);
            r.ScorchRadius=Math.Max(ScorchRadius,Math.Min(r.FireRadius*1.3,ScorchRadius*1.5));
            return r;
        }
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
                if(plan.Look!=null&&plan.Look.Modern)
                {
                    r.Delay=Numbers.Clamp(0.55*VolumeEvolution.FireballDuration(plan),0.6,3);
                    r.Ramp=3.5; r.Patchy=1;
                    r.FireSeconds=Math.Min(r.FireSeconds+r.Delay,maxSeconds+r.Delay);
                }
                r.FireRadius=Numbers.Clamp(0.35*R,1.2,25);
                r.SmokeSeconds=Math.Min(r.FireSeconds+8,maxSeconds+8);
                double D=2*r.FireRadius, Q=1700*Math.PI*r.FireRadius*r.FireRadius*fire*(water?0.5:1);
                r.FlameHeight=Numbers.Clamp(0.235*Math.Pow(Q,0.4)-1.02*D,0.8*D,45);
                if(r.Patchy>0) r.FlameHeight*=0.8;   // several low flames over the spill rather than one tall cone
                r.PuffHz=Numbers.Clamp(1.5/Math.Sqrt(Math.Max(D,0.5)),0.2,2);
                r.PlumeSpeed=Numbers.Clamp(2.5+0.35*r.FireRadius,2.5,10);
                r.FlameRate=Numbers.Clamp(8+3*r.FireRadius*fire,6,40);
                r.SmokeRate=Numbers.Clamp(4+1.6*r.FireRadius,4,28);
                if(ground&&Aftermath.CreepSpeed(plan)>0)
                {
                    // Grass caught by the fire keeps burning on its own for about a minute after the fuel: the smoke lasts.
                    r.SmokeSeconds=Math.Min(Math.Max(r.SmokeSeconds,r.FireSeconds+55),maxSeconds+8);
                }
                if(water&&plan.Look!=null&&plan.Look.Modern)
                {
                    if(plan.Depth>8) r=new ResiduePlan();   // released deep down, the fuel disperses before it reaches the air
                    else if(Splash.Slick(plan))
                    {
                        // A slick: wider, lower flames in patches, burning longer, black smoke.
                        r.FireRadius=Numbers.Clamp(r.FireRadius*1.8,2,40); r.FlameHeight*=0.55; r.Patchy=1;
                        r.FireSeconds=Math.Min(r.FireSeconds*1.6,maxSeconds+r.Delay); r.SmokeSeconds=Math.Min(r.FireSeconds+10,maxSeconds+10);
                        r.SmokeRate=Numbers.Clamp(4+1.6*r.FireRadius,4,28);
                    }
                }
            }
            if(plan.Warhead!=WarheadKind.None)
            {
                // A warhead blackens and craters the ground; only incendiary, fuel-air and nuclear ones leave fires burning.
                if(plan.Warhead==WarheadKind.Structure) { if(plan.Soot<0.3) { r.FireSeconds=0; r.SmokeSeconds=0; } }   // a fuel store keeps burning
                else if(!Core.Warhead.LeavesFire(plan.Warhead)) { r.FireSeconds=0; r.SmokeSeconds=0; }
                else if(plan.Warhead==WarheadKind.Incendiary&&r.HasFire)
                {
                    // Burning gel splashes wide and keeps burning.
                    r.FireRadius=Numbers.Clamp(0.3*plan.WarheadRange,r.FireRadius,25);
                    r.FireSeconds=Math.Min(r.FireSeconds*1.8,maxSeconds+r.Delay); r.SmokeSeconds=Math.Max(r.SmokeSeconds,r.FireSeconds+10);
                    r.SmokeRate=Numbers.Clamp(4+1.6*r.FireRadius,4,28); r.FlameRate=Numbers.Clamp(8+3*r.FireRadius,6,40);
                }
                if(ground&&plan.Warhead==WarheadKind.Structure)
                {
                    // A fallen building leaves no stain of its own (draped over the ruins and roads, wide pale patches looked
                    // like decals); a fuel store blackens the ground where its fuel burned.
                    if(plan.Soot>0.3) { r.ScorchRadius=Numbers.Clamp(0.3*plan.WarheadRange,3,25); r.ScorchDarkness=0.45; r.ScorchSeconds=maxSeconds; }
                }
                else if(ground&&plan.Warhead!=WarheadKind.Emp)
                {
                    r.ScorchRadius=Numbers.Clamp(0.35*plan.WarheadRange,1.5,60);
                    r.ScorchDarkness=air<0.1?0.35:0.55; r.ScorchSeconds=maxSeconds;
                    if(plan.Warhead==WarheadKind.Nuke) { r.ScorchRadius=0; r.ScorchSeconds=0; }
                }
                return r;
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
            double u=age/r.FireSeconds, spread=r.Ramp>0?Numbers.Clamp((age-r.Delay)/r.Ramp,0,1):Math.Min(1,age/1.8);
            return spread*spread*(3-2*spread)*Math.Sqrt(1-u);
        }
        public static double SmokeStart(FxPlan plan) { return Math.Max(1.2,0.7*VolumeEvolution.FireballDuration(plan)); }
        // How the 1.1 ground mark looks (Scorch.shader _ScorchLook): 1 (0 = the 1.0 mark), crater (0 a burn, 1 a
        // warhead's crater, between: a hard crash's gouge), drawn out along a grazing impact (0..1), its direction (radians,
        // from the blast frame's x toward z). vx, vy, vz: the impact's velocity in the blast frame (m/s, y up).
        public static Vec4 ScorchLook(FxPlan plan,double vx,double vy,double vz)
        {
            if(plan==null||plan.Look==null||!plan.Look.Modern) return new Vec4();
            bool warhead=plan.Warhead!=WarheadKind.None&&plan.Warhead!=WarheadKind.Structure;
            double along=Math.Sqrt(vx*vx+vz*vz), speed=Math.Sqrt(along*along+vy*vy);
            double crater=warhead?(plan.Warhead==WarheadKind.Incendiary||plan.Warhead==WarheadKind.Thermobaric?0.45:1):
                Smooth(60,250,plan.ImpactSpeed)*(plan.FireFraction>0.15?0.5:0.85);
            double grazing=speed>1?along/speed:0;
            double stretch=Numbers.Clamp(grazing*Smooth(15,120,along),0,1)*(warhead?0.25:0.85);
            return new Vec4(1,crater,stretch,along>1e-3?Math.Atan2(vz,vx):0);
        }
        static double Smooth(double a,double b,double x) { double t=Numbers.Clamp((x-a)/(b-a),0,1); return t*t*(3-2*t); }
        public static double ScorchLevel(ResiduePlan r,double age)
        {
            if(r.ScorchRadius<=0||age<0||age>=r.ScorchSeconds) return 0;
            double fadeIn=Math.Min(1,age/0.8), fadeOut=Math.Min(1,(r.ScorchSeconds-age)/Math.Min(6,r.ScorchSeconds*0.3+1e-3));
            return fadeIn*fadeOut*r.ScorchDarkness;
        }
    }
}
