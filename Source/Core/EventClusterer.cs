using System;
namespace VolumetricExplosionFX.Core
{
    public sealed class EventCluster
    {
        public int Id, Members;
        public DestructionEvent Representative;
        public Vec3 Position;
        public double FirstTime, LastTime, TotalScore, RepresentativeScore;
        public bool Ground;
        public Vec3 GroundPoint;
        double groundScore;
        internal void Note(DestructionEvent e,double score)
        {
            if(e.Kind!=EventKind.GroundImpact || Ground&&score<=groundScore) return;
            Ground=true; GroundPoint=e.Part.Position; groundScore=score;
        }
        public const int MaxSamples=3;
        public readonly Vec3[] SamplePositions=new Vec3[MaxSamples];
        public readonly double[] SampleTimes=new double[MaxSamples], SampleScores=new double[MaxSamples];
        public int SampleCount;
        internal void Sample(Vec3 position,double time,double score)
        {
            int slot=SampleCount<MaxSamples?SampleCount++:-1;
            if(slot<0)
            {
                int weakest=0; for(int i=1;i<MaxSamples;i++) if(SampleScores[i]<SampleScores[weakest]) weakest=i;
                if(score<=SampleScores[weakest]) return;
                slot=weakest;
            }
            SamplePositions[slot]=position; SampleTimes[slot]=time; SampleScores[slot]=score;
        }
    }
    public sealed class EventClusterer
    {
        readonly EventCluster[] slots;
        readonly double window, radius;
        int nextId;
        public int Dropped { get; private set; }
        public int Replaced { get; private set; }
        public int LastEvicted { get; private set; }
        public EventClusterer(int capacity=24, double window=0.08, double radius=12)
        { if(capacity<1||window<=0||radius<=0) throw new ArgumentOutOfRangeException(); slots=new EventCluster[capacity]; this.window=window; this.radius=radius; }
        public int Add(DestructionEvent e, double score)
        {
            int free=-1, weakest=-1; LastEvicted=0;
            for(int i=0;i<slots.Length;i++)
            {
                EventCluster c=slots[i]; if(c==null) { if(free<0) free=i; continue; }
                if(weakest<0||c.RepresentativeScore<slots[weakest].RepresentativeScore) weakest=i;
                if(c.Members<128 && e.Time>=c.FirstTime && e.Time-c.FirstTime<=window &&
                   e.Part.Environment.Body==c.Representative.Part.Environment.Body &&
                   (e.Kind==EventKind.WaterImpact)==(c.Representative.Kind==EventKind.WaterImpact) &&
                   (e.Part.Position-c.Position).Length<=radius)
                {
                    c.Position=(c.Position*c.Members+e.Part.Position)*(1.0/(c.Members+1));
                    c.Members++; c.LastTime=e.Time; c.TotalScore=Numbers.Clamp(c.TotalScore+score,0,100);
                    if(score>c.RepresentativeScore)
                    {
                        c.Sample(c.Representative.Origin,c.Representative.Time,c.RepresentativeScore);
                        c.Representative=e; c.RepresentativeScore=score;
                    }
                    else c.Sample(e.Origin,e.Time,score);
                    c.Note(e,score);
                    return c.Id;
                }
            }
            if(free<0)
            {
                if(weakest<0||score<=slots[weakest].RepresentativeScore) { Dropped++; return 0; }
                free=weakest; Replaced++; LastEvicted=slots[weakest].Id;
            }
            slots[free]=new EventCluster { Id=++nextId, Members=1, Representative=e, Position=e.Part.Position, FirstTime=e.Time, LastTime=e.Time, TotalScore=score, RepresentativeScore=score };
            slots[free].Note(e,score);
            return slots[free].Id;
        }
        public EventCluster TakeReady(double now)
        {
            int best=-1;
            for(int i=0;i<slots.Length;i++) if(slots[i]!=null&&now-slots[i].FirstTime>=window &&
                (best<0||slots[i].RepresentativeScore>slots[best].RepresentativeScore)) best=i;
            if(best<0) return null;
            EventCluster result=slots[best];slots[best]=null;return result;
        }
        public void Shift(Vec3 offset)
        {
            for(int i=0;i<slots.Length;i++) if(slots[i]!=null)
            {
                slots[i].Position-=offset; slots[i].GroundPoint-=offset;
                for(int k=0;k<slots[i].SampleCount;k++) slots[i].SamplePositions[k]-=offset;
            }
        }
        public void Clear() { Array.Clear(slots,0,slots.Length); }
    }
    public sealed class RecentEvents
    {
        readonly int[] ids; readonly double[] times;
        int cursor;
        public RecentEvents(int capacity=512) { ids=new int[capacity]; times=new double[capacity]; for(int i=0;i<times.Length;i++) times[i]=double.NegativeInfinity; }
        public bool Accept(int instanceId, double now)
        {
            for(int i=0;i<ids.Length;i++) if(ids[i]==instanceId&&now-times[i]<2) return false;
            ids[cursor]=instanceId; times[cursor]=now; cursor=(cursor+1)%ids.Length; return true;
        }
    }
}
