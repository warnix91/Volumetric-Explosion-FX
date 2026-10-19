namespace VolumetricExplosionFX.Core
{
    // 1.1: runs the explosion models once at flight start, out of sight, so their code is compiled before the
    // first blast (the first one in a flight took 8-10 ms more to set up than the next ones: KSP compiles code on first
    // use). Pure calculations on made-up explosions: nothing is drawn or kept.
    public static class Warmup
    {
        public static void Run()
        {
            var tank=new FxPlan { PartKind=PartKind.LiquidTank, Kind=EventKind.GroundImpact, Atmosphere=1, ImpactSpeed=60, Radius=6,
                FireFraction=0.9, SmokeFraction=0.9, VisualEnergyScore=4.7, BurnableKg=1500, Soot=0.6, Fill=1, ExpansionSpeed=6, Lifetime=6,
                Look=LookProfile.Default(LookKind.Tank) };
            Chemistry.Apply(tank);
            var still=new Vec3(0,0,0);
            var ground=BlastLayout.Create(tank,new Vec3(0,-60,0),true,false,1,null);
            var air=BlastLayout.Create(tank,new Vec3(0,0,0),false,false,2,null);
            foreach(double age in new[]{0.0,0.5,2.0,8.0})
            {
                VolumeEvolution.Evaluate(tank,age,true,true,true,false,ground);
                VolumeEvolution.Evaluate(tank,age,true,true,false,false,air);
            }
            Residue.Plan(tank,true,false,45);
            var sea=new FxPlan { PartKind=PartKind.LiquidTank, Kind=EventKind.WaterImpact, Atmosphere=1, ImpactSpeed=120, Radius=6,
                FireFraction=0.5, SmokeFraction=0.5, VisualEnergyScore=5, BurnableKg=1500, Look=LookProfile.Default(LookKind.Tank) };
            foreach(double age in new[]{0.0,0.5,2.0}) WaterSplash.Evaluate(sea,age,9.81,still,still,3);
            var nuke=new FxPlan { PartKind=PartKind.LiquidTank, Kind=EventKind.GroundImpact, Atmosphere=1, ImpactSpeed=100, Radius=2,
                VisualEnergyScore=6, Look=LookProfile.Default(LookKind.Tank) };
            Warhead.Apply(nuke,new WarheadInfo { Kind=WarheadKind.Nuke, TntKg=20e6, RangeM=900 });
            Chemistry.Apply(nuke);
            var cloud=BlastLayout.Create(nuke,new Vec3(0,-100,0),true,false,4,null);
            double box=VolumeEvolution.BoxRadius(nuke,cloud);
            foreach(double age in new[]{0.0,3.0,30.0})
            {
                VolumeState s=VolumeEvolution.Evaluate(nuke,age,true,true,true,false,cloud);
                NukeCloud.Evaluate(nuke,cloud,s,age,box,4,600000);
            }
        }
    }
}
