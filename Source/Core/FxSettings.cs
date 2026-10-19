namespace VolumetricExplosionFX.Core
{
    public sealed class FxSettings
    {
        public bool Enabled=true, Explosions=true, Debris=true, Ground=true, Water=true, Burst=true, Lighting=true, Debug=false, Volumetric=true;
        public bool ReplaceStockVisuals=true;
        public bool Shockwave=true;
        // 1.1: nearby explosions shake the camera, the blast arriving after the flash (saved).
        public bool CameraShake=true;
        // 1.1 photo mode for screenshots: 0 normal, 1 slow motion, 2 frozen (VEFX effects only; this session only, not saved).
        public int PhotoMode;
        // 1.1: the settings window's style (0 glass, 1 neon, 2 terminal, 3 paper, 4 mission), layout (0 open,
        // 1 panel, 2 compact), backgrounds' opacity and size on screen (saved).
        public int UiStyle, UiLayout;
        public double UiOpacity=0.82, UiSize=0.9;
        // Last quick setup chosen in the window (-1: none; not saved).
        public int Profile=-1;
        public bool Breakup=false;
        public bool PartDebris=true, Residue=true;
        public Quality Quality=Quality.Ultra;
        public int MaxEvents=32, MaxDebris=3072, MaxLights=6, ParticlesPerEvent=256;
        public int MaxVolumes=16, VolumeSteps=48;
        public int MaxDebrisParts=32, DebrisTriangles=12000;
        public double ResidueSeconds=45;
        public double Distance=10000, Intensity=1, DebrisDensity=1;
        // Hand-tuned look per kind of explosion (shared, edited live by the look editor).
        public LookLibrary Looks=new LookLibrary();
        // Shows the look editor page (advanced; off for regular players).
        public bool LookEditor;
        // Lowers volume detail and light count while frames exceed the budget.
        public bool Adaptive=false;
        // Volumes are ray-marched at 1/VolumeDivisor of the screen width and height (1 = full).
        // Independent of the quality presets.
        public int VolumeDivisor=1;
        public void ApplyPreset(Quality quality)
        {
            Quality=quality;
            switch(quality)
            {
                // 1.1: every blast of a big crash or a battle gets its own volume (a burning wreck holds its slot for its
                // whole fire): fewer volumes left blasts drawn as flat sprites or missing. Lower presets save on detail
                // (march steps, resolution) rather than on the number of explosions.
                case Quality.Low: MaxEvents=10; MaxDebris=256; MaxLights=1; ParticlesPerEvent=64; MaxVolumes=4; VolumeSteps=24; MaxDebrisParts=6; DebrisTriangles=2400; break;
                case Quality.Medium: MaxEvents=20; MaxDebris=768; MaxLights=3; ParticlesPerEvent=128; MaxVolumes=10; VolumeSteps=32; MaxDebrisParts=16; DebrisTriangles=4500; break;
                case Quality.High: MaxEvents=28; MaxDebris=1536; MaxLights=4; ParticlesPerEvent=192; MaxVolumes=12; VolumeSteps=40; MaxDebrisParts=24; DebrisTriangles=7500; break;
                case Quality.Ultra: MaxEvents=32; MaxDebris=3072; MaxLights=6; ParticlesPerEvent=256; MaxVolumes=16; VolumeSteps=48; MaxDebrisParts=32; DebrisTriangles=12000; break;
            }
            Validate();
        }
        // Quick setups: 0 cinematic (everything at full detail), 1 balanced, 2 performance (modest PCs).
        public void ApplyProfile(int profile)
        {
            switch(profile)
            {
                case 0: ApplyPreset(Quality.Ultra); VolumeDivisor=1; Adaptive=false; ResidueSeconds=90; break;
                case 1: ApplyPreset(Quality.High); VolumeDivisor=2; Adaptive=true; ResidueSeconds=45; break;
                case 2: ApplyPreset(Quality.Medium); VolumeDivisor=2; Adaptive=true; ResidueSeconds=30; DebrisTriangles=3000; break;
                default: return;
            }
            Profile=profile; Validate();
        }
        public static double PhotoSpeed(int mode) { return mode==1?0.2:mode==2?0:1; }
        public void CopyFrom(FxSettings o)
        {
            Enabled=o.Enabled; Explosions=o.Explosions; Debris=o.Debris; Ground=o.Ground; Water=o.Water; Burst=o.Burst; Lighting=o.Lighting;
            Debug=o.Debug; Volumetric=o.Volumetric; ReplaceStockVisuals=o.ReplaceStockVisuals; Shockwave=o.Shockwave; Breakup=o.Breakup;
            PartDebris=o.PartDebris; Residue=o.Residue; Quality=o.Quality; MaxEvents=o.MaxEvents; MaxDebris=o.MaxDebris; MaxLights=o.MaxLights;
            ParticlesPerEvent=o.ParticlesPerEvent; MaxVolumes=o.MaxVolumes; VolumeSteps=o.VolumeSteps; MaxDebrisParts=o.MaxDebrisParts;
            DebrisTriangles=o.DebrisTriangles; ResidueSeconds=o.ResidueSeconds; Distance=o.Distance; Intensity=o.Intensity; DebrisDensity=o.DebrisDensity;
            Looks=o.Looks; LookEditor=o.LookEditor; Adaptive=o.Adaptive; VolumeDivisor=o.VolumeDivisor;
            CameraShake=o.CameraShake; PhotoMode=o.PhotoMode; Profile=o.Profile; UiStyle=o.UiStyle; UiLayout=o.UiLayout; UiOpacity=o.UiOpacity; UiSize=o.UiSize;
        }
        public string Structure { get { return Volumetric+"|"+MaxEvents+"|"+ParticlesPerEvent+"|"+MaxDebrisParts; } }
        public void Validate()
        {
            MaxEvents=(int)Numbers.Clamp(MaxEvents,1,32); MaxDebris=(int)Numbers.Clamp(MaxDebris,0,4096);
            MaxLights=(int)Numbers.Clamp(MaxLights,0,6); ParticlesPerEvent=(int)Numbers.Clamp(ParticlesPerEvent,16,256);
            Distance=Numbers.Clamp(Distance,100,30000); Intensity=Numbers.Clamp(Intensity,0.25,2);
            DebrisDensity=Numbers.Clamp(DebrisDensity,0,2);
            MaxVolumes=(int)Numbers.Clamp(MaxVolumes,0,16); VolumeSteps=(int)Numbers.Clamp(VolumeSteps,16,56);
            MaxDebrisParts=(int)Numbers.Clamp(MaxDebrisParts,0,48); DebrisTriangles=(int)Numbers.Clamp(DebrisTriangles,300,16000);
            ResidueSeconds=Numbers.Clamp(ResidueSeconds,0,180); VolumeDivisor=(int)Numbers.Clamp(VolumeDivisor,1,3);
            UiStyle=(int)Numbers.Clamp(UiStyle,0,4); UiLayout=(int)Numbers.Clamp(UiLayout,0,2);
            UiOpacity=Numbers.Clamp(UiOpacity,0.35,1); UiSize=Numbers.Clamp(UiSize,0.7,1.3);
        }
    }
}
