using System;
using UnityEditor;
using UnityEngine;

public static class BlueNoise
{
    public const string AssetPath="Assets/Noise/VefxBlueNoise.asset";
    public const int Size=64;
    const string Version="VefxBlueNoise-v1";
    public static Texture2D Ensure()
    {
        var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
        if(existing!=null&&NoiseTexture.Current(AssetPath,Version)) return existing;
        if(!AssetDatabase.IsValidFolder("Assets/Noise")) AssetDatabase.CreateFolder("Assets","Noise");
        if(existing!=null) AssetDatabase.DeleteAsset(AssetPath);
        float[] rank=Generate();
        var t=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true);
        t.name=Version; t.wrapMode=TextureWrapMode.Repeat; t.filterMode=FilterMode.Point;
        var c=new Color32[Size*Size];
        for(int i=0;i<c.Length;i++) { byte v=(byte)Mathf.Clamp(Mathf.RoundToInt(rank[i]*255),0,255); c[i]=new Color32(v,v,v,255); }
        t.SetPixels32(c); t.Apply(false,false);
        AssetDatabase.CreateAsset(t,AssetPath);
        AssetDatabase.SaveAssets();
        NoiseTexture.Stamp(AssetPath,Version);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
    }
    static float[] Generate()
    {
        const int total=Size*Size;
        const float sigma=1.9f;
        var kernel=new float[total];
        for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
        {
            int dx=Math.Min(x,Size-x), dy=Math.Min(y,Size-y);
            kernel[y*Size+x]=Mathf.Exp(-(dx*dx+dy*dy)/(2*sigma*sigma));
        }
        var on=new bool[total]; var energy=new float[total];
        Action<int,float> apply=(p,sign)=>
        {
            int px=p%Size, py=p/Size;
            for(int y=0;y<Size;y++)
            {
                int ky=((y-py)%Size+Size)%Size;
                for(int x=0;x<Size;x++) energy[y*Size+x]+=sign*kernel[ky*Size+((x-px)%Size+Size)%Size];
            }
        };
        Func<bool,int> extreme=(ones)=>
        {
            int best=-1; float value=ones?float.MinValue:float.MaxValue;
            for(int i=0;i<total;i++)
            {
                if(on[i]!=ones) continue;
                if(ones?energy[i]>value:energy[i]<value) { value=energy[i]; best=i; }
            }
            return best;
        };
        var rnd=new System.Random(20261005);
        int initial=total/10;
        for(int k=0;k<initial;)
        {
            int p=rnd.Next(total);
            if(on[p]) continue;
            on[p]=true; apply(p,1); k++;
        }
        for(int guard=0;guard<total*4;guard++)
        {
            int cluster=extreme(true); on[cluster]=false; apply(cluster,-1);
            int voidAt=extreme(false);
            on[voidAt]=true; apply(voidAt,1);
            if(voidAt==cluster) break;
        }
        var rank=new float[total];
        var start=(bool[])on.Clone(); var startEnergy=(float[])energy.Clone();
        for(int r=initial-1;r>=0;r--) { int p=extreme(true); on[p]=false; apply(p,-1); rank[p]=r; }
        on=start; energy=startEnergy;
        for(int r=initial;r<total;r++) { int p=extreme(false); on[p]=true; apply(p,1); rank[p]=r; }
        for(int i=0;i<total;i++) rank[i]=(rank[i]+0.5f)/total;
        return rank;
    }
}
