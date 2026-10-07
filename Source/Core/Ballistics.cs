using System;
namespace VolumetricExplosionFX.Core
{
    // Effect frame: y up, metres and seconds; linear drag in 1/s.
    public static class Ballistics
    {
        // Series near zero; keep the split consistent with the fragment shader.
        public static double E1(double u) { return u<0.02?1-u*(0.5-u*(1.0/6-u/24)):(1-Math.Exp(-u))/u; }
        public static double E2(double u) { return u<0.02?0.5-u*(1.0/6-u*(1.0/24-u/120)):(u-1+Math.Exp(-u))/(u*u); }
        public static Vec3 Position(Vec3 start,Vec3 velocity,double drag,double gravity,double t)
        {
            t=Math.Max(t,0); double u=Math.Max(drag,0)*t, a=t*E1(u), b=t*t*E2(u);
            return new Vec3(start.X+velocity.X*a,start.Y+velocity.Y*a-gravity*b,start.Z+velocity.Z*a);
        }
        public static double Height(double y0,double vy,double drag,double gravity,double t)
        { t=Math.Max(t,0); double u=Math.Max(drag,0)*t; return y0+vy*t*E1(u)-gravity*t*t*E2(u); }
        public static double SpinAngle(double rate,double drag,double t)
        { t=Math.Max(t,0); return rate*t*E1(Math.Max(drag,0)*t); }
        public static double ApexTime(double vy,double drag,double gravity)
        {
            if(vy<=0||gravity<=0) return 0;
            return drag>1e-6?Math.Log(1+drag*vy/gravity)/drag:vy/gravity;
        }
        public static double LandingTime(double y0,double vy,double drag,double gravity,double floor,double horizon)
        {
            drag=Math.Max(drag,0);
            double apex=ApexTime(vy,drag,gravity);
            if(Height(y0,vy,drag,gravity,apex)<=floor) return 0;
            double lo=apex, hi=apex+0.5;
            while(Height(y0,vy,drag,gravity,hi)>floor)
            {
                if(hi>horizon) return double.PositiveInfinity;
                lo=hi; hi=hi*2+0.5;
            }
            for(int i=0;i<60;i++) { double mid=(lo+hi)*0.5; if(Height(y0,vy,drag,gravity,mid)>floor) lo=mid; else hi=mid; }
            return hi;
        }
    }
    public struct FragmentMotion
    {
        public Vec3 Pivot, Velocity, SpinAxis;
        public double SpinRate, Drag, Heat, Char, LandTime, RestHeight, BurnTime;
        public bool Burning;
        public double LandHeight, Hop, SlideTime, SettleAngle;
        public Vec3 Slide, SettleAxis;
    }
    public static class DebrisPlanner
    {
        static readonly Vec3 Up=new Vec3(0,1,0);
        public static int Pieces(FxPlan plan,double partSize)
        {
            double violence=Numbers.Clamp(plan.VisualEnergyScore/6,0,1.5);
            double size=Numbers.Clamp(partSize,0.2,30);
            return (int)Numbers.Clamp(Math.Round(4+7*Math.Sqrt(size)*(0.45+0.55*violence)+8*plan.FireFraction),3,48);
        }
        public static double LaunchSpeed(FxPlan plan)
        {
            double fire=Numbers.Clamp(plan.FireFraction,0,1);
            double kinetic=Numbers.Clamp(Math.Sqrt(Math.Max(plan.KineticJoules,0))/400,0,30);
            return Numbers.Clamp(4+fire*(10+8*Numbers.Clamp(plan.VisualEnergyScore,0,9))+kinetic,4,90);
        }
        public static FragmentMotion[] Launch(FxPlan plan,FractureResult cut,Quat toEffect,Vec3 offset,Vec3 center,Vec3 partVelocity,bool surface,uint seed)
        {
            var rnd=new SplitRandom(seed);
            double air=Numbers.Clamp(plan.Atmosphere,0,1), fire=Numbers.Clamp(plan.FireFraction,0,1);
            double speed=LaunchSpeed(plan), mean=0;
            for(int f=0;f<cut.Count;f++) mean+=cut.Radius[f];
            mean=Math.Max(mean/Math.Max(cut.Count,1),0.05);
            Vec3 inherited=surface?new Vec3(partVelocity.X*0.35,Math.Max(0,-partVelocity.Y)*0.06,partVelocity.Z*0.35):partVelocity;
            var result=new FragmentMotion[cut.Count];
            for(int f=0;f<cut.Count;f++)
            {
                var m=new FragmentMotion();
                m.Pivot=Rotate(toEffect,cut.Pivot[f])+offset;
                Vec3 normal=Rotate(toEffect,cut.Normal[f]);
                Vec3 radial=(m.Pivot-center).Normalized(normal);
                Vec3 dir=(radial*0.6+normal*0.3+rnd.Direction()*0.5).Normalized(radial);
                if(surface&&dir.Y<0.12) dir=new Vec3(dir.X,0.12+Math.Abs(dir.Y)*0.6,dir.Z).Normalized(Up);
                double size=Math.Max(cut.Radius[f],0.04);
                double small=Numbers.Clamp(Math.Pow(mean/size,0.5),0.5,2.0);
                m.Velocity=dir*(speed*small*(0.5+0.7*rnd.Next()))+inherited;
                m.SpinAxis=rnd.Direction();
                m.SpinRate=Numbers.Clamp(m.Velocity.Length/Math.Max(size,0.2)*(0.08+0.22*rnd.Next()),0.4,18);
                m.Drag=Numbers.Clamp(air*(0.35+0.7*rnd.Next())/Math.Max(size*1.2,0.25),0,3);
                bool hot=rnd.Next()<0.25+0.6*fire;
                m.Heat=hot?Numbers.Clamp(fire*(0.35+0.65*rnd.Next())+0.25*Math.Max(0,1-air)*fire,0,1):0.15*rnd.Next()*Math.Max(fire,0.2);
                m.Char=Numbers.Clamp(fire*(0.15+0.55*rnd.Next())+0.1*rnd.Next(),0,1);
                m.Burning=air>0.1&&fire>0.1&&m.Heat>0.5&&size>=mean*0.7;
                m.BurnTime=m.Burning?1.5+5*m.Heat*rnd.Next():0;
                m.LandTime=double.PositiveInfinity; m.RestHeight=-1e9;
                result[f]=m;
            }
            return result;
        }
        public static double LowestPoint(FractureResult cut,int fragment,Quat toEffect,Vec3 axis,double angle)
        { return LowestPoint(cut,fragment,toEffect,axis,angle,new Vec3(1,0,0),0); }
        public static double LowestPoint(FractureResult cut,int fragment,Quat toEffect,Vec3 axis,double angle,Vec3 axis2,double angle2)
        {
            Vec3 pivot=Rotate(toEffect,cut.Pivot[fragment]);
            double low=0;
            int start=cut.FirstVertex[fragment], end=start+cut.VertexCounts[fragment];
            for(int v=start;v<end;v++)
            {
                Vec3 q=Vec3.Rotate(Rotate(toEffect,cut.Positions[v])-pivot,axis,angle);
                if(angle2!=0) q=Vec3.Rotate(q,axis2,angle2);
                if(q.Y<low) low=q.Y;
            }
            return low;
        }
        public static void Land(FractureResult cut,int fragment,Quat toEffect,ref FragmentMotion m,double floor,double gravity,double horizon,bool wet=false)
        {
            m.Hop=0; m.Slide=new Vec3(); m.SlideTime=0; m.SettleAngle=0; m.SettleAxis=new Vec3(1,0,0);
            double t=Ballistics.LandingTime(m.Pivot.Y,m.Velocity.Y,m.Drag,gravity,floor,horizon);
            if(double.IsPositiveInfinity(t)) { m.LandTime=t; m.RestHeight=floor; m.LandHeight=floor; return; }
            double rest=floor;
            for(int i=0;i<2;i++)
            {
                double low=LowestPoint(cut,fragment,toEffect,m.SpinAxis,Ballistics.SpinAngle(m.SpinRate,m.Drag,t));
                rest=floor-low;
                t=Ballistics.LandingTime(m.Pivot.Y,m.Velocity.Y,m.Drag,gravity,rest,horizon);
                if(double.IsPositiveInfinity(t)) break;
            }
            m.LandTime=t; m.LandHeight=rest; m.RestHeight=rest;
            if(double.IsPositiveInfinity(t)) return;
            double k=Math.Max(m.Drag,0), decay=Math.Exp(-k*t);
            double vy=m.Velocity.Y*decay-(k>1e-6?gravity*(1-decay)/k:gravity*t);
            if(!wet)
            {
                m.Hop=Math.Min(0.22*Math.Max(-vy,0),5);
                if(m.Hop<0.6) m.Hop=0;
                double friction=0.5*Math.Max(gravity,0.5);
                Vec3 slide=new Vec3(m.Velocity.X*decay,0,m.Velocity.Z*decay)*0.35;
                double speed=slide.Length;
                if(speed*speed/(2*friction)>30) { slide=slide*(Math.Sqrt(60*friction)/speed); speed=slide.Length; }
                m.Slide=slide; m.SlideTime=speed/friction;
            }
            double angle=Ballistics.SpinAngle(m.SpinRate,m.Drag,t);
            Vec3 n=Vec3.Rotate(Rotate(toEffect,cut.Normal[fragment]),m.SpinAxis,angle);
            Vec3 target=n.Y>=0?Up:new Vec3(0,-1,0);
            Vec3 axis=Vec3.Cross(n,target); double s=axis.Length;
            if(s>1e-6) { m.SettleAxis=axis*(1/s); m.SettleAngle=Math.Atan2(s,Vec3.Dot(n,target)); }
            m.RestHeight=floor-LowestPoint(cut,fragment,toEffect,m.SpinAxis,angle,m.SettleAxis,m.SettleAngle);
        }
        public static double HopHeight(FragmentMotion m,double gravity,double tau)
        {
            if(m.Hop<=0||gravity<=0) return 0;
            double th=2*m.Hop/gravity;
            return tau>0&&tau<th?m.Hop*tau-0.5*gravity*tau*tau:0;
        }
        public static double Settled(FragmentMotion m,double gravity,double tau)
        {
            double th=m.Hop>0&&gravity>0?2*m.Hop/gravity:0;
            double x=Numbers.Clamp(tau/(th+0.35+0.25*Math.Min(m.SettleAngle,1.6)),0,1);
            return x*x*(3-2*x);
        }
        public static Vec3 Where(FragmentMotion m,double gravity,double age)
        {
            if(age>=m.LandTime)
            {
                Vec3 p=Ballistics.Position(m.Pivot,m.Velocity,m.Drag,gravity,m.LandTime);
                double tau=age-m.LandTime, s=Math.Min(tau,m.SlideTime);
                double skid=m.SlideTime>1e-6?s-s*s/(2*m.SlideTime):0;
                double settled=Settled(m,gravity,tau);
                double y=settled>=1?m.RestHeight:m.LandHeight+(m.RestHeight-m.LandHeight)*settled;
                return new Vec3(p.X+m.Slide.X*skid,y+HopHeight(m,gravity,tau),p.Z+m.Slide.Z*skid);
            }
            return Ballistics.Position(m.Pivot,m.Velocity,m.Drag,gravity,age);
        }
        public static Vec3 Rotate(Quat q,Vec3 v)
        {
            var u=new Vec3(q.X,q.Y,q.Z); Vec3 t=Vec3.Cross(u,v)*2;
            return v+t*q.W+Vec3.Cross(u,t);
        }
    }
}
