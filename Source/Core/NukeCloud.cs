using System;
namespace VolumetricExplosionFX.Core
{
    // 1.1: a nuclear cloud as NukeVolume.shader draws it, rebuilt from the records of the tests instead of a baked
    // simulation stretched over kilometres (Glasstone & Dolan ch. II; the colour films of Teapot and Upshot-Knothole):
    // a white-hot sphere (a hemisphere on the ground) grows to its full size in a second and cools through yellow and
    // orange; as it rises its edges lag and roll under, so it turns into a toroid whose gas climbs up the middle, spills
    // over the top and rolls down and in under the rim, glowing red inside a reddish-brown skin (nitrogen dioxide) for
    // seconds; the afterwinds suck dust up from the ground into a stem with a dust skirt at its foot; the cap keeps
    // climbing and widening for minutes, its colour fading to pinkish brown, then grey-white in moist air.
    // Proxy units: the volume's box radius is one unit; y up from the burst point (the ground under a ground burst).
    public struct NukeFrame
    {
        public Vec4 Ball;    // cap centre y, cap radius, cap half height, how far it has rolled into a mushroom (0..1)
        public Vec4 Torus;   // ring radius, tube radius, roll angle of its circulation (rad), heat of its core (0..1)
        public Vec4 Stem;    // ground y, stem radius, top of the dust climbed so far (y), stem density
        public Vec4 Skirt;   // dust skirt radius, height, density; distance the stem's dust has flowed up
        public Vec4 Cap;     // smoke albedo rgb, white-hot share of the fireball
        public Vec4 Dust;    // dust albedo rgb, fade of the whole cloud
        public Vec4 Extra;   // Wilson cloud radius, its strength, condensation collar strength, ice cap strength
        public Vec4 Scale;   // extinction per proxy unit, detail (boiling of the surface), age (s), box / body radius
        public Vec4 Heat;    // how far its skin has cooled into smoke (0..1), density of the fireball's gas, march detail (1 full), afterglow
        // 1.1: pileus clouds (Castle, Licorne): smooth white hoods of condensation over the rising cap in moist
        // air, left at their altitude while the cap punches through them (their rim a skirt round it for a moment): top
        // height, radius, strength, thickness (proxy units). Pile2: the condensation rings round the stem: count,
        // strength, age (s), the fireball's glowing time (s).
        public Vec4 Pile0, Pile1, Pile2;
        // 1.1: how it goes in its last minutes: erosion (0..1, it breaks up from its edges into wisps), the wind's
        // shear at the cap (x, z, proxy units; the stem leans under it), and the height below which the stem has gone.
        public Vec4 Late;
        // 1.1: the blast wave's front in the air: its radius (proxy units), the strength of its veil, how far the
        // Wilson cloud has cleared from the top into a low ring (0..1).
        public Vec4 Air;
        // 1.1: the earth a ground burst throws out at once: radius of the ejecta, unused, strength, unused.
        public Vec4 Jets;
        // 1.1: the blast wave over the ground: radius of its foot, depth and height of the wall of dust it raises
        // (proxy units), strength.
        public Vec4 Wave;
    }
    public static class NukeCloud
    {
        // The flash a nuclear burst throws over the whole view (a white-out, 0..1), from its age (s), the eye's distance (m)
        // and its fireball's radius (m): blinding for a fraction of a second, then a glare fading over a couple of seconds.
        // The blinding flash of the burst (NukeGlare's star), 0..2: a fraction of a second.
        // Two pulses, as the films show: a spike, then the thermal pulse that keeps the fireball blinding for a second
        // or more, longer the bigger the yield (second maximum ~0.04 kt^0.44 s; a fraction of a second only read as a blink).
        public static double Flash(double age,double tf)
        {
            if(age<0) return 0;
            double kt=Math.Max(Math.Pow(10,(tf-5.5)/2.2)-1,0.01);   // (the yield behind FireballDuration)
            double tp=0.05+0.04*Math.Pow(kt,0.44), tau=0.6+0.35*Math.Log10(1+kt);
            double pulse=0.95*Smooth(0,tp,age)*Math.Exp(-Math.Max(age-tp,0)/tau);
            return 2*Math.Max(Math.Exp(-age/0.06),pulse)*Smooth(0,0.012,age);
        }
        // The glare over the fireball as a camera sees it: its size (fireball
        // radii), the flash, the soft bloom of the white-hot ball, its warmth (1 white .. 0 orange).
        public static Vec4 Glare(NukeFrame f,double age,double tf)
        {
            if(age<0) return new Vec4();
            double flash=Flash(age,tf), white=f.Cap.W, heat=f.Torus.W, m=f.Ball.W, fade=f.Dust.W;
            return new Vec4((2.4+5.5*flash)*(1+0.3*(1-m)),flash*fade,(1.1*white+0.35*heat*(1-m))*fade,1-Smooth(0.1*tf,1.5*tf,age));
        }
        // The light the burst throws over the land (Unity light intensity) and its colour: a white flash outshining the sun,
        // then the fireball's warm glow for as long as it burns.
        // night 0..1: at night the eye adapts and the glowing cloud lights the land for a minute and more, far out
        // (it was too dark at night).
        public static double LightLevel(double age,double tf,double night=0)
        {
            if(age<0) return 0;
            return (6*Math.Exp(-age/(0.25+0.08*tf))+1.8*Math.Exp(-age/(1.1*tf))+(0.5+0.6*night)*Math.Exp(-age/(6*tf)))*(1+2*night)*Smooth(0,0.02,age);
        }
        public static double LightRange(double age,double tf,double fireball,double night=0,double height=0)
        {
            return Numbers.Clamp(Math.Max(fireball*80*(0.45+0.55*Math.Exp(-age/(0.6*tf)))*(1+0.5*night),2.4*height),2000,50000);
        }
        // The air round the glowing cloud lit by it (NukeGlare's halo): radius (m), strength, temperature (0 red ..
        // 1 yellow). Faint by day; at night a wide orange glow for as long as the cloud glows, and its red afterglow.
        public static Vec4 Halo(NukeFrame f,double box,double night)
        {
            double heat=Math.Max(f.Torus.W,0), fade=f.Dust.W;
            double s=((0.15+0.85*night)*Math.Pow(heat,0.8)+0.5*night*f.Heat.W)*fade;
            return new Vec4(2.6*f.Ball.Y*box,s,Numbers.Clamp(heat*1.6,0,1),0);
        }
        public static Vec3 LightColour(double age,double tf)
        {
            double w=Smooth(0.1*tf,1.6*tf,age);
            return new Vec3(1,0.97+(0.56-0.97)*w,0.92+(0.26-0.92)*w);
        }
        // Warmth of the screen's white-out: white at the flash, an amber wash as it fades (the fireball outshining the sky).
        public static double ScreenWarmth(double age) { return 1-Math.Exp(-Math.Max(age,0)/0.45); }
        public static double ScreenFlash(double age,double distance,double fireball)
        {
            if(age<0||age>9) return 0;
            double near=1/(1+Math.Pow(distance/Math.Max(150*fireball,800),2));
            double k=Numbers.Clamp(Math.Sqrt(fireball/150),0.85,2);
            return Numbers.Clamp((0.95*Math.Exp(-age/(0.3*k))+0.4*Math.Exp(-age/(1.6*k)))*Smooth(0,0.03,age)*near,0,0.97);
        }
        static double Smooth(double a,double b,double x) { double t=Numbers.Clamp((x-a)/(b-a),0,1); return t*t*(3-2*t); }
        // Cloud top above the burst point (m): the climb to NukeTop (Glasstone fig. 2.16), slowed at first while the bubble
        // gathers speed (lift time ~0.4 tf).
        public static double Top(BlastLayout layout,double fireball,double tf,double age)
        {
            double top=layout.NukeTop>0?layout.NukeTop:Math.Max(fireball*4,700), tau=layout.NukeTau>0?layout.NukeTau:60;
            double lift=0.4*tf, ts=Math.Max(0,age-lift*(1-Math.Exp(-age/lift)));
            return fireball+Math.Max(top-fireball,0)*(1-Math.Exp(-ts/tau));
        }
        // Fireball radius (m) while it grows: most of its size within a tenth of its glowing time.
        public static double Growth(double fireball,double tf,double age)
        {
            double g=1-Math.Exp(-age/Math.Max(0.05*tf,1e-3));
            return fireball*(0.08+0.92*Math.Sqrt(g));
        }
        public static NukeFrame Evaluate(FxPlan plan,BlastLayout layout,VolumeState s,double age,double box,double seed,double bodyRadius)
        {
            var f=new NukeFrame();
            if(plan==null||layout==null||box<=0) return f;
            double kt=Math.Max(plan.WarheadTnt/1e6,0.001), Rf=Math.Max(VolumeEvolution.FireballRadius(plan),1), tf=VolumeEvolution.FireballDuration(plan);
            NukeKind kind=layout.Nuke==NukeKind.None?NukeKind.Surface:layout.Nuke;
            double hb=kind==NukeKind.Surface?0:Math.Max(layout.NukeGround,0);
            double air=Numbers.Clamp(plan.Atmosphere,0,1);
            bool water=s.Water, vacuum=air<0.03;
            double R=Growth(Rf,tf,age), top=Math.Max(Top(layout,Rf,tf,age),R);
            // Thin air (Duna) lifts a smaller, lower cloud; in vacuum (the Mun) nothing lifts it at all: the glowing ball
            // swells as its debris flies apart and fades within seconds, the regolith it threw up spreading out below it.
            if(vacuum) { R*=1+2.2*Smooth(0.1*tf,2.5*tf,age); top=R; }
            else if(air<0.5) top=R+(top-R)*(0.35+0.65*Math.Sqrt(air/0.5));
            // The fireball rolls up into a mushroom over a couple of its glowing times.
            double m=vacuum?0:Smooth(0.25*tf,2.2*tf,age);
            double capR=Math.Max(R*(1+0.2*m),0.3*top);
            double halfH=R+(0.55*capR-R)*m;
            // Its last minutes: it spreads and flattens as it drifts.
            double life=VolumeEvolution.VolumeDuration(plan), late=Smooth(0.45*life,0.96*life,age);
            capR*=1+0.7*late; halfH*=1-0.4*late;
            // On the ground the fireball starts as a hemisphere (its centre on the ground) and lifts off as it climbs.
            double cy=Math.Max(0,top-halfH);
            double ring=0.6*capR, tube=0.42*capR;
            // The circulation turns about once for every tube radius or so the cloud has climbed.
            double roll=0.9*(top-Rf)/Math.Max(tube,1);
            // Incandescence: a blinding core that fades within a couple of glowing times, and a long red-orange tail glowing in
            // the heart of the rolling cap for tens of seconds (the iconic glowing mushroom of the test films).
            double heat=vacuum?Math.Exp(-age/(0.15*tf)):0.5*Math.Exp(-age/(0.9*tf))+0.38*Math.Exp(-age/(6*tf))+0.12*Math.Exp(-age/(22*tf));
            f.Ball=new Vec4(cy/box,capR/box,halfH/box,m);
            f.Torus=new Vec4(ring/box,tube/box,roll,heat);
            // The dust stem: on a ground burst it trails the fireball from the moment it lifts off (it used to catch
            // up with the cap only after a couple of glowing times, so the cap rose alone and the stem appeared later), thin
            // at first and thickening as the afterwinds suck up more dirt; later, thinner and only part of the way after a
            // low air burst; none after a high one. About a fifth of the cap's radius (the test films: a cap four to six
            // stems wide).
            double ground=-hb;
            double under=cy-0.55*halfH;
            double reach=vacuum?0:kind==NukeKind.Surface?Smooth(0.03*tf,0.7*tf,age):kind==NukeKind.LowAir?Numbers.Clamp(1.35-0.22*hb/Rf,0.25,1)*Smooth(0.5*tf,4*tf,age):0;
            double stemR=capR*Numbers.Clamp(0.2*Math.Pow(kt/20,-0.1),0.11,0.26)*(kind==NukeKind.Surface?1:0.65);
            double stemTop=ground+reach*Math.Max(under-ground,0);
            double stemDensity=reach>0?(kind==NukeKind.Surface?1:0.6)*(0.4+0.6*Smooth(0.1*tf,1.6*tf,age)):0;
            stemDensity*=1-0.5*late;
            f.Stem=new Vec4(ground/box,stemR/box,stemTop/box,stemDensity);
            // The dust skirt at the stem's foot: a broad, low cloud of dirt that billows out from ground zero.
            // (On water it is the base surge: a ring of white mist rolling out over the sea, wider and longer-lived.)
            double skirtR=stemR*(1.5+(water?4.2:2.1)*Smooth(0.05*tf,(water?5:4)*tf,age));
            double skirt=kind==NukeKind.Surface?1:kind==NukeKind.LowAir?0.7*Math.Exp(-hb/(2.5*Rf)):0;
            skirt*=Smooth(0.03*tf,0.4*tf,age);
            skirt*=1-Smooth(0.3*life,0.65*life,age);
            if(vacuum) { skirtR=Rf*(1+3*Smooth(0.05*tf,1.5*tf,age)); skirt=kind==NukeKind.Surface?0.6*Smooth(0.02*tf,0.2*tf,age)*(1-Smooth(0.6*tf,3.5*tf,age)):0; }
            double flow=1.4*(top-Rf)+12*age;   // metres the dust has climbed (it rises faster than the cap)
            f.Skirt=new Vec4(skirtR/box,(water?0.34:0.22)*skirtR/box,skirt*(water?0.85:1),flow/box);
            // Colours: reddish-brown while hot (nitrogen dioxide over the glowing core), pinkish brown, then grey-white in
            // moist air, light grey-beige in dry air, dusty brown when the ground was sucked into it.
            double c1=Smooth(0.6*tf,3.5*tf,age), c2=Smooth(4*tf,14*tf,age);
            // (Less saturated: in sunlight the nitrogen dioxide reads as a dark reddish brown, not an orange cotton ball.)
            double r1=0.36,g1=0.21,b1=0.14, r2=0.5,g2=0.44,b2=0.4, r3,g3,b3;
            if(vacuum) { r1=0.55; g1=0.53; b1=0.52; r2=0.62; g2=0.61; b2=0.6; }   // no air: no nitrogen dioxide
            if(kind==NukeKind.Surface&&!water) { r3=0.4*plan.DustR+0.42; g3=0.4*plan.DustG+0.38; b3=0.4*plan.DustB+0.33; }
            else if(layout.NukeHumid||water) { r3=0.86; g3=0.85; b3=0.84; }
            else { r3=0.76; g3=0.72; b3=0.68; }
            double cr=r1+(r2-r1)*c1, cg=g1+(g2-g1)*c1, cb=b1+(b2-b1)*c1;
            cr+=(r3-cr)*c2; cg+=(g3-cg)*c2; cb+=(b3-cb)*c2;
            double white=1-Smooth(0.03*tf,0.3*tf,age);   // blinding white for the first seconds
            f.Cap=new Vec4(cr,cg,cb,white);
            double fade=Numbers.Clamp(s.Fade,0,1)*(1-0.85*s.Dissolve);
            // Dust of the ground's colour; spray and mist over water.
            // (A darker earth-brown than the lifted dust of an ordinary blast: the column of a ground burst reads dark against
            // the sky in the test films; a pale tan made a flat tube.)
            f.Dust=water?new Vec4(0.74,0.77,0.8,fade):new Vec4(Numbers.Clamp(0.45*plan.DustR+0.25,0,1),Numbers.Clamp(0.45*plan.DustG+0.2,0,1),Numbers.Clamp(0.45*plan.DustB+0.14,0,1),fade);
            f.Extra=new Vec4(s.NukeWilson.X,s.NukeWilson.Y,s.Nuke.Z,s.NukeIce.Z);
            // Condensation rings round the stem where it climbs through moist layers (two, three at megatons):
            // count and strength.
            // As they form they spread out from the stem; later they fray into wisps (count, strength, spread, fray).
            double ringsOn=layout.NukeHumid&&kind!=NukeKind.HighAir&&!vacuum?Smooth(0.6*tf,1.2*tf,age)*(1-Smooth(6*tf,10*tf,age)):0;
            // Rings: count, strength, age (s), glowing time (s): the shader runs each ring's own life (forming from
            // the stem outward, evaporating from its edge and through holes).
            ringsOn=layout.NukeHumid&&kind!=NukeKind.HighAir&&!vacuum&&age>0.55*tf&&age<12*tf?1:0;
            f.Pile2=f.Stem.W>0.001&&ringsOn>0?new Vec4(s.NukeGround.W,0.9*Numbers.Clamp(s.Fade,0,1),age,tf):new Vec4();
            // About 0.03 per metre: a cloud kilometres thick shows only its first tens of metres, as real ones do.
            double boil=vacuum?0.15:Smooth(0.03*tf,0.6*tf,age);
            f.Scale=new Vec4(0.03*box,boil,age,box/Math.Max(bodyRadius,1000));
            // The skin cools first: patches of dark reddish-brown smoke over the glowing fireball, its glow left in the folds.
            // Pileus discs over the cap in moist air: formed as the cap shoots up (about one fireball time in), stacked over
            // it, left at their altitude as the cap climbs through them, spreading and thinning over a minute or two.
            if((layout.NukeHumid||water)&&!vacuum&&air>0.5)
            {
                double t0=0.9*tf, R0=Growth(Rf,tf,t0), top0=Math.Max(Top(layout,Rf,tf,t0),R0);
                double m0=Smooth(0.25*tf,2.2*tf,t0), cap0=Math.Max(R0*(1+0.2*m0),0.3*top0);
                double spread=1+0.7*Smooth(t0,t0+8*tf,age);
                for(int k=0;k<1;k++)   // (one: two stacked hoods were a stack of saucers)
                {
                    // Formed ahead of the cap, where its top will be a little later: it floats over the climbing cloud as a
                    // white hood, then the cloud punches through it (its rim a skirt round the cloud for a moment).
                    double ahead=t0+(0.6+1.4*k)*tf, Rr=Growth(Rf,tf,ahead), topR=Math.Max(Top(layout,Rf,tf,ahead),Rr);
                    double capAt=Math.Max(Rr*(1+0.2*Smooth(0.25*tf,2.2*tf,ahead)),0.3*topR);
                    double height=topR+(0.12+0.05*k)*capAt;
                    double passed=Smooth(height-0.3*capR,height+1.1*capR,top);
                    // (once the cloud has punched through, a ragged skirt of it hangs round the cap for a while
                    // instead of vanishing at once: nothing was left over the cloud)
                    double strength=Smooth(t0+0.25*k*tf,t0+(0.6+0.25*k)*tf,age)*(1-0.2*k)*(1-0.55*passed)*(1-Smooth(t0+5*tf,t0+11*tf,age));
                    var hood=new Vec4(height/box,capAt*1.15*(1+0.25*Smooth(t0,t0+6*tf,age))/box,strength,0.09*capAt/box);
                    if(k==0) f.Pile0=hood; else f.Pile1=hood;
                }
            }
            // y: how dense the fireball's gas still is (in vacuum it thins out to nothing as it flies apart).
            // w: the ionised air in the cloud glows a deep red-violet for tens of seconds (lost in daylight: it is what keeps
            // a mushroom visible at night). None in vacuum.
            double afterglow=vacuum?0:Smooth(0.3*tf,1.0*tf,age)*(0.62*Math.Exp(-Math.Max(age-tf,0)/(3.5*tf))+0.38*Math.Exp(-Math.Max(age-tf,0)/(16*tf)));
            // (The crust of cooled smoke forms later and in patches, the glow showing through its cracks.)
            f.Heat=new Vec4(Smooth(0.3*tf,2.5*tf,age),vacuum?(1-Smooth(0.15*tf,1.2*tf,age))/(1+R/Rf):1,1,afterglow);
            // How it goes (it vanished at the end of its life without any sign of going): it drifts and is sheared
            // downwind (the cap further than the stem, a little from the start, much more later), it spreads, and from about
            // half its life it breaks up from its edges into wisps and holes; the stem goes first, from the ground up.
            double wx=layout.SimLeanX, wz=layout.SimLeanZ, wl=Math.Sqrt(wx*wx+wz*wz);
            if(wl<1e-6) { double a=seed*2.399; wx=Math.Cos(a); wz=Math.Sin(a); } else { wx/=wl; wz/=wl; }
            double shear=vacuum?0:top*(0.05*Smooth(4*tf,0.4*life,age)+0.2*Smooth(0.4*life,0.98*life,age));
            double gone=Smooth(0.5*life,0.86*life,age);
            double cut=ground-3*stemR+gone*(1.08*Math.Max(under-ground,0)+3*stemR);
            f.Late=new Vec4(Smooth(0.5*life,life,age),wx*shear/box,wz*shear/box,cut/box);
            // The blast wave's front in the air: its radius (no veil: a white veil read as a glass dome).
            double frontM=s.ShockRadius*box;
            f.Air=new Vec4(s.ShockRadius,0,s.NukeWilson.W,0);
            // The wall of dust (spray at sea) the blast wave raises as it races over the ground, tallest at its front.
            double rg=Math.Sqrt(Math.Max(frontM*frontM-hb*hb,0));
            double wall=air>0.3&&!vacuum&&hb<6*Rf?Numbers.Clamp(s.ShockStrength,0,1)*Smooth(0.02,0.3,age)*(1-Smooth(0.7,0.95,s.ShockRadius))*
                (1-Smooth(2*Rf,6*Rf,hb))*Smooth(2.5*Rf,4*Rf,rg):0;
            f.Wave=new Vec4(rg/box,(0.3*rg+0.5*Rf)/box,Numbers.Clamp(0.06*rg+0.35*Rf,20,320)/box,wall);
            // A ground burst throws out the earth (spray over water) in fingers at once: they fly out to about three
            // fireball radii and fall back into the base surge within a few glowing times (Sugar, Uncle, Baker).
            if(kind==NukeKind.Surface&&!vacuum)
            {
                double re=Rf*(0.6+2.4*Smooth(0,1.4*tf,age));
                double js=Smooth(0.01*tf,0.12*tf,age)*(1-Smooth(0.6*tf,1.8*tf,age));
                f.Jets=new Vec4(re/box,0,js,0);
            }
            return f;
        }
    }
}
