using System;
using System.Diagnostics;
using System.Globalization;
using VolumetricExplosionFX.Core;
using Debug=UnityEngine.Debug;
using Screen=UnityEngine.Screen;
using Time=UnityEngine.Time;
namespace VolumetricExplosionFX.Ksp
{
    internal sealed class PerfProbe
    {
        readonly FrameStats idle=new FrameStats(900), busy=new FrameStats(7200), ours=new FrameStats(7200);
        readonly Stopwatch watch=new Stopwatch();
        EffectLoad peak;
        bool episode; float started, quietSince=-1; int gcAtStart; double ourMs;
        string note;
        internal int Episodes { get; private set; }
        internal void Begin() { watch.Reset(); watch.Start(); }
        internal void End() { if(watch.IsRunning) { watch.Stop(); ourMs+=watch.Elapsed.TotalMilliseconds; } }
        internal void Note(string what) { note=what; }
        internal void Frame(EffectLoad now,string quality)
        {
            float t=Time.unscaledTime;
            double frame=Time.unscaledDeltaTime*1000.0, mine=ourMs; ourMs=0;
            if(now.Busy)
            {
                if(!episode) { episode=true; started=t; gcAtStart=GC.CollectionCount(0); busy.Clear(); ours.Clear(); peak=default(EffectLoad); }
                quietSince=-1;
                busy.Add(frame); ours.Add(mine); peak.Peak(now);
                if(t-started>=60) { Report(quality,"continuing"); episode=false; }
                return;
            }
            idle.Add(frame);
            if(!episode) return;
            if(quietSince<0) quietSince=t;
            else if(t-quietSince>=1) { Report(quality,null); episode=false; }
        }
        internal void Flush(string quality) { if(episode&&busy.Count>0) Report(quality,"scene exit"); episode=false; }
        void Report(string quality,string why)
        {
            var c=CultureInfo.InvariantCulture;
            float length=(quietSince>=0?quietSince:Time.unscaledTime)-started;
            string baseline=idle.Count>=30?string.Format(c,"median {0:F1} ms",idle.Median):"not measured yet";
            Debug.Log(string.Format(c,"[VEFX] Performance: {0:F1} s of effects{1}{2} | {3}x{4} {5} | frame median {6:F1} ms, p95 {7:F1}, worst {8:F1} (without effects: {9}) | "+
                "VEFX update median {10:F2} ms, p95 {11:F2}, worst {12:F2} | peak {13} blasts, {14} fireball volumes, {15} fire volumes, {16} particles, "+
                "{17} wrecks / {18} pieces, {19} lights | GC {20}",
                length,note!=null?" ("+note+")":"",why!=null?" ["+why+"]":"",Screen.width,Screen.height,quality,
                busy.Median,busy.P95,busy.Worst,baseline,ours.Median,ours.P95,ours.Worst,
                peak.Blasts,peak.FireballVolumes,peak.FireVolumes,peak.Particles,peak.Wrecks,peak.WreckPieces,peak.Lights,
                GC.CollectionCount(0)-gcAtStart));
            note=null; Episodes++;
        }
    }
}
