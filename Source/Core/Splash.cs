using System;
namespace VolumetricExplosionFX.Core
{
    public static class Splash
    {
        public static double ColumnHeight(FxPlan plan)
        {
            if(plan.Kind!=EventKind.WaterImpact) return 0;
            return Numbers.Clamp(3*Math.Sqrt(Math.Max(plan.ImpactSpeed,0))*Math.Sqrt(Math.Max(plan.Radius,1)/3),6,90);
        }
    }
}
