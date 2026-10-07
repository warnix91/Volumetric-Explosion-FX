using System;
using UnityEngine;
namespace VolumetricExplosionFX.Rendering
{
    internal sealed class ProceduralAssets : IDisposable
    {
        internal readonly Material Glow, Cloud, Debris, Trail;
        internal readonly Mesh Shard;
        internal readonly string ShaderSummary;
        readonly Texture2D glow, cloud, trail;
        internal readonly bool HeatDebris;
        internal ProceduralAssets(Shader original,Shader debris)
        {
            Shader additive=original!=null?original:SupportedShader("glow",new[]{"Particles/Standard Unlit","Legacy Shaders/Particles/Additive","Particles/Additive"});
            Shader alpha=original!=null?original:SupportedShader("cloud",new[]{"Legacy Shaders/Particles/Alpha Blended","Particles/Alpha Blended","Particles/Standard Unlit"});
            Shader lit=debris;
            if(lit==null) foreach(string name in new[]{"KSP/Diffuse","Legacy Shaders/Diffuse","Diffuse"})
            { Shader candidate=Shader.Find(name); if(candidate!=null&&candidate.isSupported) { lit=candidate; break; } }
            HeatDebris=debris!=null;
            ShaderSummary="API="+SystemInfo.graphicsDeviceType+" | glow="+additive.name+" | cloud="+alpha.name+" | debris="+(lit!=null?lit.name:"glow fallback");
            glow=Texture(false); cloud=Texture(true);
            Glow=new Material(additive); Glow.mainTexture=glow;
            Configure(Glow,true);
            Cloud=new Material(alpha); Cloud.mainTexture=cloud;
            Configure(Cloud,false);
            if(original!=null)
            {
                Glow.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha); Glow.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.One);
                Cloud.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha); Cloud.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                Glow.renderQueue=3001; Cloud.renderQueue=3000;
            }
            trail=TrailTexture(); Trail=new Material(alpha); Trail.mainTexture=trail; Trail.mainTextureScale=new Vector2(0.4f,1);
            Configure(Trail,false);
            if(original!=null) { Trail.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha); Trail.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); Trail.renderQueue=3000; }
            if(lit!=null) { Debris=new Material(lit); if(!HeatDebris) Debris.color=new Color(0.62f,0.62f,0.6f,1); }
            else Debris=Glow;
            Shard=new Mesh(); Shard.name="Original procedural visual shard";
            var top=new[]{new Vector3(-0.5f,0,-0.18f),new Vector3(-0.12f,0.06f,-0.34f),new Vector3(0.38f,0.02f,-0.22f),
                new Vector3(0.52f,-0.05f,0.12f),new Vector3(0.1f,0.08f,0.3f),new Vector3(-0.36f,0.03f,0.2f),new Vector3(0,0.1f,0)};
            var vertices=new Vector3[top.Length*2];
            for(int i=0;i<top.Length;i++) { vertices[i]=top[i]; vertices[i+top.Length]=top[i]-new Vector3(0,0.035f,0); }
            var triangles=new System.Collections.Generic.List<int>();
            for(int i=0;i<6;i++) { int a=i, b=(i+1)%6; triangles.AddRange(new[]{6,b,a}); triangles.AddRange(new[]{13,a+7,b+7}); }
            for(int i=0;i<6;i++) { int a=i, b=(i+1)%6; triangles.AddRange(new[]{a,b,b+7,a,b+7,a+7,b,a,b+7,b+7,a,a+7}); }
            Shard.vertices=vertices; Shard.triangles=triangles.ToArray();
            Shard.RecalculateNormals(); Shard.RecalculateBounds();
        }
        static Shader SupportedShader(string role,string[] candidates)
        {
            for(int i=0;i<candidates.Length;i++)
            {
                Shader shader=Shader.Find(candidates[i]);
                if(shader!=null&&shader.isSupported) return shader;
                Debug.LogWarning("[VEFX] "+role+" shader "+candidates[i]+": "+
                    (shader==null?"missing":"unsupported")+" on "+SystemInfo.graphicsDeviceType+"; trying fallback.");
            }
            throw new InvalidOperationException("No supported "+role+" particle shader on "+SystemInfo.graphicsDeviceType+".");
        }
        static void Configure(Material material,bool additive)
        {
            if(material.HasProperty("_TintColor")) material.SetColor("_TintColor",Color.white);
            if(material.HasProperty("_Color")) material.SetColor("_Color",Color.white);
            if(!material.HasProperty("_Mode")) return;
            material.SetFloat("_Mode",additive?4:2);
            material.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend",(int)(additive?UnityEngine.Rendering.BlendMode.One:UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.SetInt("_ZWrite",0);
            material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_ALPHABLEND_ON"); material.renderQueue=3000;
        }
        static Texture2D Texture(bool turbulent)
        {
            const int side=64; Texture2D t=new Texture2D(side,side,TextureFormat.RGBA32,false);
            t.name=turbulent?"Original layered noise puff":"Original soft spark"; t.wrapMode=TextureWrapMode.Clamp;
            Color[] data=new Color[side*side];
            for(int y=0;y<side;y++) for(int x=0;x<side;x++)
            {
                float u=(x+0.5f)/side*2-1, v=(y+0.5f)/side*2-1;
                float edge=Mathf.Clamp01(1-u*u-v*v);
                float n=turbulent?(0.45f+0.35f*Mathf.PerlinNoise(u*3.7f+12,v*3.7f+7)+0.2f*Mathf.PerlinNoise(u*11+2,v*11+19)):1;
                float a=edge*edge*n;
                if(!turbulent) a=0.5f*Mathf.Exp(-(u*u+v*v)*18)*(edge>0?1:0)+0.5f*edge*edge;
                data[y*side+x]=turbulent?new Color(n,n,n,a):new Color(a,a,a,a);
            }
            t.SetPixels(data); t.Apply(false,true); return t;
        }
        static Texture2D TrailTexture()
        {
            const int w=64, h=32; var t=new Texture2D(w,h,TextureFormat.RGBA32,false);
            t.name="Original smoke trail band"; t.wrapMode=TextureWrapMode.Repeat;
            var data=new Color[w*h];
            for(int y=0;y<h;y++) for(int x=0;x<w;x++)
            {
                float u=(x+0.5f)/w, v=(y+0.5f)/h*2-1;
                float c=Mathf.Cos(u*Mathf.PI*2)*1.6f, d=Mathf.Sin(u*Mathf.PI*2)*1.6f;
                float n=0.55f+0.45f*Mathf.PerlinNoise(c+5+v*0.8f,d+9);
                float band=Mathf.Clamp01(1-v*v); band*=band;
                data[y*w+x]=new Color(n,n,n,band*n);
            }
            t.SetPixels(data); t.Apply(false,true); return t;
        }
        public void Dispose()
        {
            if(Debris!=Glow) UnityEngine.Object.Destroy(Debris); UnityEngine.Object.Destroy(Glow); UnityEngine.Object.Destroy(Cloud);
            UnityEngine.Object.Destroy(Trail); UnityEngine.Object.Destroy(glow); UnityEngine.Object.Destroy(cloud); UnityEngine.Object.Destroy(trail); UnityEngine.Object.Destroy(Shard);
        }
    }
    internal struct VisualRandom
    {
        uint state;
        internal VisualRandom(uint seed) { state=seed==0?1:seed; }
        internal float Next() { state^=state<<13;state^=state>>17;state^=state<<5; return (state&0xffffff)/16777216f; }
        internal Vector3 Direction()
        { float z=Next()*2-1, a=Next()*Mathf.PI*2, r=Mathf.Sqrt(Mathf.Max(0,1-z*z)); return new Vector3(r*Mathf.Cos(a),z,r*Mathf.Sin(a)); }
    }
}
