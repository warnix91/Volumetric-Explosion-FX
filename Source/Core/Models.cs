using System;
namespace VolumetricExplosionFX.Core
{
    public struct Vec3
    {
        public readonly double X, Y, Z;
        public Vec3(double x, double y, double z) { X=x; Y=y; Z=z; }
        public double Length { get { return Math.Sqrt(X*X+Y*Y+Z*Z); } }
        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static Vec3 operator *(Vec3 a, double b) { return new Vec3(a.X*b,a.Y*b,a.Z*b); }
        public static double Dot(Vec3 a, Vec3 b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
        public static Vec3 Cross(Vec3 a, Vec3 b) { return new Vec3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X); }
        public Vec3 Normalized(Vec3 fallback) { double l=Length; return l>1e-12?this*(1/l):fallback; }
        // Angle in radians; axis must be normalized.
        public static Vec3 Rotate(Vec3 v, Vec3 axis, double angle)
        {
            double c=Math.Cos(angle), s=Math.Sin(angle);
            return v*c+Cross(axis,v)*s+axis*(Dot(axis,v)*(1-c));
        }
    }
    public struct Vec4
    {
        public readonly double X, Y, Z, W;
        public Vec4(double x, double y, double z, double w) { X=x; Y=y; Z=z; W=w; }
    }
    public struct Quat
    {
        public readonly double X,Y,Z,W;
        public Quat(double x,double y,double z,double w) { X=x;Y=y;Z=z;W=w; }
    }
    [Flags] public enum ResourceKind { None=0, Fuel=1, Oxidizer=2, Cryogenic=4, Solid=8, Hypergolic=16, Electric=32, Pressurized=64, Sooty=128, Clean=256 }
    public enum PartKind { Structure, LiquidTank, Engine, SolidBooster, Battery, Capsule, Wing, Fairing, Avionics, PressurizedTank }
    public enum EventKind { Destruction, GroundImpact, WaterImpact }
    public enum Quality { Low, Medium, High, Ultra, Custom }
    public sealed class ResourceSnapshot
    {
        public readonly string Name;
        public readonly double Amount, Capacity, MassKg;
        public ResourceSnapshot(string name, double amount, double capacity, double massKg)
        { Name=name; Amount=Numbers.Positive(amount); Capacity=Numbers.Positive(capacity); MassKg=Numbers.Positive(massKg); }
    }
    public sealed class EnvironmentContext
    {
        public readonly string Body;
        public readonly double Density, PressureKpa, Gravity, Altitude;
        public readonly bool HasOcean, Landed, Splashed;
        public readonly Vec3 Up;
        public EnvironmentContext(string body, double density, double pressure, double gravity, double altitude,
                                  bool ocean, bool landed, bool splashed, Vec3 up)
        { Body=body; Density=Numbers.Positive(density); PressureKpa=Numbers.Positive(pressure); Gravity=Numbers.Positive(gravity);
          Altitude=altitude; HasOcean=ocean; Landed=landed; Splashed=splashed; Up=up; }
        public double AtmosphereBlend { get { return 1-Math.Exp(-Density/0.35); } }
    }
    // Value snapshots only; no live game-object references.
    public sealed class PartSnapshot
    {
        public readonly uint PersistentId;
        public readonly int InstanceId;
        public readonly Guid VesselId;
        public readonly string Name;
        // Surface velocity is ground-relative; inertial velocity includes the floating frame.
        public readonly Vec3 Position, SurfaceVelocity, InertialVelocity, AngularVelocity, Dimensions;
        public readonly double MassKg, Temperature;
        public readonly Quat Rotation;
        public readonly string[] Modules;
        public readonly ResourceSnapshot[] Resources;
        public readonly EnvironmentContext Environment;
        public PartSnapshot(uint id, int instanceId, Guid vessel, string name, Vec3 position, Vec3 surfaceVelocity,
            Vec3 inertialVelocity, Vec3 angularVelocity, Vec3 dimensions, double massKg, double temperature,
            string[] modules, ResourceSnapshot[] resources, EnvironmentContext environment, Quat? rotation=null)
        { PersistentId=id; InstanceId=instanceId; VesselId=vessel; Name=name; Position=position;
          SurfaceVelocity=surfaceVelocity; InertialVelocity=inertialVelocity; AngularVelocity=angularVelocity;
          Dimensions=dimensions; MassKg=Numbers.Positive(massKg); Temperature=Numbers.Positive(temperature);
          Modules=modules ?? new string[0]; Resources=resources ?? new ResourceSnapshot[0]; Environment=environment; Rotation=rotation??new Quat(0,0,0,1); }
        public PartSnapshot At(Vec3 position)
        { return new PartSnapshot(PersistentId,InstanceId,VesselId,Name,position,SurfaceVelocity,InertialVelocity,AngularVelocity,
            Dimensions,MassKg,Temperature,Modules,Resources,Environment,Rotation); }
    }
    public sealed class DestructionEvent
    {
        public readonly PartSnapshot Part;
        public readonly EventKind Kind;
        public readonly double Time;
        // Observed position before ground anchoring.
        public readonly Vec3 Origin;
        public DestructionEvent(PartSnapshot part, EventKind kind, double time) { Part=part; Kind=kind; Time=time; Origin=part.Position; }
        public DestructionEvent(PartSnapshot part, EventKind kind, double time, Vec3 origin) { Part=part; Kind=kind; Time=time; Origin=origin; }
    }
    public static class Numbers
    {
        public static double Positive(double x) { return double.IsNaN(x)||double.IsInfinity(x)||x<0 ? 0 : x; }
        public static double Clamp(double x, double min, double max) { return Math.Max(min,Math.Min(max,double.IsNaN(x)?min:x)); }
    }
}
