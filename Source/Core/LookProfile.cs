using System;
namespace VolumetricExplosionFX.Core
{
    // Which artistic profile an explosion uses. Chosen from what exploded and where.
    public enum LookKind { Tank, Solid, Vessel, Atmospheric, HighAltitude, Reentry }

    // Hand-tunable look of one kind of explosion. Every value is a dimensionless dial:
    // 1 means "as designed", the Classic profile reproduces the 1.0 renderer exactly.
    public sealed class LookProfile
    {
        public const int Count=13;
        public static readonly string[] Keys={ "fireballShape","turbulence","temperatureGradient","smokeDensity","smokeOpacity",
            "lightIntensity","expansionSpeed","coolingRate","noiseScale","shockwaveStrength","effectLifetime","flameHeat","sootDarkness" };
        // Lowest and highest accepted value of each dial, in Keys order.
        public static readonly double[] Min={ 0,0,0,0,0.2, 0,0.4,0.4,0.5,0,0.5,-0.3,0 };
        public static readonly double[] Max={ 2,2,2,3,3, 4,2.5,2.5,2,3,2.5,0.3,1 };
        readonly double[] v=new double[Count];
        // True for the 1.1 renderer extras (smoke that erodes into tendrils, richer shading); Classic switches them off.
        public bool Modern=true;
        public LookProfile(double shape,double turbulence,double gradient,double smokeDensity,double smokeOpacity,double light,
            double expansion,double cooling,double noiseScale,double shock,double lifetime,double flameHeat,double sootDarkness)
        {
            v[0]=shape; v[1]=turbulence; v[2]=gradient; v[3]=smokeDensity; v[4]=smokeOpacity; v[5]=light;
            v[6]=expansion; v[7]=cooling; v[8]=noiseScale; v[9]=shock; v[10]=lifetime; v[11]=flameHeat; v[12]=sootDarkness;
            Validate();
        }
        // Billow amplitude of the silhouette (bigger = lumpier).
        public double Shape { get { return v[0]; } }
        // Fine eroded detail on the fireball's surface (0 = 1.0 renderer, smooth).
        public double Turbulence { get { return v[1]; } }
        // Contrast between hot pockets and cooler lanes; also spreads the cooling over time.
        public double Gradient { get { return v[2]; } }
        public double SmokeDensity { get { return v[3]; } }
        public double SmokeOpacity { get { return v[4]; } }
        public double LightIntensity { get { return v[5]; } }
        // Higher = the fireball reaches full size sooner.
        public double ExpansionSpeed { get { return v[6]; } }
        // Higher = the flames go out sooner.
        public double CoolingRate { get { return v[7]; } }
        // Higher = finer billows.
        public double NoiseScale { get { return v[8]; } }
        public double ShockwaveStrength { get { return v[9]; } }
        public double EffectLifetime { get { return v[10]; } }
        // Shifts the whole flame ramp: positive = whiter, negative = deeper red.
        public double FlameHeat { get { return v[11]; } }
        // How dark cooling, sooty gas becomes (0 = the 1.0 light grey).
        public double SootDarkness { get { return v[12]; } }
        public double this[int i] { get { return v[i]; } set { v[i]=value; Validate(); } }
        public void Validate() { for(int i=0;i<Count;i++) v[i]=Numbers.Clamp(v[i],Min[i],Max[i]); }
        public LookProfile Clone()
        { return new LookProfile(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8],v[9],v[10],v[11],v[12]) { Modern=Modern }; }
        public void CopyFrom(LookProfile o) { for(int i=0;i<Count;i++) v[i]=o.v[i]; Modern=o.Modern; }
        public bool SameAs(LookProfile o) { if(Modern!=o.Modern) return false; for(int i=0;i<Count;i++) if(Math.Abs(v[i]-o.v[i])>1e-9) return false; return true; }
        public static int IndexOf(string key) { for(int i=0;i<Count;i++) if(string.Equals(Keys[i],key,StringComparison.OrdinalIgnoreCase)) return i; return -1; }

        // The exact 1.0 look, kept for A/B comparison and as the fallback when no profile is chosen.
        public static LookProfile Classic() { return new LookProfile(1,0,0,1,1,1,1,1,1,1,1,0,0) { Modern=false }; }

