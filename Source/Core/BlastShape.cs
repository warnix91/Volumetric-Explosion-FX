using System;
namespace VolumetricExplosionFX.Core
{
    public enum BlastForm { Rupture, Grazing, Vertical, Streak, Vacuum, Water }
    public struct BlastSource
    {
        public Vec3 Offset; public double Delay, Weight;
        public BlastSource(Vec3 offset,double delay,double weight) { Offset=offset; Delay=delay; Weight=weight; }
    }
    public struct LobeSeed
    {
        public Vec3 Offset, Drift, Axis;
        public double Tau, Size, Delay, Heat, Stretch;
        public bool Active;
        public bool Relative;
    }
    public struct LobeState { public Vec3 Center, Axis, Displacement; public double Radius, Stretch, Heat; }
    public struct SplitRandom
    {
        ulong state;
        public SplitRandom(uint seed) { state=0x9E3779B97F4A7C15UL^((ulong)seed*0xD1B54A32D192ED03UL); if(state==0) state=1; }
        public double Next()
        {
            state^=state<<13; state^=state>>7; state^=state<<17;
            return (state>>11)*(1.0/9007199254740992.0);
        }
        public double Range(double a,double b) { return a+(b-a)*Next(); }
        public Vec3 Direction()
        {
            double z=Next()*2-1, a=Next()*Math.PI*2, r=Math.Sqrt(Math.Max(0,1-z*z));
            return new Vec3(r*Math.Cos(a),z,r*Math.Sin(a));
        }
    }
    public sealed class BlastLayout
    {
        public const int Count=4;
        public const double SurfaceReach=1.9;
        static readonly Vec3 Up=new Vec3(0,1,0);
        public readonly LobeSeed[] Lobes=new LobeSeed[Count];
        public BlastForm Form;
        public Vec3 Direction=Up;
        public double Speed, RadiusMeters, ExtentMeters, NoiseScale=1, Roughness=0.6, Stem;
        public double Expansion=1, Tempo=1, HeatBias=1, SootBias;
        public static double ExpansionFor(double air)
        {
            air=Numbers.Clamp(air,0,0.999);
            if(air<0.05) return 1;
            double relative=Math.Max(-0.35*Math.Log(1-air)/1.225,1e-3);
            return Numbers.Clamp(Math.Pow(1/relative,1.0/3),1,2.5);
        }
        double spreadMax, liftMax;
        public int ActiveCount { get { int n=0; for(int i=0;i<Count;i++) if(Lobes[i].Active) n++; return n; } }
        public static BlastLayout Default(FxPlan plan,bool ground=false,bool water=false) { return Create(plan,new Vec3(),ground,water,1,null); }
        public static BlastLayout Create(FxPlan plan,Vec3 velocity,bool ground,bool water,uint seed,BlastSource[] sources)
        {
            var L=new BlastLayout();
            var rnd=new SplitRandom(seed);
            double R=VolumeEvolution.FireballRadius(plan), air=Numbers.Clamp(plan.Atmosphere,0,1);
            double tf=VolumeEvolution.FireballDuration(plan);
            if(water) R*=0.6; // most of the energy goes into spray and steam
            L.Expansion=ExpansionFor(air);
            R*=L.Expansion;
            L.RadiusMeters=R;
            var mood=new SplitRandom(seed^0x6D6F6F64u);
            L.Tempo=mood.Range(0.85,1.15); L.HeatBias=mood.Range(0.82,1.05); L.SootBias=mood.Range(-0.2,0.25);
            double speed=Numbers.Clamp(velocity.Length,0,15000);
            Vec3 dir=speed>1e-3?velocity*(1/speed):Up;
            double vh=Math.Sqrt(velocity.X*velocity.X+velocity.Z*velocity.Z), down=Math.Max(0,-velocity.Y);
            Vec3 fwd=vh>1e-3?new Vec3(velocity.X/vh,0,velocity.Z/vh):new Vec3(1,0,0);
            Vec3 side=new Vec3(-fwd.Z,0,fwd.X);
            L.Speed=speed; L.Direction=dir;
            if(water) L.Form=BlastForm.Water;
            else if(air<0.05) L.Form=BlastForm.Vacuum;
            else if(!ground) L.Form=speed>40?BlastForm.Streak:BlastForm.Rupture;
            else if(speed<25) L.Form=BlastForm.Rupture;
            else if(down>1.2*vh) L.Form=BlastForm.Vertical;
            else L.Form=BlastForm.Grazing;
            L.NoiseScale=rnd.Range(0.8,1.25); L.Roughness=rnd.Range(0.5,0.75);
            double cap=3*R;
            var main=new LobeSeed { Active=true, Size=1, Heat=1, Axis=Up, Stretch=1, Tau=0.15 };
            switch(L.Form)
            {
                case BlastForm.Grazing: main.Axis=fwd; main.Stretch=1+Math.Min(0.6,vh/250); main.Drift=fwd*(vh*0.25); main.Tau=0.15; main.Size=0.85; break;
                case BlastForm.Vertical: main.Stretch=0.45; main.Size=0.9; break; // splash pancake, rounds up as it rises
                case BlastForm.Streak: main.Axis=dir; main.Stretch=1+Math.Min(1.0,speed/400);
                    main.Tau=Numbers.Clamp(0.08/Math.Max(air,0.08),0.05,1.2); break;
                case BlastForm.Vacuum: main.Axis=ground?Tilted(ref rnd,0.9):rnd.Direction(); main.Stretch=rnd.Range(1,1.3); break;
                case BlastForm.Water: main.Stretch=0.7; break;
                default: main.Axis=Tilted(ref rnd,0.35); main.Stretch=rnd.Range(0.9,1.2); break;
            }
            L.Lobes[0]=Capped(main,cap);
            int next=1;
            if(sources!=null) for(int i=0;i<sources.Length&&next<Count;i++)
            {
                Vec3 off=ClampLength(sources[i].Offset,2.5*R);
                L.Lobes[next++]=Capped(new LobeSeed { Active=true, Offset=off, Delay=Numbers.Clamp(sources[i].Delay,0,1.5),
                    Size=Numbers.Clamp(0.45+0.4*Numbers.Clamp(sources[i].Weight,0,1),0.4,0.9), Heat=rnd.Range(0.85,1),
                    Axis=main.Axis, Stretch=main.Stretch, Drift=main.Drift*0.8, Tau=main.Tau },cap);
            }
            int extra;
            switch(L.Form)
            {
                case BlastForm.Rupture: extra=1+(int)(rnd.Next()*3); break;
                case BlastForm.Water: extra=1+(int)(rnd.Next()*2); break;
                default: extra=2+(int)(rnd.Next()*2); break;
            }
            double spin=rnd.Next()*Math.PI*2;
            for(int k=1;k<=extra&&next<Count;k++)
            {
                var lobe=new LobeSeed { Active=true, Heat=1, Stretch=1, Axis=Up, Tau=0.3, Relative=true };
                switch(L.Form)
                {
                    case BlastForm.Grazing:
                        lobe.Offset=fwd*(R*(0.25+0.22*k))+side*(R*rnd.Range(-0.3,0.3))+Up*(R*rnd.Range(0,0.25));
                        lobe.Drift=fwd*(vh*(0.3+0.12*k)*rnd.Range(0.85,1.15))+side*(vh*rnd.Range(-0.1,0.1))+Up*rnd.Range(2,10);
                        lobe.Tau=0.1+0.1*k; lobe.Size=0.8-0.1*k+rnd.Range(-0.06,0.06); lobe.Delay=0.02*k+rnd.Range(0,0.06);
                        lobe.Axis=fwd; lobe.Stretch=1+Math.Min(0.6,vh/250); lobe.Relative=false; break;
                    case BlastForm.Vertical:
                    {
                        double a=2*Math.PI*k/extra+rnd.Range(-0.4,0.4);
                        var radial=new Vec3(Math.Cos(a),0,Math.Sin(a));
                        lobe.Offset=radial*(R*0.3); lobe.Drift=radial*((0.5*down+15)*rnd.Range(0.8,1.2)); lobe.Tau=0.25;
                        lobe.Size=rnd.Range(0.5,0.62); lobe.Delay=rnd.Range(0.02,0.08); lobe.Heat=0.95;
                        lobe.Axis=radial; lobe.Stretch=2.0; lobe.Size+=0.08; break;
                    }
                    case BlastForm.Streak:
                        lobe.Offset=dir*(-R*0.1*k);
                        lobe.Drift=dir*(-speed*(0.25+0.15*k)*rnd.Range(0.9,1.1))+rnd.Direction()*(speed*0.05);
                        lobe.Tau=Numbers.Clamp((0.06+0.04*k)/Math.Max(air,0.08),0.05,1.5);
                        lobe.Size=0.8-0.12*k; lobe.Delay=0.02*k; lobe.Axis=dir; lobe.Stretch=1+Math.Min(1.2,speed/300); break;
                    case BlastForm.Vacuum:
                    {
                        Vec3 d=ground?Tilted(ref rnd,0.9):rnd.Direction();
                        lobe.Offset=d*(R*0.25); lobe.Drift=d*(R*rnd.Range(0.6,1.2)/Math.Max(tf,0.5)); lobe.Tau=Math.Max(tf,0.5);
                        lobe.Size=rnd.Range(0.5,0.7); lobe.Delay=rnd.Range(0,0.05); lobe.Heat=rnd.Range(0.8,1);
                        lobe.Axis=d; lobe.Stretch=rnd.Range(1.1,1.6); break;
                    }
                    case BlastForm.Water:
                    {
                        double a=rnd.Next()*Math.PI*2;
                        var radial=new Vec3(Math.Cos(a),0,Math.Sin(a));
                        lobe.Offset=radial*(R*0.3); lobe.Drift=radial*rnd.Range(5,15); lobe.Size=0.5;
                        lobe.Delay=rnd.Range(0.02,0.1); lobe.Heat=0.8; lobe.Stretch=0.6; break;
                    }
                    default:
                    {
                        double azimuth=spin+k*2.39996+rnd.Range(-0.35,0.35);
                        double elevation=ground?rnd.Range(0.25,0.95):rnd.Range(-0.9,1.0);
                        var d=new Vec3(Math.Cos(elevation)*Math.Cos(azimuth),Math.Sin(elevation),Math.Cos(elevation)*Math.Sin(azimuth));
                        lobe.Offset=d*(R*rnd.Range(0.65,0.9)); lobe.Drift=d*rnd.Range(2,8);
                        lobe.Size=rnd.Range(0.38,0.58); lobe.Delay=rnd.Range(0.03,0.3); lobe.Heat=rnd.Range(0.8,1);
                        lobe.Axis=d; lobe.Stretch=rnd.Range(1,1.3); break;
                    }
                }
                L.Lobes[next++]=Capped(lobe,cap);
            }
            if(plan.VisualEnergyScore>3.5&&air>0.05&&!water&&mood.Next()<0.45)
            {
                int slot=next<Count?next++:Count-1;
                if(slot>0&&(sources==null||slot>sources.Length))
                {
                    Vec3 d=ground?Tilted(ref mood,0.8):mood.Direction();
                    L.Lobes[slot]=Capped(new LobeSeed { Active=true, Heat=1, Stretch=mood.Range(1,1.25), Axis=d, Tau=0.3,
                        Offset=d*(R*mood.Range(0.45,0.9)), Drift=d*mood.Range(3,9)+Up*mood.Range(2,6),
                        Size=mood.Range(0.5,0.72), Delay=mood.Range(0.35,1.1) },cap);
                }
            }
            bool surfaceForm=L.Form==BlastForm.Rupture||L.Form==BlastForm.Grazing||L.Form==BlastForm.Vertical;
            L.Stem=ground&&air>0.3&&R>5&&surfaceForm?rnd.Range(0.6,1):0;
            L.spreadMax=VolumeEvolution.SpreadMax(air);
            L.liftMax=air*R*VolumeEvolution.LiftFactor+(ground||water?0.45*R*L.spreadMax:0);
            double extent=0;
            for(int i=0;i<Count;i++) if(L.Lobes[i].Active) extent=Math.Max(extent,L.Reach(L.Lobes[i]));
            L.ExtentMeters=extent;
            return L;
        }
        double Reach(LobeSeed s)
        {
            return s.Offset.Length*(s.Relative?spreadMax:1)+s.Drift.Length*s.Tau+s.Size*RadiusMeters*spreadMax*Math.Max(s.Stretch,1)*SurfaceReach+liftMax;
        }
        public bool AddSecondary(Vec3 offset,double delay,double weight)
        {
            if(RadiusMeters<=0) return false;
            for(int i=1;i<Count;i++) if(!Lobes[i].Active)
            {
                var s=new LobeSeed { Active=true, Heat=1, Axis=Up, Stretch=1, Tau=0.3, Drift=Up*3,
                    Size=Numbers.Clamp(0.4+0.35*Numbers.Clamp(weight,0,1),0.35,0.75), Delay=Math.Max(0,delay) };
                double room=ExtentMeters-s.Size*RadiusMeters*spreadMax*SurfaceReach-liftMax-s.Drift.Length*s.Tau;
                if(room<=0) return false;
                s.Offset=ClampLength(offset,room);
                Lobes[i]=s;
                return true;
            }
            return false;
        }
        static LobeSeed Capped(LobeSeed s,double cap)
        {
            double carry=s.Drift.Length*s.Tau, room=Math.Max(0,cap-s.Offset.Length);
            if(carry>room&&carry>1e-9) s.Drift=s.Drift*(room/carry);
            return s;
        }
        static Vec3 ClampLength(Vec3 v,double max)
        {
            double l=v.Length; return l>max&&l>1e-9?v*(max/l):v;
        }
        static Vec3 Tilted(ref SplitRandom rnd,double spread)
        {
            Vec3 d=rnd.Direction();
            var v=new Vec3(d.X*spread,Math.Abs(d.Y)*(1-spread)+0.25,d.Z*spread);
            double l=v.Length; return v*(1/l);
        }
    }
}
