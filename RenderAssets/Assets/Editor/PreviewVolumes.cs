using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VolumetricExplosionFX.Core;

public static class PreviewVolumes
{
    internal static int Width=480, Height=270, Columns=5;
    internal static double[] Ages={0.03,0.1,0.2,0.35,0.6,0.9,1.4,2.0,3.0,4.5};
    internal static float Distance=1, Elevation=-1;   // Elevation: camera pitch in degrees (-1 = default view)
    internal sealed class Scenario
    {
        public string Name; public EventKind Kind; public double Density, Speed, Mass, Fuel, Oxidizer, Size, Solid;
        public bool Vacuum, Water, Airburst, Moon;   // Moon: airless ground (grey regolith, black sky, 1.63 m/s2)
        public Vec3 Velocity; public BlastSource[] Sources; public uint Seed=37;
        public string Body="Kerbin";   // dust colour (BodyLook); Duna: thin air, 2.94 m/s2
        internal double Gravity { get { return Moon||Vacuum?1.63:Body=="Duna"?2.94:9.81; } }
    }
    internal static readonly Scenario[] Scenarios={
        new Scenario{Name="tank-crash-ksc",Kind=EventKind.GroundImpact,Density=1.225,Speed=60,Mass=2250,Fuel=900,Oxidizer=1100,Size=2.6,Velocity=new Vec3(8,-14,3)},
        new Scenario{Name="heavy-stage-crash",Kind=EventKind.GroundImpact,Density=1.225,Speed=140,Mass=36000,Fuel=14000,Oxidizer=18000,Size=8,Velocity=new Vec3(30,-136,10)},
        new Scenario{Name="grazing-crash",Kind=EventKind.GroundImpact,Density=1.225,Speed=125,Mass=9000,Fuel=3500,Oxidizer=4300,Size=4,Velocity=new Vec3(120,-35,0),Seed=11},
        new Scenario{Name="vertical-crash",Kind=EventKind.GroundImpact,Density=1.225,Speed=160,Mass=9000,Fuel=3500,Oxidizer=4300,Size=4,Velocity=new Vec3(6,-160,4),Seed=23},
        new Scenario{Name="rocket-chain",Kind=EventKind.GroundImpact,Density=1.225,Speed=20,Mass=9000,Fuel=3500,Oxidizer=4300,Size=4,Velocity=new Vec3(0,-15,0),Seed=5,
            Sources=new[]{new BlastSource(new Vec3(0,9,1),0.05,0.8),new BlastSource(new Vec3(1,18,0),0.12,0.6),new BlastSource(new Vec3(-1,26,-1),0.22,0.5)}},
        new Scenario{Name="breakup-streak",Kind=EventKind.Destruction,Density=0.5,Speed=420,Mass=9000,Fuel=3500,Oxidizer=4300,Size=4,Airburst=true,Velocity=new Vec3(380,170,0),Seed=17},
        new Scenario{Name="fast-empty-impact",Kind=EventKind.GroundImpact,Density=1.225,Speed=450,Mass=6000,Size=3},
        new Scenario{Name="airburst",Kind=EventKind.Destruction,Density=0.4,Speed=600,Mass=9000,Fuel=3500,Oxidizer=4300,Size=4,Airburst=true},
        new Scenario{Name="vacuum",Kind=EventKind.Destruction,Density=0,Speed=2200,Mass=9000,Fuel=3500,Oxidizer=4300,Size=4,Vacuum=true},
        new Scenario{Name="mun-crash",Kind=EventKind.GroundImpact,Density=0,Speed=60,Mass=2250,Fuel=900,Oxidizer=1100,Size=2.6,Velocity=new Vec3(8,-40,3),Moon=true,Seed=41},
        new Scenario{Name="srb-burst",Kind=EventKind.GroundImpact,Density=1.225,Speed=12,Mass=8500,Solid=7000,Size=7,Velocity=new Vec3(0,-8,0),Seed=29},
        new Scenario{Name="srb-flight",Kind=EventKind.Destruction,Density=0.95,Speed=260,Mass=8500,Solid=7000,Size=7,Airburst=true,Velocity=new Vec3(140,220,0),Seed=31},
        new Scenario{Name="reentry-breakup",Kind=EventKind.Destruction,Density=0.012,Speed=2300,Mass=3000,Fuel=200,Oxidizer=250,Size=2.5,Airburst=true,Velocity=new Vec3(2200,-650,0),Seed=43},
        new Scenario{Name="high-altitude",Kind=EventKind.Destruction,Density=0.12,Speed=600,Mass=9000,Fuel=3500,Oxidizer=4300,Size=4,Airburst=true,Velocity=new Vec3(0,0,0),Seed=53},
        new Scenario{Name="tank-crash-b",Kind=EventKind.GroundImpact,Density=1.225,Speed=60,Mass=2250,Fuel=900,Oxidizer=1100,Size=2.6,Velocity=new Vec3(8,-14,3),Seed=101},
        new Scenario{Name="tank-crash-c",Kind=EventKind.GroundImpact,Density=1.225,Speed=60,Mass=2250,Fuel=900,Oxidizer=1100,Size=2.6,Velocity=new Vec3(8,-14,3),Seed=202},
        new Scenario{Name="tank-crash-d",Kind=EventKind.GroundImpact,Density=1.225,Speed=60,Mass=2250,Fuel=900,Oxidizer=1100,Size=2.6,Velocity=new Vec3(8,-14,3),Seed=303},
        new Scenario{Name="duna-crash",Kind=EventKind.GroundImpact,Density=0.068,Speed=90,Mass=4000,Fuel=600,Oxidizer=750,Size=3,Velocity=new Vec3(25,-85,0),Seed=47,Body="Duna"},
        new Scenario{Name="water-impact",Kind=EventKind.WaterImpact,Density=1.225,Speed=120,Mass=20000,Fuel=2000,Oxidizer=2500,Size=5,Water=true},
    };
    internal sealed class Bench : IDisposable
    {
        public Scenario S; public FxPlan Plan; public bool Ground, Water;
        public float Box, Dome;
        public bool NoVolume;   // re-entry breakup: no fireball volume, as in flight
        public Camera Camera; public RenderTexture Target;
        public BlastLayout Layout;
        public GameObject Volume, Refraction; public Material VolumeMaterial, FlareMaterial, DistortMaterial;
        public Light Sun, Flash;
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        public T Own<T>(T o) where T:UnityEngine.Object { owned.Add(o); return o; }
        public void Dispose()
        {
            RenderTexture.active=null;
            if(Camera!=null) Camera.targetTexture=null;
            if(Target!=null) Target.Release();
            for(int i=owned.Count-1;i>=0;i--) if(owned[i]!=null) UnityEngine.Object.DestroyImmediate(owned[i]);
        }
    }
    internal static Shader Load(string name,bool required)
    {
        Shader shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/"+name+".shader");
        if(shader!=null&&shader.isSupported) return shader;
        if(required) throw new Exception(name+" unavailable on "+SystemInfo.graphicsDeviceType);
        return null;
    }
    public static void Render()
    {
        try
        {
            string output=Environment.GetEnvironmentVariable("VEFX_PREVIEW_OUTPUT");
            if(string.IsNullOrEmpty(output)) throw new Exception("Preview output directory required.");
            Directory.CreateDirectory(output);
            string frame=Environment.GetEnvironmentVariable("VEFX_PREVIEW_FRAME");
            if(!string.IsNullOrEmpty(frame))
            {
                var inv=System.Globalization.CultureInfo.InvariantCulture;
                string[] parts=frame.Split(';'); string[] size=parts[0].Split('x');
                Width=int.Parse(size[0],inv); Height=int.Parse(size[1],inv);
                Ages=Array.ConvertAll(parts[1].Split(','),x=>double.Parse(x,inv));
                Columns=Ages.Length; if(parts.Length>2) Distance=float.Parse(parts[2],inv);
                if(parts.Length>3) Elevation=float.Parse(parts[3],inv);
            }
            DumpNoise(output);
            string only=Environment.GetEnvironmentVariable("VEFX_PREVIEW_ONLY");
            foreach(var s in Scenarios) if(string.IsNullOrEmpty(only)||only.Contains(s.Name)) RenderScenario(s,output);
            Debug.Log("[VEFX] Preview rendered: "+output);
            EditorApplication.Exit(0);
        }
        catch(Exception ex) { Debug.LogError("[VEFX] "+ex); EditorApplication.Exit(1); }
    }
    public static void Perf()
    {
        try
        {
            string output=Environment.GetEnvironmentVariable("VEFX_PREVIEW_OUTPUT");
            if(string.IsNullOrEmpty(output)) throw new Exception("Preview output directory required.");
            Directory.CreateDirectory(output);
            var inv=System.Globalization.CultureInfo.InvariantCulture;
            Width=2560; Height=1440;
            var report=new System.Text.StringBuilder();
            report.AppendLine("GPU "+SystemInfo.graphicsDeviceName+" | "+SystemInfo.graphicsDeviceType+" | "+Width+"x"+Height);
            var cases=new[]{
                new {Name="tank-crash-ksc",Age=0.4,Distance=0.18f},
                new {Name="tank-crash-ksc",Age=0.4,Distance=0.45f},
                new {Name="tank-crash-ksc",Age=1.6,Distance=0.3f},
                new {Name="heavy-stage-crash",Age=1.0,Distance=0.25f},
                new {Name="water-impact",Age=0.6,Distance=0.3f},
                new {Name="grazing-crash",Age=0.6,Distance=0.3f},
                new {Name="rocket-chain",Age=0.8,Distance=0.3f},
            };
            foreach(var c in cases)
            {
                Scenario s=Array.Find(Scenarios,x=>x.Name==c.Name);
                using(Bench b=Build(s,c.Distance))
                {
                    Apply(b,c.Age);
                    b.Volume.SetActive(false); if(b.Refraction!=null) b.Refraction.SetActive(false);
                    double scene=Measure(b,8,30);
                    Apply(b,c.Age);
                    double total=Measure(b,8,30);
                    report.AppendLine(string.Format(inv,"{0,-18} age {1,4:F1} s  camera x{2,4:F2}  scene {3,6:F2} ms  with effect {4,6:F2} ms  effect {5,6:F2} ms",
                        c.Name,c.Age,c.Distance,scene,total,total-scene));
                    File.WriteAllBytes(Path.Combine(output,string.Format(inv,"perf-{0}-{1:F1}-{2:F2}.png",c.Name,c.Age,c.Distance)),Capture(b,640,360).EncodeToPNG());
                }
            }
            File.WriteAllText(Path.Combine(output,"perf.txt"),report.ToString());
            Debug.Log("[VEFX] Perf\n"+report);
            EditorApplication.Exit(0);
        }
        catch(Exception ex) { Debug.LogError("[VEFX] "+ex); EditorApplication.Exit(1); }
    }
    static double Measure(Bench b,int warmup,int frames)
    {
        var probe=new Texture2D(1,1,TextureFormat.RGBA32,false);
        var times=new List<double>();
        var watch=new System.Diagnostics.Stopwatch();
        try
        {
            for(int i=0;i<warmup+frames;i++)
            {
                watch.Restart();
                b.Camera.Render(); RenderTexture.active=b.Target;
                probe.ReadPixels(new Rect(0,0,1,1),0,0,false);
                if(i>=warmup) times.Add(watch.Elapsed.TotalMilliseconds);
            }
        }
        finally { RenderTexture.active=null; UnityEngine.Object.DestroyImmediate(probe); }
        times.Sort(); return times[times.Count/2];
    }
    internal static Texture2D Capture(Bench b,int w,int h)
    {
        var small=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);
        var image=new Texture2D(w,h,TextureFormat.RGB24,false);
        try
        {
            b.Camera.targetTexture=small; b.Camera.Render(); RenderTexture.active=small;
            image.ReadPixels(new Rect(0,0,w,h),0,0); image.Apply();
        }
        finally { RenderTexture.active=null; b.Camera.targetTexture=b.Target; small.Release(); UnityEngine.Object.DestroyImmediate(small); }
        return image;
    }
    static void DumpNoise(string output)
    {
        Texture3D noise=NoiseTexture.Ensure();
        int n=noise.width; Color32[] px=noise.GetPixels32();
        var sheet=new Texture2D(n*4,n,TextureFormat.RGB24,false);
        for(int y=0;y<n;y++) for(int x=0;x<n;x++)
        {
            Color32 c=px[(7*n+y)*n+x];
            sheet.SetPixel(x,y,new Color32(c.r,c.r,c.r,255)); sheet.SetPixel(n+x,y,new Color32(c.g,c.g,c.g,255));
            sheet.SetPixel(2*n+x,y,new Color32(c.b,c.b,c.b,255)); sheet.SetPixel(3*n+x,y,new Color32(c.a,c.a,c.a,255));
        }
        sheet.Apply(); File.WriteAllBytes(Path.Combine(output,"noise-channels.png"),sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
    }
    internal static FxPlan Plan(Scenario s,out bool ground,out bool water)
    {
        var resources=new List<ResourceSnapshot>();
        if(s.Fuel>0) resources.Add(new ResourceSnapshot("LiquidFuel",s.Fuel/5,s.Fuel/5,s.Fuel));
        if(s.Oxidizer>0) resources.Add(new ResourceSnapshot("Oxidizer",s.Oxidizer/5,s.Oxidizer/5,s.Oxidizer));
        if(s.Solid>0) resources.Add(new ResourceSnapshot("SolidFuel",s.Solid/7.5,s.Solid/7.5,s.Solid));
        var env=new EnvironmentContext(s.Moon?"Mun":s.Body,s.Density,s.Density*82.7,s.Gravity,0,true,false,false,new Vec3(0,1,0));
        var part=new PartSnapshot(1,1,Guid.Empty,"preview",new Vec3(),new Vec3(0,-s.Speed,0),new Vec3(0,-s.Speed,0),new Vec3(),
            new Vec3(s.Size*0.5,s.Size,s.Size*0.5),s.Mass,300,new string[0],resources.ToArray(),env);
        var settings=new FxSettings();
        ground=s.Kind==EventKind.GroundImpact; water=s.Kind==EventKind.WaterImpact;
        return VisualEnergy.Calculate(new DestructionEvent(part,s.Kind,0),new ResourceClassifier(),settings);
    }
    internal static Texture2D GroundTexture(Color a,Color b)
    {
        var t=new Texture2D(256,256,TextureFormat.RGB24,true); t.wrapMode=TextureWrapMode.Repeat;
        var data=new Color[256*256];
        for(int y=0;y<256;y++) for(int x=0;x<256;x++)
        {
            float n=Mathf.PerlinNoise(x*0.05f,y*0.11f)*0.6f+Mathf.PerlinNoise(x*0.21f+30,y*0.17f+11)*0.4f;
            bool line=x%128<3||y%128<3;
            data[y*256+x]=line?Color.Lerp(b,Color.white,0.6f):Color.Lerp(a,b,n);
        }
        t.SetPixels(data); t.Apply(true); return t;
    }
    internal static Bench Build(Scenario s,float distance)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var b=new Bench{S=s};
        bool ground,water; b.Plan=Plan(s,out ground,out water); b.Ground=ground; b.Water=water;
        b.Layout=BlastLayout.Create(b.Plan,s.Velocity,ground,water,s.Seed,s.Sources);
        VolumeState first=VolumeEvolution.Evaluate(b.Plan,0,true,true,ground,water,b.Layout);
        b.Box=(float)first.BoxRadius; b.Dome=(float)VolumeEvolution.DomeRadius(b.Plan);
        b.NoVolume=Reentry.Applies(b.Plan,ground||water||s.Kind!=EventKind.Destruction,b.Plan.ImpactSpeed);
        float dome=b.Dome;
        b.Sun=new GameObject("Preview sun").AddComponent<Light>();
        b.Sun.type=LightType.Directional; b.Sun.transform.rotation=Quaternion.Euler(38,-35,0);
        bool dark=s.Vacuum||s.Moon;
        bool high=!dark&&s.Density<0.05;
        b.Sun.color=new Color(1,0.96f,0.9f); b.Sun.intensity=dark?1.3f:1.1f; b.Sun.shadows=LightShadows.None;
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight=dark?new Color(0.04f,0.04f,0.05f):new Color(0.42f,0.47f,0.55f);
        if(!dark) RenderSettings.skybox=b.Own(new Material(Shader.Find("Skybox/Procedural"))); else RenderSettings.skybox=null;
        if(!s.Vacuum)
        {
            var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.localScale=new Vector3(200,1,200);
            plane.transform.position=new Vector3(0,s.Airburst?-dome*6:0,0);
            var planeMaterial=b.Own(new Material(Shader.Find("Standard")));
            planeMaterial.mainTexture=b.Own(s.Water?GroundTexture(new Color(0.07f,0.18f,0.26f),new Color(0.12f,0.26f,0.34f)):
                s.Moon?GroundTexture(new Color(0.36f,0.36f,0.35f),new Color(0.52f,0.52f,0.5f)):
                s.Body=="Duna"?GroundTexture(new Color(0.42f,0.22f,0.14f),new Color(0.6f,0.34f,0.22f)):
                GroundTexture(new Color(0.42f,0.37f,0.26f),new Color(0.62f,0.55f,0.4f)));
            planeMaterial.mainTextureScale=new Vector2(100,100);
            planeMaterial.SetFloat("_Glossiness",s.Water?0.75f:0.05f);
            plane.GetComponent<Renderer>().sharedMaterial=planeMaterial;
        }
        var cameraObject=new GameObject("Preview camera");
        b.Camera=cameraObject.AddComponent<Camera>();
        b.Camera.clearFlags=dark||high?CameraClearFlags.SolidColor:CameraClearFlags.Skybox; b.Camera.backgroundColor=high?new Color(0.02f,0.035f,0.09f):Color.black;
        b.Camera.allowHDR=false; b.Camera.allowMSAA=false; b.Camera.fieldOfView=50;
        b.Camera.nearClipPlane=0.3f; b.Camera.farClipPlane=20000; b.Camera.depthTextureMode=DepthTextureMode.Depth;
        Vector3 target=new Vector3(0,ground||water?dome*0.35f:0,0);
        b.Camera.transform.position=target+new Vector3(-dome*2.6f,dome*(ground||water?0.55f:0.2f),-dome*3.4f)*distance;
        if(Elevation>=0)
        {
            float reach=dome*4.3f*distance, e=Elevation*Mathf.Deg2Rad;
            b.Camera.transform.position=target+new Vector3(-0.61f*Mathf.Cos(e),Mathf.Sin(e),-0.79f*Mathf.Cos(e))*reach;
        }
        if(b.Camera.transform.position.y<1.5f&&!s.Vacuum&&!s.Airburst) b.Camera.transform.position=new Vector3(b.Camera.transform.position.x,1.5f,b.Camera.transform.position.z);
        b.Camera.transform.LookAt(target);
        b.Target=b.Own(new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32)); b.Camera.targetTexture=b.Target;
        b.Volume=GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(b.Volume.GetComponent<Collider>());
        Mesh cube=b.Own(UnityEngine.Object.Instantiate(b.Volume.GetComponent<MeshFilter>().sharedMesh));
        Vector3[] vertices=cube.vertices; for(int i=0;i<vertices.Length;i++) vertices[i]*=2;
        cube.vertices=vertices; cube.RecalculateBounds(); b.Volume.GetComponent<MeshFilter>().sharedMesh=cube;
        b.VolumeMaterial=b.Own(new Material(Load("HeroVolume",true))); b.Volume.GetComponent<Renderer>().sharedMaterial=b.VolumeMaterial;
        b.VolumeMaterial.SetTexture("_NoiseTex",NoiseTexture.Ensure());
        b.VolumeMaterial.SetTexture("_BlueNoise",BlueNoise.Ensure());
        b.Volume.transform.localScale=Vector3.one*b.Box;
        var flare=new GameObject("Preview flare"); flare.transform.SetParent(b.Volume.transform,false);
        Mesh quad=b.Own(new Mesh());
        quad.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
        quad.triangles=new[]{0,1,2,0,2,3}; quad.bounds=new Bounds(Vector3.zero,Vector3.one*8);
        flare.AddComponent<MeshFilter>().sharedMesh=quad;
        b.FlareMaterial=b.Own(new Material(Load("HeroFlare",true))); flare.AddComponent<MeshRenderer>().sharedMaterial=b.FlareMaterial;
        if(Environment.GetEnvironmentVariable("VEFX_DEBUG_NOFLARE")!=null) flare.SetActive(false);
        if(!s.Vacuum&&!s.Airburst&&Environment.GetEnvironmentVariable("VEFX_DEBUG_NOPILLARS")==null) for(int i=-3;i<=3;i++)
        {
            var pillar=GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.Object.DestroyImmediate(pillar.GetComponent<Collider>());
            pillar.transform.position=new Vector3(i*dome*0.9f+dome*1.2f,dome*0.6f,dome*2.4f);
            pillar.transform.localScale=new Vector3(dome*0.18f,dome*1.2f,dome*0.18f);
        }
        Shader distortShader=Load("HeatDistortion",false);
        if(distortShader!=null)
        {
            b.Refraction=new GameObject("Preview refraction"); b.Refraction.transform.localScale=Vector3.one*b.Box;
            b.Refraction.AddComponent<MeshFilter>().sharedMesh=quad;
            b.DistortMaterial=b.Own(new Material(distortShader)); b.Refraction.AddComponent<MeshRenderer>().sharedMaterial=b.DistortMaterial;
        }
        b.Flash=new GameObject("Preview flash").AddComponent<Light>(); b.Flash.type=LightType.Point; b.Flash.shadows=LightShadows.None;
        b.Flash.color=new Color(1,0.75f,0.45f);
        b.Flash.range=Mathf.Clamp((float)Math.Max(b.Layout.RadiusMeters*2.5,b.Plan.Radius*5),10,120);
        return b;
    }
    internal static void Apply(Bench b,double age)
    {
        Vector3 light=-b.Sun.transform.forward;
        VolumeState state=VolumeEvolution.Evaluate(b.Plan,age,true,true,b.Ground,b.Water,b.Layout);
        Material m=b.VolumeMaterial;
        ApplyState(m,state,b.S.Seed);
        m.SetVector("_LightDirection",new Vector4(light.x,light.y,light.z,RenderSettings.ambientLight.grayscale));
        m.SetColor("_SunColor",new Color(b.Sun.color.r,b.Sun.color.g,b.Sun.color.b,b.Sun.intensity));
        m.SetFloat("_Steps",32);
        b.FlareMaterial.SetVector("_Flare",V(VolumeEvolution.Flare(state)));
        b.Volume.SetActive(!b.NoVolume&&state.Visible&&Environment.GetEnvironmentVariable("VEFX_DEBUG_NOVOLUME")==null);
        if(b.Refraction!=null)
        {
            Vec4 shock,haze; VolumeEvolution.Distortion(state,0,out shock,out haze);
            b.DistortMaterial.SetVector("_Shock",V(shock)); b.DistortMaterial.SetVector("_Haze",V(haze)); b.DistortMaterial.SetFloat("_Seed",37);
            b.Refraction.SetActive(!b.NoVolume&&VolumeEvolution.DistortionVisible(state,0)&&Environment.GetEnvironmentVariable("VEFX_PREVIEW_NO_REFRACTION")==null);
        }
        b.Flash.transform.position=new Vector3(0,(float)(VolumeEvolution.LightHeight(state)*b.Box),0);
        b.Flash.intensity=(float)VolumeEvolution.LightIntensity(state);
        b.Flash.enabled=!b.NoVolume&&b.Flash.intensity>0.01f;
    }
    static void RenderScenario(Scenario s,string output)
    {
        using(Bench b=Build(s,Distance))
        {
            var sheet=b.Own(new Texture2D(Width*Columns,Height*((Ages.Length+Columns-1)/Columns),TextureFormat.RGB24,false));
            var frame=b.Own(new Texture2D(Width,Height,TextureFormat.RGB24,false));
            for(int f=0;f<Ages.Length;f++)
            {
                Apply(b,Ages[f]);
                b.Camera.Render(); RenderTexture.active=b.Target;
                frame.ReadPixels(new Rect(0,0,Width,Height),0,0); frame.Apply();
                int col=f%Columns;
                sheet.SetPixels(col*Width,((sheet.height/Height)-1-f/Columns)*Height,Width,Height,frame.GetPixels());
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(output,"preview-"+s.Name+".png"),sheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(output,"preview-"+s.Name+".txt"),string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "dome radius {0:F1} m, box {1:F1} m, plan radius {2:F2} m, score {3:F2}, fire {4:F2}, ages {5}\n",
                b.Dome,b.Box,b.Plan.Radius,b.Plan.VisualEnergyScore,b.Plan.FireFraction,string.Join(" ",Array.ConvertAll(Ages,a=>a.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        }
    }
    internal static Vector4 V(Vec4 v) { return new Vector4((float)v.X,(float)v.Y,(float)v.Z,(float)v.W); }
    static readonly string[] LobeNames={"0","1","2","3","4"};
    public static void ApplyState(Material m,VolumeState state,double seed)
    {
        Vec4 shell,core,cloud,context,fireball,smoke;
        VolumeEvolution.Pack(state,seed,out shell,out core,out cloud,out context,out fireball,out smoke);
        m.SetVector("_Fire",V(fireball)); m.SetVector("_Smoke",V(smoke));
        m.SetVector("_Shell",V(shell)); m.SetVector("_Core",V(core));
        m.SetVector("_Cloud",V(cloud)); m.SetVector("_Context",V(context));
        m.SetVector("_Bounds",V(VolumeEvolution.Bounds(state)));
        m.SetVector("_Motion",V(VolumeEvolution.Motion(state)));
        m.SetVector("_Dust",V(VolumeEvolution.Dust(state)));
        for(int i=0;i<5;i++)
        {
            Vec4 c,a,d; VolumeEvolution.PackLobe(state,i,out c,out a,out d);
            m.SetVector("_L"+LobeNames[i],V(c)); m.SetVector("_A"+LobeNames[i],V(a)); m.SetVector("_D"+LobeNames[i],V(d));
        }
    }
}
