using System;
namespace VolumetricExplosionFX.Core
{
    // Explosions of combat mods (BDArmory and the weapon packs built on it): what the warhead is, and how it looks.
    // Visual approximations from footage of real detonations, not a damage model; the combat mod keeps all damage.
    public enum WarheadKind { None, Shell, HighExplosive, Bomb, ContinuousRod, Thermobaric, Incendiary, Nuke, Emp, Kinetic, Structure }
    public struct WarheadInfo
    {
        public WarheadKind Kind;
        public double TntKg, RangeM;   // explosive mass (TNT equivalent) and blast radius reported by the combat mod
    }
    public static class Warhead
    {
        static bool Has(string s,string part) { return s.IndexOf(part,StringComparison.Ordinal)>=0; }
        // A whole word of the name ("emp" in "EMP pod", not in "tempest").
        static bool Word(string s,string word)
        {
            for(int i=s.IndexOf(word,StringComparison.Ordinal);i>=0;i=s.IndexOf(word,i+1,StringComparison.Ordinal))
            {
                bool before=i==0||!char.IsLetter(s[i-1]), after=i+word.Length>=s.Length||!char.IsLetter(s[i+word.Length]);
                if(before&&after) return true;
            }
            return false;
        }
        // warheadType and source are the combat mod's enum names; weapon is its weapon or part name.
        public static WarheadKind Classify(string warheadType,string source,string weapon,double tntKg)
        {
            string t=warheadType??"", w=(weapon??"").ToLowerInvariant();
            if(t=="EMP") return WarheadKind.Emp;   // a nuclear EMP device: the flash without the fireball
            if(t=="Nuke"||Has(w,"nuke")||Has(w,"nuclear")||Has(w,"atomic")||tntKg>=5000) return WarheadKind.Nuke;
            if(Word(w,"emp")) return WarheadKind.Emp;
            if(t=="Kinetic") return WarheadKind.Kinetic;
            if(Has(w,"thermobaric")||Has(w,"fuel-air")||Has(w,"fuelair")||Has(w,"fuel air")) return WarheadKind.Thermobaric;
            if(Has(w,"napalm")||Has(w,"incend")||Has(w,"thermite")||Has(w,"phosphor")) return WarheadKind.Incendiary;
            if(Has(t,"Continuous")) return WarheadKind.ContinuousRod;
            if(source=="Bullet") return WarheadKind.Shell;
            if(Has(w,"bomb")||Has(w,"gbu")||Has(w,"jdam")||Has(w,"mk8")||Has(w,"fab")) return WarheadKind.Bomb;
            // A named missile stays a missile however heavy its warhead (anti-ship missiles carry hundreds of kilograms).
            if(Has(w,"missile")||Word(w,"asm")||Word(w,"agm")||Word(w,"aim")||Word(w,"sam")||Has(w,"cruise")||Has(w,"torpedo")||Has(w,"rocket")) return WarheadKind.HighExplosive;
            if(tntKg>=80) return WarheadKind.Bomb;
            return WarheadKind.HighExplosive;
        }
        // Every detonation of a combat mod is drawn by VEFX (the combat mod's own visuals never show).
        public static bool Replaces(WarheadInfo w) { return w.Kind!=WarheadKind.None; }
        // Small cannon shells, kinetic strikes and near-empty charges come by the dozen: a light burst (flash, sparks, smoke),
        // not a full explosion with its own volume and wreckage.
        public static bool Small(WarheadInfo w)
        {
            if(w.Kind==WarheadKind.None||w.Kind==WarheadKind.Kinetic) return true;
            if(w.Kind==WarheadKind.Shell) return w.TntKg<0.25;
            return w.TntKg<=0.01&&w.Kind!=WarheadKind.Emp;
        }
        // Radius (m) of the light burst of a small hit: from the charge, or from the calibre (mm) of a solid slug.
        public static double BurstSize(double tntKg,double caliberMm)
        {
            return tntKg>0?Numbers.Clamp(Math.Pow(tntKg,1.0/3)*1.6,0.2,6):Numbers.Clamp(caliberMm*0.006,0.15,1.5);
        }
        public static void Apply(FxPlan plan,WarheadInfo w)
        {
            if(plan==null||w.Kind==WarheadKind.None) return;
            // Up to 10 Mt (it was capped at 1 kt: every nuclear warhead above that looked the same).
            double tnt=Numbers.Clamp(w.TntKg,0.01,1e10), energy=tnt*4.184e6, range=Numbers.Clamp(w.RangeM,1,5000);
            plan.Warhead=w.Kind; plan.WarheadRange=range; plan.WarheadTnt=tnt;
            plan.KineticJoules=energy*0.25;
            plan.VisualEnergyScore=Numbers.Clamp(Math.Log10(1+energy/1000),2,8.5);
            plan.Radius=Numbers.Clamp(0.3*range,0.35,30);   // the blast wave reaches far: the dome is most of the lethal radius
            plan.Hydrogen=0; plan.Mono=0; plan.Brown=0; plan.OreKg=0; plan.ElectricFraction=0;
            double burn, soot, white, vapor=0, shade;
            switch(w.Kind)
            {
                // Cannon shell: a sharp white flash, a small burst of flame and a puff of grey-black smoke.
                case WarheadKind.Shell: burn=6; soot=0.55; white=0.4; shade=0.34; break;
                // Missile or rocket: flash, a brief orange afterburn of the explosive, a dark grey smoke ball.
                case WarheadKind.HighExplosive: burn=7; soot=0.45; white=0.32; shade=0.3; break;
                // Bomb: the same but heavier: more soil and smoke, a taller column.
                case WarheadKind.Bomb: burn=8; soot=0.55; white=0.28; shade=0.27; break;
                // Air-to-air continuous rod: a ring of fragments and a light grey puff, little fire.
                case WarheadKind.ContinuousRod: burn=5; soot=0.3; white=0.4; shade=0.5; break;
                // Fuel-air: a huge, slow orange fireball.
                case WarheadKind.Thermobaric: burn=30; soot=0.35; white=0.08; shade=0.32; break;
                // Incendiary: a burst of burning gel, thick black smoke, fires left on the ground.
                case WarheadKind.Incendiary: burn=12; soot=0.85; white=0.1; shade=0.18; break;
                // Nuclear: a blinding flash, a giant fireball rising into a mushroom, a condensation cloud.
                case WarheadKind.Nuke: burn=1e7; soot=0.15; white=0.12; vapor=0; shade=0.6; break;   // the condensation is its ring (VolumeEvolution.NukeLook), not a lasting white fog
                // EMP: a blue-white electric flash, no fire.
                // An EMP missile with a real charge (most of them) blows up as well, with an electric flash in it.
                case WarheadKind.Emp: if(tnt>=0.5) { burn=5; soot=0.45; white=0.35; shade=0.38; plan.ElectricFraction=0.6; }
                    else { burn=0; soot=0; white=0; shade=0.6; plan.ElectricFraction=1; } break;
                default: burn=0; soot=0.3; white=0; shade=0.5; break;
            }
            plan.BurnableKg=Math.Min(tnt*burn,1e7);
            plan.FireFraction=plan.BurnableKg/(plan.BurnableKg+10);
            plan.SmokeFraction=plan.Atmosphere*plan.FireFraction;
            plan.Soot=soot; plan.WhiteFlame=white;
            plan.SmokeR=shade+(w.Kind==WarheadKind.Nuke?0.02:0); plan.SmokeG=shade*0.96; plan.SmokeB=shade*0.9;
            plan.VaporFraction=Math.Max(plan.VaporFraction,vapor);
            // A nuclear cloud's condensation is drawn by its own features (Wilson cloud, rings, ice cap): the generic white
            // vapour ball, sized to the dome, became a pale fog over kilometres.
            if(w.Kind==WarheadKind.Nuke) plan.VaporFraction=0;
            // Only a burst on (or in) the ground throws earth up; one in the air stays a ball of smoke.
            bool soil=plan.Kind==EventKind.GroundImpact&&(w.Kind==WarheadKind.Bomb||w.Kind==WarheadKind.HighExplosive||w.Kind==WarheadKind.Nuke||w.Kind==WarheadKind.Shell);
            if(soil)
            {
                // A ground burst throws the soil up: the cloud is mostly earth (brown-grey, the colour of the ground), the
                // fire a brief flash inside it. Carried as rock dust (the ore path) and dirt jets.
                double earth=w.Kind==WarheadKind.Bomb?0.65:w.Kind==WarheadKind.Nuke?0.35:0.4;
                plan.SmokeR+=(plan.DustR*0.62-plan.SmokeR)*earth; plan.SmokeG+=(plan.DustG*0.6-plan.SmokeG)*earth; plan.SmokeB+=(plan.DustB*0.58-plan.SmokeB)*earth;
                plan.Soot*=1-0.6*earth;
                plan.OreKg=w.Kind==WarheadKind.Nuke?1e6:tnt*(w.Kind==WarheadKind.Bomb?6:2);
                plan.ImpactSpeed=Math.Max(plan.ImpactSpeed,w.Kind==WarheadKind.Bomb?140:w.Kind==WarheadKind.Nuke?250:60);
            }
            if(w.Kind==WarheadKind.Nuke) { plan.VisualEnergyScore=Numbers.Clamp(8.6+0.4*Math.Log10(Math.Max(tnt/1e6,0.01)),8.2,10); plan.Radius=30; plan.Lifetime=60; plan.SmokeR=0.6; plan.SmokeG=0.43; plan.SmokeB=0.34; }   // a rusty, reddish-brown cloud (nitrogen dioxide, soil)
            plan.DebrisCount=w.Kind==WarheadKind.Shell?Math.Min(plan.DebrisCount,16):Math.Max(plan.DebrisCount,(int)Numbers.Clamp(20+12*Math.Log10(1+tnt),20,80));
        }
        // A building coming down (KSC facility or another destructible structure): mostly concrete dust rolling out from the
        // base in a broad, low cloud that hangs, chunks of wall thrown out, and fire only where it held fuel (tank farm).
        // width and height in metres, fuelKg of propellant stored there.
        public static void Collapse(FxPlan plan,double width,double height,double fuelKg)
        {
            if(plan==null) return;
            double w=Numbers.Clamp(width,4,200), h=Numbers.Clamp(height,3,120), volume=w*w*h;
            plan.Warhead=WarheadKind.Structure; plan.WarheadRange=w;
            plan.Hydrogen=0; plan.Mono=0; plan.Brown=0; plan.ElectricFraction=0; plan.VaporFraction=0;
            plan.VisualEnergyScore=Numbers.Clamp(2.5+Math.Log10(1+volume/50),3,8);
            plan.Radius=Numbers.Clamp(0.32*w,3,40);
            // The cloud is sized as a fireball would be (radius 2.9 (0.3 m)^(1/3)): about 0.6 of the building's width of dust,
            // but almost without fire unless it held fuel.
            double dust=Math.Pow(0.62*w/2.9,3)/0.3;
            plan.BurnableKg=Math.Min(Math.Max(Numbers.Clamp(fuelKg,0,1e6),dust),1e7);
            plan.FireFraction=fuelKg>0?fuelKg/(fuelKg+10):0.04;
            plan.SmokeFraction=plan.Atmosphere*Math.Max(plan.FireFraction,0.6);
            plan.OreKg=volume*60;   // rubble dust: the rock-dust path
            plan.Soot=fuelKg>0?0.55:0.08; plan.WhiteFlame=0;
            // Concrete and plaster dust, pale grey-beige; darker where fuel burns.
            plan.SmokeR=fuelKg>0?0.38:0.7; plan.SmokeG=fuelKg>0?0.36:0.67; plan.SmokeB=fuelKg>0?0.34:0.61;
            plan.ImpactSpeed=Math.Max(plan.ImpactSpeed,40);
            plan.DebrisCount=Math.Max(plan.DebrisCount,(int)Numbers.Clamp(30+w*0.8,30,90));
            plan.Lifetime=Math.Max(plan.Lifetime,10);
        }
        // How long a warhead's fireball keeps burning, as a share of a fuel fireball of the same size (footage of missile,
        // shell and bomb impacts: a flash and a brief orange burst, then dark smoke and dust).
        public static double FireLife(WarheadKind k)
        {
            switch(k)
            {
                // (a real fireball again, about a second, as in 1.0: at 0.3-0.4 it was a brown crust at once and
                // read as no fireball at all)
                case WarheadKind.Shell: case WarheadKind.ContinuousRod: case WarheadKind.Kinetic: return 0.8;
                case WarheadKind.HighExplosive: case WarheadKind.Emp: return 0.95;
                case WarheadKind.Bomb: return 0.98;
                default: return 1;
            }
        }
        // Ground fires are left only by incendiary and fuel-air warheads; a plain explosive blackens and craters. (Not a
        // nuclear one any more: a fire burning at ground zero under the mushroom read as a bug.)
        public static bool LeavesFire(WarheadKind k) { return k==WarheadKind.Incendiary||k==WarheadKind.Thermobaric; }
        // How close two nuclear blasts must be to be the same warhead seen twice (its part dying, then its detonation, which
        // can be hundreds of metres apart at a missile's speed).
        public static double SameNuke(double fireballRadius) { return Math.Max(800,3*fireballRadius); }
    }
}
