using System;
namespace VolumetricExplosionFX.Core
{
    public sealed class SnapshotCache
    {
        sealed class Entry { internal PartSnapshot Snapshot; internal EventKind Kind; internal Vec3 Position; internal double Time; }
        readonly Entry[] slots; int cursor;
        public SnapshotCache(int capacity=256) { if(capacity<1) throw new ArgumentOutOfRangeException();slots=new Entry[capacity]; }
        Entry Find(int id,double now)
        { for(int i=0;i<slots.Length;i++) if(slots[i]!=null&&slots[i].Snapshot.InstanceId==id&&now-slots[i].Time>=0&&now-slots[i].Time<2) return slots[i];return null; }
        public void Capture(PartSnapshot snapshot,EventKind kind,Vec3 position,double now)
        {
            if(snapshot==null) return;
            Entry e=Find(snapshot.InstanceId,now);
            if(e==null) { e=new Entry();slots[cursor]=e;cursor=(cursor+1)%slots.Length; }
            if(e.Snapshot==null||e.Kind==EventKind.Destruction||kind!=EventKind.Destruction) e.Snapshot=snapshot;
            if(kind!=EventKind.Destruction||e.Kind==EventKind.Destruction) { e.Kind=kind;e.Position=position; }
            e.Time=now;
        }
        public DestructionEvent Confirm(int id,double now,PartSnapshot fallback)
        {
            Entry e=Find(id,now);
            if(e==null) return fallback==null?null:new DestructionEvent(fallback,EventKind.Destruction,now);
            PartSnapshot s=e.Snapshot;
            var moved=new PartSnapshot(s.PersistentId,s.InstanceId,s.VesselId,s.Name,e.Position,s.SurfaceVelocity,s.InertialVelocity,
                s.AngularVelocity,s.Dimensions,s.MassKg,s.Temperature,s.Modules,s.Resources,s.Environment,s.Rotation);
            e.Time=double.NegativeInfinity;
            return new DestructionEvent(moved,e.Kind,now);
        }
        public void Shift(Vec3 offset)
        { for(int i=0;i<slots.Length;i++) if(slots[i]!=null) slots[i].Position-=offset; }
    }
}