        // Shipped 1.1 defaults per kind: first pass on the look-development bench, to be refined in game.
        public static LookProfile Default(LookKind kind)
        {
            switch(kind)
            {
                // Aluminised propellant: short, white-hot, little soot, bright flash.
                case LookKind.Solid: return new LookProfile(0.9,1.0,0.8,0.6,1,1.35,1.2,1,1.1,1,1,0,0.2);
                // Whole vehicle: heavier soot and broader, slower billows.
                case LookKind.Vessel: return new LookProfile(1.15,1.1,1.1,1.25,1.1,1.25,0.9,0.9,0.9,1.1,1.15,-0.02,0.65);
                // In flight: the gas is free to billow, no ground skirt.
                case LookKind.Atmospheric: return new LookProfile(1.1,1.1,1.0,1.0,1,1.1,1,1,1,0.9,1,0.02,0.55);
                // Thin air: fast, ragged dispersal and little smoke.
                case LookKind.HighAltitude: return new LookProfile(0.85,1.2,0.9,0.5,0.8,1,1.15,1.0,1.15,0.6,0.95,-0.07,0.4);
                // Breakup during re-entry: plasma-hot, brief.
                case LookKind.Reentry: return new LookProfile(0.7,1.2,0.6,0.4,0.8,1.2,1.5,1.4,1.2,0.5,0.8,0.15,0.3);
                default: return new LookProfile(1.05,1.0,1.0,1.0,1,1.15,1,1,1,1,1,0.06,0.5);
            }
        }
        public static LookKind Select(FxPlan plan,bool surface,bool reentry,int members)
        {
            if(reentry) return LookKind.Reentry;
            if(plan.WhiteFlame>0.5) return LookKind.Solid;
            if(!surface)
            {
                double air=Numbers.Clamp(plan.Atmosphere,0,1);
                return air<0.4?LookKind.HighAltitude:LookKind.Atmospheric;
            }
            return members>=4||plan.BurnableKg>20000?LookKind.Vessel:LookKind.Tank;
        }
        // Shader dials: (shape, turbulence, gradient, flame heat), (smoke opacity, soot darkness, 0, 0).
        // Smoke density, timing, noise scale, shock and light act on the CPU side (VolumeEvolution).
        public Vec4 Shader0 { get { return new Vec4(Shape,Turbulence,Gradient,FlameHeat); } }
        public Vec4 Shader1 { get { return new Vec4(SmokeOpacity,SootDarkness,Modern?1:0,0); } }
    }

    // One editable profile per kind. A null library means everything renders as Classic.
    public sealed class LookLibrary
    {
        public static readonly string[] Names=Enum.GetNames(typeof(LookKind));
        readonly LookProfile[] profiles=new LookProfile[Names.Length];
        public bool UseClassic;
        // Test blasts from the look editor force the profile being edited (-1 = choose normally).
        public int Forced=-1;
        public LookLibrary() { for(int i=0;i<profiles.Length;i++) profiles[i]=LookProfile.Default((LookKind)i); }
        public LookKind Choose(LookKind natural) { return Forced>=0&&Forced<profiles.Length?(LookKind)Forced:natural; }
        public bool IsDefault(LookKind k) { return profiles[(int)k].SameAs(LookProfile.Default(k)); }
        public LookProfile this[LookKind k] { get { return profiles[(int)k]; } }
        public LookProfile For(LookKind k) { return UseClassic?Classic:profiles[(int)k]; }
        static readonly LookProfile Classic=LookProfile.Classic();
        // In place, so explosions already on screen follow the change (live preview).
        public void Reset() { for(int i=0;i<profiles.Length;i++) Reset((LookKind)i); UseClassic=false; }
        public void Reset(LookKind k) { profiles[(int)k].CopyFrom(LookProfile.Default(k)); }
        public static bool TryParse(string name,out LookKind kind)
        {
            for(int i=0;i<Names.Length;i++) if(string.Equals(Names[i],name,StringComparison.OrdinalIgnoreCase)) { kind=(LookKind)i; return true; }
            kind=LookKind.Tank; return false;
        }
    }
}
