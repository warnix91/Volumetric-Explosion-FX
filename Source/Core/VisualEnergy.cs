using System;
namespace VolumetricExplosionFX.Core
{
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
    }
    public static class VisualEnergy
    {
        public static FxPlan Calculate(DestructionEvent e, ResourceClassifier classifier, FxSettings settings)
        {
            PartSnapshot p=e.Part;
            double atmosphere=p.Environment.AtmosphereBlend;
            ResourceKind contents=classifier.Contents(p);
            double fuel=0, oxidizer=0, selfContained=0, cryogenic=0, electric=0;
            double sootMass=0, cleanMass=0, solidMass=0, brownMass=0;
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
                BurnableKg=Math.Min(burnable,1e7), Soot=soot*(1-white), SmokeR=sr, SmokeG=sg, SmokeB=sb, WhiteFlame=Numbers.Clamp(white,0,1),
                Radius=radius, FireFraction=fire, SmokeFraction=atmosphere*fire,
                VaporFraction=cryogenic/(cryogenic+10), ElectricFraction=electric/(electric+50), ExpansionSpeed=radius*(2.5-1.5*atmosphere),
                Lifetime=1.2+atmosphere*5,
                DebrisCount=settings.Debris?(int)Numbers.Clamp((8+score*12)*settings.DebrisDensity,0,settings.ParticlesPerEvent):0,
                PuffCount=(int)Numbers.Clamp(8+score*7,8,settings.ParticlesPerEvent/2)
            };
        }
    }
}
