using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VolumetricExplosionFX.Core;
using VolumetricExplosionFX.Rendering;

public static class PreviewVolumes
{
    internal static int Width=480, Height=270, Columns=5;
    internal static double[] Ages={0.03,0.1,0.2,0.35,0.6,0.9,1.4,2.0,3.0,4.5};
    internal static float Distance=1, Elevation=-1;   // Elevation: camera pitch in degrees (-1 = default view)
    internal sealed class Scenario
    {
        public string Name; public EventKind Kind; public double Density, Speed, Mass, Fuel, Oxidizer, Size, Solid;
        public string Extra;   // other resources, "Name=kg;Name=kg"
        public WarheadKind Warhead; public double Tnt, Range;   // a combat mod's warhead (BDArmory), drawn as VEFX does in the game
        public double Depth;   // under water (m)
        public double BurstHeight;   // a nuclear air burst's height above the ground (m)
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
        new Scenario{Name="mono-tank",Kind=EventKind.GroundImpact,Density=1.225,Speed=50,Mass=1200,Size=1.6,Velocity=new Vec3(5,-50,2),Seed=61,Extra="MonoPropellant=750"},
        new Scenario{Name="hydrogen-stage",Kind=EventKind.GroundImpact,Density=1.225,Speed=60,Mass=9000,Size=4,Velocity=new Vec3(8,-60,3),Seed=67,Extra="LqdHydrogen=700;LqdOxygen=4200"},
        new Scenario{Name="hypergolic-stage",Kind=EventKind.GroundImpact,Density=1.225,Speed=60,Mass=9000,Size=4,Velocity=new Vec3(8,-60,3),Seed=71,Extra="UDMH=2500;NTO=4500"},
        new Scenario{Name="ore-crash",Kind=EventKind.GroundImpact,Density=1.225,Speed=90,Mass=8000,Size=3.5,Velocity=new Vec3(20,-85,0),Seed=73,Extra="Ore=6000"},
        new Scenario{Name="missile-ground",Kind=EventKind.GroundImpact,Density=1.225,Speed=300,Mass=60,Size=0.6,Velocity=new Vec3(200,-150,0),Seed=91,Warhead=WarheadKind.HighExplosive,Tnt=20,Range=30},
        new Scenario{Name="bomb-ground",Kind=EventKind.GroundImpact,Density=1.225,Speed=200,Mass=400,Size=1,Velocity=new Vec3(40,-190,0),Seed=93,Warhead=WarheadKind.Bomb,Tnt=250,Range=70},
        new Scenario{Name="rod-air",Kind=EventKind.Destruction,Density=0.8,Speed=600,Mass=20,Size=0.4,Velocity=new Vec3(75,8,0),Seed=95,Airburst=true,Warhead=WarheadKind.ContinuousRod,Tnt=6,Range=20},
        new Scenario{Name="thermobaric",Kind=EventKind.GroundImpact,Density=1.225,Speed=150,Mass=300,Size=1,Velocity=new Vec3(30,-140,0),Seed=97,Warhead=WarheadKind.Thermobaric,Tnt=100,Range=60},
        new Scenario{Name="napalm",Kind=EventKind.GroundImpact,Density=1.225,Speed=150,Mass=300,Size=1,Velocity=new Vec3(120,-80,0),Seed=99,Warhead=WarheadKind.Incendiary,Tnt=60,Range=40},
        new Scenario{Name="nuke",Kind=EventKind.GroundImpact,Density=1.225,Speed=100,Mass=1000,Size=2,Velocity=new Vec3(0,-100,0),Seed=101,Warhead=WarheadKind.Nuke,Tnt=20e6,Range=900},
        new Scenario{Name="nuke-air",Kind=EventKind.Destruction,Density=1.225,Speed=100,Mass=1000,Size=2,Velocity=new Vec3(0,-30,0),Seed=111,Warhead=WarheadKind.Nuke,Tnt=20e6,Range=900,Airburst=true,BurstHeight=500},
        new Scenario{Name="nuke-high",Kind=EventKind.Destruction,Density=0.9,Speed=100,Mass=1000,Size=2,Velocity=new Vec3(0,-30,0),Seed=113,Warhead=WarheadKind.Nuke,Tnt=20e6,Range=900,Airburst=true,BurstHeight=2500},
        new Scenario{Name="nuke-mt",Kind=EventKind.GroundImpact,Density=1.225,Speed=100,Mass=1000,Size=2,Velocity=new Vec3(0,-100,0),Seed=117,Warhead=WarheadKind.Nuke,Tnt=1.2e9,Range=900},
        new Scenario{Name="nuke-mun",Kind=EventKind.GroundImpact,Density=0,Speed=100,Mass=1000,Size=2,Velocity=new Vec3(0,-100,0),Moon=true,Seed=119,Warhead=WarheadKind.Nuke,Tnt=20e6,Range=900},
        new Scenario{Name="nuke-sea",Kind=EventKind.WaterImpact,Density=1.225,Speed=100,Mass=1000,Size=2,Water=true,Velocity=new Vec3(0,-100,0),Seed=121,Warhead=WarheadKind.Nuke,Tnt=20e6,Range=900},
        new Scenario{Name="emp-air",Kind=EventKind.Destruction,Density=0.9,Speed=300,Mass=50,Size=0.5,Velocity=new Vec3(30,0,0),Seed=103,Airburst=true,Warhead=WarheadKind.Emp,Tnt=5,Range=40},
        new Scenario{Name="building-collapse",Kind=EventKind.GroundImpact,Density=1.225,Speed=3,Mass=500000,Size=20,Velocity=new Vec3(0,-3,0),Seed=107,Warhead=WarheadKind.Structure,Tnt=22,Range=45},
        new Scenario{Name="fuel-depot",Kind=EventKind.GroundImpact,Density=1.225,Speed=3,Mass=200000,Size=12,Velocity=new Vec3(0,-3,0),Seed=109,Warhead=WarheadKind.Structure,Tnt=14,Range=25,Fuel=20000},
        new Scenario{Name="torpedo",Kind=EventKind.WaterImpact,Density=1.225,Speed=20,Mass=500,Size=0.6,Water=true,Velocity=new Vec3(0,-5,0),Seed=111,Warhead=WarheadKind.HighExplosive,Tnt=200,Range=40,Depth=8},
        new Scenario{Name="fuel-slick",Kind=EventKind.WaterImpact,Density=1.225,Speed=60,Mass=4000,Fuel=1800,Oxidizer=2200,Size=3,Water=true,Velocity=new Vec3(25,-50,0),Seed=113},
        new Scenario{Name="shell-ground",Kind=EventKind.GroundImpact,Density=1.225,Speed=800,Mass=10,Size=0.2,Velocity=new Vec3(100,-60,0),Seed=105,Warhead=WarheadKind.Shell,Tnt=2,Range=8},
        new Scenario{Name="battery-burst",Kind=EventKind.GroundImpact,Density=1.225,Speed=40,Mass=300,Size=1.2,Velocity=new Vec3(3,-40,1),Seed=83,Extra="ElectricCharge=4000"},
        new Scenario{Name="xenon-burst",Kind=EventKind.Destruction,Density=0.9,Speed=100,Mass=900,Size=1.6,Airburst=true,Seed=79,Extra="XenonGas=400"},
        new Scenario{Name="water-impact",Kind=EventKind.WaterImpact,Density=1.225,Speed=120,Mass=20000,Fuel=2000,Oxidizer=2500,Size=5,Water=true},
        // 1.1: more water impacts for the volumetric splash: a plane ditching at a shallow angle, a bomb, an empty stage.
        new Scenario{Name="plane-ditch",Kind=EventKind.WaterImpact,Density=1.225,Speed=95,Mass=12000,Fuel=1500,Oxidizer=0,Size=4,Water=true,Velocity=new Vec3(88,-35,0),Seed=131},
        new Scenario{Name="bomb-sea",Kind=EventKind.WaterImpact,Density=1.225,Speed=220,Mass=400,Size=1,Water=true,Velocity=new Vec3(40,-215,0),Seed=133,Warhead=WarheadKind.Bomb,Tnt=250,Range=70},
        new Scenario{Name="stage-sea",Kind=EventKind.WaterImpact,Density=1.225,Speed=160,Mass=6000,Size=3,Water=true,Velocity=new Vec3(10,-158,4),Seed=137},
    };
    internal sealed class Bench : IDisposable
    {
        public Scenario S; public FxPlan Plan; public bool Ground, Water;
        public float Box, Dome;
        public bool NoVolume;   // re-entry breakup: no fireball volume, as in flight
        public Camera Camera; public RenderTexture Target;
        public BlastLayout Layout;
        public GameObject Volume, Refraction; public Material VolumeMaterial, FlareMaterial, DistortMaterial, NukeMaterial;
        public GameObject Glare; public Material GlareMaterial;
        public GameObject Splash; public Material SplashMaterial; public MaterialPropertyBlock SplashBlock;   // 1.1: a water splash's volume // 1.1: a nuclear fireball's glare, anchored in the world
        public Light Sun, Flash, Lamp;
        public LowResVolumes LowRes; public GameObject Deck;
        public GameObject NukeShadow; public Material NukeShadowMaterial;   // the nuclear cloud's shadow on the (flat) ground
        public System.Collections.Generic.List<Renderer> Plumes=new System.Collections.Generic.List<Renderer>();   // VEFX_BENCH_PLUME
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        public T Own<T>(T o) where T:UnityEngine.Object { owned.Add(o); return o; }
        public void Dispose()
        {
            RenderTexture.active=null;
            if(LowRes!=null) { LowRes.Dispose(); LowRes=null; }
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
                new {Name="tank-crash-ksc",Age=1.6,Distance=1.5f},
                new {Name="heavy-stage-crash",Age=1.6,Distance=2.5f},
                new {Name="tank-crash-ksc",Age=5.0,Distance=1.5f},
                new {Name="nuke",Age=1.5,Distance=0.7f},
                new {Name="nuke",Age=20.0,Distance=1.6f},
                new {Name="nuke-mt",Age=3.0,Distance=1.0f},
                new {Name="nuke-mt",Age=20.0,Distance=1.6f},
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
                Prepare(b); b.Camera.Render(); RenderTexture.active=b.Target;
                probe.ReadPixels(new Rect(0,0,1,1),0,0,false);
                if(i>=warmup) times.Add(watch.Elapsed.TotalMilliseconds);
            }
        }
        finally { RenderTexture.active=null; UnityEngine.Object.DestroyImmediate(probe); }
        times.Sort(); return times[times.Count/2];
    }
    // VEFX_PREVIEW_HALF=2 or 3: the runtime's reduced-resolution path (command buffer + composite).
    internal static void Prepare(Bench b)
    {
        int divisor;
        if(!int.TryParse(Environment.GetEnvironmentVariable("VEFX_PREVIEW_HALF"),out divisor)||divisor<1||b.Volume==null) return;
        if(b.LowRes==null) { b.LowRes=new LowResVolumes(b.Own(new Material(Load("Composite",true)))); Shader probe=Load("CloudProbe",false); if(probe!=null) b.LowRes.ProbeMaterial=b.Own(new Material(probe)); }
        b.Volume.GetComponent<Renderer>().enabled=false;
        bool show=b.Volume.activeSelf;
        b.LowRes.Overlays=Environment.GetEnvironmentVariable("VEFX_BENCH_NOOVERLAY")!=null?null:b.Plumes;   // (VEFX_BENCH_NOOVERLAY: the flames are not drawn again over the clouds)
        // VEFX_BENCH_CLOUDDECK=altitude (m): a white, see-through cloud deck drawn in the transparent pass, as EVE's
        // are; the volumes must be hidden where it lies between the eye and them (flat world: the planet's centre far below).
        float deck;
        if(float.TryParse(Environment.GetEnvironmentVariable("VEFX_BENCH_CLOUDDECK"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out deck))
        {
            if(b.Deck==null)
            {
                b.Deck=b.Own(GameObject.CreatePrimitive(PrimitiveType.Quad)); UnityEngine.Object.DestroyImmediate(b.Deck.GetComponent<Collider>());
                var mat=b.Own(new Material(Shader.Find("Sprites/Default"))); mat.color=new Color(0.95f,0.95f,0.97f,0.85f); int dq; mat.renderQueue=int.TryParse(Environment.GetEnvironmentVariable("VEFX_BENCH_DECKQUEUE"),out dq)?dq:4000;   // (3000: like the haze of the transparent pass, which must not hide anything)
                b.Deck.GetComponent<Renderer>().sharedMaterial=mat;
                b.Deck.transform.rotation=Quaternion.Euler(90,0,0); b.Deck.transform.localScale=new Vector3(60000,60000,1);
            }
            b.Deck.transform.position=new Vector3(0,deck,0);
            b.LowRes.CloudOcclusion=true; b.LowRes.CloudBands=new Vector4(deck-150,deck+150,0,0); b.LowRes.Planet=new Vector4(0,-6e5f,0,6e5f);
            b.LowRes.ShowProbe(b.Camera,true);
        }
        b.LowRes.Begin(b.Camera,divisor);
        if(show) b.LowRes.Add(b.Volume.GetComponent<MeshFilter>().sharedMesh,b.Volume.transform.localToWorldMatrix,b.Volume.GetComponent<Renderer>().sharedMaterial,null);
        if(b.Splash!=null)
        {
            b.Splash.GetComponent<Renderer>().enabled=false;
            if(b.Splash.activeSelf) b.LowRes.Add(b.Splash.GetComponent<MeshFilter>().sharedMesh,b.Splash.transform.localToWorldMatrix,b.SplashMaterial,b.SplashBlock);
        }
        b.LowRes.End();
    }
    internal static Texture2D Capture(Bench b,int w,int h)
    {
        var small=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);
        var image=new Texture2D(w,h,TextureFormat.RGB24,false);
        try
        {
            b.Camera.targetTexture=small; Prepare(b); b.Camera.Render(); RenderTexture.active=small;
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
        if(!string.IsNullOrEmpty(s.Extra)) foreach(string item in s.Extra.Split(';'))
        {
            string[] kv=item.Split('='); double kg=double.Parse(kv[1],System.Globalization.CultureInfo.InvariantCulture);
            resources.Add(new ResourceSnapshot(kv[0],kg,kg,kg));
        }
        var env=new EnvironmentContext(s.Moon?"Mun":s.Body,s.Density,s.Density*82.7,s.Gravity,-s.Depth,true,false,s.Depth>0,new Vec3(0,1,0));
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
        VolumeEvolution.UseSim=Environment.GetEnvironmentVariable("VEFX_PREVIEW_NOSIM")!="1";
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var b=new Bench{S=s};
        bool ground,water; b.Plan=Plan(s,out ground,out water); b.Ground=ground; b.Water=water;
        b.Plan.Look=Look(b.Plan,ground||water||s.Kind!=EventKind.Destruction,(s.Sources!=null?s.Sources.Length:0)+1);
        // VEFX_BENCH_TNT=kg: the same warhead with another charge (a nuke's yield x 1e6), to compare sizes.
        double benchTnt; if(!double.TryParse(Environment.GetEnvironmentVariable("VEFX_BENCH_TNT"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out benchTnt)) benchTnt=s.Tnt;
        if(s.Warhead==WarheadKind.Structure) Warhead.Collapse(b.Plan,s.Range,s.Tnt,s.Fuel);   // building: Range = width, Tnt = height (m)
        else if(s.Warhead!=WarheadKind.None) Warhead.Apply(b.Plan,new WarheadInfo{ Kind=s.Warhead, TntKg=benchTnt, RangeM=s.Range });
        b.Plan.BurstHeight=s.BurstHeight;
        Chemistry.Apply(b.Plan);
        // VEFX_PREVIEW_SALT: the same scenario as another explosion (as the game draws a fresh seed for each one).
        uint salt; if(!uint.TryParse(Environment.GetEnvironmentVariable("VEFX_PREVIEW_SALT"),out salt)) salt=0;
        if(salt!=0) Chemistry.Vary(b.Plan,salt);
        b.Layout=BlastLayout.Create(b.Plan,s.Velocity,ground,water,s.Seed^salt,s.Sources);
        if(Environment.GetEnvironmentVariable("VEFX_BENCH_HUMID")!=null) { b.Layout.Humidity=1; b.Layout.NukeHumid=b.Layout.NukeWilson=b.Layout.Nuke!=NukeKind.None; }   // condensation cloud
        if(Environment.GetEnvironmentVariable("VEFX_BENCH_DRY")!=null) { b.Layout.Humidity=0; b.Layout.NukeHumid=b.Layout.NukeWilson=false; }
        // VEFX_BENCH_CHARACTER=Fireball|Jet|Chain|Flash: a tank of that fill, with the first seed that lets go that way.
        string want=Environment.GetEnvironmentVariable("VEFX_BENCH_CHARACTER"); TankCharacter wanted;
        if(!string.IsNullOrEmpty(want)&&Enum.TryParse(want,out wanted))
        {
            b.Plan.Fill=wanted==TankCharacter.Flash?0.05:wanted==TankCharacter.Jet?0.45:1;
            if(wanted==TankCharacter.Chain) b.Plan.BurnableKg=Math.Max(b.Plan.BurnableKg,5000);
            for(uint k=1;k<400&&b.Layout.Character!=wanted;k++) b.Layout=BlastLayout.Create(b.Plan,s.Velocity,ground,water,s.Seed^salt^(k*2654435761u),s.Sources);
            Debug.Log("[VEFX bench] tank character "+b.Layout.Character);
        }
        // VEFX_BENCH_RUN=n: force the baked fireball shape (ground 0-3, air 0-1) to compare them side by side.
        int run; if(int.TryParse(Environment.GetEnvironmentVariable("VEFX_BENCH_RUN"),out run)) { b.Layout.SimRun=run; b.Layout.SimAirRun=run; }
        Debug.Log("[VEFX bench] fireball shape: ground run "+b.Layout.SimRun+", air run "+b.Layout.SimAirRun);
        VolumeState first=VolumeEvolution.Evaluate(b.Plan,0,true,true,ground,water,b.Layout);
        b.Box=(float)first.BoxRadius; b.Dome=(float)VolumeEvolution.DomeRadius(b.Plan);
        b.NoVolume=Reentry.Applies(b.Plan,ground||water||s.Kind!=EventKind.Destruction,b.Plan.ImpactSpeed);
        float dome=b.Dome;
        // VEFX_BENCH_SCALE=metres: pillars and camera at a fixed size instead of following the blast (compares sizes).
        float unit=dome; float fixedUnit; if(float.TryParse(Environment.GetEnvironmentVariable("VEFX_BENCH_SCALE"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out fixedUnit)&&fixedUnit>0) unit=fixedUnit;
        b.Sun=new GameObject("Preview sun").AddComponent<Light>();
        b.Sun.type=LightType.Directional; b.Sun.transform.rotation=Quaternion.Euler(38,-35,0);
        bool dark=s.Vacuum||s.Moon;
        bool high=!dark&&s.Density<0.05;
        b.Sun.color=new Color(1,0.96f,0.9f); b.Sun.intensity=dark?1.3f:1.1f; b.Sun.shadows=LightShadows.None;
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight=dark?new Color(0.04f,0.04f,0.05f):new Color(0.42f,0.47f,0.55f);
        string dusk=Environment.GetEnvironmentVariable("VEFX_PREVIEW_DUSK");
        bool night=dusk=="2";
        if(dusk=="1"||night)
        {
            // Dusk: shows how much the explosion lights its surroundings.
            b.Sun.intensity=0.12f; b.Sun.color=new Color(1,0.7f,0.5f); b.Sun.transform.rotation=Quaternion.Euler(4,-35,0);
            RenderSettings.ambientLight=dark?new Color(0.01f,0.01f,0.015f):new Color(0.06f,0.07f,0.1f);
            // Night: no sun at all, only a faint sky; fires and lamps are the light.
            if(night) { b.Sun.intensity=0; RenderSettings.ambientLight=new Color(0.012f,0.014f,0.022f); }
        }
        if(Environment.GetEnvironmentVariable("VEFX_PREVIEW_LAMP")=="1")
        {
            // A floodlight on the ground beside the blast, aimed at the smoke column.
            b.Lamp=new GameObject("Preview floodlight").AddComponent<Light>();
            b.Lamp.type=LightType.Spot; b.Lamp.spotAngle=45; b.Lamp.range=dome*10; b.Lamp.intensity=3;
            b.Lamp.color=new Color(1,0.95f,0.85f); b.Lamp.shadows=LightShadows.None;
            b.Lamp.transform.position=new Vector3(dome*2.4f,dome*0.1f,-dome*1.4f);
            b.Lamp.transform.LookAt(new Vector3(0,dome*1.3f,0));
        }
        if(!dark) RenderSettings.skybox=b.Own(new Material(Shader.Find("Skybox/Procedural"))); else RenderSettings.skybox=null;
        if(!s.Vacuum)
        {
            var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);
            float floorScale=s.Warhead==WarheadKind.Nuke?Mathf.Max(200,unit*8):200;   // a nuclear cloud needs kilometres of ground under it
            plane.transform.localScale=new Vector3(floorScale,1,floorScale);
            plane.transform.position=new Vector3(0,s.BurstHeight>0?-(float)s.BurstHeight:s.Airburst?-dome*6:0,0);
            var planeMaterial=b.Own(new Material(Shader.Find("Standard")));
            planeMaterial.mainTexture=b.Own(s.Water?GroundTexture(new Color(0.07f,0.18f,0.26f),new Color(0.12f,0.26f,0.34f)):
                s.Moon?GroundTexture(new Color(0.36f,0.36f,0.35f),new Color(0.52f,0.52f,0.5f)):
                s.Body=="Duna"?GroundTexture(new Color(0.42f,0.22f,0.14f),new Color(0.6f,0.34f,0.22f)):
                GroundTexture(new Color(0.42f,0.37f,0.26f),new Color(0.62f,0.55f,0.4f)));
            planeMaterial.mainTextureScale=new Vector2(floorScale*0.5f,floorScale*0.5f);
            planeMaterial.SetFloat("_Glossiness",s.Water?0.75f:0.05f);
            plane.GetComponent<Renderer>().sharedMaterial=planeMaterial;
        }
        var cameraObject=new GameObject("Preview camera");
        b.Camera=cameraObject.AddComponent<Camera>();
        b.Camera.clearFlags=dark||high||night?CameraClearFlags.SolidColor:CameraClearFlags.Skybox; b.Camera.backgroundColor=high?new Color(0.02f,0.035f,0.09f):night?new Color(0.008f,0.01f,0.02f):Color.black;
        b.Camera.allowHDR=false; b.Camera.allowMSAA=false; b.Camera.fieldOfView=50;
        b.Camera.nearClipPlane=0.3f; b.Camera.farClipPlane=20000; b.Camera.depthTextureMode=DepthTextureMode.Depth;
        Vector3 target=new Vector3(0,ground||water?unit*0.35f:0,0);
        // VEFX_BENCH_TARGET=k: aim the camera at k dome radii above the blast (the rising cloud) instead of the fireball.
        float aim; if(float.TryParse(Environment.GetEnvironmentVariable("VEFX_BENCH_TARGET"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out aim)) target=new Vector3(0,unit*aim,0);
        b.Camera.transform.position=target+new Vector3(-unit*2.6f,unit*(ground||water?0.55f:0.2f),-unit*3.4f)*distance;
        if(Elevation>=0)
        {
            float reach=unit*4.3f*distance, e=Elevation*Mathf.Deg2Rad;
            b.Camera.transform.position=target+new Vector3(-0.61f*Mathf.Cos(e),Mathf.Sin(e),-0.79f*Mathf.Cos(e))*reach;
        }
        // VEFX_BENCH_YAW=degrees: turn the camera around the blast (180 = looking toward the sun, backlit smoke).
        float yaw; if(float.TryParse(Environment.GetEnvironmentVariable("VEFX_BENCH_YAW"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out yaw))
            b.Camera.transform.position=target+Quaternion.Euler(0,yaw,0)*(b.Camera.transform.position-target);
        if(b.Camera.transform.position.y<1.5f&&!s.Vacuum&&!s.Airburst) b.Camera.transform.position=new Vector3(b.Camera.transform.position.x,1.5f,b.Camera.transform.position.z);
        b.Camera.transform.LookAt(target);
        b.Target=b.Own(new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32)); b.Camera.targetTexture=b.Target;
        // VEFX_BENCH_PLUME=1: two transparent "engine plumes" (sprite quads), one between the camera and the blast, one
        // behind it: the one in front must stay visible over the cloud, the one behind must be hidden by it.
        // VEFX_BENCH_DEFERRED=1: the camera renders deferred, as KSP does with the Deferred mod.
        if(Environment.GetEnvironmentVariable("VEFX_BENCH_DEFERRED")!=null) b.Camera.renderingPath=RenderingPath.DeferredShading;
        if(Environment.GetEnvironmentVariable("VEFX_BENCH_PLUME")!=null)
        {
            var plumeMat=b.Own(new Material(Shader.Find("Legacy Shaders/Particles/Additive"))); plumeMat.SetColor("_TintColor",new Color(0.25f,0.5f,1f,0.8f));   // additive, like Waterfall flames
            Vector3 toCam=(b.Camera.transform.position-target).normalized;
            foreach(float k in new[]{0.55f,-1.4f})
            {
                var q=GameObject.CreatePrimitive(PrimitiveType.Quad); UnityEngine.Object.DestroyImmediate(q.GetComponent<Collider>());
                q.GetComponent<Renderer>().sharedMaterial=plumeMat;
                q.transform.position=target+toCam*unit*(k>0?k*Vector3.Distance(b.Camera.transform.position,target)/unit:k)+Vector3.up*unit*0.1f+(k>0?Vector3.zero:Vector3.Cross(Vector3.up,toCam).normalized*unit*0.3f);
                q.transform.rotation=Quaternion.LookRotation(-toCam); q.transform.localScale=new Vector3(unit*0.25f,unit*1.2f,1)*(k>0?0.5f:1f);
                b.Own(q); b.Plumes.Add(q.GetComponent<Renderer>());
            }
        }
        b.Volume=GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(b.Volume.GetComponent<Collider>());
        Mesh cube=b.Own(UnityEngine.Object.Instantiate(b.Volume.GetComponent<MeshFilter>().sharedMesh));
        Vector3[] vertices=cube.vertices; for(int i=0;i<vertices.Length;i++) vertices[i]*=2;
        cube.vertices=vertices; cube.RecalculateBounds(); b.Volume.GetComponent<MeshFilter>().sharedMesh=cube;
        b.VolumeMaterial=b.Own(new Material(Load("HeroVolume",true))); b.Volume.GetComponent<Renderer>().sharedMaterial=b.VolumeMaterial;
        b.VolumeMaterial.SetTexture("_NoiseTex",NoiseTexture.Ensure());
        b.VolumeMaterial.SetTexture("_BlueNoise",BlueNoise.Ensure());
        BindFireSim(b.VolumeMaterial);
        // 1.1: a nuclear cloud has its own renderer (NukeVolume); VEFX_PREVIEW_OLDNUKE=1 shows the old one.
        Shader nukeShader=s.Warhead==WarheadKind.Nuke&&Environment.GetEnvironmentVariable("VEFX_PREVIEW_OLDNUKE")==null?Load("NukeVolume",false):null;
        if(nukeShader!=null)
        {
            b.NukeMaterial=b.Own(new Material(nukeShader));
            b.NukeMaterial.SetTexture("_NoiseTex",NoiseTexture.Ensure()); b.NukeMaterial.SetTexture("_BlueNoise",BlueNoise.Ensure());
            b.NukeMaterial.EnableKeyword("VEFX_SIM");   // (as in the game: it writes its front, LowResVolumes keys that on VEFX_SIM)
            Shader shadowShader=Load("NukeShadow",false);
            if(shadowShader!=null&&s.Kind==EventKind.GroundImpact&&s.Density>0.3)
            {
                // The game's screen-space shadow, on a box round the blast (flat ground at y = 0, the blast frame is the world).
                float reach=0.95f*(float)b.Box;
                b.NukeShadow=b.Own(GameObject.CreatePrimitive(PrimitiveType.Cube)); UnityEngine.Object.DestroyImmediate(b.NukeShadow.GetComponent<Collider>());
                b.NukeShadow.transform.position=new Vector3(0,200,0); b.NukeShadow.transform.localScale=new Vector3(2*reach,1400,2*reach);
                b.NukeShadowMaterial=b.Own(new Material(shadowShader)); b.NukeShadowMaterial.SetTexture("_NoiseTex",NoiseTexture.Ensure());
                b.NukeShadowMaterial.SetMatrix("_NkFrame",Matrix4x4.identity); b.NukeShadowMaterial.SetVector("_NkReach",new Vector4(reach,0,0,0));
                b.NukeShadow.GetComponent<Renderer>().sharedMaterial=b.NukeShadowMaterial;
            }
            Shader glareShader=Load("NukeGlare",false);
            if(glareShader!=null)
            {
                b.Glare=b.Own(new GameObject("Preview nuclear glare"));
                Mesh gq=b.Own(new Mesh());
                gq.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
                gq.triangles=new[]{0,1,2,0,2,3}; gq.bounds=new Bounds(Vector3.zero,Vector3.one*2e5f);
                b.Glare.AddComponent<MeshFilter>().sharedMesh=gq;
                b.GlareMaterial=b.Own(new Material(glareShader)); b.Glare.AddComponent<MeshRenderer>().sharedMaterial=b.GlareMaterial;
                b.Glare.SetActive(false);
            }
        }
        // 1.1: a water impact's splash, drawn as the game draws it (SplashVolume); VEFX_DEBUG_NOSPLASH hides it.
        Shader splashShader=water&&WaterSplash.Applies(b.Plan)?Load("SplashVolume",false):null;
        if(splashShader!=null)
        {
            b.Splash=b.Own(new GameObject("Preview water splash"));
            b.Splash.AddComponent<MeshFilter>().sharedMesh=cube;
            b.SplashMaterial=b.Own(new Material(splashShader)); b.SplashMaterial.SetTexture("_NoiseTex",NoiseTexture.Ensure()); b.SplashMaterial.SetTexture("_BlueNoise",BlueNoise.Ensure());
            b.SplashMaterial.EnableKeyword("VEFX_SIM");   // (it writes its front, as in the game)
            b.Splash.AddComponent<MeshRenderer>().sharedMaterial=b.SplashMaterial;
            b.SplashBlock=new MaterialPropertyBlock();
        }
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
            pillar.transform.position=new Vector3(i*unit*0.9f+unit*1.2f,unit*0.6f,unit*2.4f);
            pillar.transform.localScale=new Vector3(unit*0.18f,unit*1.2f,unit*0.18f);
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
        ApplyState(m,state,b.S.Seed,b.Plan.Look);
        if(state.Sim) m.EnableKeyword("VEFX_SIM"); else m.DisableKeyword("VEFX_SIM");
        if(Environment.GetEnvironmentVariable("VEFX_DEBUG_STATE")!=null) Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,"[VEFX state] {0} age {1:F2} dur {2:F2} tf {3:F2} fade {4:F2} dissolve {5:F2} body {6:F3} heat {7:F2} frame0 {8:F2} box {9:F1}",b.S.Name,age,VolumeEvolution.VolumeDuration(b.Plan),VolumeEvolution.FireballDuration(b.Plan),state.Fade,state.Dissolve,state.FireBody,state.FireHeat,state.Lobe0.SimFrame,state.BoxRadius)+string.Format(System.Globalization.CultureInfo.InvariantCulture," | lobe0 centre y {0:F2} r {1:F2} origin y {2:F2} scale {3:F3} after {4:F1} | lobe1 centre y {5:F2} r {6:F2} (box units)",state.Lobe0.Center.Y,state.Lobe0.Radius,state.Lobe0.SimOrigin.Y,state.Lobe0.SimScale,state.Lobe0.SimAfter,state.Lobe1.Center.Y,state.Lobe1.Radius));
        if(Environment.GetEnvironmentVariable("VEFX_DEBUG_LOBES")!=null)
            for(int i=0;i<4;i++) { LobeState l=state.Lobe(i); Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,"[VEFX lobes] age {0:F1} lobe {1} sim {2} centre y {3:F3} r {4:F3} origin y {5:F3} scale {6:F4} frame {7:F2} after {8:F2} delay {9:F2} size {10:F2}",age,i,l.Sim,l.Center.Y,l.Radius,l.SimOrigin.Y,l.SimScale,l.SimFrame,l.SimAfter,b.Layout.Lobes[i].Delay,b.Layout.Lobes[i].Size)); }
        m.SetVector("_LightDirection",new Vector4(light.x,light.y,light.z,RenderSettings.ambientLight.grayscale));
        m.SetColor("_SunColor",new Color(b.Sun.color.r,b.Sun.color.g,b.Sun.color.b,b.Sun.intensity));
        m.SetFloat("_Steps",32);
        // VEFX_BENCH_SUN: the star as the game publishes it (_VefxSun), for the blast wave's shadow on the ground.
        if(Environment.GetEnvironmentVariable("VEFX_BENCH_SUN")!=null) Shader.SetGlobalVector("_VefxSun",new Vector4(light.x,light.y,light.z,b.Sun.intensity>0.01f?1:0));
        Vector4 nukeFlare=Vector4.zero;
        if(b.NukeMaterial!=null&&state.Modern)
        {
            NukeFrame nf=NukeCloud.Evaluate(b.Plan,b.Layout,state,age,b.Box,b.S.Seed,600000);
            Material nm=b.NukeMaterial;
            nm.SetVector("_NkBall",V(nf.Ball)); nm.SetVector("_NkTorus",V(nf.Torus)); nm.SetVector("_NkStem",V(nf.Stem)); nm.SetVector("_NkSkirt",V(nf.Skirt));
            nm.SetVector("_NkCap",V(nf.Cap)); nm.SetVector("_NkDust",V(nf.Dust)); nm.SetVector("_NkExtra",V(nf.Extra)); nm.SetVector("_NkScale",V(nf.Scale)); nm.SetVector("_NkHeat",V(nf.Heat));
            // The sun's strength as KSP gives it (its dimming at dusk comes from the elevation, in the shader), and the sky.
            float sunOn=b.Sun.intensity>0.01f?1:0;
            nm.SetVector("_NkSun",new Vector4(sunOn,sunOn,(float)(nf.Ball.X+nf.Ball.Z),0));
            Color amb=RenderSettings.ambientLight; nm.SetVector("_VefxSky",new Vector4(amb.r*0.85f,amb.g*0.85f,amb.b*0.85f,1));
            nm.SetVector("_LightDirection",new Vector4(light.x,light.y,light.z,RenderSettings.ambientLight.grayscale));
            nm.SetColor("_SunColor",new Color(b.Sun.color.r,b.Sun.color.g,b.Sun.color.b,b.Sun.intensity));
            nm.SetVector("_Context",new Vector4(b.Ground?1:b.Water?2:0,(float)state.Atmosphere,(float)age,(float)(b.S.Seed%997)));
            b.Volume.GetComponent<Renderer>().sharedMaterial=nm;
            nm.SetVector("_NkPile0",V(nf.Pile0)); nm.SetVector("_NkPile1",V(nf.Pile1)); nm.SetVector("_NkPile2",V(nf.Pile2));
            // VEFX_BENCH_BOLT: a lightning stroke flashing in the cap's underside, as the game passes it.
            Vector3 boltAt=new Vector3((float)(nf.Ball.Y*0.4*b.Box),(float)((nf.Ball.X-0.3*nf.Ball.Z)*b.Box),(float)(-nf.Ball.Y*0.2*b.Box));
            Shader.SetGlobalVector("_VefxBolt",Environment.GetEnvironmentVariable("VEFX_BENCH_BOLT")!=null?new Vector4(boltAt.x,boltAt.y,boltAt.z,1):Vector4.zero);
            Shader.SetGlobalFloat("_VefxBoltRange",280);
            if(b.NukeShadowMaterial!=null)
            {
                Material sm=b.NukeShadowMaterial;
                sm.SetVector("_Sun",new Vector4(light.x,light.y,light.z,b.Sun.intensity>0.01f?1:0));
                sm.SetVector("_Shadow",new Vector4((float)b.Box,0.5f,1.4f,37));
                sm.SetVector("_NkSh0",new Vector4((float)nf.Late.Y,(float)nf.Ball.X,(float)nf.Late.Z,(float)nf.Ball.Y));
                sm.SetVector("_NkSh1",new Vector4((float)Math.Max(nf.Ball.Z*1.1,0.5*nf.Ball.Y),(float)(nf.Stem.Y*1.1),(float)nf.Stem.Z,(float)nf.Stem.X));
                sm.SetVector("_NkSh2",new Vector4(1,(float)nf.Stem.W,(float)nf.Dust.W*(1-0.8f*(float)nf.Late.X),0));
            }
            nm.SetVector("_NkLate",V(nf.Late)); nm.SetVector("_NkAir",V(nf.Air)); nm.SetVector("_NkJets",V(nf.Jets)); nm.SetVector("_NkWave",V(nf.Wave));
            // VEFX_BENCH_GROUNDMAP: a ground map as the game fills it (flat here, the sea on one side) to check the
            // dust wall's ground path; without it the shader keeps to the burst's flat ground.
            if(Environment.GetEnvironmentVariable("VEFX_BENCH_GROUNDMAP")!=null)
            {
                var gm=new Texture2D(64,16,TextureFormat.RGFloat,false,true); gm.wrapModeU=TextureWrapMode.Repeat; gm.wrapModeV=TextureWrapMode.Clamp;
                var px=new Color[64*16];
                for(int i=0;i<px.Length;i++) { float ang=((i%64+0.5f)/64-0.5f)*2*Mathf.PI, r=(i/64)/15f*0.95f*(float)b.Box; px[i]=new Color(0,Mathf.Cos(ang)<-0.2f&&r>600?1:0,0,0); }
                gm.SetPixels(px); gm.Apply(false,false);
                nm.SetTexture("_NkGroundMap",gm); nm.SetVector("_NkGroundInfo",new Vector4(1,0.95f,0,0));
            }
            // The glare, as HeroVolume sets it: anchored on the fireball; the core glare is off for a nuclear burst.
            nukeFlare=new Vector4(0,1e-6f,0,0);
            if(b.Glare!=null)
            {
                double tfN=VolumeEvolution.FireballDuration(b.Plan), rfM=VolumeEvolution.FireballRadius(b.Plan);
                Vec4 g=NukeCloud.Glare(nf,age,tfN), h=NukeCloud.Halo(nf,b.Box,b.Sun.intensity>0.01f?0:1);
                b.GlareMaterial.SetVector("_Halo",V(h));
                b.Glare.SetActive(g.Y+g.Z+h.Y>0.01&&Environment.GetEnvironmentVariable("VEFX_DEBUG_NOFLARE")==null);
                b.Glare.transform.position=Vector3.up*(float)Math.Max(nf.Ball.X*b.Box,0.5*rfM);
                b.GlareMaterial.SetVector("_Glare",new Vector4((float)(g.X*rfM),(float)g.Y,(float)g.Z,(float)g.W));
            }
        }
        b.FlareMaterial.SetVector("_Flare",nukeFlare.y>0?Vector4.zero:V(VolumeEvolution.Flare(state)));
        b.Volume.SetActive(!b.NoVolume&&state.Visible&&Environment.GetEnvironmentVariable("VEFX_DEBUG_NOVOLUME")==null);
        if(b.Refraction!=null)
        {
            Vec4 shock,haze; VolumeEvolution.Distortion(state,0,out shock,out haze);
            b.DistortMaterial.SetVector("_Shock",V(shock)); b.DistortMaterial.SetVector("_Haze",V(haze)); b.DistortMaterial.SetFloat("_Seed",37); b.DistortMaterial.SetFloat("_ShockGain",(float)VolumeEvolution.ShockRimGain(state)); b.DistortMaterial.SetFloat("_RingDepth",(float)VolumeEvolution.RingDepth(state));
            b.DistortMaterial.SetFloat("_SrcBlend",1); b.DistortMaterial.SetFloat("_DstBlend",0);   // (the 1.0 ring for every blast, as HeroVolume)
            float tanV=Mathf.Tan(b.Camera.fieldOfView*0.5f*Mathf.Deg2Rad);
            bool ringGone=VolumeEvolution.RingOutOfView(state,Vector3.Distance(b.Camera.transform.position,b.Refraction.transform.position),tanV*Mathf.Sqrt(1+b.Camera.aspect*b.Camera.aspect));
            b.Refraction.SetActive(!b.NoVolume&&VolumeEvolution.DistortionVisible(state,0)&&!ringGone&&Environment.GetEnvironmentVariable("VEFX_PREVIEW_NO_REFRACTION")==null);
        }
        b.Flash.transform.position=new Vector3(0,(float)(VolumeEvolution.LightHeight(state)*b.Box),0);
        b.Flash.intensity=(float)VolumeEvolution.LightIntensity(state);
        if(b.Plan.Look!=null&&!b.Plan.Look.SameAs(LookProfile.Classic()))
        {
            Vec3 c=ExplosionLight.Colour(state); b.Flash.color=new Color((float)c.X,(float)c.Y,(float)c.Z);
            b.Flash.range=(float)ExplosionLight.Range(state,b.Layout.RadiusMeters,b.Dome); b.Flash.renderMode=LightRenderMode.ForcePixel;
        }
        if(b.NukeMaterial!=null&&state.Modern)
        {
            // The nuclear land light, as FxInstance sets it.
            double tfL=VolumeEvolution.FireballDuration(b.Plan), rfL=VolumeEvolution.FireballRadius(b.Plan), nightL=b.Sun.intensity>0.01f?0:1;
            NukeFrame lf=NukeCloud.Evaluate(b.Plan,b.Layout,state,age,b.Box,b.S.Seed,600000);
            b.Flash.intensity=(float)(NukeCloud.LightLevel(age,tfL,nightL)*state.Fade);
            Vec3 lc=NukeCloud.LightColour(age,tfL); b.Flash.color=new Color((float)lc.X,(float)lc.Y,(float)lc.Z);
            b.Flash.range=(float)NukeCloud.LightRange(age,tfL,rfL,nightL,Math.Max(lf.Ball.X,0.02)*b.Box);
            b.Flash.transform.position=new Vector3(0,(float)(Math.Max(lf.Ball.X,0.02)*b.Box),0);
        }
        b.Flash.enabled=!b.NoVolume&&b.Flash.intensity>0.01f;
        if(b.Splash!=null)
        {
            Vec3 breeze=Aftermath.Wind(b.S.Seed,state.Atmosphere,true);
            SplashFrame sf=WaterSplash.Evaluate(b.Plan,age,b.S.Gravity,new Vec3(b.S.Velocity.X,0,b.S.Velocity.Z),breeze,b.S.Seed);
            b.Splash.SetActive(sf.Visible&&Environment.GetEnvironmentVariable("VEFX_DEBUG_NOSPLASH")==null);
            if(Environment.GetEnvironmentVariable("VEFX_DEBUG_STATE")!=null) Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[VEFX splash] age {0:F1} reach {1:F1} top {2:F1} spray {3} haze {4} surge {5} foam {6} jet {7}",age,sf.Reach,sf.Top,V(sf.Spray),V(sf.Haze),V(sf.Surge),V(sf.Foam),V(sf.Jet)));
            if(sf.Visible)
            {
                float scale,centre; SplashProperties.Proxy(sf,out scale,out centre);
                b.Splash.transform.position=new Vector3(0,centre,0); b.Splash.transform.localScale=Vector3.one*scale;
                Color amb=RenderSettings.ambientLight; b.SplashMaterial.SetVector("_VefxSky",new Vector4(amb.r*0.85f,amb.g*0.85f,amb.b*0.85f,1));
                Color fc=b.Flash.color; float fi=b.Flash.enabled?b.Flash.intensity*0.12f:0;
                SplashProperties.Apply(b.SplashBlock,sf,light,RenderSettings.ambientLight.grayscale,b.Sun.intensity>0.01f?1:0,
                    new Vector4(fc.r*fi,fc.g*fi,fc.b*fi,b.Flash.range*0.3f),b.Flash.transform.position,1,(float)state.Atmosphere);
                b.Splash.GetComponent<Renderer>().SetPropertyBlock(b.SplashBlock);
            }
        }
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
                Prepare(b); b.Camera.Render(); RenderTexture.active=b.Target;
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
    // The baked fireball simulation; VEFX_PREVIEW_NOSIM=1 renders the analytic fireball for comparison.
    internal static void BindFireSim(Material m)
    {
        bool off=Environment.GetEnvironmentVariable("VEFX_PREVIEW_NOSIM")=="1";
        VolumeEvolution.UseSim=!off;
        m.SetTexture("_FireSimGround",FireSimTexture.Ensure("ground")); m.SetTexture("_FireSimAir",FireSimTexture.Ensure("air"));
        m.SetTexture("_FireSimGround2",FireSimTexture.Ensure("ground2")); m.SetTexture("_FireSimGround3",FireSimTexture.Ensure("ground3"));
        m.SetTexture("_FireSimGround4",FireSimTexture.Ensure("ground4")); m.SetTexture("_FireSimAir2",FireSimTexture.Ensure("air2"));
        FireSim.GroundLoaded=off?1:FireSim.GroundRuns.Length; FireSim.AirLoaded=off?1:FireSim.AirRuns.Length;
        m.SetTexture("_FireSimNuke",FireSimTexture.Ensure("nuke")); m.SetTexture("_FireSimNuke2",FireSimTexture.Ensure("nuke2")); m.SetTexture("_FireSimNukeAir",FireSimTexture.Ensure("nukeair"));
        FireSim.NukeHave[0]=FireSim.NukeHave[1]=FireSim.NukeHave[2]=!off;
        m.SetVector("_SimNukeDims",new Vector4(FireSimTable.NukeNx,FireSimTable.NukeNy,FireSimTable.NukeNz,FireSimTable.NukeFrames));
        m.SetVector("_SimNukeGeom",new Vector4(FireSimTable.NukeHalf,FireSimTable.NukeHeight,0,0));
        m.SetVector("_SimDims",new Vector4(FireSimTable.GroundNx,FireSimTable.GroundNy,FireSimTable.GroundNz,FireSimTable.GroundFrames));
        m.SetVector("_SimAirDims",new Vector4(FireSimTable.AirNx,FireSimTable.AirNy,FireSimTable.AirNz,FireSimTable.AirFrames));
        m.SetVector("_SimGeom",new Vector4(FireSimTable.GroundHalf,FireSimTable.GroundHeight,FireSimTable.AirHalf,FireSimTable.AirHeight));
    }
    internal static Vector4 V(Vec4 v) { return new Vector4((float)v.X,(float)v.Y,(float)v.Z,(float)v.W); }
    static readonly string[] LobeNames={"0","1","2","3","4"};
    // VEFX_PREVIEW_LOOK: "classic" (the 1.0 look) or "default", optionally followed by
    // dial overrides, e.g. "default;turbulence=1.4;temperatureGradient=0.8".
    internal static LookProfile Look(FxPlan plan,bool surface,int members)
    {
        string spec=Environment.GetEnvironmentVariable("VEFX_PREVIEW_LOOK")??"default";
        string[] parts=spec.Split(';');
        LookKind kind=LookProfile.Select(plan,surface,Reentry.Applies(plan,surface,plan.ImpactSpeed),members);
        LookProfile look=parts[0].Trim().ToLowerInvariant()=="classic"?LookProfile.Classic():LookProfile.Default(kind);
        for(int i=1;i<parts.Length;i++)
        {
            string[] kv=parts[i].Split('=');
            int index=kv.Length==2?LookProfile.IndexOf(kv[0].Trim()):-1;
            double value;
            if(index>=0&&double.TryParse(kv[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)) look[index]=value;
        }
        Debug.Log("[VEFX preview] look "+kind+" = "+string.Join(" ",Array.ConvertAll(LookProfile.Keys,k=>k+"="+look[LookProfile.IndexOf(k)].ToString(System.Globalization.CultureInfo.InvariantCulture))));
        return look;
    }
    public static void ApplyState(Material m,VolumeState state,double seed,LookProfile look=null)
    {
        if(look==null) look=LookProfile.Classic();
        m.SetVector("_Look0",V(look.Shader0)); m.SetVector("_Look1",V(look.Shader1));
        Vec4 shell,core,cloud,context,fireball,smoke;
        VolumeEvolution.Pack(state,seed,out shell,out core,out cloud,out context,out fireball,out smoke);
        m.SetVector("_Fire",V(fireball)); m.SetVector("_Smoke",V(smoke));
        m.SetVector("_Shell",V(shell)); m.SetVector("_Core",V(core));
        m.SetVector("_Cloud",V(cloud)); m.SetVector("_Context",V(context));
        m.SetVector("_Bounds",V(VolumeEvolution.Bounds(state)));
        m.SetVector("_Motion",V(VolumeEvolution.Motion(state)));
        m.SetVector("_Dust",V(VolumeEvolution.Dust(state))); m.SetVector("_SimShape",V(state.SimShape)); m.SetFloat("_SimVariant",state.SimRun); m.SetFloat("_SimLinger",(float)state.SimLinger); m.SetFloat("_SimStem",(float)state.SimStem); m.SetVector("_SmokeMix",V(state.SmokeMix)); m.SetVector("_SimFlame",new Vector4((float)(1-state.FlameFaint),0,0,0));
        m.SetVector("_Nuke",V(state.Nuke)); m.SetVector("_NukeStem",V(state.NukeStem)); m.SetVector("_NukeRing",V(state.NukeRing));
        m.SetVector("_NukeGround",V(state.NukeGround)); m.SetVector("_NukeIce",V(state.NukeIce)); m.SetVector("_NukeWilson",V(state.NukeWilson)); m.SetVector("_NukeCap",V(state.NukeCap));
        for(int i=0;i<5;i++)
        {
            Vec4 c,a,d; VolumeEvolution.PackLobe(state,i,out c,out a,out d);
            m.SetVector("_L"+LobeNames[i],V(c)); m.SetVector("_A"+LobeNames[i],V(a)); m.SetVector("_D"+LobeNames[i],V(d));
        }
    }
}
