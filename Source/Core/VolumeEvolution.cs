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
        public double Condensation;   // 1.1: condensation cloud strength (0..1)
        public double Heat, CoreRadius, Glow;
        public double Cloud, Vapor, Dust, Rise;
        public double Fade;
        // Share of the cooled gas that has broken up into tendrils (0 fresh, 1 gone); 1.1 looks only.
        public double Dissolve;
        // The profile asked for the 1.1 renderer extras (bigger flash, gentler refraction).
        public bool Modern;
        // The fireball comes from the baked fluid simulation (lobes carry simulation boxes, see FireSimTable).
        public bool Sim;
        // How many times larger the proxy box is than the 1.0 box: optical paths of the shock shell and the dust,
        // measured in proxy units, are multiplied by it so a larger box does not thin them out (0 = Classic, no change).
        public double ProxyGain;
        // 1.1 simulated fireball character: vertical scale, lean (x, z per unit height), warp amplitude (see BlastLayout).
        public Vec4 SimShape;
        public int SimRun;    // the baked run the copies use (FireSim.GroundRuns or AirRuns, by Ground)
        public double SimLinger;   // a warhead's cooled cloud stays dense (0: fuel explosions, as baked)
        public double FlameFaint;  // 1.1: how much fainter and clearer than a hydrocarbon flame it is (hydrogen; 0 normal)
        public double SimStem;     // 1: the late stem under the cloud thins out from the ground up (fuel explosions)
        public Vec4 SmokeMix;      // second smoke mixed through the cloud (rgb, amount)
        // 1.1 nuclear detonation (ground burst): on, how far the dust stem has risen (0..1), condensation ring, white-hot;
        // stem base (x, z), cloud underside height, stem radius; ring major radius, minor radius, height (proxy units).
        public Vec4 Nuke, NukeStem, NukeRing;
        // ground under it (proxy y), dust skirt, stem density, condensation rings; ice cap: cap centre y, cap radius, amount,
        // top; Wilson cloud: radius, amount, thickness, how far it has turned from a dome into a ring.
        public Vec4 NukeGround, NukeIce, NukeWilson, NukeCap;
        // Look-profile multiplier of the explosion light.
        public double LightScale;
        public double FireRadius, FireHeat, FireRise, FireSoot, FireBody, SmokeR, SmokeG, SmokeB;
        public double WhiteFlame;
        public double DustR, DustG, DustB;
        public LobeState Lobe0, Lobe1, Lobe2, Lobe3, Stem;
        public Vec3 BoundsCenter;
        public double BoundsRadius, NoiseRadius, Roughness, StemDensity, Pulse;
        // Roll angle in radians.
        public double Roll;
        public double ShockRadius, ShockStrength, Haze, HazeHeight;
        // The 1.0 blast wave's ring (radius in proxy units, strength): an ordinary blast's wave in the air is drawn
        // as in 1.0 again, the front above still drives the ground ring of dust, the jolt and a nuclear wave.
        public double RingRadius, RingStrength, RingScale;   // RingScale: the dome radius (proxy units), the ring's bend is sized on it
        public double RingReach;   // a nuclear ring fades out from half this far to this far (m)
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
        static readonly LookProfile Classic=LookProfile.Classic();
        // Set once the baked fire simulation texture is loaded (the bench and the game); 1.1 looks then use it.
        public static bool UseSim;
        public static LookProfile Look(FxPlan plan) { return plan!=null&&plan.Look!=null?plan.Look:Classic; }
        static double Smooth(double a,double b,double x) { double t=Numbers.Clamp((x-a)/(b-a),0,1); return t*t*(3-2*t); }
        public static double SpreadMax(double air) { return 1+0.6*air+0.5*(1-air); }
        public static double Spread(double air,double tf,double age) { return 1+(SpreadMax(air)-1)*Smooth(0.5*tf,2.2*tf,age); }
        const double Participating=0.3;
        public const double LiftFactor=0.55;
        public static double FireballRadius(FxPlan plan)
        {
            double m=Participating*Numbers.Clamp(plan.BurnableKg,0,1e7);
            if(m<1) return 0;
            if(plan.WarheadTnt>0&&plan.Warhead!=WarheadKind.Structure&&plan.Look!=null&&plan.Look.Modern)
            {
                // 1.1: a warhead's fireball scales with its real power. Nuclear: about 170 m x yield(kt)^(1/3) (a 1.5 kt
                // warhead ~200 m, 100 kt ~800 m, 1 Mt ~1.6 km); the fuel cap made every nuke the same 300 m ball.
                // Conventional: 4.2 m x TNT(kg)^0.38 (25 kg ~14 m, 250 kg ~34 m, 1 t ~58 m), wider apart than the cube root
                // of the burnt mass (bombs and missiles looked alike), times the kind's own fire (thermobaric bigger).
                if(plan.Warhead==WarheadKind.Nuke) return Numbers.Clamp(61*Math.Pow(plan.WarheadTnt/1e6,0.4),30,1600);
                double kind=Math.Pow(Numbers.Clamp(plan.BurnableKg/(7*plan.WarheadTnt),0.3,6),1.0/3);
                return Numbers.Clamp(4.2*Math.Pow(plan.WarheadTnt,0.38)*kind,1.5,95);
            }
            // 1.1 brutal pass: a detonation's fireball and the earth and smoke it throws up fill a third more of the sky.
            double brutal=plan.Warhead!=WarheadKind.None&&plan.Warhead!=WarheadKind.Structure&&plan.Look!=null&&plan.Look.Modern?1.3:1;
            return Numbers.Clamp(2.9*Math.Pow(m,1.0/3)*brutal,1,plan.Warhead==WarheadKind.Nuke?300:60*brutal);
        }
        public static double FireballDuration(FxPlan plan)
        {
            // A nuclear fireball burns and rises for longer the bigger it is (1.5 kt ~6 s, 100 kt ~10 s, 1 Mt ~12 s).
            if(plan.Warhead==WarheadKind.Nuke&&plan.WarheadTnt>0&&plan.Look!=null&&plan.Look.Modern)
                return Numbers.Clamp(5.5+2.2*Math.Log10(1+plan.WarheadTnt/1e6),4,14);
            double m=Participating*Numbers.Clamp(plan.BurnableKg,0,1e7); return Numbers.Clamp(m<30000?0.45*Math.Pow(m,1.0/3):2.6*Math.Pow(m,1.0/6),0.8,9);
        }
        public static double DomeRadius(FxPlan plan) { return Numbers.Clamp(Math.Max(plan.Radius*2.4,FireballRadius(plan)*1.2),1.2,plan.Warhead==WarheadKind.Nuke?2400:110); }
        static double ClassicBox(FxPlan plan,BlastLayout layout)
        {
            double box=DomeRadius(plan)/ShellLimit;
            if(layout!=null&&layout.RadiusMeters>0) box=Math.Max(box,layout.ExtentMeters/0.97);
            return box*1.25;
        }
        public static double BoxRadius(FxPlan plan,BlastLayout layout)
        {
            double box=DomeRadius(plan)/ShellLimit;
            if(layout!=null&&layout.RadiusMeters>0) box=Math.Max(box,layout.ExtentMeters/0.97);
            box*=1.25*(Look(plan).Modern?1.3:1);   // room for the hot gas to keep rising
            if(layout!=null&&UseSim&&Look(plan).Modern) box=Math.Max(box,SimBox(layout));
            return box;
        }
        // Proxy box that holds every baked-simulation lobe for as long as it lives.
        static double SimBox(BlastLayout layout)
        {
            double need=0, spreadMax=SpreadMax(1), refRadius, maxXZ, maxY;
            FireSim.Extents(layout.SimRun,layout.SimAirRun,out refRadius,out maxXZ,out maxY);
            for(int i=0;i<BlastLayout.Count;i++)
            {
                LobeSeed seed=layout.Lobes[i];
                if(!seed.Active) continue;
                double peak=seed.Size*layout.RadiusMeters*1.3*(1+0.35*(spreadMax-1))*layout.SimBreadth;
                double scale=peak/refRadius;
                double reach=seed.Offset.Length*(seed.Relative?spreadMax:1)+seed.Drift.Length*seed.Tau
                    +scale*Math.Max(maxXZ*1.2,(maxY+3.6)*Math.Max(1,layout.SimTall))
                      *(1+Math.Sqrt(layout.SimLeanX*layout.SimLeanX+layout.SimLeanZ*layout.SimLeanZ));
                need=Math.Max(need,reach);
                if(layout.Nuke!=NukeKind.None)
                {
                    // A nuclear cloud climbs kilometres above its baked run and spreads: the box holds its top (and the
                    // ground under an air burst) where the climb eases to a stop, at 0.82 of the box.
                    double top=Math.Max(layout.NukeTop,scale*maxY)*1.04+layout.NukeGround;
                    need=Math.Max(need,Math.Max(top/0.8,Math.Max(0.3*layout.NukeTop,scale*maxXZ)*1.3));
                }
            }
            return need*1.05;
        }
        public static double TimeScale(FxPlan plan) { return Numbers.Clamp(0.1+0.013*DomeRadius(plan),0.12,0.9); }
        public static double VolumeDuration(FxPlan plan)
        {
            double dome=Numbers.Clamp(TimeScale(plan)*(5+5*Numbers.Clamp(plan.Atmosphere,0,1)),1.2,7);
            double fire=FireballRadius(plan)>0?FireballDuration(plan)*1.15*(1.1+1.0*Numbers.Clamp(plan.Atmosphere,0,1))+(plan.VisualEnergyScore>3.5?0.8:0):0;
            double life=Numbers.Clamp(Numbers.Clamp(Math.Max(dome,fire),1.2,14)*Look(plan).EffectLifetime,1.2,20);
            // 1.1 simulated fireball: its own gas is the lingering cloud (no sprite puffs take over), so it stays longer.
            // A nuclear cloud hangs in the sky for a minute.
            // A nuclear cloud hangs in the sky for minutes: here 1.5-2.5 minutes, longer the bigger.
            // A nuclear cloud hangs for an hour; here it climbs and spreads for four to five minutes before breaking up.
            if(plan.Warhead==WarheadKind.Nuke&&UseSim&&Look(plan).Modern) return Numbers.Clamp(240+30*Math.Log10(1+plan.WarheadTnt/1e6),220,330);
            // The dust of a fallen building takes a long while to settle.
            if(plan.Warhead==WarheadKind.Structure&&UseSim&&Look(plan).Modern) return 30;
            // The earth a bomb throws up hangs as a brown cloud for a while before it settles.
            if((plan.Warhead==WarheadKind.Bomb||plan.Warhead==WarheadKind.HighExplosive)&&UseSim&&Look(plan).Modern) return Math.Max(Math.Min(life*1.8,32),plan.Warhead==WarheadKind.Bomb?24:14);
            // The cloud holds together and keeps rising long after the fire (1.1 photoreal pass): a little more time to drift.
            return UseSim&&Look(plan).Modern?Math.Min(life*2.2,36):life;
        }
        public static VolumeState Evaluate(FxPlan plan,double age,bool explosions,bool burst,bool ground=false,bool water=false)
        { return Evaluate(plan,age,explosions,burst,ground,water,BlastLayout.Default(plan,ground&&!water,water)); }
        public static VolumeState Evaluate(FxPlan plan,double age,bool explosions,bool burst,bool ground,bool water,BlastLayout layout)
        {
            age=Numbers.Clamp(age,0,400);   // a nuclear cloud lives up to 330 s (others at most ~36 s)
            double air=Numbers.Clamp(plan.Atmosphere,0,1);
            ground=ground&&!water;
            double fire=explosions?Numbers.Clamp(plan.FireFraction,0,1):0;
            double vapor=burst?Numbers.Clamp(plan.VaporFraction,0,1):0;
            double kinetic=ground||water?Smooth(300,2500,plan.ImpactSpeed)*0.9:0;
            double dust=ground?Smooth(1.5,5,plan.VisualEnergyScore):0;
            double ore=ground&&Look(plan).Modern?Chemistry.OreDust(plan):0;   // 1.1: an ore tank bursts into rock dust
            if(ore>0) dust=Math.Max(dust,0.9*ore);
            double spray=water?Smooth(1.5,5,plan.VisualEnergyScore):0;
            double strength=Math.Max(Math.Max(fire,kinetic),Math.Max(Math.Max(dust,spray),vapor));
            double tau=TimeScale(plan);
            double box=BoxRadius(plan,layout), domeLocal=DomeRadius(plan)/box;
            double classicBox=ClassicBox(plan,layout);
            double k=domeLocal/ShellLimit;
            LookProfile look=Look(plan);
            VolumeState s=new VolumeState { BoxRadius=box, Age=age, Atmosphere=air, Ground=ground, Water=water, Scale=k,
                SmokeR=plan.SmokeR, SmokeG=plan.SmokeG, SmokeB=plan.SmokeB, Roughness=layout!=null?layout.Roughness:0.6,
                WhiteFlame=Numbers.Clamp(plan.WhiteFlame,0,1), DustR=plan.DustR, DustG=plan.DustG, DustB=plan.DustB, LightScale=look.LightIntensity, Modern=look.Modern };
            if(ore>0) { Vec3 rock=Chemistry.OreColour(new Vec3(plan.DustR,plan.DustG,plan.DustB)); s.DustR=rock.X; s.DustG=rock.Y; s.DustB=rock.Z; }
            double decelerated=1-Math.Exp(-age/(0.35*tau)), coasting=Math.Min(1,age/(tau*0.6));
            s.ShellRadius=Math.Max(0.03,domeLocal*(air*decelerated+(1-air)*coasting));
            s.ShellThickness=(0.05+0.05*Smooth(0,4*tau,age))*k;
            double shellBase=(air*strength*(ground||water?1:0.7)+(1-air)*0.1*Math.Max(Math.Max(fire,kinetic),vapor))*(0.35+0.65*strength);
            shellBase*=1-0.3*fire;
            shellBase*=water?0.2:1-0.45*air;
            double shellLife=tau*(0.5+0.1*air);
            s.ShellOpacity=Numbers.Clamp(shellBase*look.ShockwaveStrength*(look.Modern?0.07:1)*Smooth(0,0.03,age)*Math.Exp(-age/shellLife),0,1);
            s.ShellErosion=0.85*Smooth(tau*0.3,tau*(1.6+1.0*(1-air)),age);
            s.Condensation=Condensation(plan,layout,age,tau,air,look);
            if(s.Condensation>0)
            {
                // The air behind the shock front cools as it expands and its moisture flashes into a white shell for a moment.
                s.ShellOpacity=Math.Max(s.ShellOpacity,0.35*s.Condensation);
                s.ShellErosion=Math.Min(s.ShellErosion,0.15+0.7*(1-s.Condensation));
            }
            double heat0=Math.Max(fire,kinetic);
            s.Heat=Numbers.Clamp(heat0*Math.Exp(-age/(0.18+0.22*air+0.004*DomeRadius(plan))),0,1);
            s.CoreRadius=(0.07+0.2*(1-Math.Exp(-age/0.5)))*k;
            s.Glow=Numbers.Clamp(heat0*Math.Exp(-age/(look.Modern?(plan.Warhead==WarheadKind.Nuke?0.3:0.11):0.06)),0,1);
            double cold=vapor*(air>0.05?Smooth(0,0.3,age):Math.Exp(-age*3));
            s.Cloud=Numbers.Clamp(cold,0,1);
            s.Vapor=s.Cloud>0?1:0;
            s.Rise=Math.Min(age*0.05,0.3)*air*k;
            s.Dust=Numbers.Clamp(Math.Max(dust,spray)*(0.3+0.7*air)*Smooth(0,0.4,age)*Math.Exp(-age/(tau*5)),0,1);
            // 1.1: the dust the blast lifts is this volume's skirt (no sprite ring): thicker, and it hangs for seconds.
            if(look.Modern&&UseSim&&ground&&!water)
                s.Dust=Numbers.Clamp(Math.Max(dust,spray)*(0.3+0.7*air)*1.35*Smooth(0,0.35,age)*Math.Exp(-age/(tau*14)),0,1)*
                    (plan.Warhead==WarheadKind.Nuke?0.25:1)*   // a nuke's dust is its stem, skirt and ground wave (this disc spans the dome)
                    (air<0.05?Math.Exp(-age/1.5):1);   // in vacuum the ejecta fly off on ballistic arcs and thin out: no hanging haze
            double R=layout!=null?layout.RadiusMeters:FireballRadius(plan), tf=FireballDuration(plan)*(layout!=null?layout.Tempo:1);
            if(R>0&&fire>0.02&&layout!=null) EvaluateFireball(ref s,plan,layout,age,R,tf,air,fire,ground||water,box);
            s.SimShape=s.Sim&&layout!=null?new Vec4(layout.SimTall,layout.SimLeanX,layout.SimLeanZ,layout.SimWarp):new Vec4(1,0,0,1);
            s.SimRun=s.Sim&&layout!=null?FireSim.Usable(ground||water,ground||water?layout.SimRun:layout.SimAirRun):0;
            s.SimLinger=s.Sim&&layout!=null?layout.SimLinger:0;
            // Hydrogen burns with a faint, nearly clear flame; the white of its cloud is steam (Chemistry).
            s.FlameFaint=s.Sim?0.78*Numbers.Clamp(plan.Hydrogen,0,1):0;
            // 1 for fuel explosions, plus how far the stem has gone once the cloud holds its last whole frame (the shader thins
            // the stem from the ground up with the frame, then with this).
            s.SimStem=s.Sim&&(plan.Warhead==WarheadKind.None||FireSim.IsNukeRun(s.SimRun))?1+Smooth(0,3,s.Lobe0.SimAfter):0;
            s.SmokeMix=s.Sim&&layout!=null?layout.SmokeMix:new Vec4();
            s.ShockRadius=DomeRadius(plan)*(0.12+2.6*(1-Math.Exp(-age/(0.4*tau))))/box;
            s.ShockStrength=Numbers.Clamp(air*strength*look.ShockwaveStrength*Smooth(0,0.02,age)*Math.Exp(-age/(1.2*tau)),0,1.5);
            s.RingRadius=s.ShockRadius; s.RingStrength=s.ShockStrength; s.RingScale=domeLocal;
            if(look.Modern)
            {
                // 1.1: once the fireball stops pushing, the blast wave keeps running at about the speed of sound, fading as it spreads.
                // It stays a near-field effect: gone by sixteen dome radii (350-1200 m), not kilometres.
                double sound=340*Math.Sqrt(air), reachMetres=plan.Warhead==WarheadKind.Nuke?NukeBlastReach(plan):Numbers.Clamp(16*DomeRadius(plan),350,1200);
                s.ShockRadius+=sound*Math.Max(0,age-0.3*tau)*Smooth(0.3*tau,1.5*tau,age)/box;
                // A nuclear front runs at the speed of sound from the burst, leaving the fireball at once: the visible
                // front, the camera's jolt (Shake.Arrival) and BDArmory's boom (delayed by distance / speed of sound) arrive
                // together (it ran ahead of them by a couple of seconds).
                if(plan.Warhead==WarheadKind.Nuke)
                    s.ShockRadius=Math.Max(1.15*FireballRadius(plan)*(1-Math.Exp(-age/0.05)),sound*age)/box;
                s.RingReach=reachMetres;
                double metres=s.ShockRadius*box;
                // A nuclear blast wave stays strong while it crosses kilometres (it is what one sees race over the ground).
                double keepShock=plan.Warhead==WarheadKind.Nuke?Math.Max(2.4*tau,0.45*reachMetres/Math.Max(sound,50)):4.5*tau;
                s.ShockStrength=Numbers.Clamp(air*strength*look.ShockwaveStrength*Smooth(0,0.02,age)*Math.Exp(-age/keepShock)*
                    (1-Smooth(0.4*reachMetres,reachMetres,metres)),0,1.5);
                // A nuclear blast front in the test films: a sharp, shimmering shell sweeping out over the land for kilometres
                // and bending everything behind it as it passes, weakening with distance. It faded in time (gone ~2 s after the
                // flash, before reaching 1 km); now with distance only, out to twice the reach above (20 kt: 6.5 km).
                if(plan.Warhead==WarheadKind.Nuke)
                {
                    double vis=2*reachMetres;
                    s.ShockStrength=Numbers.Clamp(air*strength*look.ShockwaveStrength*Smooth(0,0.02,age)*
                        (0.4+0.6*Math.Exp(-metres/(0.6*reachMetres)))*(1-Smooth(0.55*vis,vis,metres)),0,1.5);
                }
            }
            // (after the blast wave: the Wilson cloud sits at its front)
            if(s.Sim&&layout!=null&&plan.Warhead==WarheadKind.Nuke&&R>0) NukeLook(ref s,plan,layout,age,tf,box);
            s.Haze=Numbers.Clamp(air*Math.Max(fire,kinetic*0.5)*Smooth(0.03,0.25,age)*Math.Exp(-age/(1.5+4*tau)),0,1);
            s.HazeHeight=Math.Max((0.6+0.9*Smooth(0,2,age))*k,s.FireRise+s.FireRadius*1.6);
            double duration=VolumeDuration(plan);
            if(look.Modern)
            {
                // The smoke breaks up from its edges instead of fading as a smooth ball.
                s.Dissolve=UseSim?0.75*Smooth(0.62*duration,0.98*duration,age):Smooth(0.5*duration,0.96*duration,age);
                s.Fade=Numbers.Clamp((duration-age)/((UseSim?0.25:0.07)*duration),0,1);
                s.ProxyGain=box/Math.Max(classicBox,1e-6);
            }
            else s.Fade=Numbers.Clamp((duration-age)/(0.3*duration),0,1);
            return s;
        }
        static void EvaluateFireball(ref VolumeState s,FxPlan plan,BlastLayout layout,double age,double R,double tf,double air,double fire,bool surface,double box)
        {
            LookProfile look=Look(plan);
            double burn=tf*(0.12+0.88*air)*(1-0.55*s.WhiteFlame)/look.CoolingRate;
            // A high explosive's fireball is gone in a fraction of a second (its products burn out at once): what stays is
            // a cloud of dark smoke and dust, not a lingering ball of fire (it burned like a fuel tank).
            if(look.Modern) burn*=Warhead.FireLife(plan.Warhead);
            // A warhead's fireball glows for half a second at least, however small (a missile's was a brown crust at once).
            if(look.Modern&&plan.Warhead!=WarheadKind.None&&plan.Warhead!=WarheadKind.Nuke&&plan.Warhead!=WarheadKind.Structure) burn=Math.Max(burn,1.3);
            if(plan.Warhead==WarheadKind.Nuke&&look.Modern) burn*=1.9;   // a nuclear cloud glows red inside its rolling cap for long
            double tg=tf/look.ExpansionSpeed/Math.Max(layout!=null?layout.Burst:1,1); // growth time scale (a detonation grows faster)
            double riseFraction=air*Smooth(0.15*tf,1.2*tf,age);
            double lift=R*LiftFactor*riseFraction;
            if(look.Modern) lift+=R*0.75*air*Smooth(0.8*tf,3.5*tf,age);   // the cooled gas goes on rising as a thermal
            double heatMax=0, pulse=0;
            bool sim=UseSim&&look.Modern;
            s.Sim=sim;
            double minX=1e9,minY=1e9,minZ=1e9,maxX=-1e9,maxY=-1e9,maxZ=-1e9;
            double snap=look.Modern?0.065:0.1;   // 1.1 brutal pass: the fireball slams out to its size
            double grow0=air*(1-Math.Exp(-age/(snap*tg)))+(1-air)*Math.Min(1,age/(0.12*tg));
            double spread0=Spread(air,tf,age);
            for(int i=0;i<BlastLayout.Count;i++)
            {
                LobeSeed seed=layout.Lobes[i];
                double ta=age-seed.Delay;
                if(!seed.Active||ta<0) { s.SetLobe(i,new LobeState { Axis=Up, Stretch=1 }); continue; }
                double grow=air*(1-Math.Exp(-ta/(snap*tg)))+(1-air)*Math.Min(1,ta/(0.12*tg));
                double spread=Spread(air,tf,ta);
                double radius=seed.Size*R*grow*spread;
                Vec3 carry=seed.Drift*(seed.Tau*(1-Math.Exp(-ta/Math.Max(seed.Tau,1e-3))));
                double groundLift=surface?0.45*radius:0;
                Vec3 offset=seed.Relative?seed.Offset*(grow0*spread0):seed.Offset;
                Vec3 center=offset+carry+Up*(lift+groundLift);
                double stretch=1+(seed.Stretch-1)*Math.Exp(-ta/(0.8*tf));
                if(i==0&&Math.Abs(seed.Axis.Y)>0.7) stretch*=1-0.3*riseFraction;
                // 1.1: a longer red tail instead of an abrupt end; it stays a glowing fireball a while before it cools, as in 1.0.
                double heat=seed.Heat*layout.HeatBias*fire*(1-Smooth((look.Modern?0.5:0.25)*burn,(look.Modern?1.8:1.1)*burn,ta));
                if(seed.Delay>0) pulse=Math.Max(pulse,seed.Heat*fire*Math.Exp(-ta/0.12));
                heatMax=Math.Max(heatMax,heat);
                LobeState lobe;
                if(sim) lobe=SimLobe(layout,i,seed,ta,R,tf,air,surface,look,box,heat,spread,offset+carry,grow);
                else lobe=new LobeState { Center=center*(1/box), Radius=radius/box, Axis=seed.Axis, Stretch=stretch, Heat=heat,
                    Displacement=(carry+Up*lift)*(1/box) };
                s.SetLobe(i,lobe);
                double reach=sim?lobe.Radius:lobe.Radius*Math.Max(stretch,1)*BlastLayout.SurfaceReach;
                minX=Math.Min(minX,lobe.Center.X-reach); maxX=Math.Max(maxX,lobe.Center.X+reach);
                minY=Math.Min(minY,lobe.Center.Y-reach); maxY=Math.Max(maxY,lobe.Center.Y+reach);
                minZ=Math.Min(minZ,lobe.Center.Z-reach); maxZ=Math.Max(maxZ,lobe.Center.Z+reach);
            }
            // 1.1: the other copies of the baked fireball join the main one as they cool and rise: one cloud
            // (a smaller, later copy rose under the main one and the two ended stacked as separate clouds: "nuage
            // séparé"). Its cap is drawn up into the main cap (a fifth of the way down from its top), keeping a little of
            // its side offset: a lumpier single cloud.
            if(sim&&look.Modern&&!FireSim.IsNukeRun(FireSim.Usable(surface,surface?layout.SimRun:layout.SimAirRun))&&s.Lobe0.Sim)
            {
                double join=Smooth(0.6*tf,2.6*tf,age);
                bool moved=false;
                for(int i=1;i<BlastLayout.Count&&join>0;i++)
                {
                    LobeState l=s.Lobe(i);
                    if(!l.Sim) continue;
                    Vec3 to=s.Lobe0.SimOrigin-l.SimOrigin;
                    double up=s.Lobe0.SimCapTop-0.2*(s.Lobe0.SimCapTop-s.Lobe0.SimCapLow)-l.SimCapTop;
                    Vec3 delta=new Vec3(to.X*0.6,up,to.Z*0.6)*join;
                    l.SimOrigin=l.SimOrigin+delta; l.Center=l.Center+delta; l.SimCapLow+=delta.Y; l.SimCapTop+=delta.Y;
                    s.SetLobe(i,l); moved=true;
                }
                if(moved)
                {
                    minX=minY=minZ=1e9; maxX=maxY=maxZ=-1e9;
                    for(int i=0;i<BlastLayout.Count;i++)
                    {
                        LobeState l=s.Lobe(i);
                        if(!l.Sim) continue;
                        minX=Math.Min(minX,l.Center.X-l.Radius); maxX=Math.Max(maxX,l.Center.X+l.Radius);
                        minY=Math.Min(minY,l.Center.Y-l.Radius); maxY=Math.Max(maxY,l.Center.Y+l.Radius);
                        minZ=Math.Min(minZ,l.Center.Z-l.Radius); maxZ=Math.Max(maxZ,l.Center.Z+l.Radius);
                    }
                }
            }
            s.FireRadius=s.Lobe0.Radius; s.FireRise=s.Lobe0.Center.Y; s.FireHeat=Numbers.Clamp(heatMax,0,1); s.Pulse=pulse;
            s.Roll=air*Math.Min(2.2,0.8*age/Math.Max(tf,0.1));
            s.NoiseRadius=Math.Max(0.02,R*(0.35+0.65*grow0)*spread0/box/Math.Max(layout.NoiseScale*look.NoiseScale,0.25));
            s.FireSoot=Numbers.Clamp(plan.Soot*look.SmokeDensity*(1+2*layout.SootBias)*(look.Modern?0.22+0.78*Smooth(0.25*tf,1.1*tf,age):0.4+0.6*Smooth(0.02*tf,0.5*tf,age)),0,1);
            double tl=tf*look.EffectLifetime;
            double keep=air>0.05?(look.Modern?(sim?1-0.3*Smooth(1.2*tl,3.5*tl,age):1-0.7*Smooth(0.9*tl,2.2*tl,age)):1-Smooth(0.7*tl,2.0*tl,age)):1-Smooth(0.03*tl,0.2*tl,age);
            double dense=0.12+0.88*air+0.6*(1-air)*Math.Exp(-age/(0.06*tf));
            // The baked simulation already thins the gas as it spreads: no analytic dilution on top of it.
            double dilution=sim?1:spread0*spread0*spread0;
            // A falling building's gas is its dust: as thick as a fireball's smoke, though hardly any of it burns.
            double gas=plan.Warhead==WarheadKind.Structure&&look.Modern?Math.Max(fire,1.0):fire;
            s.FireBody=Numbers.Clamp(gas*dense*Smooth(0,0.03,age)*keep/dilution/layout.Expansion,0,1);
            double head=s.Lobe0.Center.Y;
            if(layout.Stem>0&&surface&&head>0&&lift>0.2*R&&!sim)
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
        // One fireball copy of the baked simulation: where its box sits, how big, which frame, and the sphere that holds the gas.
        static LobeState SimLobe(BlastLayout layout,int index,LobeSeed seed,double ta,double R,double tf,double air,bool surface,LookProfile look,double box,double heat,double spread,Vec3 center,double grow)
        {
            FireSim.Variant v=FireSim.Run(surface,surface?layout.SimRun:layout.SimAirRun);
            double peak=seed.Size*R*1.3*(1+0.35*(spread-1))*layout.SimBreadth;
            double scale=Math.Max(peak/v.RefRadius,1e-3)*(0.3+0.7*Numbers.Clamp(grow,0,1));   // the opening blast grows from a small seed
            int h=(int)(seed.Size*1000+seed.Delay*777+index*131+Math.Abs(seed.Offset.X)*13+Math.Abs(seed.Offset.Z)*7+layout.Tempo*997)&0x7fffffff;
            // No two copies alike: own heading, mirror, pace and horizontal stretch (area-preserving).
            double pace=0.85+0.35*((h>>5)%101)/100.0, stretch=0.8+0.45*((h>>9)%101)/100.0;
            double life=2.8*tf*pace/Math.Max(look.ExpansionSpeed,0.2);   // whole baked run (8 units): lift-off at ~2-3 units falls inside the fire phase
            // An ordinary explosion plays its run 1.7 times slower: its fireball sits on the ground for a moment and
            // its smoke climbs at a believable pace (it shot up two or three fireball radii within a second and a half). Its
            // size still slams out (the growth has its own clock).
            if(!FireSim.IsNukeRun(FireSim.Usable(surface,surface?layout.SimRun:layout.SimAirRun))) life=Math.Max(life*1.6,2.8);   // (and 2.8 s at least: a missile's shot up within a second)
            life*=Math.Max(layout.SimPace,0.2);   // (a warhead's: slower still, BlastShape)
            // The burst plays as baked; past it, a warhead's cloud phase plays SimSlow times slower (it hangs in the air).
            double u=ta/life, slow=Math.Max(layout.SimSlow,1);
            // Brutal opening: the first third of the run plays front-loaded (the burst slams out, then rolls on).
            const double Punch=0.72;
            if(u<0.35) u=0.35*Math.Pow(u/0.35,Punch);
            else u=0.35+(u-0.35)/slow;
            // (Cold dust stops at the bake's early spread along the ground, before the hot gas would lift off.)
            double whole=layout.SimHugsGround?Math.Max(2,0.32*v.LastWhole):v.LastWhole;
            double frame=Math.Min(FireSim.Frame(v,u*v.TMax),whole);
            // When this copy reaches its last whole frame (see FireSim.Whole): the inverse of the clock above.
            double lastU=FireSim.TimeOf(v,whole)/v.TMax, lastTa=(lastU<=0.35?0.35*Math.Pow(lastU/0.35,1/Punch):0.35+(lastU-0.35)*slow)*life;
            double yaw=(h%628)/100.0; bool mirror=((h>>3)&1)==1;
            // Turned and drawn out along the crash path when the impact sets it (BlastShape).
            if(!double.IsNaN(layout.SimYaw)) { yaw=layout.SimYaw+(((h>>11)%101)/100.0-0.5)*0.35; stretch=layout.SimStretch*(0.94+0.12*((h>>13)%101)/100.0); }
            double cos=Math.Cos(yaw), sin=Math.Sin(yaw);
            var boxes=new double[6]; FireSim.Box(v,frame,boxes);
            double cx=0.5*(boxes[0]+boxes[1])/stretch, cy=0.5*(boxes[2]+boxes[3]), cz=0.5*(boxes[4]+boxes[5])*stretch;
            double r=0.5*Math.Sqrt((boxes[1]-boxes[0])*(boxes[1]-boxes[0])+(boxes[3]-boxes[2])*(boxes[3]-boxes[2])+(boxes[5]-boxes[4])*(boxes[5]-boxes[4]))*Math.Max(stretch,1/stretch);
            if(mirror) cx=-cx;
            // Past its last whole frame the cloud holds its shape and goes on rising, at first as fast as the bake's top was
            // rising (about 0.8 simulation units per unit of its time), slowing over a few seconds, then drifting; and spreading.
            double after=Math.Max(0,ta-lastTa);
            double rise=0, tall=layout.SimTall>0?layout.SimTall:1;
            if(after>0)
            {
                // At the bake's own pace, easing over about half a run, then a slow drift; spreading by up to 20 %.
                // A nuclear cloud keeps climbing and spreading for a long while (toward the tropopause); others settle sooner.
                bool nukeRun=FireSim.IsNukeRun(FireSim.Usable(surface,surface?layout.SimRun:layout.SimAirRun));
                double spreadMore=nukeRun?layout.NukeSpread:0.2;
                double tau=nukeRun?Numbers.Clamp(0.9*life,1.5,30):Numbers.Clamp(0.45*life,1.5,6), speed=0.8*scale*v.TMax/life/slow;
                // Largest this copy gets (fully spread and grown): the ceiling below stays put, so the cloud never sinks back.
                double grown=seed.Size*R*1.3*(1+0.35*(SpreadMax(air)-1))*layout.SimBreadth/v.RefRadius*(1+spreadMore);
                rise=speed*tau*(1-Math.Exp(-after/tau))+peak*0.02*after;
                // It slows to a stop under the top of the proxy box instead of rising out of it (it was cut off there).
                double topAtRest=surface?FireSim.Top(v,frame)*grown*tall:center.Y+(FireSim.Top(v,frame)*tall-v.SourceY)*grown;
                double room=Math.Max(0,0.82*box-topAtRest);
                if(nukeRun&&layout.NukeTop>0)
                {
                    // A nuclear cloud climbs toward its real height and its cap widens with it to about 0.3 of its height
                    // (it was a small head on a long stem: the cap spread only 2.2 times while it climbed kilometres).
                    double top0=surface?FireSim.Top(v,frame)*scale*tall:center.Y+(FireSim.Top(v,frame)*tall-v.SourceY)*scale;
                    double T=top0+Math.Max(layout.NukeTop-top0,0)*(1-Math.Exp(-after/layout.NukeTau));
                    double half0=0.5*Math.Max(boxes[1]-boxes[0],boxes[5]-boxes[4])*scale;
                    double widen=Numbers.Clamp(0.3*T/Math.Max(half0,1e-3),1,14);
                    scale*=1+(widen-1)*(1-Math.Exp(-after/6));
                    double topNow=surface?FireSim.Top(v,frame)*scale*tall:center.Y+(FireSim.Top(v,frame)*tall-v.SourceY)*scale;
                    double headroom=Math.Max(0,0.82*box-topNow);
                    rise=Math.Min(Math.Max(T-topNow,0),headroom);
                }
                else if(layout.SimHugsGround)
                {
                    // Cold dust: it barely rises and keeps rolling outward (half again as wide over about ten seconds).
                    rise=Math.Min(peak*0.01*after,room);
                    scale*=1+0.5*(1-Math.Exp(-after/10));
                }
                else
                {
                    rise=room>1e-6?room*(1-Math.Exp(-rise/room)):0;   // eases to a stop, no sudden halt
                    scale*=1+spreadMore*(1-Math.Exp(-after/(nukeRun?45:8)));
                }
                center=center+Up*rise;
            }
            // On the ground every copy stands on the ground: its simulated floor-hugging gas would otherwise be a flat-bottomed
            // sheet hanging in the air. The rise past the bake still lifts it.
            var origin=new Vec3(center.X,surface?0:center.Y-v.SourceY*scale,center.Z);
            if(surface&&after>0) origin=origin+Up*rise;
            // Each explosion's own shape: taller or squatter, and leaning (shear with height); the shader applies the same.
            double lift=cy*scale*tall;
            var span=new Vec3(origin.X+(cx*cos-cz*sin)*scale+layout.SimLeanX*lift,origin.Y+lift,origin.Z+(cx*sin+cz*cos)*scale+layout.SimLeanZ*lift);
            double reach=r*scale*Math.Max(tall,1)*(1+Math.Sqrt(layout.SimLeanX*layout.SimLeanX+layout.SimLeanZ*layout.SimLeanZ));
            // The heading vector carries the stretch in its length (the shader divides the cross axis by its square).
            return new LobeState { Sim=true, Center=span*(1/box), Radius=Math.Max(reach,1e-3)/box, Axis=Up, Stretch=1, Heat=heat,
                SimOrigin=origin*(1/box), SimCos=cos*stretch, SimSin=sin*stretch, SimScale=scale/box*(mirror?-1:1), SimFrame=frame, SimAfter=after,
                // The nuclear run's gas reaches down to the ground (its wake, cut by the shader): its cap is the top third.
                SimCapLow=(origin.Y+(boxes[2]+(FireSim.IsNukeRun(FireSim.Usable(surface,surface?layout.SimRun:layout.SimAirRun))?0.68:0.55)*(boxes[3]-boxes[2]))*scale*tall)/box,
                SimCapTop=(origin.Y+boxes[3]*scale*tall)/box,
                SimCapFrac=(boxes[2]+0.68*(boxes[3]-boxes[2]))/Math.Max(v.Height,1e-3),
                SimCapRadius=0.5*Math.Max(boxes[1]-boxes[0],boxes[5]-boxes[4])*scale/box };
        }
        // A nuclear fireball's own sequence (Glasstone & Dolan ch. II): a white-hot sphere for the first moments; then, on a
        // ground burst, the afterwinds suck dust up from the ground into a stem that climbs to the cloud's underside over
        // several seconds, with a dust skirt at its base; in moist air a condensation ring forms around the stem for a while.
        static void NukeLook(ref VolumeState s,FxPlan plan,BlastLayout layout,double age,double tf,double box)
        {
            LobeState cap=s.Lobe0;
            NukeKind kind=layout.Nuke==NukeKind.None?NukeKind.Surface:layout.Nuke;
            double Rn=Math.Max(FireballRadius(plan),1), ground=-layout.NukeGround/box;   // the ground under the burst (proxy y)
            double white=1-Smooth(0.05*tf,0.4*tf,age);
            // The dust stem: on the ground the afterwinds suck it up at once into a thick column under the cloud; after a low
            // air burst a thinner one rises later and slower, and only reaches the cloud when the burst was low.
            double stemReach=kind==NukeKind.Surface?1:kind==NukeKind.LowAir?Numbers.Clamp(1.35-0.22*layout.NukeGround/Rn,0.25,1):0;
            double stem=kind==NukeKind.Surface?Smooth(0.1*tf,1.8*tf,age):stemReach*Smooth(0.5*tf,4*tf,age);
            double stemDensity=kind==NukeKind.Surface?1:0.55;
            double skirt=kind==NukeKind.Surface?1:kind==NukeKind.LowAir?0.8*Math.Exp(-layout.NukeGround/(2.5*Rn))*Smooth(0.2*tf,1.2*tf,age):0;
            // Condensation rings around the stem in moist air (several at megaton yields), for a while.
            // (Only in moist air: in dry air a faint one read as a second, smaller cap under the first.)
            double rings=Smooth(0.6*tf,1.4*tf,age)*(1-Smooth(3.5*tf,9*tf,age))*(layout.NukeHumid?1:0)*(kind==NukeKind.HighAir?0:1);
            double ringCount=layout.NukeThermo?3:2;
            // An ice cap: the cloud rising fast pushes moist air ahead of it, which freezes into a smooth white cap over it
            // (megaton yields, moist air), until the cloud punches through.
            double ice=layout.NukeThermo&&layout.NukeHumid?Smooth(1.0*tf,1.6*tf,age)*(1-Smooth(3*tf,5*tf,age)):0;
            // The Wilson cloud: in moist air the air behind the shock front cools as it expands and flashes into fog for a
            // second or two, a white dome around the fireball turning into a ring before it clears.
            // In every weather, dense, for a few seconds.
            double wilson=layout.NukeWilson?1.5*Smooth(0.08*tf,0.15*tf,age)*(1-Smooth(0.6*tf,1.4*tf,age))*(s.Water?1:0.6+0.4*Smooth(0.1,0.9,layout.Humidity)):0;
            double low=Math.Max(cap.SimCapLow,ground+0.02), capR=Math.Max(cap.SimCapRadius,0.02);
            // x: 1 + the afterglow of the ionised air in the cloud (it glows red-violet for a long while: seen at night).
            double afterglow=Smooth(0.3*tf,1.0*tf,age)*Math.Exp(-Math.Max(age-tf,0)/(3.5*tf));
            s.Nuke=new Vec4(1+afterglow,stem,rings,white);
            // Stem radius (Glasstone): about half the cloud radius below ~20 kt, a fifth to a tenth at megatons (the shader's
            // ragged width reaches about 1.2 times this).
            double stemR=Numbers.Clamp(0.42*Math.Pow(Math.Max(plan.WarheadTnt/1e6,0.01)/20,-0.12),0.18,0.48)*(kind==NukeKind.Surface?1:0.6);   // (0.34 read as a thin tube)
            s.NukeStem=new Vec4(cap.SimOrigin.X,cap.SimOrigin.Z,low,stemR*capR);
            s.NukeRing=new Vec4(0.62*capR,0.1*capR,ground+0.72*(low-ground),cap.SimCapFrac);   // under the cap; w: the shader's cut
            s.NukeGround=new Vec4(ground,skirt,stemDensity,ringCount);
            s.NukeIce=new Vec4(0.5*(low+cap.SimCapTop),capR,ice,cap.SimCapTop);
            // (It forms behind the front and stays where it formed, a kilometre or so across at 20 kt, instead of riding the
            // front out; it clears from the top, leaving a ring low down.)
            double rw=Math.Min(s.ShockRadius,4.5*Rn*(1+0.3*Smooth(0.15*tf,0.8*tf,age))/box);
            s.NukeWilson=new Vec4(rw,wilson,0.22*rw,Smooth(0.45*tf,1.1*tf,age));
            // Once the cap has rolled up and cooled, it is drawn as its own shape with cloud-sized billows (the baked
            // simulation stretched over kilometres has no relief and a grainy surface); its lumps ride a slow roll.
            s.NukeCap=new Vec4(Smooth(2.2*tf,4.5*tf,age),0.006,1,0);
            // The cloud is reddish-brown at first (nitrogen oxides), then turns white as its water condenses in moist air,
            // light grey in dry air, and dusty grey-brown when the ground was sucked into it.
            double t=Smooth(1.5*tf,9*tf,age);   // (Trinity stayed reddish-brown for half a minute and more)
            double r1,g1,b1;
            // (The cap of a ground burst is the fireball's own condensing gas with the dust in it: light grey-brown in
            // sunlight whatever the ground; the stem under it carries the ground's colour. It was a dark olive over grass.)
            if(kind==NukeKind.Surface) { r1=0.35*plan.DustR+0.42; g1=0.35*plan.DustG+0.38; b1=0.35*plan.DustB+0.34; }
            else if(layout.NukeHumid) { r1=0.88; g1=0.86; b1=0.84; }
            else { r1=0.76; g1=0.72; b1=0.69; }
            s.SmokeR=0.62+(r1-0.62)*t; s.SmokeG=0.36+(g1-0.36)*t; s.SmokeB=0.26+(b1-0.26)*t;
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
            if(l.Sim)
            {
                axis=new Vec4(l.SimCos,l.SimSin,l.SimScale,l.SimFrame);
                displacement=new Vec4(l.SimOrigin.X,l.SimOrigin.Y,l.SimOrigin.Z,l.Heat);
                return;
            }
            axis=new Vec4(l.Axis.X,l.Axis.Y,l.Axis.Z,Math.Max(l.Stretch,0.2));
            displacement=new Vec4(l.Displacement.X,l.Displacement.Y,l.Displacement.Z,i<4?l.Heat:s.StemDensity);
        }
        public static Vec4 Bounds(VolumeState s) { return new Vec4(s.BoundsCenter.X,s.BoundsCenter.Y,s.BoundsCenter.Z,s.BoundsRadius); }
        public static Vec4 Motion(VolumeState s) { return new Vec4(s.Roll,s.WhiteFlame,s.Dissolve,s.Sim?1:0); }
        public static Vec4 Dust(VolumeState s) { return new Vec4(s.DustR,s.DustG,s.DustB,s.ProxyGain); }
        public static Vec4 Flare(VolumeState s)
        {
            double height=(s.Ground||s.Water?s.CoreRadius*0.7:0)+s.Rise+s.FireRise*0.5;
            double damp=s.FireBody>0.02?0.55+0.45*s.Glow:1;
            double boost=s.Modern?1.45:1;   // 1.1: a much bigger, brighter opening flash (brutal pass)
            if(s.Nuke.X>0) boost*=1.7;      // a nuclear flash floods the sky
            return new Vec4((0.22+0.9*s.Glow+0.2*s.Heat)*s.Scale*boost,Numbers.Clamp(s.Glow*0.9+s.Heat*0.25,0,1)*(s.Modern?1.35:1)*(s.Nuke.X>0?1.2:1)*s.Fade*damp,height,
                Numbers.Clamp(Math.Max(s.Heat*1.2,s.Glow*1.6),0,1));
        }
        // The wave in the air is the 1.0 ring for every blast: a camera-facing ring that bends the scene, with
        // its pale rim. An ordinary blast's on the 1.0 timeline; a nuclear one on its front, which runs at the
        // speed of sound with its dust wall, the jolt and BDArmory's boom.
        // How far the ring bends the scene per unit of strength (proxy units): 1.0's 0.03; an ordinary 1.1 blast's as many
        // metres as 1.0 (0.17 dome radius: tied to the larger 1.1 proxy box the 1.0 value moved it a third as much); a
        // nuclear one's a tenth of its front's radius (tens of metres over kilometres), as wide.
        static double RingBend(VolumeState s) { return !s.Modern?0.03:s.Nuke.X>0?0.1*s.ShockRadius:0.17*s.RingScale; }
        // How far behind the ring the scene is fully bent (m): a nuclear ring's half its radius; 0 for the others (half the
        // proxy, as 1.0: HeatDistortion _RingDepth).
        public static double RingDepth(VolumeState s) { return NuclearRing(s)?Math.Max(0.5*s.ShockRadius*s.BoxRadius,20):0; }
        static bool NuclearRing(VolumeState s) { return s.Modern&&s.Nuke.X>0; }
        // A nuclear ring already past the edges of the view (the camera inside it): its billboard covered the
        // whole screen for nothing. distance: camera to the blast (m); tanHalfDiagonal: tangent of half the view's diagonal.
        public static bool RingOutOfView(VolumeState s,double distance,double tanHalfDiagonal)
        {
            if(!NuclearRing(s)) return false;
            double R=s.ShockRadius*s.BoxRadius, w=Math.Max(0.1*R,0.03*s.Scale*s.BoxRadius);
            return R-3*w>Math.Max(distance,1)*tanHalfDiagonal*1.15;
        }
        public static void Distortion(VolumeState s,double groundFire,out Vec4 shock,out Vec4 haze)
        {
            bool nuke=NuclearRing(s);
            double R=nuke?s.ShockRadius:s.RingRadius, strength=nuke?s.ShockStrength:s.RingStrength;
            // A nuclear ring is gone once past its reach (its bend over the whole screen cost 3 ms of GPU a frame
            // for minutes at a megaton, long after it could be told from the land).
            if(nuke) strength*=1-Smooth(0.5*s.RingReach,s.RingReach,R*s.BoxRadius);
            double width=nuke?Math.Max(0.1*R,0.03*s.Scale):(0.045+0.06*Math.Min(1,R/Math.Max(s.Scale,1e-3)))*s.Scale;
            double hazeAmount=Math.Max(Math.Max(s.Haze,s.FireHeat*0.8*s.Atmosphere),Numbers.Clamp(groundFire,0,1)*0.7*s.Atmosphere);
            // No heat shimmer over a nuclear blast: capped to well under a metre over kilometres it could not be
            // seen, and its billboard (the haze's height: kilometres) ran over the whole screen.
            if(nuke) hazeAmount=0;
            double half=Math.Max(strength>0.002?R+3*width:0,hazeAmount>0.002?Math.Max(s.HazeHeight,0.9)*1.15:0);
            // Offsets are in proxy units: 1.1 tie them to the blast size (s.Scale, 0.8 in 1.0), so a larger proxy box
            // (room for the simulated fireball) does not bend the scenery over tens of metres.
            double reach=ShockReach(s);
            shock=new Vec4(R,RingBend(s)*strength,width,half);
            // 1.1: the hot air over the fire trembles more (the 1.0 shimmer was barely visible).
            haze=new Vec4((s.Modern?0.008:0.004)*reach*hazeAmount,s.HazeHeight,(s.Modern?0.55:0.45)*s.Scale,s.Age);

        }
        // Resolution divisor the volumes are marched at: the 1.1 simulation one step lower than the setting (it takes
        // four times more steps in the shader, VEFX_SIM), at most 1/4.
        public static int MarchDivisor(int setting,bool sim) { int d=Math.Max(1,Math.Min(setting,3)); return sim?Math.Min(d+1,4):d; }
        static double ShockReach(VolumeState s) { return s.Modern?s.Scale/0.8:1; }
        // Brightness of the shock front's rim, which the shader takes in proxy units: 1.1 keeps it at the 1.0 strength
        // whatever the proxy size, and a little stronger so the front reads at a distance.
        public static double ShockRimGain(VolumeState s) { return !s.Modern?1:0.03/System.Math.Max(RingBend(s),1e-5); }
        // Ground shadow of the fireball and its smoke (1.1): proxy size, darkness in full sun, opacity, seed.
        public static Vec4 Shadow(VolumeState s,double seed)
        {
            if(!s.Modern||s.Fade<=0||s.FireBody<=0.002) return new Vec4();
            double pale=Numbers.Clamp(((s.SmokeR+s.SmokeG+s.SmokeB)/3-0.45)*3,0,1);   // light smoke scatters light into its own shadow
            double dark=0.45*(1-0.7*s.FireHeat)*(1-0.45*pale)*s.Fade*(1-s.Dissolve)*(1-s.Dissolve);
            return new Vec4(s.BoxRadius,Numbers.Clamp(dark,0,0.45),s.FireBody*(1+2*s.FireSoot),seed%997);
        }
        // Where the ground shadow falls (1.1): a cloud rising to about twice its box radius throws its shadow away from the
        // sun, H / tan(elevation) off to the side. The draped ground mesh is centred on that and wide enough for the cloud
        // and its shadow (a disc under the blast cut the shadow of a tall cloud off in a circle). Sun in the blast frame.
        public static void ShadowDrape(double box,double sunX,double sunY,double sunZ,out double x,out double z,out double radius)
        {
            double flat=Math.Sqrt(sunX*sunX+sunZ*sunZ), lean=Math.Min(flat/Math.Max(sunY,1e-3),2.5);   // below ~22 deg the shadow fades anyway
            double reach=2*box*lean, half=0.5*reach;
            x=flat>1e-6?-sunX/flat*half:0; z=flat>1e-6?-sunZ/flat*half:0;
            radius=Math.Min(box*0.85+half,3000);
        }
        // 1.1: the dust the blast wave lifts as it runs along the ground, beyond the fireball's own skirt. Reach (m) of the
        // disk it is drawn on; 0 when this blast raises none (in the air, on water, thin air, small blasts, the 1.0 look).
        public static double GroundWaveReach(FxPlan plan,bool ground,bool water)
        {
            if(plan!=null&&plan.Warhead==WarheadKind.Nuke&&!water&&plan.Look!=null&&plan.Look.Modern&&plan.Atmosphere>=0.3&&
                plan.BurstHeight<5*FireballRadius(plan)) ground=true;   // a low nuclear air burst raises the ground's dust too
            if(!ground||water||plan==null||plan.Look==null||!plan.Look.Modern) return 0;
            if(plan.Atmosphere<0.3||plan.VisualEnergyScore<3.6||plan.Warhead==WarheadKind.Structure) return 0;   // a falling building has no blast wave
            if(plan.Warhead==WarheadKind.Nuke) return NukeBlastReach(plan);
            return Numbers.Clamp(4.5*DomeRadius(plan),45,450);
        }
        // Front (share of the reach), crest height (m), opacity, crest width (share of the reach) at this moment.
        public static Vec4 GroundWave(FxPlan plan,VolumeState s,double reach)
        {
            if(reach<=0||s.BoxRadius<=0) return new Vec4();
            double front=s.ShockRadius*s.BoxRadius;
            bool nuke=plan.Warhead==WarheadKind.Nuke;
            if(nuke) front=Math.Sqrt(Math.Max(front*front-plan.BurstHeight*plan.BurstHeight,0));   // where the sphere meets the ground
            double f=front/reach;
            double strength=Smooth(3.6,6.2,plan.VisualEnergyScore)*Numbers.Clamp(plan.Atmosphere,0,1)*(nuke?1.6:1.6);
            // The lifted dust outlives the front by seconds: the whole sheet fades out once the dust has had time to settle.
            double settle=GroundWaveSettle(plan);
            double opacity=strength*Smooth(0,0.12,s.Age)*(1-Smooth(0.6*settle,settle,s.Age));
            double height=Numbers.Clamp(0.05*Math.Min(front,reach),0.4,nuke?160:25)*(0.6+0.4*Math.Min(strength,1));
            double width=0.06;
            return new Vec4(Math.Min(f,1.5),height,opacity,width);
        }
        // How the lifted dust behaves behind the front: front speed (m/s), how long it hangs (s), how fast it billows up
        // (m/s), and the radius (m) inside which the blast lifts the full amount (beyond it, less and less: ~1/r).
        // Strength (0..1) of the condensation cloud now: big blasts (score 5-7) in dense, humid air (the 1.1 look only).
        public static double Condensation(FxPlan plan,BlastLayout layout,double age,double tau,double air,LookProfile look)
        {
            if(look==null||!look.Modern||layout==null||plan.Warhead==WarheadKind.Nuke) return 0;
            // Only very humid air, big blasts, and faint (a white dome read as a glass bubble).
            double humid=Smooth(0.7,0.95,layout.Humidity), big=Smooth(5.5,7,plan.VisualEnergyScore)*Smooth(0.6,0.9,air);
            double t=age/Math.Max(tau,1e-3);
            return humid*big*Smooth(0.05,0.2,t)*(1-Smooth(0.6,1.6,t));
        }
        public static Vec4 GroundWaveTrail(FxPlan plan,VolumeState s)
        {
            double air=Numbers.Clamp(plan.Atmosphere,0,1), dome=DomeRadius(plan);
            // A nuclear blast lifts a wall of dust kilometres out, which billows up fast and hangs for many seconds.
            if(plan.Warhead==WarheadKind.Nuke) return new Vec4(Math.Max(340*Math.Sqrt(air),50),14,9,0.35*NukeBlastReach(plan));
            return new Vec4(Math.Max(340*Math.Sqrt(air),50),Numbers.Clamp(2+0.05*dome,2.5,8),Numbers.Clamp(0.6+0.03*dome,0.6,4),Math.Max(dome*1.2,5));
        }
        // How far a nuclear blast wave stays visible over the ground (m): around the 1 psi radius, about 1.2 km x kt^(1/3)
        // (20 kt: 3.3 km, 1 Mt: 12 km). The dust it lifts runs out with it.
        public static double NukeBlastReach(FxPlan plan) { return Numbers.Clamp(1200*Math.Pow(Math.Max(plan.WarheadTnt/1e6,0.001),1.0/3),600,15000); }
        public static double GroundWaveSettle(FxPlan plan) { return plan.Warhead==WarheadKind.Nuke?40:Numbers.Clamp(6+0.15*DomeRadius(plan),7,20); }
        // A lobe as the sun sees it: centre and the radius of its gas (proxy units).
        public static Vec4 ShadowLobe(VolumeState s,int i)
        {
            LobeState l=s.Lobe(i);
            return new Vec4(l.Center.X,l.Center.Y,l.Center.Z,l.Radius*(l.Sim?0.62:1.05));
        }
        public static bool DistortionVisible(VolumeState s,double groundFire)
        {
            if(NuclearRing(s)) return s.ShockStrength*(1-Smooth(0.5*s.RingReach,s.RingReach,s.ShockRadius*s.BoxRadius))>0.002;   // (no shimmer: see Distortion)
            return s.RingStrength>0.002||s.Haze>0.002||groundFire*s.Atmosphere>0.003;
        }
        public static double LightIntensity(VolumeState s)
        { return Numbers.Clamp(1.4*s.Heat+1.6*s.Glow+1.3*s.FireHeat*(1-0.7*s.FlameFaint)+1.2*s.Pulse,0,2.2+1.3*s.Glow)*s.Fade*(0.6+0.4*s.Atmosphere)*s.LightScale; }   // (a faint hydrogen flame lights little)
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
