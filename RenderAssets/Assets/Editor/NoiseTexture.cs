using System;
using UnityEditor;
using UnityEngine;

// Noise RGBA: billows, medium detail, fine detail, warp.
public static class NoiseTexture
{
    public const string AssetPath="Assets/Noise/VefxNoise3D.asset";
    public const int Size=64;
    public static Texture3D Ensure()
    {
        var existing=AssetDatabase.LoadAssetAtPath<Texture3D>(AssetPath);
        if(existing!=null&&existing.width==Size&&Current(AssetPath,Version)) return existing;
        if(!AssetDatabase.IsValidFolder("Assets/Noise")) AssetDatabase.CreateFolder("Assets","Noise");
        if(existing!=null) AssetDatabase.DeleteAsset(AssetPath);
        var tex=Generate();
        AssetDatabase.CreateAsset(tex,AssetPath);
        AssetDatabase.SaveAssets();
        Stamp(AssetPath,Version);
        return AssetDatabase.LoadAssetAtPath<Texture3D>(AssetPath);
    }
    // Store generator version in importer data; asset names are not stable.
    internal static bool Current(string path,string version)
    {
        var importer=AssetImporter.GetAtPath(path);
        return importer!=null&&importer.userData==version;
    }
    internal static void Stamp(string path,string version)
    {
        var importer=AssetImporter.GetAtPath(path);
        if(importer==null) return;
        importer.userData=version; importer.SaveAndReimport();
    }
    const string Version="VefxNoise3D-v2";
    static Texture3D Generate()
    {
        var pixels=new Color32[Size*Size*Size];
        System.Threading.Tasks.Parallel.For(0,Size,z=>
        {
            for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
            {
                float u=(x+0.5f)/Size, v=(y+0.5f)/Size, w=(z+0.5f)/Size;
                float bx=Gradient(u,v,w,4,61)*0.07f, by=Gradient(u,v,w,4,62)*0.07f, bz=Gradient(u,v,w,4,63)*0.07f;
                float smooth=0.5f+0.5f*(Gradient(u,v,w,4,11)*0.55f+Gradient(u,v,w,8,12)*0.3f+Gradient(u,v,w,16,13)*0.15f)*1.35f;
                float cells=1-Cellular(u+bx,v+by,w+bz,4,21);
                float billow=Mathf.Clamp01(cells*0.6f+Mathf.Clamp01(smooth)*0.55f-0.05f);
                float medium=1-(Cellular(u+bx*0.6f,v+by*0.6f,w+bz*0.6f,8,31)*0.65f+Cellular(u+bx*0.4f,v+by*0.4f,w+bz*0.4f,16,32)*0.35f);
                float fine=1-(Cellular(u+bx*0.4f,v+by*0.4f,w+bz*0.4f,16,41)*0.6f+Cellular(u+bx*0.3f,v+by*0.3f,w+bz*0.3f,24,42)*0.4f);
                float warp=0.5f+0.5f*(Gradient(u,v,w,2,51)*0.6f+Gradient(u,v,w,4,52)*0.4f)*1.4f;
                pixels[(z*Size+y)*Size+x]=new Color32(Byte(billow),Byte(medium),Byte(fine),Byte(warp));
            }
        });
        var tex=new Texture3D(Size,Size,Size,TextureFormat.RGBA32,false);
        tex.name=Version; tex.wrapMode=TextureWrapMode.Repeat; tex.filterMode=FilterMode.Bilinear;
        tex.SetPixels32(pixels); tex.Apply(false,false);
        return tex;
    }
    static byte Byte(float x) { return (byte)Mathf.RoundToInt(Mathf.Clamp01(x)*255); }
    static uint Mix(uint h) { h^=h>>16; h*=0x8A3B6C4Du; h^=h>>13; h*=0xC2B2AE35u; h^=h>>16; return h; }
    static uint Cell(int x,int y,int z,uint seed) { return Mix((uint)x+Mix((uint)y+Mix((uint)z+Mix(seed)))); }
    static float Unit(uint h) { return (h&0xFFFFFF)/16777216f; }
    static int Wrap(int i,int n) { int r=i%n; return r<0?r+n:r; }
    const float CellSoftness=7;
    static float Cellular(float u,float v,float w,int cells,uint seed)
    {
        float fx=u*cells, fy=v*cells, fz=w*cells;
        int ix=Mathf.FloorToInt(fx), iy=Mathf.FloorToInt(fy), iz=Mathf.FloorToInt(fz);
        float best=9;
        var near=new float[27]; int count=0;
        for(int dz=-1;dz<=1;dz++) for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++)
        {
            int cx=ix+dx, cy=iy+dy, cz=iz+dz;
            uint h=Cell(Wrap(cx,cells),Wrap(cy,cells),Wrap(cz,cells),seed);
            float px=cx+Unit(h)-fx, py=cy+Unit(Mix(h^0x51u))-fy, pz=cz+Unit(Mix(h^0xA7u))-fz;
            float d=Mathf.Sqrt(px*px+py*py+pz*pz); near[count++]=d; if(d<best) best=d;
        }
        float sum=0;
        for(int i=0;i<count;i++) sum+=Mathf.Exp(-CellSoftness*(near[i]-best));
        return Mathf.Clamp01(best-Mathf.Log(sum)/CellSoftness+0.06f);
    }
    static float Gradient(float u,float v,float w,int period,uint seed)
    {
        float fx=u*period, fy=v*period, fz=w*period;
        int ix=Mathf.FloorToInt(fx), iy=Mathf.FloorToInt(fy), iz=Mathf.FloorToInt(fz);
        float tx=fx-ix, ty=fy-iy, tz=fz-iz;
        float sx=Fade(tx), sy=Fade(ty), sz=Fade(tz);
        float result=0;
        for(int c=0;c<8;c++)
        {
            int ox=c&1, oy=(c>>1)&1, oz=(c>>2)&1;
            uint h=Cell(Wrap(ix+ox,period),Wrap(iy+oy,period),Wrap(iz+oz,period),seed);
            float gx=Unit(h)*2-1, gy=Unit(Mix(h^0x3Du))*2-1, gz=Unit(Mix(h^0x9Eu))*2-1;
            float len=Mathf.Sqrt(gx*gx+gy*gy+gz*gz)+1e-6f;
            float dot=(gx*(tx-ox)+gy*(ty-oy)+gz*(tz-oz))/len;
            float weight=(ox==1?sx:1-sx)*(oy==1?sy:1-sy)*(oz==1?sz:1-sz);
            result+=dot*weight;
        }
        return Mathf.Clamp(result*1.6f,-1,1);
    }
    static float Fade(float t) { return t*t*t*(t*(t*6-15)+10); }
}
