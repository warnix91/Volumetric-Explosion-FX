using System;
namespace VolumetricExplosionFX.Core
{
    public sealed class FrameStats
    {
        readonly double[] samples, sorted;
        int count, next;
        public FrameStats(int capacity) { samples=new double[Math.Max(1,capacity)]; sorted=new double[samples.Length]; }
        public int Count { get { return count; } }
        public void Add(double ms)
        {
            if(double.IsNaN(ms)||double.IsInfinity(ms)) return;
            samples[next]=ms; next=(next+1)%samples.Length;
            if(count<samples.Length) count++;
        }
        public void Clear() { count=0; next=0; }
        public double Percentile(double q)
        {
            if(count==0) return 0;
            Array.Copy(samples,sorted,count);
            Array.Sort(sorted,0,count);
            int i=(int)Math.Ceiling(Numbers.Clamp(q,0,1)*count)-1;
            return sorted[Math.Max(0,Math.Min(count-1,i))];
        }
        public double Median { get { return Percentile(0.5); } }
        public double P95 { get { return Percentile(0.95); } }
        public double Worst { get { return Percentile(1); } }
    }
    public struct EffectLoad
    {
        public int Blasts, FireballVolumes, FireVolumes, Particles, Wrecks, WreckPieces, Lights;
        public void Peak(EffectLoad now)
        {
            Blasts=Math.Max(Blasts,now.Blasts); FireballVolumes=Math.Max(FireballVolumes,now.FireballVolumes);
            FireVolumes=Math.Max(FireVolumes,now.FireVolumes); Particles=Math.Max(Particles,now.Particles);
            Wrecks=Math.Max(Wrecks,now.Wrecks); WreckPieces=Math.Max(WreckPieces,now.WreckPieces); Lights=Math.Max(Lights,now.Lights);
        }
        public bool Busy { get { return Blasts>0||Wrecks>0; } }
    }
}
