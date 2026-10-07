using System;
namespace VolumetricExplosionFX.Core
{
    // Proxy coordinates: [-1,1], scaled by BoxRadius in metres.
    public struct VolumeState
    {
        public double BoxRadius, Age, Atmosphere;
        public double Scale;
        public bool Ground, Water;
        public double ShellRadius, ShellThickness, ShellOpacity, ShellErosion;
        public double Heat, CoreRadius, Glow;
        public double Cloud, Vapor, Dust, Rise;
        public double Fade;
        public double FireRadius, FireHeat, FireRise, FireSoot, FireBody, SmokeR, SmokeG, SmokeB;
        public double WhiteFlame;
        public double DustR, DustG, DustB;
        public LobeState Lobe0, Lobe1, Lobe2, Lobe3, Stem;
        public Vec3 BoundsCenter;
        public double BoundsRadius, NoiseRadius, Roughness, StemDensity, Pulse;
        // Roll angle in radians.
        public double Roll;
        public double ShockRadius, ShockStrength, Haze, HazeHeight;
        public bool Visible { get { return Fade>0 && (Heat>0.002 || ShellOpacity*(1-ShellErosion)>0.002 || Cloud>0.002 || Dust>0.002 || FireBody>0.002); } }
        public LobeState Lobe(int i)
        {
            switch(i) { case 0: return Lobe0; case 1: return Lobe1; case 2: return Lobe2; case 3: return Lobe3; default: return Stem; }
        }
        public void SetLobe(int i,LobeState l)
        {
            switch(i) { case 0: Lobe0=l; break; case 1: Lobe1=l; break; case 2: Lobe2=l; break; case 3: Lobe3=l; break; default: Stem=l; break; }
        }
    }
    public static class VolumeEvolution
    {
        public const double ShellLimit=0.9;
        static readonly Vec3 Up=new Vec3(0,1,0);
        static double Smooth(double a,double b,double x) { double t=Numbers.Clamp((x-a)/(b-a),0,1); return t*t*(3-2*t); }
        public static double SpreadMax(double air) { return 1+0.6*air+0.5*(1-air); }
        public static double Spread(double air,double tf,double age) { return 1+(SpreadMax(air)-1)*Smooth(0.5*tf,2.2*tf,age); }
        const double Participating=0.3;
        public const double LiftFactor=0.55;
        public static double FireballRadius(FxPlan plan)
        { double m=Participating*Numbers.Clamp(plan.BurnableKg,0,1e7); return m<1?0:Numbers.Clamp(2.9*Math.Pow(m,1.0/3),1,60); }
        public static double FireballDuration(FxPlan plan)
        { double m=Participating*Numbers.Clamp(plan.BurnableKg,0,1e7); return Numbers.Clamp(m<30000?0.45*Math.Pow(m,1.0/3):2.6*Math.Pow(m,1.0/6),0.8,9); }
        public static double DomeRadius(FxPlan plan) { return Numbers.Clamp(Math.Max(plan.Radius*2.4,FireballRadius(plan)*1.2),1.2,80); }
        public static double BoxRadius(FxPlan plan,BlastLayout layout)
        {
            double box=DomeRadius(plan)/ShellLimit;
            if(layout!=null&&layout.RadiusMeters>0) box=Math.Max(box,layout.ExtentMeters/0.97);
            return box*1.25;
        }
        public static double TimeScale(FxPlan plan) { return Numbers.Clamp(0.1+0.013*DomeRadius(plan),0.12,0.9); }
        public static double VolumeDuration(FxPlan plan)
        {
            double dome=Numbers.Clamp(TimeScale(plan)*(5+5*Numbers.Clamp(plan.Atmosphere,0,1)),1.2,7);
            double fire=FireballRadius(plan)>0?FireballDuration(plan)*1.15*(1.1+1.0*Numbers.Clamp(plan.Atmosphere,0,1))+(plan.VisualEnergyScore>3.5?0.8:0):0;
            return Numbers.Clamp(Math.Max(dome,fire),1.2,14);
        }
        public static VolumeState Evaluate(FxPlan plan,double age,bool explosions,bool burst,bool ground=false,bool water=false)
        { return Evaluate(plan,age,explosions,burst,ground,water,BlastLayout.Default(plan,ground&&!water,water)); }
        public static VolumeState Evaluate(FxPlan plan,double age,bool explosions,bool burst,bool ground,bool water,BlastLayout layout)
        {
            age=Numbers.Clamp(age,0,60);
            double air=Numbers.Clamp(plan.Atmosphere,0,1);
            ground=ground&&!water;
            double fire=explosions?Numbers.Clamp(plan.FireFraction,0,1):0;
            double vapor=burst?Numbers.Clamp(plan.VaporFraction,0,1):0;
            double kinetic=ground||water?Smooth(300,2500,plan.ImpactSpeed)*0.9:0;
            double dust=ground?Smooth(1.5,5,plan.VisualEnergyScore):0;
            double spray=water?Smooth(1.5,5,plan.VisualEnergyScore):0;
            double strength=Math.Max(Math.Max(fire,kinetic),Math.Max(Math.Max(dust,spray),vapor));
            double tau=TimeScale(plan);
            double box=BoxRadius(plan,layout), domeLocal=DomeRadius(plan)/box;
            double k=domeLocal/ShellLimit;
            VolumeState s=new VolumeState { BoxRadius=box, Age=age, Atmosphere=air, Ground=ground, Water=water, Scale=k,
                SmokeR=plan.SmokeR, SmokeG=plan.SmokeG, SmokeB=plan.SmokeB, Roughness=layout!=null?layout.Roughness:0.6,
                WhiteFlame=Numbers.Clamp(plan.WhiteFlame,0,1), DustR=plan.DustR, DustG=plan.DustG, DustB=plan.DustB };
            double decelerated=1-Math.Exp(-age/(0.35*tau)), coasting=Math.Min(1,age/(tau*0.6));
            s.ShellRadius=Math.Max(0.03,domeLocal*(air*decelerated+(1-air)*coasting));
            s.ShellThickness=(0.05+0.05*Smooth(0,4*tau,age))*k;
            double shellBase=(air*strength*(ground||water?1:0.7)+(1-air)*0.1*Math.Max(Math.Max(fire,kinetic),vapor))*(0.35+0.65*strength);
            shellBase*=1-0.3*fire;
            shellBase*=water?0.2:1-0.45*air;
            double shellLife=tau*(0.5+0.1*air);
            s.ShellOpacity=Numbers.Clamp(shellBase*Smooth(0,0.03,age)*Math.Exp(-age/shellLife),0,1);
            s.ShellErosion=0.85*Smooth(tau*0.3,tau*(1.6+1.0*(1-air)),age);
            double heat0=Math.Max(fire,kinetic);
            s.Heat=Numbers.Clamp(heat0*Math.Exp(-age/(0.18+0.22*air+0.004*DomeRadius(plan))),0,1);
            s.CoreRadius=(0.07+0.2*(1-Math.Exp(-age/0.5)))*k;
            s.Glow=Numbers.Clamp(heat0*Math.Exp(-age/0.06),0,1);
            double cold=vapor*(air>0.05?Smooth(0,0.3,age):Math.Exp(-age*3));
            s.Cloud=Numbers.Clamp(cold,0,1);
            s.Vapor=s.Cloud>0?1:0;
            s.Rise=Math.Min(age*0.05,0.3)*air*k;
            s.Dust=Numbers.Clamp(Math.Max(dust,spray)*(0.3+0.7*air)*Smooth(0,0.4,age)*Math.Exp(-age/(tau*5)),0,1);
            double R=layout!=null?layout.RadiusMeters:FireballRadius(plan), tf=FireballDuration(plan)*(layout!=null?layout.Tempo:1);
            if(R>0&&fire>0.02&&layout!=null) EvaluateFireball(ref s,plan,layout,age,R,tf,air,fire,ground||water,box);
            s.ShockRadius=DomeRadius(plan)*(0.12+2.6*(1-Math.Exp(-age/(0.4*tau))))/box;
            s.ShockStrength=Numbers.Clamp(air*strength*Smooth(0,0.02,age)*Math.Exp(-age/(1.2*tau)),0,1);
            s.Haze=Numbers.Clamp(air*Math.Max(fire,kinetic*0.5)*Smooth(0.03,0.25,age)*Math.Exp(-age/(1.5+4*tau)),0,1);
            s.HazeHeight=Math.Max((0.6+0.9*Smooth(0,2,age))*k,s.FireRise+s.FireRadius*1.6);
            double duration=VolumeDuration(plan);
            s.Fade=Numbers.Clamp((duration-age)/(0.3*duration),0,1);
            return s;
        }
        static void EvaluateFireball(ref VolumeState s,FxPlan plan,BlastLayout layout,double age,double R,double tf,double air,double fire,bool surface,double box)
        {
            double burn=tf*(0.12+0.88*air)*(1-0.55*s.WhiteFlame);
            double riseFraction=air*Smooth(0.15*tf,1.2*tf,age);
            double lift=R*LiftFactor*riseFraction;
            double heatMax=0, pulse=0;
            double minX=1e9,minY=1e9,minZ=1e9,maxX=-1e9,maxY=-1e9,maxZ=-1e9;
            double grow0=air*(1-Math.Exp(-age/(0.1*tf)))+(1-air)*Math.Min(1,age/(0.12*tf));
            double spread0=Spread(air,tf,age);
            for(int i=0;i<BlastLayout.Count;i++)
            {
                LobeSeed seed=layout.Lobes[i];
                double ta=age-seed.Delay;
                if(!seed.Active||ta<0) { s.SetLobe(i,new LobeState { Axis=Up, Stretch=1 }); continue; }
                double grow=air*(1-Math.Exp(-ta/(0.1*tf)))+(1-air)*Math.Min(1,ta/(0.12*tf));
                double spread=Spread(air,tf,ta);
                double radius=seed.Size*R*grow*spread;
                Vec3 carry=seed.Drift*(seed.Tau*(1-Math.Exp(-ta/Math.Max(seed.Tau,1e-3))));
                double groundLift=surface?0.45*radius:0;
                Vec3 offset=seed.Relative?seed.Offset*(grow0*spread0):seed.Offset;
                Vec3 center=offset+carry+Up*(lift+groundLift);
                double stretch=1+(seed.Stretch-1)*Math.Exp(-ta/(0.8*tf));
                if(i==0&&Math.Abs(seed.Axis.Y)>0.7) stretch*=1-0.3*riseFraction;
                double heat=seed.Heat*layout.HeatBias*fire*(1-Smooth(0.25*burn,1.1*burn,ta));
                if(seed.Delay>0) pulse=Math.Max(pulse,seed.Heat*fire*Math.Exp(-ta/0.12));
                heatMax=Math.Max(heatMax,heat);
                var lobe=new LobeState { Center=center*(1/box), Radius=radius/box, Axis=seed.Axis, Stretch=stretch, Heat=heat,
                    Displacement=(carry+Up*lift)*(1/box) };
                s.SetLobe(i,lobe);
                double reach=lobe.Radius*Math.Max(stretch,1)*BlastLayout.SurfaceReach;
                minX=Math.Min(minX,lobe.Center.X-reach); maxX=Math.Max(maxX,lobe.Center.X+reach);
                minY=Math.Min(minY,lobe.Center.Y-reach); maxY=Math.Max(maxY,lobe.Center.Y+reach);
                minZ=Math.Min(minZ,lobe.Center.Z-reach); maxZ=Math.Max(maxZ,lobe.Center.Z+reach);
            }
            s.FireRadius=s.Lobe0.Radius; s.FireRise=s.Lobe0.Center.Y; s.FireHeat=Numbers.Clamp(heatMax,0,1); s.Pulse=pulse;
            s.Roll=air*Math.Min(2.2,0.8*age/Math.Max(tf,0.1));
            s.NoiseRadius=Math.Max(0.02,R*(0.35+0.65*grow0)*spread0/box/Math.Max(layout.NoiseScale,0.5));
            s.FireSoot=Numbers.Clamp(plan.Soot*(1+2*layout.SootBias)*(0.4+0.6*Smooth(0.02*tf,0.5*tf,age)),0,1);
            double keep=air>0.05?1-Smooth(0.7*tf,2.0*tf,age):1-Smooth(0.03*tf,0.2*tf,age);
            double dense=0.12+0.88*air+0.6*(1-air)*Math.Exp(-age/(0.06*tf));
            s.FireBody=Numbers.Clamp(fire*dense*Smooth(0,0.03,age)*keep/(spread0*spread0*spread0)/layout.Expansion,0,1);
            double head=s.Lobe0.Center.Y;
            if(layout.Stem>0&&surface&&head>0&&lift>0.2*R)
            {
                double stemRadius=0.28*R*spread0/box;
                s.Stem=new LobeState { Center=new Vec3(s.Lobe0.Center.X,head*0.5,s.Lobe0.Center.Z), Radius=stemRadius, Axis=Up,
                    Stretch=Math.Max(1,head*0.5/stemRadius), Displacement=Up*(lift*0.5/box) };
                s.StemDensity=layout.Stem*Smooth(0.25*R,0.6*R,lift)*keep;
                minY=Math.Min(minY,-0.05);
            }
            else s.Stem=new LobeState { Axis=Up, Stretch=1 };
            if(maxX<minX) { s.BoundsRadius=0; return; }
            s.BoundsCenter=new Vec3((minX+maxX)*0.5,(minY+maxY)*0.5,(minZ+maxZ)*0.5);
            s.BoundsRadius=0.5*Math.Sqrt((maxX-minX)*(maxX-minX)+(maxY-minY)*(maxY-minY)+(maxZ-minZ)*(maxZ-minZ));
        }
        public static void Pack(VolumeState s,double seed,out Vec4 shell,out Vec4 core,out Vec4 cloud,out Vec4 context)
        {
            Vec4 fireball,smoke;Pack(s,seed,out shell,out core,out cloud,out context,out fireball,out smoke);
        }
        public static void Pack(VolumeState s,double seed,out Vec4 shell,out Vec4 core,out Vec4 cloud,out Vec4 context,out Vec4 fireball,out Vec4 smoke)
        {
            fireball=new Vec4(s.NoiseRadius,s.FireHeat,s.Roughness,s.FireBody);
            smoke=new Vec4(s.SmokeR,s.SmokeG,s.SmokeB,s.FireSoot);
            shell=new Vec4(s.ShellRadius,s.ShellThickness,s.ShellOpacity,s.ShellErosion);
            core=new Vec4(s.CoreRadius,s.Heat,s.Glow,s.Rise);
            cloud=new Vec4(s.Cloud,s.Dust,s.Vapor,s.Fade);
            context=new Vec4(s.Ground?1:(s.Water?2:0),s.Atmosphere,s.Age,seed%997);
        }
        public static void PackLobe(VolumeState s,int i,out Vec4 center,out Vec4 axis,out Vec4 displacement)
        {
            LobeState l=s.Lobe(i);
            center=new Vec4(l.Center.X,l.Center.Y,l.Center.Z,l.Radius);
            axis=new Vec4(l.Axis.X,l.Axis.Y,l.Axis.Z,Math.Max(l.Stretch,0.2));
            displacement=new Vec4(l.Displacement.X,l.Displacement.Y,l.Displacement.Z,i<4?l.Heat:s.StemDensity);
        }
        public static Vec4 Bounds(VolumeState s) { return new Vec4(s.BoundsCenter.X,s.BoundsCenter.Y,s.BoundsCenter.Z,s.BoundsRadius); }
        public static Vec4 Motion(VolumeState s) { return new Vec4(s.Roll,s.WhiteFlame,0,0); }
        public static Vec4 Dust(VolumeState s) { return new Vec4(s.DustR,s.DustG,s.DustB,0); }
        public static Vec4 Flare(VolumeState s)
        {
            double height=(s.Ground||s.Water?s.CoreRadius*0.7:0)+s.Rise+s.FireRise*0.5;
            double damp=s.FireBody>0.02?0.55+0.45*s.Glow:1;
            return new Vec4((0.22+0.9*s.Glow+0.2*s.Heat)*s.Scale,Numbers.Clamp(s.Glow*0.9+s.Heat*0.25,0,1)*s.Fade*damp,height,
                Numbers.Clamp(Math.Max(s.Heat*1.2,s.Glow*1.6),0,1));
        }
        public static void Distortion(VolumeState s,double groundFire,out Vec4 shock,out Vec4 haze)
        {
            double width=(0.045+0.06*Math.Min(1,s.ShockRadius/Math.Max(s.Scale,1e-3)))*s.Scale;
            double hazeAmount=Math.Max(Math.Max(s.Haze,s.FireHeat*0.8*s.Atmosphere),Numbers.Clamp(groundFire,0,1)*0.7*s.Atmosphere);
            double half=Math.Max(s.ShockStrength>0.002?s.ShockRadius+3*width:0,hazeAmount>0.002?Math.Max(s.HazeHeight,0.9)*1.15:0);
            shock=new Vec4(s.ShockRadius,0.03*s.ShockStrength,width,half);
            haze=new Vec4(0.004*hazeAmount,s.HazeHeight,0.45*s.Scale,s.Age);
        }
        public static bool DistortionVisible(VolumeState s,double groundFire)
        { return s.ShockStrength>0.002||s.Haze>0.002||groundFire*s.Atmosphere>0.003; }
        public static double LightIntensity(VolumeState s)
        { return Numbers.Clamp(1.4*s.Heat+1.6*s.Glow+1.3*s.FireHeat+1.2*s.Pulse,0,2.2+1.3*s.Glow)*s.Fade*(0.6+0.4*s.Atmosphere); }
        public static double LightHeight(VolumeState s) { return Math.Max(Flare(s).Z,s.FireHeat>0.05?s.FireRise:0); }
        public static void ShockFront(FxPlan plan,BlastLayout layout,out double speed,out double timeConstant,out double reach)
        {
            double tau=TimeScale(plan);
            timeConstant=0.4*tau;
            reach=2.6*DomeRadius(plan);
            speed=reach/timeConstant;
        }
    }
}
