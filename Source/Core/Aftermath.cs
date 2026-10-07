using System;
namespace VolumetricExplosionFX.Core
{
    public struct AftermathPuff { public Vec3 Position, Velocity; public double Size, Life, Alpha; }
    public static class Aftermath
    {
        public static double Start(FxPlan plan,double tempo=1)
        {
            double air=Numbers.Clamp(plan.Atmosphere,0,1);
            if(air<0.05||plan.FireFraction<0.05||VolumeEvolution.FireballRadius(plan)<=0) return double.PositiveInfinity;
            return Math.Max(0.75*VolumeEvolution.FireballDuration(plan)*tempo,0.55*VolumeEvolution.VolumeDuration(plan));
        }
        public static Vec3 Wind(uint seed,double air)
        {
            var rnd=new SplitRandom(seed^0x57494E44u);
            double a=rnd.Next()*Math.PI*2, speed=(0.8+2.6*rnd.Next())*Numbers.Clamp(air,0,1);
            return new Vec3(Math.Cos(a)*speed,0,Math.Sin(a)*speed);
        }
        public static int Build(VolumeState s,FxPlan plan,uint seed,Vec3 wind,bool surface,AftermathPuff[] output)
        {
            if(output==null||output.Length==0||s.BoxRadius<=0) return 0;
            double air=Numbers.Clamp(plan.Atmosphere,0,1);
            var rnd=new SplitRandom(seed^0xAF7E12u);
            double box=s.BoxRadius;
            double alpha=Numbers.Clamp(0.2+0.75*Math.Min(1,s.FireBody*2.2+0.25*plan.Soot),0.2,0.85)*(0.6+0.4*air)*0.7;
            double r0=0;
            for(int i=0;i<4;i++) r0=Math.Max(r0,s.Lobe(i).Radius*box);
            if(r0<0.5) return 0;
            int count=0;
            for(int i=0;i<4&&count<output.Length;i++)
            {
                LobeState l=s.Lobe(i);
                double r=l.Radius*box;
                if(r<0.3*r0) continue;
                var c=l.Center*box;
                int n=(int)Numbers.Clamp(3+4*r/r0,4,7);
                for(int k=0;k<n&&count<output.Length;k++)
                {
                    Vec3 d=rnd.Direction(); double u=Math.Pow(rnd.Next(),1.0/3);
                    Vec3 offset=d*(r*0.6*u);
                    double size=Math.Max(r,0.5*r0)*rnd.Range(1.2,1.6)*(1.1-0.25*u);
                    Vec3 p=c+offset;
                    if(surface) p=new Vec3(p.X,Math.Max(p.Y,size*0.3),p.Z);
                    AftermathPuff puff=Puff(p,offset,size,alpha*0.75,air,wind,ref rnd);
                    puff.Velocity=puff.Velocity+new Vec3(0,Math.Max(0,offset.Y/Math.Max(r,1e-6))*1.6*air,0);
                    output[count++]=puff;
                }
            }
            LobeState stem=s.Stem;
            if(surface&&s.StemDensity>0.05&&stem.Radius>0)
            {
                double radius=stem.Radius*box, height=stem.Center.Y*box*2;
                int n=(int)Numbers.Clamp(height/(radius*1.6),2,6);
                for(int k=0;k<n&&count<output.Length;k++)
                {
                    double size=radius*rnd.Range(2.2,2.8), y=Math.Max(height*(k+0.5)/n,size*0.3);
                    var p=new Vec3(stem.Center.X*box+rnd.Range(-0.3,0.3)*radius,y,stem.Center.Z*box+rnd.Range(-0.3,0.3)*radius);
                    output[count++]=Puff(p,new Vec3(),size,alpha*Numbers.Clamp(s.StemDensity*1.5,0.3,1),air,wind,ref rnd);
                }
            }
            return count;
        }
        static AftermathPuff Puff(Vec3 position,Vec3 offset,double size,double alpha,double air,Vec3 wind,ref SplitRandom rnd)
        {
            Vec3 rise=new Vec3(0,(0.5+1.6*air)*rnd.Range(0.7,1.3),0);
            return new AftermathPuff { Position=position, Velocity=rise+offset*0.05+wind*rnd.Range(0.8,1.2),
                Size=size, Life=rnd.Range(25,50)*(0.6+0.4*air), Alpha=alpha*rnd.Range(0.85,1.1) };
        }
    }
}
