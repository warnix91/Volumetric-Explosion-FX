using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Map A: (+X,-X,+Y,opacity); map B: (-Y,+Z,-Z,incandescence).
// Radiance is scaled by 0.4; the shader restores it with 2.5.
public static class SmokeFlipbook
{
    public const string PathA="Assets/Smoke/VefxSmokeA.asset", PathB="Assets/Smoke/VefxSmokeB.asset";
    public const int Tiles=8, Frame=128;
    const int G=80;                     // simulation grid per frame (G^3 voxels over [-1,1]^3)
    const float Extinction=7;           // optical depth per unit length at density 1
    const float Encode=0.4f;
    const string Version="VefxSmoke-v5";
    public static void Ensure(out Texture2D a,out Texture2D b)
    {
        a=AssetDatabase.LoadAssetAtPath<Texture2D>(PathA); b=AssetDatabase.LoadAssetAtPath<Texture2D>(PathB);
        if(a!=null&&b!=null&&NoiseTexture.Current(PathA,Version)&&NoiseTexture.Current(PathB,Version)) return;
        if(!AssetDatabase.IsValidFolder("Assets/Smoke")) AssetDatabase.CreateFolder("Assets","Smoke");
        Color32[] pa,pb; Bake(out pa,out pb);
        if(a!=null) AssetDatabase.DeleteAsset(PathA);
        if(b!=null) AssetDatabase.DeleteAsset(PathB);
        AssetDatabase.CreateAsset(Make(pa,Version+"-A"),PathA);
        AssetDatabase.CreateAsset(Make(pb,Version+"-B"),PathB);
        AssetDatabase.SaveAssets();
        NoiseTexture.Stamp(PathA,Version); NoiseTexture.Stamp(PathB,Version);
        a=AssetDatabase.LoadAssetAtPath<Texture2D>(PathA); b=AssetDatabase.LoadAssetAtPath<Texture2D>(PathB);
    }
    static Texture2D Make(Color32[] pixels,string name)
    {
        int size=Tiles*Frame;
        var t=new Texture2D(size,size,TextureFormat.RGBA32,true,true);
        t.name=name; t.wrapMode=TextureWrapMode.Clamp; t.filterMode=FilterMode.Trilinear;
        t.SetPixels32(pixels); t.Apply(true,false);
        return t;
    }
    static void Bake(out Color32[] A,out Color32[] B)
    {
        Color32[] noise=NoiseTexture.Ensure().GetPixels32(); int n=NoiseTexture.Size;
        int size=Tiles*Frame; A=new Color32[size*size]; B=new Color32[size*size];
        var density=new float[G*G*G]; var heat=new float[G*G*G];
        var light=new float[6][]; for(int i=0;i<6;i++) light[i]=new float[G*G*G];
        var image=new float[8][]; for(int i=0;i<8;i++) image[i]=new float[G*G];
        for(int f=0;f<Tiles*Tiles;f++)
        {
            float tau=(f%Tiles)/(Tiles-1f);
            Field(noise,n,f/Tiles,tau,density,heat);
            Lights(density,light);
            View(density,heat,light,image);
            Blit(image,A,B,f);
        }
    }
    static int Index(int x,int y,int z) { return (z*G+y)*G+x; }
    static Vector4 Sample(Color32[] noise,int n,Vector3 p)
    {
        float fx=p.x*n-0.5f, fy=p.y*n-0.5f, fz=p.z*n-0.5f;
        int x0=Mathf.FloorToInt(fx), y0=Mathf.FloorToInt(fy), z0=Mathf.FloorToInt(fz);
        float tx=fx-x0, ty=fy-y0, tz=fz-z0;
        Vector4 r=Vector4.zero;
        for(int c=0;c<8;c++)
        {
            int dx=c&1, dy=(c>>1)&1, dz=(c>>2)&1;
            int x=Wrap(x0+dx,n), y=Wrap(y0+dy,n), z=Wrap(z0+dz,n);
            Color32 v=noise[(z*n+y)*n+x];
            float w=(dx==1?tx:1-tx)*(dy==1?ty:1-ty)*(dz==1?tz:1-tz);
            r+=new Vector4(v.r,v.g,v.b,v.a)*(w/255f);
        }
        return r;
    }
    static int Wrap(int i,int n) { int r=i%n; return r<0?r+n:r; }
    static void Field(Color32[] noise,int n,int variant,float tau,float[] density,float[] heat)
    {
        var rnd=new System.Random(4099+variant*7919);
        int lumps=3+rnd.Next(4);
        var centre=new Vector3[lumps]; var size=new float[lumps];
        for(int k=0;k<lumps;k++)
        {
            float a=(float)(rnd.NextDouble()*Math.PI*2), e=(float)(rnd.NextDouble()*1.4-0.5);
            float r=k==0?0.1f:0.3f+0.25f*(float)rnd.NextDouble();
            centre[k]=new Vector3(Mathf.Cos(a)*Mathf.Cos(e),Mathf.Sin(e)*0.8f,Mathf.Sin(a)*Mathf.Cos(e))*r;
            size[k]=k==0?0.55f+0.1f*(float)rnd.NextDouble():0.28f+0.2f*(float)rnd.NextDouble();
        }
        var offset=new Vector3((float)rnd.NextDouble(),(float)rnd.NextDouble(),(float)rnd.NextDouble());
        float grow=0.78f+0.3f*(1-Mathf.Exp(-2.6f*tau));
        float rough=0.55f+0.4f*tau, sharp=Mathf.Lerp(4.5f,1.3f,tau), thin=Mathf.Lerp(1,0.45f,tau);
        float front=1.15f-2.6f*tau, hot=Mathf.Clamp01(1-tau*2.2f);
        Parallel.For(0,G,z=>
        {
            for(int y=0;y<G;y++) for(int x=0;x<G;x++)
            {
                var p=new Vector3((x+0.5f)/G*2-1,(y+0.5f)/G*2-1,(z+0.5f)/G*2-1);
                Vector4 w=Sample(noise,n,p*0.5f+offset+new Vector3(0.13f,0.37f+tau*0.12f,0.71f));
                Vector3 q=p+new Vector3(w.w-0.5f,w.y-0.5f,w.x-0.5f)*0.6f;
                float billow=Sample(noise,n,q*1.05f+offset*1.7f+new Vector3(0.21f,-0.35f*tau,0.47f)).x;
                float medium=Sample(noise,n,q*2.1f+offset*2.3f+new Vector3(0.61f,-0.6f*tau,0.11f)).y;
                float fine=Sample(noise,n,q*4.3f+offset*3.1f+new Vector3(0.3f,-tau,0.8f)).z;
                var s=new Vector3(q.x,q.y*(q.y<0?1+0.35f*tau:1-0.1f*tau),q.z);
                float sum=0;
                for(int k=0;k<lumps;k++) sum+=Mathf.Exp(-6*(s-centre[k]*grow).magnitude/(size[k]*grow));
                float d=-Mathf.Log(Mathf.Max(sum,1e-6f))/6;
                float shape=1-d+(billow-0.5f)*rough+(medium-0.5f)*0.32f+(fine-0.5f)*0.15f;
                float rho=Mathf.Clamp01(shape*sharp)*thin;
                float rim=Mathf.Clamp01((d-0.55f)/0.5f);
                rho=Mathf.Max(0,rho-(0.1f+0.45f*tau*tau)*(1-fine)*(0.5f+0.5f*rim));
                float border=Mathf.Clamp01((1-Mathf.Max(Mathf.Abs(p.x),Mathf.Max(Mathf.Abs(p.y),Mathf.Abs(p.z))))*8);
                int i=Index(x,y,z);
                density[i]=rho*border;
                heat[i]=hot*Mathf.Clamp01((front-d)*2.2f+0.35f+(billow-0.5f)*1.2f+(medium-0.5f)*0.5f);
            }
        });
    }
    static void Lights(float[] rho,float[][] light)
    {
        float step=2f/G*Extinction;
        Parallel.For(0,G*G,k=>
        {
            int a=k%G, b=k/G;
            float acc=0;
            for(int x=G-1;x>=0;x--) { int i=Index(x,a,b); light[0][i]=Transmit(acc); acc+=rho[i]*step; }
            acc=0; for(int x=0;x<G;x++) { int i=Index(x,a,b); light[1][i]=Transmit(acc); acc+=rho[i]*step; }
            acc=0; for(int y=G-1;y>=0;y--) { int i=Index(a,y,b); light[2][i]=Transmit(acc); acc+=rho[i]*step; }
            acc=0; for(int y=0;y<G;y++) { int i=Index(a,y,b); light[3][i]=Transmit(acc); acc+=rho[i]*step; }
            acc=0; for(int z=G-1;z>=0;z--) { int i=Index(a,b,z); light[4][i]=Transmit(acc); acc+=rho[i]*step; }
            acc=0; for(int z=0;z<G;z++) { int i=Index(a,b,z); light[5][i]=Transmit(acc); acc+=rho[i]*step; }
        });
    }
    static float Transmit(float depth) { return 0.72f*Mathf.Exp(-depth)+0.28f*Mathf.Exp(-depth*0.3f); }
    static readonly float[] Phase={0.84f,0.84f,0.84f,0.84f,0.48f,2.1f};
    static void View(float[] rho,float[] heat,float[][] light,float[][] image)
    {
        float step=2f/G*Extinction;
        Parallel.For(0,G*G,k=>
        {
            int x=k%G, y=k/G;
            float transmittance=1, emission=0;
            float l0=0,l1=0,l2=0,l3=0,l4=0,l5=0;
            for(int z=G-1;z>=0;z--)
            {
                int i=Index(x,y,z);
                float a=1-Mathf.Exp(-rho[i]*step);
                if(a<=0) continue;
                float w=transmittance*a;
                l0+=w*light[0][i]; l1+=w*light[1][i]; l2+=w*light[2][i]; l3+=w*light[3][i]; l4+=w*light[4][i]; l5+=w*light[5][i];
                float h=heat[i]; emission+=w*h*h;
                transmittance*=1-a;
                if(transmittance<1e-4f) break;
            }
            float alpha=1-transmittance, inv=alpha>1e-4f?1/alpha:0;
            image[0][k]=l0*inv*Phase[0]; image[1][k]=l1*inv*Phase[1]; image[2][k]=l2*inv*Phase[2];
            image[3][k]=l3*inv*Phase[3]; image[4][k]=l4*inv*Phase[4]; image[5][k]=l5*inv*Phase[5];
            image[6][k]=alpha; image[7][k]=emission*inv;
        });
    }
    // Texture-sheet order starts at the top-left tile.
    static void Blit(float[][] image,Color32[] A,Color32[] B,int frame)
    {
        int size=Tiles*Frame, column=frame%Tiles, row=frame/Tiles;
        int x0=column*Frame, y0=size-(row+1)*Frame;
        for(int py=0;py<Frame;py++) for(int px=0;px<Frame;px++)
        {
            float gx=(px+0.5f)/Frame*G-0.5f, gy=(py+0.5f)/Frame*G-0.5f;
            int o=(y0+py)*size+x0+px;
            A[o]=new Color32(Byte(Bilinear(image[0],gx,gy)*Encode),Byte(Bilinear(image[1],gx,gy)*Encode),Byte(Bilinear(image[2],gx,gy)*Encode),Byte(Bilinear(image[6],gx,gy)));
            B[o]=new Color32(Byte(Bilinear(image[3],gx,gy)*Encode),Byte(Bilinear(image[4],gx,gy)*Encode),Byte(Bilinear(image[5],gx,gy)*Encode),Byte(Bilinear(image[7],gx,gy)));
        }
    }
    static float Bilinear(float[] img,float x,float y)
    {
        int x0=Mathf.Clamp(Mathf.FloorToInt(x),0,G-1), y0=Mathf.Clamp(Mathf.FloorToInt(y),0,G-1);
        int x1=Mathf.Min(x0+1,G-1), y1=Mathf.Min(y0+1,G-1);
        float tx=Mathf.Clamp01(x-x0), ty=Mathf.Clamp01(y-y0);
        return Mathf.Lerp(Mathf.Lerp(img[y0*G+x0],img[y0*G+x1],tx),Mathf.Lerp(img[y1*G+x0],img[y1*G+x1],tx),ty);
    }
    static byte Byte(float v) { return (byte)Mathf.RoundToInt(Mathf.Clamp01(v)*255); }
}
