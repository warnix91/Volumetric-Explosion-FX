using System;
namespace VolumetricExplosionFX.Core
{
    public struct ReentryPlan
    {
        public bool Active;
        public int Count;                       // glowing fragments
        public double DragMin, DragMax;         // fragment drag (1/s) relative to the air
        public double Spread;                   // sideways speed given by the breakup (m/s)
        public double HeadSize, TrailWidth;     // metres
        public double GlowSeconds;              // how long the largest fragments stay incandescent
        public double FlareShare;               // share of fragments that flare up once
        public double FlashSize, FlashSeconds;  // the breakup flash
        public double WakeLength;               // length of the plasma wake (m)
        public int SmokeTrails;                 // fragments that also leave smoke (thicker air)
    }
    public static class Reentry
    {
        public const double MinSpeed=900;
        public static bool Applies(FxPlan plan,bool surface,double airSpeed)
        {
            double air=Numbers.Clamp(plan.Atmosphere,0,1);
            return !surface&&plan.Kind==EventKind.Destruction&&airSpeed>=MinSpeed&&air>0.0002&&air<0.45;
        }
        public static ReentryPlan Plan(FxPlan plan,bool surface,double airSpeed,int budget)
        {
            var r=new ReentryPlan();
            if(!Applies(plan,surface,airSpeed)||budget<8) return r;
            double air=Numbers.Clamp(plan.Atmosphere,0,1);
            double heat=Numbers.Clamp(Math.Pow(airSpeed/2500,3),0.1,2.5);
            double size=Numbers.Clamp(plan.Radius,0.5,30);
            r.Active=true;
            r.Count=(int)Math.Min(Numbers.Clamp(14+plan.VisualEnergyScore*5+18*Math.Min(heat,1.5),12,96),budget);
            double k=0.02+1.6*air;
            r.DragMin=k*0.3; r.DragMax=k*3.5;
            r.Spread=Numbers.Clamp(25+0.02*airSpeed,25,120);
            r.HeadSize=Numbers.Clamp(0.8+0.4*size,1,6)*(0.8+0.4*Math.Min(heat,1));
            r.TrailWidth=Numbers.Clamp(0.5+0.25*size,0.6,4);
            r.GlowSeconds=Numbers.Clamp(2.5+3*heat,2.5,9);
            r.FlareShare=0.18;
            r.FlashSize=Numbers.Clamp(6*size,4,120);
            r.FlashSeconds=Numbers.Clamp(0.25+0.1*size,0.25,1.2);
            r.WakeLength=Numbers.Clamp(airSpeed*0.12,60,600);
            r.SmokeTrails=air>0.05?(int)Numbers.Clamp(r.Count*Numbers.Clamp((air-0.05)*4,0,0.5),0,24):0;
            return r;
        }
        public static double GasTime(double air,double fireballRadius)
        {
            air=Numbers.Clamp(air,0,1);
            if(air<0.002) return double.PositiveInfinity;
            return Numbers.Clamp((0.45+0.035*Math.Max(fireballRadius,0))/Math.Sqrt(air),0.35,60);
        }
    }
}
