using System;
namespace VolumetricExplosionFX.Core
{
    // 1.1: a water impact drawn as a volume (SplashVolume.shader), quick and clean. What a heavy object
    // hitting the sea throws up, in order:
    // the burst: water flung out from the impact as an open crown of spray in ragged sheaves, opening in a fraction of
    // a second, sagging as it falls and thinning away within about two seconds;
    // (no jet up the middle: the thin column read as a water spout; a warhead's burst rises steeply instead,
    // a plume of spray broadening as it climbs);
    // the spray: a light veil where the burst thinned;
    // the foam: a patch of whitewater on the sea, fading within seconds.
    // Under water (a torpedo, a deep charge) a white dome rises first and bursts into plumes, with a base surge of mist.
    // Metres and seconds, blast frame (y up, the sea at y = 0).
    public struct SplashFrame
    {
        public bool Visible;
        public double Reach, Top;   // the box round it all: half width and height above the water (m)
        public Vec4 Crown;   // the burst's full radius (m), how fast it opens (s), its life (s), its clock (s, 0 = not yet)
        public Vec4 Sheet;   // the burst's thickness (m), extinction (1/m), how far it has thinned (0..1), steep (0..1: a warhead's plume)
        public Vec4 Jet;     // radius (m), top (m), extinction (1/m), torn (0..1)
        public Vec4 Spray;   // ring radius (m), half width (m), centre height (m), extinction (1/m)
        public Vec4 Haze;    // half height of the ring (m), jet spray extinction (1/m), jet spray height (m), jet spray radius (m)
        public Vec4 Surge;   // ring radius (m), half width (m), height (m), extinction (1/m)
        public Vec4 Foam;    // radius (m), ring width (m), strength (0..1), plumes (0 single jet, 1 the plumes of a deep blast)
        public Vec4 Dome;    // under water: radius (m), height (m), extinction (1/m), -
        public Vec4 Drift;   // gravity (m/s2), lean toward the travel (x, z), shadow length and march scale (m)
        public Vec4 Wind;    // drift so far (x, z, m), seed, age (s)
    }
    public static class WaterSplash
    {
        // Drawn as a volume: a water impact in the 1.1 look (a nuclear burst at sea has its own cloud).
        public static bool Applies(FxPlan plan)
        {
            return plan!=null&&plan.Kind==EventKind.WaterImpact&&plan.Look!=null&&plan.Look.Modern&&plan.Warhead!=WarheadKind.Nuke&&
                Splash.ColumnHeight(plan)>0;
        }
        static double Smooth(double a,double b,double x) { double t=Numbers.Clamp((x-a)/(b-a),0,1); return t*t*(3-2*t); }
        static double Hash(uint seed,uint salt) { uint h=(seed^salt)*2654435761u; h^=h>>15; h*=2246822519u; h^=h>>13; return (h&0xFFFFFF)/16777216.0; }
        // Sizes and times of one splash.
        struct Shape
        {
            public double H, Hc, V0, Rc, Hj, Vj, Rj, J0, Span, Jet, Mist, Deep, DomeR, Burst, Rb, Tau, Life, Steep;
        }
        static Shape Make(FxPlan plan,double gravity,uint seed)
        {
            var s=new Shape();
            double g=Math.Max(gravity,0.5);
            s.H=Splash.ColumnHeight(plan);
            s.DomeR=Splash.DomeRadius(plan);
            s.Deep=s.DomeR>0?1:0;
            // Under water the gas bubble lifts the dome first; it bursts once it has risen about its own height.
            s.Burst=s.Deep>0?0.25+Splash.DomeDelay(plan):0;
            // An explosion at the surface throws its water up in a steep plume; a crash flings it out.
            s.Steep=plan.Warhead!=WarheadKind.None&&s.Deep<=0?1:0;
            s.Hc=s.H*(s.Deep>0?0.45:0.62+0.1*Hash(seed,11));
            s.V0=Math.Sqrt(2*g*s.Hc);
            s.Rc=Numbers.Clamp(0.13*s.H+0.3*Math.Max(plan.Radius,0.5),1.2,0.5*s.H);
            if(s.Deep>0) s.Rc=Math.Max(s.Rc,0.55*s.DomeR);
            // The plumes of a deep blast, as the dome bursts. (No jet on a surface impact: a thin column up the middle read
            // as a water spout.)
            s.Jet=s.Deep;
            s.Hj=s.H;
            s.Vj=Math.Sqrt(2*g*s.Hj);
            s.Rj=Math.Max(0.38*s.DomeR,0.5);
            s.J0=s.Burst;
            s.Span=0.3*s.Vj/g;
            s.Mist=0.6+0.4*Smooth(1.5,5,plan.VisualEnergyScore);
            // The burst of spray: its radius, how fast it opens, how long it lasts (seconds: quick).
            s.Rb=Numbers.Clamp(0.55*s.Hc+0.6*s.Rc,2,0.9*s.H)*(s.Deep>0?1.3:1);
            s.Tau=Numbers.Clamp(0.08*Math.Sqrt(s.Rb),0.1,0.45);
            s.Life=Numbers.Clamp(0.6+0.25*Math.Sqrt(s.Rb),0.9,2.6)*(1+0.4*s.Steep);   // (a plume hangs a little longer)
            return s;
        }
        // The jet's top at time t: the highest of the parcels it launched (each slower than the one before).
        static double JetTop(Shape s,double t,double g)
        {
            if(t<=s.J0||s.Jet<=0) return 0;
            double end=Math.Min(t,s.J0+s.Span), best=0;
            for(int i=0;i<=24;i++)
            {
                double tau=s.J0+(end-s.J0)*i/24.0, v=s.Vj*(1-(tau-s.J0)/s.Span), dt=t-tau;
                best=Math.Max(best,v*dt-0.5*g*dt*dt);
            }
            return best;
        }
        // Seconds until the last of it (the foam) is gone.
        public static double Duration(FxPlan plan,double gravity)
        {
            if(!Applies(plan)) return 0;
            Shape s=Make(plan,gravity,0);
            double g=Math.Max(gravity,0.5);
            return Math.Max(s.Burst+s.Life,s.Jet>0?s.J0+s.Span+1.4*s.Vj/g:0)+MistSeconds(s)*1.6+1;
        }
        static double MistSeconds(Shape s) { return 2.2+0.035*s.H; }
        // travel: the object's velocity along the surface (m/s, blast frame): a grazing impact throws its crown forward.
        public static SplashFrame Evaluate(FxPlan plan,double age,double gravity,Vec3 travel,Vec3 wind,uint seed)
        {
            var f=new SplashFrame();
            if(!Applies(plan)||age<0) return f;
            double g=Math.Max(gravity,0.5);
            Shape s=Make(plan,g,seed);
            double end=Duration(plan,g);
            if(age>=end) return f;
            f.Visible=true;
            double fade=1-Smooth(end-Math.Min(4,0.4*end),end,age);
            // ---- the burst of spray ----
            double tc=age-s.Burst, apex=s.V0/g, life=s.Life;
            double prog=Smooth(0.25*life,life,tc);
            double crownOn=tc>0&&tc<life?1:0;
            f.Crown=new Vec4(s.Rb,s.Tau,life,tc>0?tc:0);
            double w0=Numbers.Clamp(0.12*s.Rb,0.3,6);
            f.Sheet=new Vec4(w0,crownOn*2.2/w0*fade,prog,s.Steep);
            double Rnow=tc>0?s.Rb*(1-Math.Exp(-tc/s.Tau)):0;
            // ---- the plumes of a deep blast ----
            double tj=age-s.J0, japex=s.Vj/g;
            double top=JetTop(s,age,g);
            double jtorn=Smooth(0.55*japex,1.4*japex,tj);
            f.Jet=new Vec4(s.Rj,top,top>0.05?s.Jet*2.4/s.Rj*fade:0,jtorn);
            // ---- a light veil of spray where the burst thinned ----
            double mist=MistSeconds(s);
            double since=Math.Max(tc-0.6*life,0);
            double spray=s.Mist*Smooth(0.3*life,life,tc)*Math.Exp(-since/mist)*fade*(1+0.8*s.Steep);   // (a plume leaves more spray)
            double ring=0.6*s.Rb;
            double halfW=0.35*s.Rb*(1+0.12*since), halfH=0.3*s.Rb*(1+0.08*since);
            f.Spray=new Vec4(ring*(1-0.4*s.Steep),halfW,Math.Max(0.35*s.Rb*(1+1.2*s.Steep)-0.6*since,halfH*0.7),spray*0.14/Math.Max(halfW,0.5));
            double jsince=Math.Max(tj-japex,0);
            double jspray=s.Jet*s.Mist*Smooth(0.6*japex,1.3*japex,tj)*Math.Exp(-jsince/mist)*fade;
            double jr=Math.Max(2.5*s.Rj,0.12*s.Hj)*(1+0.1*jsince);
            f.Haze=new Vec4(halfH,jspray*0.14/Math.Max(jr,0.5),Math.Max(0.55*s.Hj-0.8*jsince,0.25*s.Hj),jr);
            // ---- base surge: the water falling back throws a ring of mist over the surface ----
            // (a deep blast's dome collapses into it at once)
            double ts=s.Burst+(s.Deep>0?0.3:0.8*apex), us=Math.Max(age-ts,0);
            double tau=1.6, speed=0.3*s.V0+3;
            double rs=s.Rc*1.3+speed*tau*(1-Math.Exp(-us/tau));
            double surge=age>ts?s.Mist*Smooth(0,0.8,us)*Math.Exp(-us/(1.3*mist))*fade:0;
            double sw=0.12*s.H+0.12*(rs-s.Rc), sh=0.16*s.Hc*(1+0.25*Math.Min(us/tau,2));
            f.Surge=new Vec4(rs,sw,sh,surge*(s.Deep>0?0.42:0)/Math.Max(sw,0.5));
            // ---- foam on the water ----
            // (where the water fell back: about the crown's foot, spreading slowly; a disc the surge's size read as a sheet
            // of grey fog on the sea)
            double foamR=0.75*s.Rb+(s.Deep>0?0.6*s.DomeR:0);
            f.Foam=new Vec4(foamR,0.35*foamR,Smooth(0,0.4,age)*Math.Exp(-age/(1.6*mist))*fade,s.Deep);
            // ---- under water: the dome before it bursts ----
            if(s.Deep>0)
            {
                double rise=Smooth(0,s.Burst,age), gone=1-Smooth(s.Burst,s.Burst+0.45,age);
                f.Dome=new Vec4(s.DomeR*(0.6+0.4*rise),0.7*s.DomeR*rise,gone>0?3.0/Math.Max(0.15*s.DomeR,0.3)*gone:0,0);
            }
            // The travel leans the crown (a share, toward where it went) and the wind carries the spray.
            double tx=travel.X, tz=travel.Z, along=Math.Sqrt(tx*tx+tz*tz);
            double lean=Numbers.Clamp(along/Math.Max(plan.ImpactSpeed,1),0,0.9)*0.45;
            f.Drift=new Vec4(g,along>1e-3?tx/along*lean:0,along>1e-3?tz/along*lean:0,0.6*s.Rb);
            double carried=Math.Max(age-0.5*life,0);
            f.Wind=new Vec4(wind.X*carried*0.8,wind.Z*carried*0.8,seed%997,age);
            // The box round what is there now (a tight box: the march starts and ends at it): the burst, the jet, the spray,
            // the surge, the foam, the dome; the wind's drift.
            double drift=Math.Sqrt(f.Wind.X*f.Wind.X+f.Wind.Y*f.Wind.Y);
            double reach=crownOn>0?(1.35*Rnow+4*w0)*(1+lean)+1.2*lean*1.15*(1.35*Rnow+4*w0):s.Rc;
            double hTop=crownOn>0?(1.15+0.9*s.Steep)*(1.35*Rnow+4*w0):0;
            if(f.Jet.Z>0) { reach=Math.Max(reach,s.Rj*(s.Deep>0?2.6:5)+0.6*drift); hTop=Math.Max(hTop,top+s.Rj*4); }
            if(f.Spray.W>0) { reach=Math.Max(reach,ring*0.75+halfW*2.3+drift); hTop=Math.Max(hTop,f.Spray.Z+halfH*2.2); }
            if(f.Haze.Y>0) { reach=Math.Max(reach,jr*2.2+drift); hTop=Math.Max(hTop,f.Haze.Z*3.2); }
            if(f.Surge.W>0) { reach=Math.Max(reach,rs+sw*2.4+0.6*drift); hTop=Math.Max(hTop,sh*2.2); }
            if(f.Foam.Z>0) reach=Math.Max(reach,foamR*1.6+0.3*drift);
            if(f.Dome.Z>0) { reach=Math.Max(reach,f.Dome.X*1.15); hTop=Math.Max(hTop,f.Dome.Y*1.15); }
            f.Reach=reach; f.Top=Math.Max(hTop,1);
            return f;
        }
    }
}
