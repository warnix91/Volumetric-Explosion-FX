using System;
namespace VolumetricExplosionFX.Core
{
    public static class Splash
    {
        public static double ColumnHeight(FxPlan plan)
        {
            if(plan.Kind!=EventKind.WaterImpact) return 0;
            double height=Numbers.Clamp(3*Math.Sqrt(Math.Max(plan.ImpactSpeed,0))*Math.Sqrt(Math.Max(plan.Radius,1)/3),6,90);
            // A warhead bursting at the sea throws a plume of water taller than its fireball (it was sized like a
            // crash, by its speed: the fireball hid it).
            if(plan.Look!=null&&plan.Look.Modern&&plan.WarheadTnt>0&&plan.Warhead!=WarheadKind.None&&plan.Warhead!=WarheadKind.Nuke&&
                plan.Warhead!=WarheadKind.Structure)
                height=Math.Max(height,Numbers.Clamp(2.6*VolumeEvolution.FireballRadius(plan),12,200));
            if(plan.Look!=null&&plan.Look.Modern&&plan.Depth>3)
            {
                // Under water: a charge just below the surface throws the tallest column; deeper, the bubble only lifts a dome.
                double r=Math.Max(VolumeEvolution.FireballRadius(plan),plan.Radius);
                double shallow=Numbers.Clamp(2*r/plan.Depth,0.25,1.4);
                height=Numbers.Clamp(Math.Max(height,8*Math.Sqrt(r))*shallow,6,140);
            }
            return height;
        }
        // Radius (m) of the white dome an underwater blast lifts, and the seconds before it breaks the surface; 0 at the surface.
        public static double DomeRadius(FxPlan plan)
        {
            if(plan.Look==null||!plan.Look.Modern||plan.Depth<=3) return 0;
            double r=Math.Max(VolumeEvolution.FireballRadius(plan),plan.Radius);
            return Numbers.Clamp(1.4*r*Math.Pow(Numbers.Clamp(r*3/plan.Depth,0.3,1),0.5),3,150);
        }
        public static double DomeDelay(FxPlan plan) { return plan.Depth<=3?0:Numbers.Clamp(plan.Depth/60,0.05,1.5); }
        // Burning fuel on the water spreads into a wide, low slick (hydrocarbons float; fuel released deep down does not burn).
        public static bool Slick(FxPlan plan) { return plan.Look!=null&&plan.Look.Modern&&plan.Depth<=3&&plan.Soot>0.25;
        }
    }
}
