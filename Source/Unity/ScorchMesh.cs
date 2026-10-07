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
        {
            mesh.Clear(); vertices.Clear(); uvs.Clear(); indices.Clear();
            int n=cells+1; var valid=new bool[n*n]; int hits=0;
            for(int j=0;j<n;j++) for(int i=0;i<n;i++)
            {
                float u=(float)i/cells, v=(float)j/cells;
                var at=new Vector3((u*2-1)*radius,0,(v*2-1)*radius);
                Vector3 point,normal;
                bool hit=probe(at,out point,out normal);
                valid[j*n+i]=hit; if(hit) hits++;
                vertices.Add(hit?point+normal*Mathf.Clamp(radius*0.004f,0.04f,0.15f):at);
                uvs.Add(new Vector2(u,v));
            }
            if(hits<n*n/3) return false;
            float bump=Mathf.Clamp(radius*0.03f,0.3f,1.2f);
            var height=new float[n*n];
            for(int k=0;k<n*n;k++) height[k]=vertices[k].y;
            for(int j=0;j<n;j++) for(int i=0;i<n;i++)
            {
                int k=j*n+i; if(!valid[k]) continue;
                around.Clear();
                for(int dj=-1;dj<=1;dj++) for(int di=-1;di<=1;di++)
                {
                    int a=i+di, b=j+dj; if((di==0&&dj==0)||a<0||b<0||a>=n||b>=n||!valid[b*n+a]) continue;
                    around.Add(height[b*n+a]);
                }
                if(around.Count<3) continue;
                around.Sort(); float median=around[around.Count/2];
                if(height[k]>median+bump) vertices[k]=new Vector3(vertices[k].x,median,vertices[k].z);
            }
            for(int j=0;j<cells;j++) for(int i=0;i<cells;i++)
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
