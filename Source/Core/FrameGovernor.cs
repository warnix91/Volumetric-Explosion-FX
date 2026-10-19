using System;
namespace VolumetricExplosionFX.Core
{
    // Keeps heavy scenes playable: when frames take longer than the budget while effects are on
    // screen, lowers the volume detail (ray-march steps) and the light count; recovers when the
    // frame time drops. The budget never goes below what the game itself needs without effects.
    public sealed class FrameGovernor
    {
        public const double MinScale=0.35;
        double average=-1, idle=-1;
        public double Scale { get; private set; }
        public double TargetMs { get; set; }
        public FrameGovernor(double targetMs=22) { TargetMs=targetMs; Scale=1; }
        // Capped: a slow moment without effects (loading, map) must not make the budget lax.
        public double BudgetMs { get { return Numbers.Clamp(idle>0?idle+6:TargetMs,TargetMs,TargetMs*1.5); } }
        public void Update(double frameMs,double dt,bool effects)
        {
            if(double.IsNaN(frameMs)||frameMs<=0||frameMs>1000) return; // ignore pauses and loading hitches
            dt=Numbers.Clamp(dt,0,0.25);
            if(!effects)
            {
                idle=idle<0?frameMs:idle+(frameMs-idle)*Math.Min(1,dt/2.0);
                average=-1; Scale=Math.Min(1,Scale+0.5*dt); return;
            }
            average=average<0?frameMs:average+(frameMs-average)*Math.Min(1,dt/0.4);
            double budget=BudgetMs;
            if(average>budget*1.1) Scale-=Math.Min(0.6,(average/budget-1))*dt*1.5;
            else if(average<budget*0.85) Scale+=0.25*dt;
            Scale=Numbers.Clamp(Scale,MinScale,1);
        }
        public int Lights(int max) { return max<=0?0:Math.Max(1,(int)Math.Round(max*Scale)); }
        // New volumes admitted under load; beyond this, new blasts use the particle renderer. 1.1: at least three quarters
        // of them (the detail is lowered first): fewer left whole explosions drawn as flat sprites, or not at all.
        public int Volumes(int max) { return max<=0?0:Math.Max((int)Math.Ceiling(max*0.75),(int)Math.Round(max*Numbers.Clamp((Scale-0.2)/0.8,0,1))); }
    }
}
