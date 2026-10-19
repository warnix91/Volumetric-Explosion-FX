using System;
namespace VolumetricExplosionFX.Core
{
    // Colour, reach and priority of the light an explosion casts on its surroundings.
    public static class ExplosionLight
    {
        static double Smooth(double a,double b,double x) { double t=Numbers.Clamp((x-a)/(b-a),0,1); return t*t*(3-2*t); }
        // Flash: white (slightly blue in vacuum). Fireball: from yellow-white when hot to deep
        // orange as it cools; white flames (solid propellant) stay paler.
        public static Vec3 Colour(VolumeState s)
        {
            double heat=Numbers.Clamp(Math.Max(s.FireHeat,s.Heat),0,1), flash=Numbers.Clamp(s.Glow,0,1);
            double r=1, g=0.42+0.36*heat, b=0.12+0.33*heat*heat;
            double white=Numbers.Clamp(s.WhiteFlame,0,1)*0.6;
            g+=(0.86-g)*white; b+=(0.72-b)*white;
            double fr=1, fg=0.95, fb=0.88-0.06*s.Atmosphere+0.08*(1-s.Atmosphere);
            return new Vec3(r+(fr-r)*flash,g+(fg-g)*flash,b+(fb-b)*flash);
        }
        // Metres. Unity's point light fades as 1/(1+25 d²/range²), so the range must be several
        // fireball radii for the ground around a blast to receive visible light. The flash reaches further.
        public static double Range(VolumeState s,double fireballRadius,double domeRadius)
        {
            double size=Math.Max(Math.Max(fireballRadius*8,domeRadius*2),6);
            return Numbers.Clamp((size+15)*(1+0.8*Numbers.Clamp(s.Glow,0,1)),15,400);
        }
        // How much this light matters to the picture: bright, big lights near the camera win.
        public static double Priority(double intensity,double range,double cameraDistance)
        {
            if(intensity<=0.03||range<=0) return 0;
            double reach=cameraDistance/Math.Max(range,1);
            return intensity*range/(1+reach*reach*4);
        }
        // Marks the best `max` candidates (priority > 0) in `chosen`, best first in `order`.
        // Arrays are owned by the caller so nothing is allocated per frame.
        public static int Select(double[] priority,int count,int max,bool[] chosen,int[] order)
        {
            int n=0;
            for(int i=0;i<count;i++) chosen[i]=false;
            for(int k=0;k<max;k++)
            {
                int best=-1;
                for(int i=0;i<count;i++) if(!chosen[i]&&priority[i]>0&&(best<0||priority[i]>priority[best])) best=i;
                if(best<0) break;
                chosen[best]=true; order[n++]=best;
            }
            return n;
        }
        // How much a lamp of the scene (a vessel floodlight, a spotlight) lights a smoke cloud of radius `size` whose
        // centre is `distance` away: 0 out of its reach, or outside its cone (angleToCloud and halfAngle in radians;
        // spot=false for a point light). Used to pick the one lamp the 1.1 smoke volumes are lit by.
        public static double LampScore(double intensity,double range,double distance,double size,bool spot,double angleToCloud,double halfAngle)
        {
            if(intensity<=0.05||range<=0||distance-size>range) return 0;
            if(spot&&distance>size&&angleToCloud>halfAngle+Math.Asin(Numbers.Clamp(size/distance,0,1))) return 0;
            double reach=Math.Max(0,distance-size)/range;
            return intensity/(1+reach*reach*8);
        }
        // Brief white flash used when no fireball volume is shown.
        public static double FallbackFlash(double fireFraction,double age)
        { return 3*Numbers.Clamp(fireFraction,0,1)*Math.Pow(Numbers.Clamp(1-age/0.18,0,1),2); }
        public static double Flicker(double age,double phase)
        { return 1+0.15*Smooth(0.2,0.4,age)*(0.6*Math.Sin(age*13.7+phase)+0.4*Math.Sin(age*23.3+phase*2.3)); }
    }
}
