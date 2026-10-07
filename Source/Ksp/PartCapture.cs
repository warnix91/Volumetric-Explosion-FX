using System;
using System.Collections.Generic;
using UnityEngine;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Ksp
{
    internal struct SkinLook
    {
        internal Texture Texture; internal Color Color; internal Vector2 Scale, Offset;
        internal bool Same(SkinLook o) { return Texture==o.Texture&&Color==o.Color&&Scale==o.Scale&&Offset==o.Offset; }
    }
    // Capture readable geometry before the part is hidden or destroyed.
    internal sealed class CapturedPart
    {
        internal PartMesh Mesh; internal SkinLook[] Looks;
        internal Vector3 Origin; internal Quaternion Frame;
        internal Vec3 Center; internal double Size;
    }
    internal static class PartCapture
    {
        static readonly List<MeshFilter> filters=new List<MeshFilter>();
        static readonly List<Vec3> positions=new List<Vec3>();
        static readonly List<float> normals=new List<float>(), uvs=new List<float>();
        static readonly List<int> triangles=new List<int>(), groups=new List<int>();
        static readonly List<SkinLook> looks=new List<SkinLook>();
        internal static CapturedPart Read(Part part,Vector3 up,int maxTriangles)
        {
            if(part==null||part.packed||part.isKerbalEVA()) return null;
            List<MeshFilter> found=part.FindModelComponents<MeshFilter>();
            if(found==null||found.Count==0) return null;
            Vector3 origin=part.transform.position;
            Quaternion frame=Quaternion.FromToRotation(Vector3.up,up);
            return Copy(found,Matrix4x4.TRS(origin,frame,Vector3.one).inverse,origin,frame,maxTriangles,true);
        }
        internal static CapturedPart ReadPrefab(Part prefab,Vector3 origin,Vector3 up,int maxTriangles)
        {
            if(prefab==null) return null;
            List<MeshFilter> found=prefab.FindModelComponents<MeshFilter>();
            if(found==null||found.Count==0) return null;
            return Copy(found,prefab.transform.worldToLocalMatrix,origin,Quaternion.FromToRotation(Vector3.up,up),maxTriangles,false);
        }
        static CapturedPart Copy(List<MeshFilter> found,Matrix4x4 toFrame,Vector3 origin,Quaternion frame,int maxTriangles,bool live)
        {
            filters.Clear(); filters.AddRange(found);
            filters.Sort((a,b)=>Triangles(b).CompareTo(Triangles(a)));
            positions.Clear(); normals.Clear(); uvs.Clear(); triangles.Clear(); groups.Clear(); looks.Clear();
            int budget=Math.Max(maxTriangles,1)*4, used=0;
            for(int i=0;i<filters.Count;i++)
            {
                MeshFilter filter=filters[i];
                Mesh mesh=filter!=null?filter.sharedMesh:null;
                if(mesh==null||!mesh.isReadable||live&&!filter.gameObject.activeInHierarchy) continue;
                MeshRenderer renderer=filter.GetComponent<MeshRenderer>();
                if(renderer==null||live&&!renderer.enabled) continue;
                Material[] materials=renderer.sharedMaterials;
                if(materials==null||materials.Length==0) continue;
                int count=0;
                for(int s=0;s<mesh.subMeshCount;s++) if(Opaque(materials[Math.Min(s,materials.Length-1)])) count+=(int)(mesh.GetIndexCount(s)/3);
                if(count==0||used+count>budget) continue;
                Vector3[] v=mesh.vertices; Vector3[] n=mesh.normals; Vector2[] uv=mesh.uv;
                if(v==null||v.Length==0||n==null||n.Length!=v.Length) continue;
                Matrix4x4 m=toFrame*filter.transform.localToWorldMatrix, nm=m.inverse.transpose;
                // Mirrored transforms reverse triangle winding.
                bool flip=m.determinant<0;
                int first=positions.Count;
                for(int k=0;k<v.Length;k++)
                {
                    Vector3 p=m.MultiplyPoint3x4(v[k]), d=nm.MultiplyVector(n[k]).normalized;
                    positions.Add(new Vec3(p.x,p.y,p.z)); normals.Add(d.x); normals.Add(d.y); normals.Add(d.z);
                    Vector2 t=uv!=null&&uv.Length==v.Length?uv[k]:Vector2.zero; uvs.Add(t.x); uvs.Add(t.y);
                }
                for(int s=0;s<mesh.subMeshCount;s++)
                {
                    Material material=materials[Math.Min(s,materials.Length-1)];
                    if(!Opaque(material)) continue;
                    int group=Look(material);
                    int[] t=mesh.GetTriangles(s);
                    for(int k=0;k+2<t.Length;k+=3)
                    {
                        triangles.Add(first+t[k]);
                        triangles.Add(first+(flip?t[k+2]:t[k+1]));
                        triangles.Add(first+(flip?t[k+1]:t[k+2]));
                        groups.Add(group);
                    }
                }
                used+=count;
            }
            if(triangles.Count==0) return null;
            double minX=1e9,minY=1e9,minZ=1e9,maxX=-1e9,maxY=-1e9,maxZ=-1e9;
            for(int k=0;k<positions.Count;k++)
            {
                Vec3 p=positions[k];
                minX=Math.Min(minX,p.X); maxX=Math.Max(maxX,p.X); minY=Math.Min(minY,p.Y); maxY=Math.Max(maxY,p.Y); minZ=Math.Min(minZ,p.Z); maxZ=Math.Max(maxZ,p.Z);
            }
            return new CapturedPart {
                Mesh=new PartMesh { Positions=positions.ToArray(), Normals=normals.ToArray(), Uvs=uvs.ToArray(), Triangles=triangles.ToArray(), Groups=groups.ToArray() },
                Looks=looks.ToArray(), Origin=origin, Frame=frame,
                Center=new Vec3((minX+maxX)*0.5,(minY+maxY)*0.5,(minZ+maxZ)*0.5), Size=Math.Max(maxX-minX,Math.Max(maxY-minY,maxZ-minZ)) };
        }
        static int Triangles(MeshFilter f)
        {
            Mesh mesh=f!=null?f.sharedMesh:null; if(mesh==null) return 0;
            long n=0; for(int s=0;s<mesh.subMeshCount;s++) n+=mesh.GetIndexCount(s);
            return (int)Math.Min(n/3,int.MaxValue);
        }
        static bool Opaque(Material m) { return m!=null&&m.renderQueue<2450; }
        static int Look(Material m)
        {
            var look=new SkinLook { Color=m.HasProperty("_Color")?m.color:Color.white, Scale=Vector2.one, Offset=Vector2.zero };
            if(m.HasProperty("_MainTex")) { look.Texture=m.mainTexture; look.Scale=m.mainTextureScale; look.Offset=m.mainTextureOffset; }
            for(int i=0;i<looks.Count;i++) if(looks[i].Same(look)) return i;
            looks.Add(look); return looks.Count-1;
        }
    }
}
