using System;
namespace VolumetricExplosionFX.Core
{
    // What the ground under a blast is made of, read from the terrain's colour (1.1).
    public enum GroundKind { Unknown, Soil, Grass, Sand, Snow, Rock }
    public sealed class FxPlan
    {
        public PartKind PartKind;
        public ResourceKind Contents;
        public EventKind Kind;
        public double KineticJoules, VisualEnergyScore, Radius, FireFraction, SmokeFraction, VaporFraction, ElectricFraction, ExpansionSpeed, Lifetime;
        public double Atmosphere, ImpactSpeed;
        // Mass in kilograms; soot fraction and cooled smoke colour.
        public double BurnableKg, Soot, SmokeR=0.5, SmokeG=0.48, SmokeB=0.45;
        public double WhiteFlame;
        public double DustR=0.62, DustG=0.56, DustB=0.46;
        public int DebrisCount, PuffCount;
        // Share of the propellant that is hydrogen, monopropellant or hypergolic oxidiser, and kilograms of ore (1.1 chemistry only).
        public double Hydrogen, Mono, Brown, OreKg;
        // A combat mod's warhead (1.1 only, set by Warhead.Apply) and its blast radius in metres.
        public WarheadKind Warhead; public double WarheadRange;
        // TNT equivalent of a warhead (kg; a nuclear one's yield x 1e6), so explosions scale with their real power (1.1).
        public double WarheadTnt;
        // Height (m) of a burst above the ground under it (0 on or near the ground): a nuclear air burst looks different.
        public double BurstHeight;
        // How full the part's propellant tanks were (0..1; 1.1 only): a full tank, a half-empty one and a nearly empty one
        // do not blow up the same way.
        public double Fill;
        // Depth (m) of a blast under water (0 at or above the surface), and the kind of ground under a ground blast (1.1).
        public double Depth;
        public GroundKind Surface;
        // Artistic profile; null renders the 1.0 look.
        public LookProfile Look;
    }
    public static class VisualEnergy
    {
        public static FxPlan Calculate(DestructionEvent e, ResourceClassifier classifier, FxSettings settings)
        {
            PartSnapshot p=e.Part;
            double atmosphere=p.Environment.AtmosphereBlend;
            ResourceKind contents=classifier.Contents(p);
            double fuel=0, oxidizer=0, selfContained=0, cryogenic=0, electric=0;
            double sootMass=0, cleanMass=0, solidMass=0, brownMass=0, hydrogenMass=0, monoMass=0, oreMass=0, held=0, room=0;
            for(int i=0;i<p.Resources.Length;i++)
            {
                ResourceSnapshot r=p.Resources[i]; ResourceKind k=classifier.Lookup(r.Name);
                if((k&ResourceKind.Electric)!=0) electric+=r.Amount;
                if((k&ResourceKind.Fuel)!=0) fuel+=r.MassKg;
                if((k&ResourceKind.Oxidizer)!=0) oxidizer+=r.MassKg;
                if((k&ResourceKind.Solid)!=0 || (k&ResourceKind.Hypergolic)!=0 && (k&ResourceKind.Fuel)==0 && (k&ResourceKind.Oxidizer)==0) selfContained+=r.MassKg;
                if((k&ResourceKind.Cryogenic)!=0) cryogenic+=r.MassKg;
                else if((k&ResourceKind.Pressurized)!=0&&(k&(ResourceKind.Fuel|ResourceKind.Oxidizer|ResourceKind.Hypergolic|ResourceKind.Solid))==0)
                    cryogenic+=r.MassKg*0.6;
                if((k&ResourceKind.Fuel)!=0||(k&ResourceKind.Solid)!=0||(k&ResourceKind.Hypergolic)!=0&&(k&ResourceKind.Oxidizer)==0)
                {
                    if((k&ResourceKind.Solid)!=0) solidMass+=r.MassKg;
                    else if((k&ResourceKind.Sooty)!=0) sootMass+=r.MassKg;
                    else if((k&ResourceKind.Clean)!=0) cleanMass+=r.MassKg;
                    else sootMass+=r.MassKg*0.35; // methane and unknown fuels: light soot
                }
                if((k&ResourceKind.Hypergolic)!=0&&(k&ResourceKind.Oxidizer)!=0) brownMass+=r.MassKg;
                if((k&ResourceKind.Fuel)!=0&&(k&ResourceKind.Cryogenic)!=0&&(k&ResourceKind.Clean)!=0) hydrogenMass+=r.MassKg;
                if((k&ResourceKind.Hypergolic)!=0&&(k&ResourceKind.Clean)!=0&&(k&(ResourceKind.Fuel|ResourceKind.Oxidizer))==0) monoMass+=r.MassKg;
                if((k&ResourceKind.Dust)!=0) oreMass+=r.MassKg;
                if((k&(ResourceKind.Fuel|ResourceKind.Oxidizer|ResourceKind.Solid|ResourceKind.Hypergolic))!=0) { held+=r.Amount; room+=r.Capacity; }
            }
            // Visual sizing only, not a combustion or damage model.
            double burnable=selfContained+Math.Min(fuel,oxidizer)+fuel*atmosphere*0.2;
            if(e.Kind==EventKind.WaterImpact) burnable*=0.35;
            double speed=Numbers.Clamp(p.SurfaceVelocity.Length,0,15000);
            double kinetic=e.Kind==EventKind.Destruction?0:0.5*Math.Min(p.MassKg,1e7)*speed*speed;
            double score=Numbers.Clamp(Math.Log10(1+kinetic/1000+Math.Min(burnable,1e6)*40+Math.Min(p.MassKg,1e7)*0.05),0,9);
            double extent=Numbers.Clamp(p.Dimensions.Length*0.25,0.15,12);
            double radius=Numbers.Clamp((extent+Math.Pow(1+burnable+kinetic/500000,1.0/3)*0.5)*settings.Intensity,0.35,30);
            double fire=burnable/(burnable+10);
            double fuelTotal=Math.Max(sootMass+cleanMass+solidMass+brownMass,1e-9);
            double soot=Numbers.Clamp(sootMass/fuelTotal,0,1), white=solidMass/fuelTotal, brown=brownMass/fuelTotal;
            double sr=0.56-0.26*soot, sg=0.54-0.26*soot, sb=0.51-0.25*soot;
            sr+=(0.86-sr)*white; sg+=(0.85-sg)*white; sb+=(0.83-sb)*white;
            sr+=(0.6-sr)*brown; sg+=(0.36-sg)*brown; sb+=(0.18-sb)*brown;
            Vec3 dust=BodyLook.Dust(p.Environment!=null?p.Environment.Body:null);
            return new FxPlan { DustR=dust.X, DustG=dust.Y, DustB=dust.Z,
                PartKind=classifier.Classify(p), Contents=contents, Kind=e.Kind, Atmosphere=atmosphere, ImpactSpeed=speed,
                KineticJoules=kinetic, VisualEnergyScore=score,
                Hydrogen=Numbers.Clamp(hydrogenMass/fuelTotal,0,1), Mono=Numbers.Clamp(monoMass/fuelTotal,0,1), Brown=Numbers.Clamp(brown,0,1), OreKg=oreMass,
                Fill=room>0?Numbers.Clamp(held/room,0,1):0,
                Depth=WaterDepth(e,p),
                BurnableKg=Math.Min(burnable,1e7), Soot=soot*(1-white), SmokeR=sr, SmokeG=sg, SmokeB=sb, WhiteFlame=Numbers.Clamp(white,0,1),
                Radius=radius, FireFraction=fire, SmokeFraction=atmosphere*fire,
                VaporFraction=cryogenic/(cryogenic+10), ElectricFraction=electric/(electric+50), ExpansionSpeed=radius*(2.5-1.5*atmosphere),
                Lifetime=1.2+atmosphere*5,
                DebrisCount=settings.Debris?(int)Numbers.Clamp((8+score*12)*settings.DebrisDensity,0,settings.ParticlesPerEvent):0,
                PuffCount=(int)Numbers.Clamp(8+score*7,8,settings.ParticlesPerEvent/2)
            };
        }
        // How deep under the sea it broke up (m). A fast object is found a frame or two below the surface it hit (a
        // missile at 300 m/s, 6-12 m down): that drew a surface impact as an underwater blast (a dome, then a tall column).
        static double WaterDepth(DestructionEvent e,PartSnapshot p)
        {
            if(!(e.Kind==EventKind.WaterImpact||p.Environment.Splashed)||p.Environment.Altitude>=0) return 0;
            double sinking=Math.Max(-Vec3.Dot(p.SurfaceVelocity,p.Environment.Up),0);
            return Numbers.Clamp(-p.Environment.Altitude-0.04*sinking,0,5000);
        }
    }
}
