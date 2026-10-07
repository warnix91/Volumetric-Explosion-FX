using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    internal static class FragmentMeshBuilder
    {
        static readonly List<Vector3> vertices=new List<Vector3>(), normals=new List<Vector3>();
        static readonly List<Vector4> uv0=new List<Vector4>(), uv1=new List<Vector4>(), uv2=new List<Vector4>(), uv3=new List<Vector4>(),
            uv4=new List<Vector4>(), uv5=new List<Vector4>();
        static readonly List<Color32> colors=new List<Color32>();
        static readonly List<int> indices=new List<int>();
        internal const float Never=1e6f;
        internal static Quat ToQuat(Quaternion q) { return new Quat(q.x,q.y,q.z,q.w); }
        static Vector3 V(Vec3 v) { return new Vector3((float)v.X,(float)v.Y,(float)v.Z); }
        static byte Byte(double v) { return (byte)Mathf.RoundToInt(Mathf.Clamp01((float)v)*255); }
        internal static void Build(Mesh mesh,FractureResult cut,FragmentMotion[] motion,Quaternion toEffect,Vector3 offset,int groups,double gravity,double horizon,uint seed)
        {
            mesh.Clear();
            vertices.Clear(); normals.Clear(); uv0.Clear(); uv1.Clear(); uv2.Clear(); uv3.Clear(); uv4.Clear(); uv5.Clear(); colors.Clear();
            var rnd=new SplitRandom(seed);
            var pieceSeed=new byte[cut.Count];
            for(int f=0;f<cut.Count;f++) pieceSeed[f]=Byte(rnd.Next());
            for(int v=0;v<cut.VertexCount;v++)
            {
                int f=cut.Fragment[v];
                FragmentMotion m=motion[f];
                vertices.Add(toEffect*V(cut.Positions[v])+offset);
                normals.Add(toEffect*new Vector3(cut.Normals[3*v],cut.Normals[3*v+1],cut.Normals[3*v+2]));
                uv0.Add(new Vector4(cut.Uvs[2*v],cut.Uvs[2*v+1],cut.Side!=null?cut.Side[v]:0,0));
                float land=double.IsInfinity(m.LandTime)||m.LandTime>Never?Never:(float)m.LandTime;
                uv1.Add(new Vector4((float)m.Pivot.X,(float)m.Pivot.Y,(float)m.Pivot.Z,land));
                uv2.Add(new Vector4((float)m.Velocity.X,(float)m.Velocity.Y,(float)m.Velocity.Z,(float)m.Drag));
                Vector3 spin=V(m.SpinAxis)*(float)m.SpinRate;
                uv3.Add(new Vector4(spin.x,spin.y,spin.z,(float)m.RestHeight));
                Vector3 settle=V(m.SettleAxis)*(float)m.SettleAngle;
                uv4.Add(new Vector4(settle.x,settle.y,settle.z,(float)m.Hop));
                uv5.Add(new Vector4((float)m.Slide.X,(float)m.Slide.Z,(float)m.SlideTime,(float)m.LandHeight));
                colors.Add(new Color32(Byte(m.Heat),Byte(m.Char),pieceSeed[f],Byte(cut.Edge[v])));
            }
            mesh.indexFormat=vertices.Count>65000?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv0);
            mesh.SetUVs(1,uv1); mesh.SetUVs(2,uv2); mesh.SetUVs(3,uv3); mesh.SetUVs(4,uv4); mesh.SetUVs(5,uv5); mesh.SetColors(colors);
            groups=Math.Max(groups,1);
            mesh.subMeshCount=groups;
            for(int g=0;g<groups;g++)
            {
                indices.Clear();
                for(int t=0;t<cut.TriangleCount;t++)
                    if(Math.Min(cut.Groups[t],groups-1)==g) { indices.Add(cut.Triangles[3*t]); indices.Add(cut.Triangles[3*t+1]); indices.Add(cut.Triangles[3*t+2]); }
                mesh.SetTriangles(indices,g,false);
            }
            mesh.bounds=Reach(cut,motion,gravity,horizon);
        }
        static Bounds Reach(FractureResult cut,FragmentMotion[] motion,double gravity,double horizon)
        {
            var b=new Bounds(V(motion[0].Pivot),Vector3.zero);
            for(int f=0;f<cut.Count;f++)
            {
                FragmentMotion m=motion[f];
                double end=Math.Min(m.LandTime,horizon);
                double apex=Ballistics.ApexTime(m.Velocity.Y,m.Drag,gravity);
                float r=(float)cut.Radius[f]*1.2f;
                foreach(double t in new[]{0,apex,end*0.25,end*0.5,end*0.75,end})
                {
                    if(t>end) continue;
                    b.Encapsulate(new Bounds(V(DebrisPlanner.Where(m,gravity,t)),Vector3.one*(2*r)));
                }
                if(!double.IsInfinity(m.LandTime)) b.Encapsulate(new Bounds(V(DebrisPlanner.Where(m,gravity,m.LandTime+m.SlideTime+1)),Vector3.one*(2*r+(float)m.Hop)));
            }
            return b;
        }
    }
}
