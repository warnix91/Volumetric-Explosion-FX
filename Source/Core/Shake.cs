using System;
namespace VolumetricExplosionFX.Core
{
    // 1.1: how hard an explosion shakes the camera and when the jolt arrives (visual only: the view matrix, never the
    // camera, vessels or physics). The blast travels at the speed of sound, so a far explosion is seen before it is felt.
    public static class Shake
    {
        // Peak amplitude (degrees) at this distance: about a degree and a half for a big blast close by, falling off with
        // distance beyond a few fireball domes; nothing in vacuum (no air to carry the blast).
        // A nuclear blast shakes harder, from much further (the blast wave stays strong for kilometres).
        public static double Amplitude(double score,double distance,double air,double dome,bool nuclear=false)
        {
            if(air<0.02||score<3) return 0;
            double power=Math.Pow(Numbers.Clamp((score-2.8)/5.5,0,1),1.3);
            double near=Math.Max((nuclear?12:7)*dome,18);
            double fall=distance<=near?1:Math.Pow(near/distance,nuclear?1.05:1.25);
            double a=2.3*power*fall*Math.Sqrt(Numbers.Clamp(air,0,1))*(nuclear?1.6:1);   // brutal pass: a harder jolt
            return a<0.015?0:Math.Min(a,nuclear?4.5:3.2);
        }
        // Seconds before the jolt reaches the camera.
        public static double Arrival(double distance,double air) { return Math.Max(distance,0)/Speed(air); }
        // How fast the jolt's front spreads (m/s): CameraShake starts the jolt when it reaches the camera where it is
        // then (it was timed and sized from where the camera was at the burst: a fast jet was shaken in the wrong place).
        public static double Speed(double air) { return Math.Max(340*Math.Sqrt(Numbers.Clamp(air,0.02,1)),50); }
        // How long it rumbles (s).
        // (a nuclear blast rumbles on a little longer; 2.8 s, 4.5 s was too long)
        public static double Duration(double score,bool nuclear=false) { return nuclear?2.8:Numbers.Clamp(0.5+0.18*score,0.6,2.4); }
        // Envelope (0..1) t seconds after arrival: a sharp jolt that dies down.
        public static double Envelope(double t,double duration)
        {
            if(t<0||t>duration) return 0;
            return Math.Min(1,t/0.03)*Math.Exp(-t/(0.28*duration));
        }
    }
}
