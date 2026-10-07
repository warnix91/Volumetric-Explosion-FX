using System;
namespace VolumetricExplosionFX.Core
{
    public sealed class RunawayPath
    {
        public const double Step=0.05;
        readonly Vec3[] points;
        public readonly double ThrustTime, Duration;
        RunawayPath(Vec3[] p,double thrust) { points=p; ThrustTime=thrust; Duration=(p.Length-1)*Step; }
        public int Count { get { return points.Length; } }
        public Vec3 At(double t)
        {
            int last=points.Length-1;
            double x=Numbers.Clamp(t/Step,0,last);
            int i=(int)Math.Floor(x);
            if(i>=last) return points[last];
            return points[i]+(points[i+1]-points[i])*(x-i);
        }
        public static RunawayPath Create(uint seed,Vec3 velocity,double gravity,double air,double floor,double duration)
        {
            var rnd=new SplitRandom(seed^0x52554E41u);
            air=Numbers.Clamp(air,0,1); gravity=Math.Max(gravity,0);
            Vec3 forward=velocity.Normalized(new Vec3(0,1,0));
            Vec3 side=Vec3.Cross(forward,rnd.Direction()).Normalized(new Vec3(1,0,0));
            Vec3 heading=Vec3.Rotate(forward,side,rnd.Range(35,90)*Math.PI/180);
            Vec3 v=velocity+heading*rnd.Range(40,80);
            double thrust=rnd.Range(3,7), accel=rnd.Range(30,50);
            Vec3 spinAxis=rnd.Direction(), wobbleAxis=rnd.Direction();
            double spin=rnd.Range(1.0,2.4)*(rnd.Next()<0.5?-1:1), wobble=rnd.Range(0.4,1.0);
            double drag=0.015+0.06*air;
            int n=(int)Math.Ceiling(Numbers.Clamp(duration,Step,120)/Step)+1;
            var p=new Vec3[n];
            Vec3 x=new Vec3();
            bool landed=false;
            for(int i=0;i<n;i++)
            {
                p[i]=x;
                if(landed) continue;
                double t=i*Step;
                Vec3 a=new Vec3(0,-gravity,0)-v*drag;
                if(t<thrust)
                {
                    a=a+heading*accel;
                    spinAxis=Vec3.Rotate(spinAxis,wobbleAxis,wobble*Step).Normalized(spinAxis);
                    heading=Vec3.Rotate(heading,spinAxis,spin*Step).Normalized(heading);
                }
                v=v+a*Step;
                Vec3 next=x+v*Step;
                if(next.Y<=floor) { next=new Vec3(next.X,floor,next.Z); landed=true; }
                x=next;
            }
            return new RunawayPath(p,thrust);
        }
    }
}
