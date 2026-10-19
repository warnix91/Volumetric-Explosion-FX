using System.Collections.Generic;
using UnityEngine;
namespace VolumetricExplosionFX.Rendering
{
    internal delegate bool GroundProbe(Vector3 local,out Vector3 point,out Vector3 normal);
    internal static class ScorchMesh
    {
        static readonly List<Vector3> vertices=new List<Vector3>();
        static readonly List<Vector2> uvs=new List<Vector2>();
        static readonly List<int> indices=new List<int>();
        static readonly List<float> around=new List<float>();
        internal static bool Build(Mesh mesh,float radius,int cells,GroundProbe probe)
        { return Grid(mesh,-radius,radius,-radius,radius,cells,cells,radius,probe); }
        // The same, centred at (x, z) of the local frame (the ground shadow lies off to the side away from the sun).
        internal static bool Build(Mesh mesh,float x,float z,float radius,int cells,GroundProbe probe)
        { return Grid(mesh,x-radius,x+radius,z-radius,z+radius,cells,cells,radius,probe); }
        // A strip laid along local +x from -halfWidth to length+halfWidth (a scar along a crash path); u runs along it.
        internal static bool Strip(Mesh mesh,float halfWidth,float length,int cellsAlong,int cellsAcross,GroundProbe probe)
        { return Grid(mesh,-halfWidth,length+halfWidth,-halfWidth,halfWidth,cellsAlong,cellsAcross,halfWidth,probe); }
        // A disk of `rings` x `sectors` cells out to `reach`, draped on the ground (1.1 ground wave). u = radius / reach,
        // v = angle / turn. Points sticking up over their neighbours (roofs, poles) are pulled down to the ground around them.
        internal static bool Polar(Mesh mesh,float reach,int rings,int sectors,GroundProbe probe)
        {
            mesh.Clear(); vertices.Clear(); uvs.Clear(); indices.Clear();
            int n=sectors+1, total=n*rings; var valid=new bool[total]; int hits=0;
            for(int j=0;j<rings;j++) for(int i=0;i<n;i++)
            {
                float u=(float)(j+1)/rings, v=(float)i/sectors, a=v*6.2831853f;
                var at=new Vector3(Mathf.Cos(a)*u*reach,0,Mathf.Sin(a)*u*reach);
                Vector3 point,normal;
                bool hit=i<sectors?probe(at,out point,out normal):probe(new Vector3(u*reach,0,0),out point,out normal);
                valid[j*n+i]=hit; if(hit) hits++;
                vertices.Add(hit?point+normal*Mathf.Clamp(reach*0.002f,0.05f,0.3f):at);
                uvs.Add(new Vector2(u,v));
            }
            if(hits<total/3) return false;
            float bump=Mathf.Clamp(reach*0.02f,0.5f,3f);
            var height=new float[total];
            for(int k=0;k<total;k++) height[k]=vertices[k].y;
            for(int j=0;j<rings;j++) for(int i=0;i<n;i++)
            {
                int k=j*n+i; if(!valid[k]) continue;
                around.Clear();
                for(int dj=-1;dj<=1;dj++) for(int di=-1;di<=1;di++)
                {
                    int jj=j+dj, ii=(i+di+sectors)%sectors; if((di==0&&dj==0)||jj<0||jj>=rings||!valid[jj*n+ii]) continue;
                    around.Add(height[jj*n+ii]);
                }
                if(around.Count<3) continue;
                around.Sort(); float median=around[around.Count/2];
                if(height[k]>median+bump) vertices[k]=new Vector3(vertices[k].x,median,vertices[k].z);
            }
            for(int j=0;j<rings-1;j++) for(int i=0;i<sectors;i++)
            {
                int a=j*n+i, b=a+1, c=a+n, d=c+1;
                if(!(valid[a]&&valid[b]&&valid[c]&&valid[d])) continue;
                indices.Add(a); indices.Add(c); indices.Add(b); indices.Add(b); indices.Add(c); indices.Add(d);
            }
            if(indices.Count==0) return false;
            mesh.SetVertices(vertices); mesh.SetUVs(0,uvs); mesh.SetTriangles(indices,0,true);
            mesh.RecalculateNormals();
            mesh.bounds=new Bounds(Vector3.zero,new Vector3(2*reach,reach,2*reach));   // the ridge rises above the draped ground
            return true;
        }
        static bool Grid(Mesh mesh,float x0,float x1,float z0,float z1,int cellsX,int cellsZ,float radius,GroundProbe probe)
        {
            mesh.Clear(); vertices.Clear(); uvs.Clear(); indices.Clear();
            int n=cellsX+1, nz=cellsZ+1; var valid=new bool[n*nz]; int hits=0;
            for(int j=0;j<nz;j++) for(int i=0;i<n;i++)
            {
                float u=(float)i/cellsX, v=(float)j/cellsZ;
                var at=new Vector3(x0+(x1-x0)*u,0,z0+(z1-z0)*v);
                Vector3 point,normal;
                bool hit=probe(at,out point,out normal);
                valid[j*n+i]=hit; if(hit) hits++;
                vertices.Add(hit?point+normal*Mathf.Clamp(radius*0.004f,0.04f,0.15f):at);
                uvs.Add(new Vector2(u,v));
            }
            if(hits<n*nz/3) return false;
            float bump=Mathf.Clamp(radius*0.03f,0.3f,1.2f);
            var height=new float[n*nz];
            for(int k=0;k<n*nz;k++) height[k]=vertices[k].y;
            for(int j=0;j<nz;j++) for(int i=0;i<n;i++)
            {
                int k=j*n+i; if(!valid[k]) continue;
                around.Clear();
                for(int dj=-1;dj<=1;dj++) for(int di=-1;di<=1;di++)
                {
                    int a=i+di, b=j+dj; if((di==0&&dj==0)||a<0||b<0||a>=n||b>=nz||!valid[b*n+a]) continue;
                    around.Add(height[b*n+a]);
                }
                if(around.Count<3) continue;
                around.Sort(); float median=around[around.Count/2];
                if(height[k]>median+bump) vertices[k]=new Vector3(vertices[k].x,median,vertices[k].z);
            }
            for(int j=0;j<cellsZ;j++) for(int i=0;i<cellsX;i++)
            {
                int a=j*n+i, b=a+1, c=a+n, d=c+1;
                if(!(valid[a]&&valid[b]&&valid[c]&&valid[d])) continue;
                indices.Add(a); indices.Add(c); indices.Add(b); indices.Add(b); indices.Add(c); indices.Add(d);
            }
            if(indices.Count==0) return false;
            mesh.SetVertices(vertices); mesh.SetUVs(0,uvs); mesh.SetTriangles(indices,0,true);
            mesh.RecalculateNormals();
            return true;
        }
    }
}
