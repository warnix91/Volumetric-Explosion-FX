using System;
using System.Collections.Generic;
namespace VolumetricExplosionFX.Core
{
    // Copied mesh data in the part frame; Groups keeps materials separate.
    public sealed class PartMesh
    {
        public Vec3[] Positions;
        public float[] Normals, Tangents, Uvs;   // 3, 4 and 2 floats per vertex
        public int[] Triangles;                  // 3 vertex indices per triangle
        public int[] Groups;                     // material slot of each triangle
        public int VertexCount { get { return Positions==null?0:Positions.Length; } }
        public int TriangleCount { get { return Triangles==null?0:Triangles.Length/3; } }
    }
    public sealed class FractureResult
    {
        public int Count;
        public Vec3[] Pivot, Normal; public double[] Area, Radius;
        public int[] FirstVertex, VertexCounts;
        public Vec3[] Positions; public float[] Normals, Tangents, Uvs, Edge, Side; public int[] Fragment;
        public int[] Triangles, Groups;
        public int VertexCount { get { return Positions==null?0:Positions.Length; } }
        public int TriangleCount { get { return Triangles==null?0:Triangles.Length/3; } }
    }
    public static class Fracture
    {
        public static FractureResult Cut(PartMesh mesh,int pieces,uint seed,int maxTriangles,double crumple)
        {
            if(mesh==null||mesh.TriangleCount==0||mesh.VertexCount<3) return null;
            if(mesh.TriangleCount>maxTriangles*4) return null; // too detailed to cut without a hitch
            mesh=Complete(mesh);
            pieces=Math.Max(1,Math.Min(pieces,64));
            var rnd=new SplitRandom(seed);
            // Reserve two thirds of the budget for the inner skin and edge walls.
            int skinBudget=Math.Max(16,maxTriangles/3);
            double skinArea=0;
            for(int t=0;t<mesh.TriangleCount;t++)
            {
                Vec3 a0=mesh.Positions[mesh.Triangles[3*t]], b0=mesh.Positions[mesh.Triangles[3*t+1]], c0=mesh.Positions[mesh.Triangles[3*t+2]];
                skinArea+=0.5*Vec3.Cross(b0-a0,c0-a0).Length;
            }
            double side=1.52*Math.Sqrt(Math.Max(skinArea,1e-9)/(pieces*48.0));
            PartMesh m=Refine(mesh,1.5*side,skinBudget);
            int maxTriangles0=maxTriangles; maxTriangles=skinBudget;
            int T=m.TriangleCount;
            double extent=Extent(m.Positions);
            int welded;
            int[] weld=Weld(m.Positions,Math.Max(extent*2e-5,1e-6),out welded);
            int[] neighbor=Neighbors(m.Triangles,weld,welded);
            var centroid=new Vec3[T]; var area=new double[T]; var faceNormal=new Vec3[T];
            for(int t=0;t<T;t++)
            {
                Vec3 a=m.Positions[m.Triangles[3*t]], b=m.Positions[m.Triangles[3*t+1]], c=m.Positions[m.Triangles[3*t+2]];
                centroid[t]=(a+b+c)*(1.0/3);
                Vec3 n=Vec3.Cross(b-a,c-a);
                area[t]=0.5*n.Length; faceNormal[t]=n;
            }
            int components;
            int[] component=Components(neighbor,T,out components);
            var componentArea=new double[components];
            for(int t=0;t<T;t++) componentArea[component[t]]+=area[t];
            double total=0; for(int c=0;c<components;c++) total+=componentArea[c];
            if(total<=0) return null;
            var order=new int[components]; for(int c=0;c<components;c++) order[c]=c;
            Array.Sort(order,(x,y)=>componentArea[y].CompareTo(componentArea[x]));
            var seedsOf=new int[components]; int seeds=0;
            for(int i=0;i<components;i++)
            {
                int c=order[i];
                int k=Math.Max(1,(int)Math.Round(pieces*componentArea[c]/total));
                if(seeds+k>pieces*2) { if(seeds>=pieces) break; k=Math.Max(1,pieces*2-seeds); }
                seedsOf[c]=k; seeds+=k;
            }
            var label=new int[T]; var dist=new double[T];
            for(int t=0;t<T;t++) { label[t]=-1; dist[t]=double.MaxValue; }
            var axisOf=new Vec3[Math.Max(seeds,1)]; var stretchOf=new double[Math.Max(seeds,1)];
            var heap=new Heap(T);
            var trianglesOf=TrianglesByComponent(component,components,T);
            int nextLabel=0;
            for(int c=0;c<components;c++)
            {
                if(seedsOf[c]==0) continue;
                int[] list=trianglesOf[c];
                double spacing=Math.Sqrt(componentArea[c]/seedsOf[c]);
                for(int k=0;k<seedsOf[c];k++)
                {
                    int pick=PickByArea(list,area,componentArea[c],ref rnd);
                    if(label[pick]>=0) continue; // duplicate seed: the component keeps fewer pieces
                    Vec3 up=faceNormal[pick].Normalized(new Vec3(0,1,0));
                    axisOf[nextLabel]=Vec3.Cross(up,rnd.Direction()).Normalized(Perpendicular(up));
                    stretchOf[nextLabel]=0.9*rnd.Next();
                    label[pick]=nextLabel++;
                    double lead=rnd.Next();
                    dist[pick]=-spacing*(0.12*rnd.Next()+1.3*lead*lead*lead*lead);
                    heap.Push(dist[pick],pick);
                }
            }
            while(heap.Count>0)
            {
                double d; int t=heap.Pop(out d);
                if(d>dist[t]) continue;
                for(int e=0;e<3;e++)
                {
                    int n=neighbor[3*t+e];
                    if(n<0) continue;
                    Vec3 step=centroid[n]-centroid[t]; double l=step.Length; int f=label[t];
                    double across=l>1e-12?1-Math.Abs(Vec3.Dot(step,axisOf[f]))/l:0;
                    double nd=d+l*(0.65+0.7*EdgeNoise(t,n,seed))*(1+stretchOf[f]*across);
                    if(nd<dist[n]) { dist[n]=nd; label[n]=label[t]; heap.Push(nd,n); }
                }
            }
            var count=new int[nextLabel];
            for(int t=0;t<T;t++) if(label[t]>=0) count[label[t]]++;
            var keep=new bool[nextLabel]; var shuffled=new int[nextLabel];
            for(int i=0;i<nextLabel;i++) shuffled[i]=i;
            for(int i=nextLabel-1;i>0;i--) { int j=(int)(rnd.Next()*(i+1)); if(j>i) j=i; int s=shuffled[i]; shuffled[i]=shuffled[j]; shuffled[j]=s; }
            int budget=maxTriangles, kept=0;
            for(int i=0;i<nextLabel;i++)
            {
                int f=shuffled[i];
                if(count[f]==0||count[f]>budget) continue;
                keep[f]=true; budget-=count[f]; kept++;
            }
            if(kept==0) return null;
            double thickness=Numbers.Clamp(0.012*extent,0.006,0.05);
            FractureResult result=Build(m,weld,welded,label,keep,nextLabel,area,faceNormal,centroid,crumple,thickness,ref rnd);
            return result!=null&&result.TriangleCount<=maxTriangles0?result:Build(m,weld,welded,label,keep,nextLabel,area,faceNormal,centroid,crumple,0,ref rnd);
        }
        static FractureResult Build(PartMesh m,int[] weld,int welded,int[] label,bool[] keep,int labels,double[] area,Vec3[] faceNormal,Vec3[] centroid,double crumple,double thickness,ref SplitRandom rnd)
        {
            int T=m.TriangleCount, V=m.VertexCount;
            var remap=new int[labels]; int pieces=0;
            for(int f=0;f<labels;f++) remap[f]=keep[f]?pieces++:-1;
            var start=new int[pieces+1];
            for(int t=0;t<T;t++) if(label[t]>=0&&remap[label[t]]>=0) start[remap[label[t]]+1]++;
            for(int f=0;f<pieces;f++) start[f+1]+=start[f];
            var sorted=new int[start[pieces]]; var fill=new int[pieces];
            for(int t=0;t<T;t++) if(label[t]>=0&&remap[label[t]]>=0) { int f=remap[label[t]]; sorted[start[f]+fill[f]++]=t; }
            var r=new FractureResult { Count=pieces, Pivot=new Vec3[pieces], Normal=new Vec3[pieces], Area=new double[pieces], Radius=new double[pieces],
                FirstVertex=new int[pieces], VertexCounts=new int[pieces] };
            var positions=new List<Vec3>(); var normals=new List<float>(); var tangents=m.Tangents!=null?new List<float>():null;
            var uvs=new List<float>(); var edge=new List<float>(); var side=new List<float>(); var fragment=new List<int>(); var source=new List<int>();
            var triangles=new List<int>(); var groups=new List<int>();
            var map=new int[V]; var mapStamp=new int[V]; var rimStamp=new int[welded];
            for(int i=0;i<V;i++) mapStamp[i]=-1;
            for(int i=0;i<welded;i++) rimStamp[i]=-1;
            var edgeUse=new Dictionary<long,int>();
            for(int f=0;f<pieces;f++)
            {
                edgeUse.Clear();
                for(int i=start[f];i<start[f+1];i++)
                {
                    int t=sorted[i];
                    for(int e=0;e<3;e++)
                    {
                        long key=EdgeKey(weld[m.Triangles[3*t+e]],weld[m.Triangles[3*t+(e+1)%3]],welded);
                        int used; edgeUse.TryGetValue(key,out used); edgeUse[key]=used+1;
                    }
                }
                foreach(var pair in edgeUse) if(pair.Value==1)
                { rimStamp[(int)(pair.Key/welded)]=f; rimStamp[(int)(pair.Key%welded)]=f; }
                Vec3 pivot=new Vec3(), normal=new Vec3(); double pieceArea=0;
                for(int i=start[f];i<start[f+1];i++) { int t=sorted[i]; pivot+=centroid[t]*area[t]; normal+=faceNormal[t]; pieceArea+=area[t]; }
                pivot=pieceArea>1e-12?pivot*(1/pieceArea):centroid[sorted[start[f]]];
                normal=normal.Normalized(new Vec3(0,1,0));
                int first=positions.Count, firstTriangle=triangles.Count;
                for(int i=start[f];i<start[f+1];i++)
                {
                    int t=sorted[i];
                    for(int e=0;e<3;e++)
                    {
                        int v=m.Triangles[3*t+e];
                        if(mapStamp[v]!=f)
                        {
                            mapStamp[v]=f; map[v]=positions.Count;
                            positions.Add(m.Positions[v]);
                            normals.Add(m.Normals[3*v]); normals.Add(m.Normals[3*v+1]); normals.Add(m.Normals[3*v+2]);
                            if(tangents!=null) for(int k=0;k<4;k++) tangents.Add(m.Tangents[4*v+k]);
                            uvs.Add(m.Uvs[2*v]); uvs.Add(m.Uvs[2*v+1]);
                            edge.Add(rimStamp[weld[v]]==f?1:0); side.Add(0); fragment.Add(f); source.Add(v);
                        }
                        triangles.Add(map[v]);
                    }
                    groups.Add(m.Groups!=null?m.Groups[t]:0);
                }
                int vertexCount=positions.Count-first;
                double radius=0;
                for(int v=first;v<first+vertexCount;v++) radius=Math.Max(radius,(positions[v]-pivot).Length);
                Vec3 axis=Vec3.Cross(normal,rnd.Direction()).Normalized(Perpendicular(normal));
                Vec3 across=Vec3.Cross(normal,axis);
                double shift=(rnd.Next()-0.5)*0.6*radius, angle=(rnd.Next()*2-1)*0.8*crumple;
                double bend=rnd.Next()<0.6?(rnd.Next()<0.7?1:-1)*crumple/Math.Max(radius*rnd.Range(0.6,1.8),1e-6):0;
                if(bend!=0) angle=0;
                Vec3 line=pivot+across*shift;
                Vec3 k1=Vec3.Rotate(axis,normal,rnd.Next()*Math.PI)*(2*Math.PI/Math.Max(radius*rnd.Range(0.7,1.3),1e-6));
                Vec3 k2=Vec3.Rotate(axis,normal,rnd.Next()*Math.PI)*(2*Math.PI/Math.Max(radius*rnd.Range(0.35,0.7),1e-6));
                double p1=rnd.Next()*6.283, p2=rnd.Next()*6.283;
                for(int v=first;v<first+vertexCount;v++)
                {
                    Vec3 p=positions[v];
                    double turn;
                    if(bend!=0)
                    {
                        double s=Vec3.Dot(p-pivot,across);
                        turn=bend*s;
                        p=p+across*(Math.Sin(turn)/bend-s)+normal*((1-Math.Cos(turn))/bend);
                    }
                    else
                    {
                        double w=Smooth(0,0.4*radius+1e-6,Vec3.Dot(p-line,across));
                        turn=angle*w;
                        if(w>0) p=line+Vec3.Rotate(p-line,axis,turn);
                    }
                    if(turn!=0)
                    {
                        RotateFloats(normals,3*v,axis,turn);
                        if(tangents!=null) RotateFloats(tangents,4*v,axis,turn);
                    }
                    Vec3 o=m.Positions[source[v]];
                    double bump=(0.6*Math.Sin(Vec3.Dot(o,k1)+p1)+0.4*Math.Sin(Vec3.Dot(o,k2)+p2))*0.07*radius*crumple;
                    positions[v]=p+Vec3.Rotate(normal,axis,turn)*bump;
                }
                if(thickness>0) Thicken(f,first,vertexCount,firstTriangle,thickness,weld,source,rimStamp,positions,normals,tangents,uvs,edge,side,fragment,triangles,groups);
                vertexCount=positions.Count-first;
                r.Pivot[f]=pivot; r.Normal[f]=normal; r.Area[f]=pieceArea; r.Radius[f]=Math.Max(radius,1e-3);
                r.FirstVertex[f]=first; r.VertexCounts[f]=vertexCount;
            }
            r.Positions=positions.ToArray(); r.Normals=normals.ToArray(); r.Tangents=tangents!=null?tangents.ToArray():null;
            r.Uvs=uvs.ToArray(); r.Edge=edge.ToArray(); r.Side=side.ToArray(); r.Fragment=fragment.ToArray();
            r.Triangles=triangles.ToArray(); r.Groups=groups.ToArray();
            return r;
        }
        static void Thicken(int f,int first,int count,int firstTriangle,double t,int[] weld,List<int> source,int[] rimStamp,
            List<Vec3> positions,List<float> normals,List<float> tangents,List<float> uvs,List<float> edge,List<float> side,
            List<int> fragment,List<int> triangles,List<int> groups)
        {
            int back=positions.Count;
            for(int v=first;v<first+count;v++)
            {
                var n=new Vec3(normals[3*v],normals[3*v+1],normals[3*v+2]);
                positions.Add(positions[v]-n*t);
                normals.Add(-normals[3*v]); normals.Add(-normals[3*v+1]); normals.Add(-normals[3*v+2]);
                if(tangents!=null) for(int k=0;k<4;k++) tangents.Add(tangents[4*v+k]);
                uvs.Add(uvs[2*v]); uvs.Add(uvs[2*v+1]); edge.Add(edge[v]); side.Add(1); fragment.Add(f); source.Add(source[v]);
            }
            int frontTriangles=(triangles.Count-firstTriangle)/3;
            for(int i=0;i<frontTriangles;i++)
            {
                int a=triangles[firstTriangle+3*i], b=triangles[firstTriangle+3*i+1], c=triangles[firstTriangle+3*i+2];
                triangles.Add(back+a-first); triangles.Add(back+c-first); triangles.Add(back+b-first);
                groups.Add(groups[firstTriangle/3+i]);
            }
            var used=new Dictionary<long,int>();
            for(int i=0;i<frontTriangles;i++) for(int e=0;e<3;e++)
            {
                int a=triangles[firstTriangle+3*i+e], b=triangles[firstTriangle+3*i+(e+1)%3];
                long key=PairKey(weld[source[a]],weld[source[b]]); int u; used.TryGetValue(key,out u); used[key]=u+1;
            }
            for(int i=0;i<frontTriangles;i++)
            {
                int g=groups[firstTriangle/3+i];
                for(int e=0;e<3;e++)
                {
                    int a=triangles[firstTriangle+3*i+e], b=triangles[firstTriangle+3*i+(e+1)%3];
                    if(used[PairKey(weld[source[a]],weld[source[b]])]!=1) continue;
                    Vec3 pa=positions[a], pb=positions[b];
                    Vec3 n=new Vec3(normals[3*a]+normals[3*b],normals[3*a+1]+normals[3*b+1],normals[3*a+2]+normals[3*b+2]).Normalized(new Vec3(0,1,0));
                    Vec3 outward=Vec3.Cross(pb-pa,n).Normalized(n);
                    int w=positions.Count;
                    foreach(int src in new[]{a,a,b,b})
                    {
                        bool deep=positions.Count-w==1||positions.Count-w==3;
                        positions.Add(deep?positions[back+src-first]:positions[src]);
                        normals.Add((float)outward.X); normals.Add((float)outward.Y); normals.Add((float)outward.Z);
                        if(tangents!=null) for(int k=0;k<4;k++) tangents.Add(tangents[4*src+k]);
                        uvs.Add(uvs[2*src]); uvs.Add(uvs[2*src+1]); edge.Add(1); side.Add(0.5f); fragment.Add(f); source.Add(source[src]);
                    }
                    triangles.Add(w); triangles.Add(w+1); triangles.Add(w+2);
                    triangles.Add(w+2); triangles.Add(w+1); triangles.Add(w+3);
                    groups.Add(g); groups.Add(g);
                }
            }
        }
        static long PairKey(int a,int b) { return a<b?((long)a<<32)|(uint)b:((long)b<<32)|(uint)a; }
        static PartMesh Refine(PartMesh mesh,double target,int maxTriangles)
        {
            var p=new List<Vec3>(mesh.Positions); var n=new List<float>(mesh.Normals); var uv=new List<float>(mesh.Uvs);
            var tan=mesh.Tangents!=null?new List<float>(mesh.Tangents):null;
            var tri=new List<int>(mesh.Triangles); var groups=new List<int>(mesh.Groups??new int[mesh.TriangleCount]);
            double target2=target*target, quantum=Math.Max(Extent(mesh.Positions)*2e-5,1e-6);
            for(int pass=0;pass<24;pass++)
            {
                int T=tri.Count/3, welded;
                int[] weld=Weld(p.ToArray(),quantum,out welded);
                var longest=new int[T]; var length=new double[T];
                double top=0;
                for(int t=0;t<T;t++)
                {
                    int best=0; double l2=-1;
                    for(int e=0;e<3;e++)
                    {
                        Vec3 d=p[tri[3*t+(e+1)%3]]-p[tri[3*t+e]]; double l=Vec3.Dot(d,d);
                        if(l>l2) { l2=l; best=e; }
                    }
                    longest[t]=best; length[t]=l2; top=Math.Max(top,l2);
                }
                double cut2=Math.Max(target2,top*0.3);
                var split=new HashSet<long>();
                for(int t=0;t<T;t++)
                    if(length[t]>cut2) split.Add(EdgeKey(weld[tri[3*t+longest[t]]],weld[tri[3*t+(longest[t]+1)%3]],welded));
                if(split.Count==0) break;
                for(bool changed=true;changed;)
                {
                    changed=false;
                    for(int t=0;t<T;t++)
                    {
                        long own=EdgeKey(weld[tri[3*t+longest[t]]],weld[tri[3*t+(longest[t]+1)%3]],welded);
                        if(split.Contains(own)) continue;
                        for(int e=0;e<3;e++)
                            if(split.Contains(EdgeKey(weld[tri[3*t+e]],weld[tri[3*t+(e+1)%3]],welded))) { split.Add(own); changed=true; break; }
                    }
                }
                int added=0;
                for(int t=0;t<T;t++) for(int e=0;e<3;e++) if(split.Contains(EdgeKey(weld[tri[3*t+e]],weld[tri[3*t+(e+1)%3]],welded))) added++;
                if(T+added>maxTriangles) break;
                var mid=new Dictionary<long,int>();
                Func<int,int,int> midpoint=(a,b)=>
                {
                    long key=PairKey(a,b); int id;
                    if(mid.TryGetValue(key,out id)) return id;
                    id=p.Count; mid[key]=id;
                    p.Add((p[a]+p[b])*0.5);
                    var nn=new Vec3(n[3*a]+n[3*b],n[3*a+1]+n[3*b+1],n[3*a+2]+n[3*b+2]).Normalized(new Vec3(0,1,0));
                    n.Add((float)nn.X); n.Add((float)nn.Y); n.Add((float)nn.Z);
                    uv.Add((uv[2*a]+uv[2*b])*0.5f); uv.Add((uv[2*a+1]+uv[2*b+1])*0.5f);
                    if(tan!=null)
                    {
                        var tt=new Vec3(tan[4*a]+tan[4*b],tan[4*a+1]+tan[4*b+1],tan[4*a+2]+tan[4*b+2]).Normalized(new Vec3(1,0,0));
                        tan.Add((float)tt.X); tan.Add((float)tt.Y); tan.Add((float)tt.Z); tan.Add(tan[4*a+3]);
                    }
                    return id;
                };
                var next=new List<int>(tri.Count+added*3); var nextGroups=new List<int>(groups.Count+added);
                for(int t=0;t<T;t++)
                {
                    int g=groups[t], L=longest[t];
                    int a=tri[3*t+L], b=tri[3*t+(L+1)%3], c=tri[3*t+(L+2)%3];
                    if(!split.Contains(EdgeKey(weld[a],weld[b],welded))) { Add(next,a,b,c); nextGroups.Add(g); continue; }
                    bool sbc=split.Contains(EdgeKey(weld[b],weld[c],welded)), sca=split.Contains(EdgeKey(weld[c],weld[a],welded));
                    int m=midpoint(a,b);
                    if(sca) { int mca=midpoint(c,a); Add(next,a,m,mca); Add(next,m,c,mca); nextGroups.Add(g); nextGroups.Add(g); }
                    else { Add(next,a,m,c); nextGroups.Add(g); }
                    if(sbc) { int mbc=midpoint(b,c); Add(next,m,b,mbc); Add(next,m,mbc,c); nextGroups.Add(g); nextGroups.Add(g); }
                    else { Add(next,m,b,c); nextGroups.Add(g); }
                }
                tri=next; groups=nextGroups;
            }
            return new PartMesh { Positions=p.ToArray(), Normals=n.ToArray(), Uvs=uv.ToArray(), Tangents=tan!=null?tan.ToArray():null,
                Triangles=tri.ToArray(), Groups=groups.ToArray() };
        }
        static void Add(List<int> list,int a,int b,int c) { list.Add(a); list.Add(b); list.Add(c); }
        static PartMesh Complete(PartMesh m)
        {
            if(m.Normals!=null&&m.Normals.Length>=m.VertexCount*3&&m.Uvs!=null&&m.Uvs.Length>=m.VertexCount*2) return m;
            var c=new PartMesh { Positions=m.Positions, Tangents=m.Tangents!=null&&m.Tangents.Length>=m.VertexCount*4?m.Tangents:null,
                Triangles=m.Triangles, Groups=m.Groups, Normals=m.Normals, Uvs=m.Uvs };
            if(c.Uvs==null||c.Uvs.Length<m.VertexCount*2) c.Uvs=new float[m.VertexCount*2];
            if(c.Normals==null||c.Normals.Length<m.VertexCount*3)
            {
                var sum=new Vec3[m.VertexCount];
                for(int t=0;t<m.TriangleCount;t++)
                {
                    int a=m.Triangles[3*t], b=m.Triangles[3*t+1], d=m.Triangles[3*t+2];
                    Vec3 n=Vec3.Cross(m.Positions[b]-m.Positions[a],m.Positions[d]-m.Positions[a]);
                    sum[a]+=n; sum[b]+=n; sum[d]+=n;
                }
                c.Normals=new float[m.VertexCount*3];
                for(int v=0;v<m.VertexCount;v++)
                {
                    Vec3 n=sum[v].Normalized(new Vec3(0,1,0));
                    c.Normals[3*v]=(float)n.X; c.Normals[3*v+1]=(float)n.Y; c.Normals[3*v+2]=(float)n.Z;
                }
            }
            return c;
        }
        static void RotateFloats(List<float> data,int at,Vec3 axis,double angle)
        {
            var v=Vec3.Rotate(new Vec3(data[at],data[at+1],data[at+2]),axis,angle);
            data[at]=(float)v.X; data[at+1]=(float)v.Y; data[at+2]=(float)v.Z;
        }
        static Vec3 Perpendicular(Vec3 n) { return Math.Abs(n.X)<0.9?Vec3.Cross(n,new Vec3(1,0,0)).Normalized(new Vec3(0,0,1)):Vec3.Cross(n,new Vec3(0,1,0)).Normalized(new Vec3(0,0,1)); }
        static double Smooth(double a,double b,double x) { double t=Numbers.Clamp((x-a)/(b-a),0,1); return t*t*(3-2*t); }
        static double Hash01(uint h) { h^=h>>16; h*=0x7FEB352Du; h^=h>>15; h*=0x846CA68Bu; h^=h>>16; return (h&0xFFFFFF)/16777216.0; }
        static double EdgeNoise(int a,int b,uint seed) { int lo=Math.Min(a,b), hi=Math.Max(a,b); return Hash01((uint)lo*2246822519u^(uint)hi*3266489917u^seed); }
        static long EdgeKey(int a,int b,int n) { return a<b?(long)a*n+b:(long)b*n+a; }
        static double Extent(Vec3[] p)
        {
            double minX=double.MaxValue,minY=double.MaxValue,minZ=double.MaxValue,maxX=double.MinValue,maxY=double.MinValue,maxZ=double.MinValue;
            for(int i=0;i<p.Length;i++)
            {
                minX=Math.Min(minX,p[i].X); minY=Math.Min(minY,p[i].Y); minZ=Math.Min(minZ,p[i].Z);
                maxX=Math.Max(maxX,p[i].X); maxY=Math.Max(maxY,p[i].Y); maxZ=Math.Max(maxZ,p[i].Z);
            }
            return new Vec3(maxX-minX,maxY-minY,maxZ-minZ).Length;
        }
        static int[] Weld(Vec3[] p,double quantum,out int count)
        {
            var ids=new Dictionary<long,int>(p.Length); var weld=new int[p.Length];
            for(int i=0;i<p.Length;i++)
            {
                long x=(long)Math.Round(p[i].X/quantum)&0x1FFFFF, y=(long)Math.Round(p[i].Y/quantum)&0x1FFFFF, z=(long)Math.Round(p[i].Z/quantum)&0x1FFFFF;
                long key=(x<<42)|(y<<21)|z;
                int id; if(!ids.TryGetValue(key,out id)) { id=ids.Count; ids[key]=id; }
                weld[i]=id;
            }
            count=ids.Count; return weld;
        }
        static int[] Neighbors(int[] tri,int[] weld,int welded)
        {
            int T=tri.Length/3; var neighbor=new int[T*3];
            for(int i=0;i<neighbor.Length;i++) neighbor[i]=-1;
            var first=new Dictionary<long,int>(T*2);
            for(int t=0;t<T;t++) for(int e=0;e<3;e++)
            {
                int a=weld[tri[3*t+e]], b=weld[tri[3*t+(e+1)%3]];
                if(a==b) continue;
                long key=EdgeKey(a,b,welded);
                int slot;
                if(first.TryGetValue(key,out slot))
                {
                    if(slot>=0&&neighbor[slot]<0&&slot/3!=t) { neighbor[slot]=t; neighbor[3*t+e]=slot/3; first[key]=-1; }
                }
                else first[key]=3*t+e;
            }
            return neighbor;
        }
        static int[] Components(int[] neighbor,int T,out int count)
        {
            var comp=new int[T]; for(int i=0;i<T;i++) comp[i]=-1;
            var stack=new Stack<int>(); count=0;
            for(int s=0;s<T;s++)
            {
                if(comp[s]>=0) continue;
                comp[s]=count; stack.Push(s);
                while(stack.Count>0)
                {
                    int t=stack.Pop();
                    for(int e=0;e<3;e++) { int n=neighbor[3*t+e]; if(n>=0&&comp[n]<0) { comp[n]=count; stack.Push(n); } }
                }
                count++;
            }
            return comp;
        }
        static int[][] TrianglesByComponent(int[] component,int components,int T)
        {
            var size=new int[components]; for(int t=0;t<T;t++) size[component[t]]++;
            var lists=new int[components][]; for(int c=0;c<components;c++) lists[c]=new int[size[c]];
            var fill=new int[components];
            for(int t=0;t<T;t++) { int c=component[t]; lists[c][fill[c]++]=t; }
            return lists;
        }
        static int PickByArea(int[] list,double[] area,double total,ref SplitRandom rnd)
        {
            double target=rnd.Next()*total, sum=0;
            for(int i=0;i<list.Length;i++) { sum+=area[list[i]]; if(sum>=target) return list[i]; }
            return list[list.Length-1];
        }
        sealed class Heap
        {
            double[] key; int[] value; public int Count;
            public Heap(int capacity) { key=new double[Math.Max(capacity,16)]; value=new int[key.Length]; }
            public void Push(double k,int v)
            {
                if(Count==key.Length) { Array.Resize(ref key,Count*2); Array.Resize(ref value,Count*2); }
                int i=Count++;
                while(i>0) { int p=(i-1)/2; if(key[p]<=k) break; key[i]=key[p]; value[i]=value[p]; i=p; }
                key[i]=k; value[i]=v;
            }
            public int Pop(out double k)
            {
                k=key[0]; int top=value[0];
                double lastKey=key[--Count]; int lastValue=value[Count];
                int i=0;
                while(true)
                {
                    int c=2*i+1; if(c>=Count) break;
                    if(c+1<Count&&key[c+1]<key[c]) c++;
                    if(key[c]>=lastKey) break;
                    key[i]=key[c]; value[i]=value[c]; i=c;
                }
                if(Count>0) { key[i]=lastKey; value[i]=lastValue; }
                return top;
            }
        }
    }
}
