using System;
namespace VolumetricExplosionFX.Core
{
    // Maps an explosion's clock onto the baked fluid simulation (see Tools/firesim.py and FireSimTable).
    public static class FireSim
    {
        public struct Variant
        {
            public int Frames, LastWhole; public float Half, Height, TMax, TimePower, RefRadius, SourceY, MaxXZ, MaxY; public float[] Bounds, Radius; public bool Ground;
        }
        public static readonly Variant Ground=new Variant { Frames=FireSimTable.GroundFrames, Half=FireSimTable.GroundHalf, Height=FireSimTable.GroundHeight,
            TMax=FireSimTable.GroundTMax, TimePower=FireSimTable.GroundTimePower, RefRadius=FireSimTable.GroundRefRadius, SourceY=FireSimTable.GroundSourceY,
            MaxXZ=FireSimTable.GroundMaxXZ, MaxY=FireSimTable.GroundMaxY, Bounds=FireSimTable.GroundBounds, Radius=FireSimTable.GroundRadius, LastWhole=Whole(FireSimTable.GroundBounds,FireSimTable.GroundFrames,FireSimTable.GroundHeight), Ground=true };
        public static readonly Variant Ground2=new Variant { Frames=FireSimTable.Ground2Frames, Half=FireSimTable.Ground2Half, Height=FireSimTable.Ground2Height,
            TMax=FireSimTable.Ground2TMax, TimePower=FireSimTable.Ground2TimePower, RefRadius=FireSimTable.Ground2RefRadius, SourceY=FireSimTable.Ground2SourceY,
            MaxXZ=FireSimTable.Ground2MaxXZ, MaxY=FireSimTable.Ground2MaxY, Bounds=FireSimTable.Ground2Bounds, Radius=FireSimTable.Ground2Radius, LastWhole=Whole(FireSimTable.Ground2Bounds,FireSimTable.Ground2Frames,FireSimTable.Ground2Height), Ground=true };
        public static readonly Variant Ground3=new Variant { Frames=FireSimTable.Ground3Frames, Half=FireSimTable.Ground3Half, Height=FireSimTable.Ground3Height,
            TMax=FireSimTable.Ground3TMax, TimePower=FireSimTable.Ground3TimePower, RefRadius=FireSimTable.Ground3RefRadius, SourceY=FireSimTable.Ground3SourceY,
            MaxXZ=FireSimTable.Ground3MaxXZ, MaxY=FireSimTable.Ground3MaxY, Bounds=FireSimTable.Ground3Bounds, Radius=FireSimTable.Ground3Radius, LastWhole=Whole(FireSimTable.Ground3Bounds,FireSimTable.Ground3Frames,FireSimTable.Ground3Height), Ground=true };
        public static readonly Variant Ground4=new Variant { Frames=FireSimTable.Ground4Frames, Half=FireSimTable.Ground4Half, Height=FireSimTable.Ground4Height,
            TMax=FireSimTable.Ground4TMax, TimePower=FireSimTable.Ground4TimePower, RefRadius=FireSimTable.Ground4RefRadius, SourceY=FireSimTable.Ground4SourceY,
            MaxXZ=FireSimTable.Ground4MaxXZ, MaxY=FireSimTable.Ground4MaxY, Bounds=FireSimTable.Ground4Bounds, Radius=FireSimTable.Ground4Radius, LastWhole=Whole(FireSimTable.Ground4Bounds,FireSimTable.Ground4Frames,FireSimTable.Ground4Height), Ground=true };
        public static readonly Variant Air=new Variant { Frames=FireSimTable.AirFrames, Half=FireSimTable.AirHalf, Height=FireSimTable.AirHeight,
            TMax=FireSimTable.AirTMax, TimePower=FireSimTable.AirTimePower, RefRadius=FireSimTable.AirRefRadius, SourceY=FireSimTable.AirSourceY,
            MaxXZ=FireSimTable.AirMaxXZ, MaxY=FireSimTable.AirMaxY, Bounds=FireSimTable.AirBounds, Radius=FireSimTable.AirRadius, LastWhole=Whole(FireSimTable.AirBounds,FireSimTable.AirFrames,FireSimTable.AirHeight), Ground=false };
        public static readonly Variant Air2=new Variant { Frames=FireSimTable.Air2Frames, Half=FireSimTable.Air2Half, Height=FireSimTable.Air2Height,
            TMax=FireSimTable.Air2TMax, TimePower=FireSimTable.Air2TimePower, RefRadius=FireSimTable.Air2RefRadius, SourceY=FireSimTable.Air2SourceY,
            MaxXZ=FireSimTable.Air2MaxXZ, MaxY=FireSimTable.Air2MaxY, Bounds=FireSimTable.Air2Bounds, Radius=FireSimTable.Air2Radius, LastWhole=Whole(FireSimTable.Air2Bounds,FireSimTable.Air2Frames,FireSimTable.Air2Height), Ground=false };
        // 1.1: several baked runs per place, so explosions of one kind do not all take the same shape.
        // Ground: 0 mushroom on a stem, 1 squat and ragged, 2 tall narrow column, 3 bushy chaotic ball without a stem.
        // Air: 0 ragged ball, 1 calm bubble rolling up into a ring cap that trails streamers.
        // A nuclear detonation's own run (1.1): a big buoyant fireball rolling into a toroidal cap under stable air, in a box
        // twice as tall. Not among the random shapes: only a nuke takes it (NukeRun).
        public static readonly Variant Nuke=new Variant { Frames=FireSimTable.NukeFrames, Half=FireSimTable.NukeHalf, Height=FireSimTable.NukeHeight,
            TMax=FireSimTable.NukeTMax, TimePower=FireSimTable.NukeTimePower, RefRadius=FireSimTable.NukeRefRadius, SourceY=FireSimTable.NukeSourceY,
            MaxXZ=FireSimTable.NukeMaxXZ, MaxY=FireSimTable.NukeMaxY, Bounds=FireSimTable.NukeBounds, Radius=FireSimTable.NukeRadius,
            LastWhole=Whole(FireSimTable.NukeBounds,FireSimTable.NukeFrames,FireSimTable.NukeHeight), Ground=true };
        public static readonly Variant Nuke2=new Variant { Frames=FireSimTable.Nuke2Frames, Half=FireSimTable.Nuke2Half, Height=FireSimTable.Nuke2Height,
            TMax=FireSimTable.Nuke2TMax, TimePower=FireSimTable.Nuke2TimePower, RefRadius=FireSimTable.Nuke2RefRadius, SourceY=FireSimTable.Nuke2SourceY,
            MaxXZ=FireSimTable.Nuke2MaxXZ, MaxY=FireSimTable.Nuke2MaxY, Bounds=FireSimTable.Nuke2Bounds, Radius=FireSimTable.Nuke2Radius,
            LastWhole=Whole(FireSimTable.Nuke2Bounds,FireSimTable.Nuke2Frames,FireSimTable.Nuke2Height), Ground=true };
        public static readonly Variant NukeAir=new Variant { Frames=FireSimTable.NukeairFrames, Half=FireSimTable.NukeairHalf, Height=FireSimTable.NukeairHeight,
            TMax=FireSimTable.NukeairTMax, TimePower=FireSimTable.NukeairTimePower, RefRadius=FireSimTable.NukeairRefRadius, SourceY=FireSimTable.NukeairSourceY,
            MaxXZ=FireSimTable.NukeairMaxXZ, MaxY=FireSimTable.NukeairMaxY, Bounds=FireSimTable.NukeairBounds, Radius=FireSimTable.NukeairRadius,
            LastWhole=Whole(FireSimTable.NukeairBounds,FireSimTable.NukeairFrames,FireSimTable.NukeairHeight), Ground=false };
        // Nuclear runs: 4 the rolling mushroom, 5 a rougher boiling one ("Trinity"), 6 an air burst (no ground under it).
        public const int NukeRun=4, NukeRun2=5, NukeAirRun=6;
        public static readonly Variant[] NukeRuns={ Nuke, Nuke2, NukeAir };
        public static readonly bool[] NukeHave=new bool[3];   // which of them loaded
        public static bool NukeLoaded { get { return NukeHave[0]; } set { NukeHave[0]=value; } }
        public static bool IsNukeRun(int run) { return run>=NukeRun&&run<=NukeAirRun; }
        public static readonly Variant[] GroundRuns={ Ground, Ground2, Ground3, Ground4 };
        public static readonly Variant[] AirRuns={ Air, Air2 };
        // How many runs of each are loaded (VolumeAssets, the bench); 1 = only the first, as before.
        public static int GroundLoaded=1, AirLoaded=1;
        public static int Usable(bool surface,int run)
        {
            if(IsNukeRun(run))
            {
                // Without its texture a nuke falls back to the other nuclear runs, then to the mushroom (0).
                if(NukeHave[run-NukeRun]) return run;
                if(surface&&NukeHave[0]) return NukeRun;
                if(!surface&&NukeHave[2]) return NukeAirRun;
                return 0;
            }
            return run>0&&run<(surface?GroundLoaded:AirLoaded)?run:0;
        }
        public static Variant Run(bool surface,int run)
        {
            int u=Usable(surface,run);
            if(IsNukeRun(u)) return NukeRuns[u-NukeRun];
            return surface?GroundRuns[u]:AirRuns[u];
        }
        // The proxy box must hold the gas: smallest reference radius and largest extents over the first runs (as before the
        // extra shapes, so those boxes do not change) and the runs this explosion actually takes.
        static readonly double BaseRef=Math.Min(Math.Min(Ground.RefRadius,Ground2.RefRadius),Air.RefRadius);
        static readonly double BaseXZ=Math.Max(Math.Max(Ground.MaxXZ,Ground2.MaxXZ),Air.MaxXZ), BaseY=Math.Max(Math.Max(Ground.MaxY,Ground2.MaxY),Air.MaxY);
        public static void Extents(int groundRun,int airRun,out double refRadius,out double maxXZ,out double maxY)
        {
            Variant g=Run(true,groundRun), a=Run(false,airRun);
            refRadius=Math.Min(BaseRef,Math.Min(g.RefRadius,a.RefRadius));
            maxXZ=Math.Max(BaseXZ,Math.Max(g.MaxXZ,a.MaxXZ)); maxY=Math.Max(BaseY,Math.Max(g.MaxY,a.MaxY));
        }
        // Which ground run an explosion takes, from its kind and one draw (0..1): every kind has several shapes it can take.
        public static int ChooseGround(WarheadKind warhead,TankCharacter character,double pick)
        {
            switch(warhead)
            {
                case WarheadKind.Nuke: return pick<0.5?NukeRun:NukeRun2;          // its own runs (fall back to the mushroom)
                case WarheadKind.Bomb: return pick<0.4?1:pick<0.75?3:2;           // earth: squat, chaotic, or a tall column of dirt
                case WarheadKind.Structure: return pick<0.55?1:3;                 // dust: low and wide, or a rolling ragged mass
                case WarheadKind.Thermobaric: case WarheadKind.Incendiary: return pick<0.35?0:pick<0.7?3:2;
                case WarheadKind.None: break;
                default: return pick<0.22?0:pick<0.52?1:pick<0.67?2:3;           // missiles, shells, rods...: any of the four, wide ones first
            }
            switch(character)
            {
                case TankCharacter.Fireball: return pick<0.6?0:2;                 // a boiling tank rises: mushroom or column
                case TankCharacter.Jet: return pick<0.5?3:1;                      // torn open sideways: ragged shapes
                case TankCharacter.Flash: return pick<0.5?1:3;
                default: return pick<0.25?0:pick<0.55?1:pick<0.7?2:3;
            }
        }
        public static int ChooseAir(WarheadKind warhead,double pick)
        {
            if(warhead==WarheadKind.Nuke) return NukeAirRun;
            double p=pick*7.31%1;   // decorrelated from the ground choice
            return p<(warhead==WarheadKind.Nuke?0.6:0.45)?1:0;
        }
        // The last frame whose gas is still whole inside the simulated box: past it the rising cloud reaches the box's
        // ceiling, flattens against it and leaves it (it read as a flat explosion, then a cloud falling apart). Explosions
        // hold that frame and go on rising and spreading by themselves (VolumeEvolution).
        static int Whole(float[] bounds,int frames,float height)
        {
            for(int f=0;f<frames;f++) if(bounds[f*6+3]>=0.95*height) return Math.Max(f-1,1);
            return frames-1;
        }
        // Top of the gas at a (fractional) frame, in simulation units above the box floor.
        public static double Top(Variant v,double frame)
        {
            double[] box=new double[6]; Box(v,frame,box);
            return box[3];
        }
        // Simulation time (0..TMax) at which a fractional frame is reached (inverse of Frame).
        public static double TimeOf(Variant v,double frame) { return v.TMax*Math.Pow(Numbers.Clamp(frame/Math.Max(v.Frames-1,1),0,1),v.TimePower); }
        // Texture frame (fractional) reached at simulation time t.
        public static double Frame(Variant v,double t)
        {
            if(t<=0) return 0;
            double u=Math.Pow(Math.Min(t/v.TMax,1),1.0/v.TimePower);
            return u*(v.Frames-1);
        }
        // Box that holds the gas at a (fractional) frame, in simulation units: x0,x1,y0,y1,z0,z1. Continuous in
        // the frame: each edge moves from one frame's to the next, widened in between to the union of the two (the gas then is
        // the blend of both). The union taken whole jumped at every frame (the cloud's box grew by a tenth at once, moving
        // the ray march's steps: the clouds rose in jerks).
        public static void Box(Variant v,double frame,double[] box)
        {
            double fc=Numbers.Clamp(frame,0,v.Frames-1);
            int f0=Math.Max(0,Math.Min((int)Math.Floor(fc),v.Frames-1)), f1=Math.Min(f0+1,v.Frames-1);
            double t=fc-f0, widen=2*t*(1-t);
            for(int k=0;k<6;k++)
            {
                double a=v.Bounds[f0*6+k], b=v.Bounds[f1*6+k];
                double mid=a+(b-a)*t, grow=Math.Abs(b-a)*widen;
                box[k]=(k&1)==0?mid-grow:mid+grow;
            }
        }
    }
}
