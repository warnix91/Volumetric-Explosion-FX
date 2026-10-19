using System;
namespace VolumetricExplosionFX.Core
{
    // 1.1: a vehicle that hits the ground with sideways speed sprays its burning fuel ahead of it: secondary fireballs
    // roll out along its path, further and more of them the faster it was going (visual only).
    public struct FireTrailStep
    {
        public double Distance, Delay, Fuel, Speed;   // metres ahead, seconds after the impact, share of the fuel, forward speed (m/s)
        public double Side;                           // metres to the side of the path (the fuel scatters off it)
    }
    public static class FireTrail
    {
        public const int MaxSteps=5;
        // Irregular: uneven gaps, the fuel scattered to either side, uneven amounts (seeded per crash): evenly spaced
        // fireballs in a straight line, always alike, read as fake.
        public static int Plan(double horizontalSpeed,double fireballRadius,double burnableKg,FireTrailStep[] steps,uint seed=0)
        {
            var rnd=new SplitRandom(seed^0xF1AE7u);
            if(steps==null||horizontalSpeed<25||burnableKg<30||fireballRadius<=0) return 0;
            double R=Math.Max(fireballRadius,2);
            int n=(int)Numbers.Clamp(Math.Round(horizontalSpeed/35),1,Math.Min(MaxSteps,steps.Length));
            // How far the fuel is thrown: grows with speed, from about two to twelve fireball radii.
            double reach=Numbers.Clamp(horizontalSpeed*0.6,2.2*R,12*R);
            double travel=0.55*horizontalSpeed;
            double side=0;
            for(int k=0;k<n;k++)
            {
                double d=reach*(k+0.3+0.65*rnd.Next())/n;
                side=Numbers.Clamp(side+(rnd.Next()*2-1)*1.1*R,-2.6*R,2.6*R);
                steps[k]=new FireTrailStep { Distance=d, Delay=d/travel*(0.85+0.3*rnd.Next()), Fuel=0.5*Math.Pow(0.72,k)*(0.55+0.9*rnd.Next()),
                    Speed=horizontalSpeed*Math.Max(0.2,0.6-0.1*k), Side=side };
            }
            return n;
        }
    }
}
