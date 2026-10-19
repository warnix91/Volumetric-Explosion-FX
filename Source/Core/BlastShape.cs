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
    public struct LobeState
    {
        public Vec3 Center, Axis, Displacement; public double Radius, Stretch, Heat;
        // Baked-simulation lobes (1.1): where the simulation box sits, its yaw/scale/frame and the sphere that holds the gas.
        public Vec3 SimOrigin; public double SimCos, SimSin, SimScale, SimFrame, SimAfter;
        public double SimCapLow, SimCapRadius, SimCapTop;   // proxy units: height of the cloud's underside, half width of its gas (1.1)
        public double SimCapFrac;               // the same underside as a share of the baked box's height (the shader's cut) // SimAfter: seconds past the last whole frame
        public bool Sim;
    }
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
    // 1.1: how a propellant tank lets go. Mixed is the general look (random lobes).
    public enum TankCharacter { Mixed, Fireball, Jet, Chain, Flash }
    // 1.1: how a nuclear detonation looks, from where it goes off (Glasstone & Dolan ch. II): on the ground (a dirty cloud on
    // a thick dust stem), low in the air (a cleaner cloud; a thinner dust stem sucked up from the ground rises to meet it, or
    // not), high (a ring cloud and no stem).
    public enum NukeKind { None, Surface, LowAir, HighAir }
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
        // 1.1 simulated fireball character, own to each explosion: tall or squat, leaning, smooth or ragged.
        public double SimHeight=1, SimLeanX, SimLeanZ, SimWarp=1;
        // 1.1 brutal pass: how much wider than tall this explosion spreads (the simulated copies are scaled up by it and
        // squashed back to their height). SimTall: the vertical scale the shader applies on top of it.
        public double SimBreadth=1;
        // 1.1: cold dust (a building coming down) is heavier than the air: it rolls out along the ground and hangs low
        // instead of rising like a fireball (it floated up into a white cumulus).
        public bool SimHugsGround;
        // 1.1 nuclear detonation: its kind, megaton-class (condensation rings, ice cap), humid air (Wilson cloud, rings),
        // how far the cloud climbs after the baked run (m), how much it spreads, and the burst height (m).
        public NukeKind Nuke; public bool NukeThermo, NukeHumid, NukeWilson; public double NukeRise, NukeSpread, NukeGround, NukeTop, NukeTau;
        public double SimTall { get { return SimHeight/Math.Max(SimBreadth,0.5); } }
        public int SimRun, SimAirRun;   // which baked run this explosion takes on the ground and in the air (FireSim.GroundRuns, AirRuns)
        // 1.1 warheads: one cloud from one point (no satellite blobs), and how fast it reaches its size (1 = fuel fire, more = faster).
        public bool SingleCloud; public double Burst=1;
        // 1.1: how a propellant tank lets go, from how full it was (and chance): see TankCharacter.
        public TankCharacter Character;
        // 1.1: how humid the air is around this blast (0..1, drawn per explosion): above ~0.4 a big blast flashes a
        // condensation cloud (Wilson cloud) behind its shock front.
        public double Humidity;
        // 1.1: the second smoke mixed through this cloud (rgb, amount); see Chemistry.SmokeMix.
        public Vec4 SmokeMix;
        // How much slower the cloud phase of the baked run plays (1 = as baked): a warhead's cloud hangs instead of lifting away.
        public double SimSlow=1, SimLinger;
        // 1.1: the heading of the baked run (radians, NaN: at random) and its stretch along it (below 1: drawn out).
        public double SimYaw=double.NaN, SimStretch=1;
        // 1.1: how much slower the baked run plays (a warhead's fireball turned into a rising mushroom of smoke
        // within a second: 1.0 kept a fireball).
        public double SimPace=1;
        public static double ExpansionFor(double air)
        {
            air=Numbers.Clamp(air,0,0.999);
            if(air<0.05) return 1;
            double relative=Math.Max(-0.35*Math.Log(1-air)/1.225,1e-3);
            return Numbers.Clamp(Math.Pow(1/relative,1.0/3),1,2.5);
        }
        double spreadMax, liftMax;
        public int ActiveCount { get { int n=0; for(int i=0;i<Count;i++) if(Lobes[i].Active) n++; return n; } }
        // Limits how far a lobe's momentum carries it (drift x time constant).
        static LobeSeed Reach(LobeSeed s,double limit)
        {
            double carry=s.Drift.Length*s.Tau;
            if(carry>limit&&carry>1e-9) s.Drift=s.Drift*(limit/carry);
            return s;
        }
        // Full tanks mostly boil open into one fireball, half-empty ones tear and jet, big stages go in steps, nearly empty
        // ones flash; the rest keep the mixed look. A chance in each band keeps two identical tanks from always matching.
        public static TankCharacter ChooseCharacter(FxPlan plan,double chance)
        {
            double fill=Numbers.Clamp(plan.Fill,0,1);
            if(fill<0.12) return chance<0.7?TankCharacter.Flash:TankCharacter.Mixed;
            if(plan.BurnableKg>4000&&chance<0.4) return TankCharacter.Chain;
            if(fill>0.65) return chance<0.65?TankCharacter.Fireball:chance<0.85?TankCharacter.Jet:TankCharacter.Mixed;
            return chance<0.55?TankCharacter.Jet:chance<0.75?TankCharacter.Fireball:TankCharacter.Mixed;
        }
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
            var shape=new SplitRandom(seed^0x53484150u);   // own stream: the 1.0 layout draws stay unchanged
            L.SimHeight=shape.Range(0.78,1.28); L.SimWarp=shape.Range(0.65,1.5);
            double leanAngle=shape.Next()*Math.PI*2, lean=0.22*shape.Next()*shape.Next();
            L.SimLeanX=Math.Cos(leanAngle)*lean; L.SimLeanZ=Math.Sin(leanAngle)*lean;
            if(plan.Look!=null&&plan.Look.Modern&&Aftermath.SceneWind.Length>1e-6)
            {
                // With a scene breeze, a rising cloud only leans downwind, a little more the stronger the wind.
                Vec3 breeze=Aftermath.Wind(seed,plan.Atmosphere,true); double b=breeze.Length;
                double bend=Math.Min(0.05*b,0.16)*(0.7+0.6*shape.Next());
                L.SimLeanX=b>1e-6?breeze.X/b*bend:0; L.SimLeanZ=b>1e-6?breeze.Z/b*bend:0;
            }
            double runPick=shape.Next();
            L.SimBreadth=plan.Look!=null&&plan.Look.Modern?shape.Range(1.0,1.32):1;
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
            // 1.1: a vehicle crashing with sideways speed throws its burning fuel forward: the fireball rolls on along the
            // ground with the momentum (tens of metres at aircraft speeds) instead of stopping where it hit.
            bool carryOn=plan.Look!=null&&plan.Look.Modern&&L.Form==BlastForm.Grazing;
            if(carryOn) { main.Drift=fwd*(vh*0.4); main.Tau=0.45; main=Reach(main,2.5*R); }
            L.Lobes[0]=Capped(main,cap);
            int next=1;
            if(sources!=null) for(int i=0;i<sources.Length&&next<Count;i++)
            {
                Vec3 off=ClampLength(sources[i].Offset,2.5*R);
                L.Lobes[next++]=Capped(new LobeSeed { Active=true, Offset=off, Delay=Numbers.Clamp(sources[i].Delay,0,1.5),
                    Size=Numbers.Clamp(0.45+0.4*Numbers.Clamp(sources[i].Weight,0,1),0.4,0.9), Heat=rnd.Range(0.85,1),
                    Axis=main.Axis, Stretch=main.Stretch, Drift=main.Drift*0.8, Tau=main.Tau },cap);
            }
            // 1.1 propellant tanks: four ways to let go, from how full the tank was and chance (own random stream).
            bool tankLook=plan.Look!=null&&plan.Look.Modern&&plan.Warhead==WarheadKind.None&&!water&&air>0.05&&plan.BurnableKg>20&&
                (plan.PartKind==PartKind.LiquidTank||plan.PartKind==PartKind.PressurizedTank||plan.PartKind==PartKind.Engine||plan.PartKind==PartKind.SolidBooster);
            var tank=new SplitRandom(seed^0x54414E4Bu);
            L.Humidity=new SplitRandom(seed^0x57494C53u).Next();
            L.SmokeMix=Chemistry.SmokeMix(plan,new SplitRandom(seed^0x534D4B58u).Next());
            L.Character=tankLook?ChooseCharacter(plan,tank.Next()):TankCharacter.Mixed;
            int extra;
            switch(L.Form)
            {
                case BlastForm.Rupture: extra=plan.Warhead!=WarheadKind.None&&plan.Look!=null&&plan.Look.Modern?0:1+(int)(rnd.Next()*3); break;
                case BlastForm.Water: extra=1+(int)(rnd.Next()*2); break;
                default: extra=2+(int)(rnd.Next()*2); break;
            }
            if(L.Character==TankCharacter.Fireball||L.Character==TankCharacter.Chain) extra=0;
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
                        lobe.Axis=fwd; lobe.Stretch=1+Math.Min(0.6,vh/250); lobe.Relative=false;
                        if(carryOn) { lobe.Drift=lobe.Drift*1.6; lobe.Tau*=2.5; lobe.Delay+=0.05*k; lobe=Reach(lobe,3.5*R); }
                        break;
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
            if(plan.Warhead!=WarheadKind.None&&plan.Look!=null&&plan.Look.Modern)
            {
                // A detonation is one point: a single cloud, tall for a ground burst; a nuclear fireball reaches its size at once.
                L.SingleCloud=true;
                for(int i=1;i<Count;i++) if(sources==null||i>sources.Length) L.Lobes[i]=new LobeSeed();
                L.SimLeanX*=0.3; L.SimLeanZ*=0.3;
                if(plan.Warhead==WarheadKind.Bomb) { L.SimHeight=0.85; L.Burst=2.5; L.SimSlow=4; L.SimLinger=2.5; }   // earth: a broad, squat dirt cloud
                // A nuclear fireball rises and rolls into its cap as baked (its own run), then the cloud hangs and spreads.
                if(plan.Warhead==WarheadKind.Nuke) { L.SimHeight=1; L.SimBreadth=1; L.SimLeanX=0; L.SimLeanZ=0; L.Burst=8; L.SimSlow=1; L.SimLinger=1.5; }
                if(plan.Warhead==WarheadKind.HighExplosive||plan.Warhead==WarheadKind.Shell) { L.Burst=2; L.SimSlow=3; L.SimLinger=1.5; }
                if(plan.Warhead==WarheadKind.HighExplosive||plan.Warhead==WarheadKind.Shell||plan.Warhead==WarheadKind.Bomb||plan.Warhead==WarheadKind.ContinuousRod||
                    plan.Warhead==WarheadKind.Kinetic||plan.Warhead==WarheadKind.Emp) L.SimPace=1.5;
                if(plan.Warhead==WarheadKind.Thermobaric||plan.Warhead==WarheadKind.Incendiary) L.SimSlow=2;
                // A collapsing building: dust bursts out low and wide from the base and rolls on, slowly, then hangs.
                if(plan.Warhead==WarheadKind.Structure) { L.SimHugsGround=plan.FireFraction<0.2; L.SimHeight=L.SimHugsGround?0.6:0.5; if(L.SimHugsGround) L.SimBreadth=1.4; L.SimWarp=1.4; L.Burst=0.8; L.SimSlow=4; L.SimLinger=4; L.SimLeanX*=0.5; L.SimLeanZ*=0.5; }
            }
            else if(plan.VisualEnergyScore>3.5&&air>0.05&&!water&&mood.Next()<0.45)
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
            switch(L.Character)
            {
                case TankCharacter.Fireball:
                    // A full tank boils open at once (BLEVE): one huge round fireball that rises and rolls into a mushroom.
                    // (Kept close to the bake's own proportions: a strong vertical stretch and a smooth warp made a flat,
                    // sheet-like cloud and long dark strings under it.)
                    L.SingleCloud=true; L.SimHeight=tank.Range(1.08,1.18); L.SimLeanX*=0.3; L.SimLeanZ*=0.3; L.SimWarp=Math.Max(L.SimWarp,0.9);
                    L.Burst=1.35; L.SimSlow=1.4; L.SimLinger=0.4;
                    L.Lobes[0].Size=Math.Max(L.Lobes[0].Size,1); L.Lobes[0].Stretch=1; break;
                case TankCharacter.Jet:
                {
                    // A half-empty tank tears open on one side: the burning propellant jets out sideways and the cloud leans after it.
                    double a=tank.Next()*Math.PI*2; var d=new Vec3(Math.Cos(a),0,Math.Sin(a));
                    // The jet tears out sideways; the cloud above only leans a little after it (the wind does the rest).
                    L.SimLeanX+=d.X*tank.Range(0.12,0.2); L.SimLeanZ+=d.Z*tank.Range(0.12,0.2); L.SimWarp=tank.Range(1.3,1.6);
                    var tilt=new Vec3(d.X*0.8,0.6,d.Z*0.8); L.Lobes[0].Axis=tilt*(1/tilt.Length); L.Lobes[0].Stretch=1.35;
                    for(int j=0;j<2&&next<Count;j++)
                        L.Lobes[next++]=Capped(new LobeSeed { Active=true, Heat=1, Axis=d, Stretch=tank.Range(2.4,3.0), Tau=0.35,
                            Offset=d*(R*(0.5+0.45*j))+Up*(R*0.1), Drift=d*(R*tank.Range(2.5,3.5)), Size=tank.Range(0.7,0.85)-0.12*j, Delay=0.05*j+tank.Range(0,0.05) },cap);
                    break;
                }
                case TankCharacter.Chain:
                    // A big stage goes in steps: the first tank, then the others a moment later (cook-off).
                    for(int k=1;k<Count&&next<Count;k++)
                    {
                        double a=tank.Next()*Math.PI*2, el=tank.Range(0.1,0.6);
                        var d=new Vec3(Math.Cos(el)*Math.Cos(a),Math.Sin(el),Math.Cos(el)*Math.Sin(a));
                        L.Lobes[next++]=Capped(new LobeSeed { Active=true, Heat=1, Axis=Up, Stretch=tank.Range(0.95,1.15), Tau=0.3,
                            Offset=d*(R*tank.Range(0.5,0.9)), Drift=Up*tank.Range(2,6), Size=tank.Range(0.6,0.85),
                            Delay=0.35*k+tank.Range(0.1,0.5) },cap);
                    }
                    L.SimSlow=1.3; break;
                case TankCharacter.Flash:
                    // A nearly empty tank: its vapour flashes over in a quick, bright, short-lived burst with little smoke left
                    // (a pale cloud of vapour rather than black smoke).
                    L.Burst=1.8; L.SimHeight*=0.85; L.SimLinger=0; L.SimSlow=0.85;
                    plan.Soot*=0.25; plan.SmokeFraction*=0.45; plan.WhiteFlame=Math.Max(plan.WhiteFlame,0.35);
                    plan.VaporFraction=Math.Max(plan.VaporFraction,0.5);
                    plan.SmokeR+=(0.8-plan.SmokeR)*0.6; plan.SmokeG+=(0.79-plan.SmokeG)*0.6; plan.SmokeB+=(0.77-plan.SmokeB)*0.6; break;
            }
            // 1.1: the impact shapes the fireball itself (the baked run stood upright and alike whatever the crash): a fast,
            // shallow crash draws it out along the path with its top running on ahead; a hard, steep hit
            // flattens and spreads it; a slow burst rises as a ball; more fuel, a broader fire.
            if(plan.Look!=null&&plan.Look.Modern&&plan.Warhead!=WarheadKind.Nuke&&plan.Warhead!=WarheadKind.Structure&&!water&&air>0.05)
            {
                double heavy=Numbers.Clamp(Math.Log10(Math.Max(plan.BurnableKg,10)/300)/2,0,1);   // 300 kg .. 30 t
                switch(L.Form)
                {
                    case BlastForm.Grazing:
                    {
                        double k=Numbers.Clamp(vh/220,0,1);
                        L.SimYaw=Math.Atan2(fwd.Z,fwd.X); L.SimStretch=1/(1+0.45*k);
                        L.SimLeanX=fwd.X*0.22*k; L.SimLeanZ=fwd.Z*0.22*k; break;
                    }
                    case BlastForm.Vertical:
                    {
                        double k=Numbers.Clamp(down/200,0,1);
                        L.SimHeight*=1-0.3*k; L.SimBreadth*=1+0.3*k; break;
                    }
                    case BlastForm.Rupture: L.SimHeight*=1.12; break;
                }
                L.SimBreadth*=1+0.15*heavy;
            }
            // 1.1: the shape of the fireball, among those that suit this kind of explosion.
            WarheadKind kind=plan.Look!=null&&plan.Look.Modern?plan.Warhead:WarheadKind.None;
            L.SimRun=FireSim.ChooseGround(kind,L.Character,runPick); L.SimAirRun=FireSim.ChooseAir(kind,runPick);
            if(kind==WarheadKind.Nuke)
            {
                double kt=Math.Max(plan.WarheadTnt/1e6,0.001), Rn=VolumeEvolution.FireballRadius(plan);
                L.NukeGround=ground||water?0:Math.Max(plan.BurstHeight,0);
                L.Nuke=ground||water?NukeKind.Surface:L.NukeGround<5*Rn?NukeKind.LowAir:NukeKind.HighAir;
                L.NukeThermo=kt>=300; L.NukeHumid=true;   // (condensation rings and collars)
                L.NukeWilson=true;
                // The cloud climbs at 100-150 m/s for a megaton, slower for smaller yields, and keeps climbing for minutes
                // (here: the asymptote of a 60 s ease) while its cap spreads to several fireball radii.
                L.NukeRise=Numbers.Clamp(150*Math.Pow(kt/1000,0.25),25,260)*60;
                L.NukeSpread=L.Nuke==NukeKind.HighAir?0.9:1.2;
                // Where the cloud stops (Glasstone & Dolan fig. 2.16 / 9.96, mid-latitudes): its top about 3 km x yield^0.3
                // above the burst (1.5 kt ~3.5 km, 10 kt ~6 km, 20 kt ~7.5 km, 1 Mt ~20 km), its cap about 0.3 of that in
                // radius (10 kt: 19 000 ft top, 10 000 ft base, 10 000 ft across). The real cloud takes ~10 minutes to get
                // there; here it covers most of the way in two to four (time constant 40 s at 0.25 kt to 130 s at 1 Mt).
                // (Below 20 kt the height follows the yield more steeply: a 0.25 kt and a 1.5 kt warhead looked alike.)
                L.NukeTop=kt<20?Numbers.Clamp(7510*Math.Pow(kt/20,0.45),400,7510):Numbers.Clamp(3048*Math.Pow(kt,0.301),7510,21000);
                // It climbs fast at first (about 80 m/s at 1.5 kt, 170 m/s at 20 kt), slowing as it nears the top.
                L.NukeTau=Numbers.Clamp(25+15*Math.Log10(kt),18,90);
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
