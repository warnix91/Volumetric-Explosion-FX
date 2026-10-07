namespace VolumetricExplosionFX.Core
{
    public sealed class FxSettings
    {
        public bool Enabled=true, Explosions=true, Debris=true, Ground=true, Water=true, Burst=true, Lighting=true, Debug=false, Volumetric=true;
        public bool ReplaceStockVisuals=true;
        public bool Shockwave=true;
        public bool Breakup=false;
        public bool PartDebris=true, Residue=true;
        public Quality Quality=Quality.Medium;
        public int MaxEvents=12, MaxDebris=768, MaxLights=3, ParticlesPerEvent=128;
        public int MaxVolumes=4, VolumeSteps=32;
        public int MaxDebrisParts=16, DebrisTriangles=4500;
        public double ResidueSeconds=45;
        public double Distance=10000, Intensity=1, DebrisDensity=1;
        public void ApplyPreset(Quality quality)
        {
            Quality=quality;
            switch(quality)
            {
                case Quality.Low: MaxEvents=6; MaxDebris=256; MaxLights=1; ParticlesPerEvent=64; MaxVolumes=2; VolumeSteps=24; MaxDebrisParts=6; DebrisTriangles=2400; break;
                case Quality.Medium: MaxEvents=12; MaxDebris=768; MaxLights=3; ParticlesPerEvent=128; MaxVolumes=4; VolumeSteps=32; MaxDebrisParts=16; DebrisTriangles=4500; break;
                case Quality.High: MaxEvents=20; MaxDebris=1536; MaxLights=4; ParticlesPerEvent=192; MaxVolumes=6; VolumeSteps=40; MaxDebrisParts=24; DebrisTriangles=7500; break;
                case Quality.Ultra: MaxEvents=24; MaxDebris=3072; MaxLights=6; ParticlesPerEvent=256; MaxVolumes=8; VolumeSteps=48; MaxDebrisParts=32; DebrisTriangles=12000; break;
            }
            Validate();
        }
        public void CopyFrom(FxSettings o)
        {
            Enabled=o.Enabled; Explosions=o.Explosions; Debris=o.Debris; Ground=o.Ground; Water=o.Water; Burst=o.Burst; Lighting=o.Lighting;
            Debug=o.Debug; Volumetric=o.Volumetric; ReplaceStockVisuals=o.ReplaceStockVisuals; Shockwave=o.Shockwave; Breakup=o.Breakup;
            PartDebris=o.PartDebris; Residue=o.Residue; Quality=o.Quality; MaxEvents=o.MaxEvents; MaxDebris=o.MaxDebris; MaxLights=o.MaxLights;
            ParticlesPerEvent=o.ParticlesPerEvent; MaxVolumes=o.MaxVolumes; VolumeSteps=o.VolumeSteps; MaxDebrisParts=o.MaxDebrisParts;
            DebrisTriangles=o.DebrisTriangles; ResidueSeconds=o.ResidueSeconds; Distance=o.Distance; Intensity=o.Intensity; DebrisDensity=o.DebrisDensity;
        }
        public string Structure { get { return Volumetric+"|"+MaxEvents+"|"+ParticlesPerEvent+"|"+MaxDebrisParts; } }
        public void Validate()
        {
            MaxEvents=(int)Numbers.Clamp(MaxEvents,1,24); MaxDebris=(int)Numbers.Clamp(MaxDebris,0,4096);
            MaxLights=(int)Numbers.Clamp(MaxLights,0,6); ParticlesPerEvent=(int)Numbers.Clamp(ParticlesPerEvent,16,256);
            Distance=Numbers.Clamp(Distance,100,30000); Intensity=Numbers.Clamp(Intensity,0.25,2);
            DebrisDensity=Numbers.Clamp(DebrisDensity,0,2);
            MaxVolumes=(int)Numbers.Clamp(MaxVolumes,0,8); VolumeSteps=(int)Numbers.Clamp(VolumeSteps,16,56);
            MaxDebrisParts=(int)Numbers.Clamp(MaxDebrisParts,0,48); DebrisTriangles=(int)Numbers.Clamp(DebrisTriangles,300,16000);
            ResidueSeconds=Numbers.Clamp(ResidueSeconds,0,180);
        }
    }
}
