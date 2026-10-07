using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VolumetricExplosionFX.Core;
using VolumetricExplosionFX.Rendering;

// Editor-only wreckage rendering probes.
public static class PreviewWreck
{
    sealed class Wreck : IDisposable
    {
        public PreviewVolumes.Bench B;
        public Transform Effect, Ground;
        public Mesh Mesh, ScorchMesh; public Material Fragments, Scorch, FireMaterial, SmokeMaterial;
        public FractureResult Cut; public FragmentMotion[] Motion; public double Gravity;
        public ResiduePlan Residue; public bool HasScorch;
        public ParticleSystem Fire, Column, Tongues, TrailFire, TrailSmoke, TrailFlames;
        public TrailShower Tracers; public Material TrailMaterial;
        public FireVolumeRenderer FireVolume;
        public BlastParticles Particles;
        public Vector3 V, EffectStart; public double GasRate; public bool Airborne, Chase;
        public readonly List<int> Traced=new List<int>(); public readonly List<Vector3> TracerAt=new List<Vector3>();
        public readonly ResidueEmitter Emitter=new ResidueEmitter(); public readonly DebrisTrails Trails=new DebrisTrails();
        public readonly List<UnityEngine.Object> Owned=new List<UnityEngine.Object>();
        public T Own<T>(T o) where T:UnityEngine.Object { Owned.Add(o); return o; }
        public void Dispose()
        {
            for(int i=Owned.Count-1;i>=0;i--) if(Owned[i]!=null) UnityEngine.Object.DestroyImmediate(Owned[i]);
            B.Dispose();
        }
    }
    static readonly string[] Names={"tank-crash-ksc","grazing-crash","rocket-chain","airburst","breakup-streak","vacuum","mun-crash","srb-burst","srb-flight","reentry-breakup","duna-crash","water-impact","fast-empty-impact","high-altitude","tank-crash-b","tank-crash-c","tank-crash-d"};
    public static void Render()
    {
        try
        {
            string output=Environment.GetEnvironmentVariable("VEFX_PREVIEW_OUTPUT");
            if(string.IsNullOrEmpty(output)) throw new Exception("Preview output directory required.");
            Directory.CreateDirectory(output);
            int width=480, height=270; double[] ages={0.15,0.5,1.2,2.5,5,9,16,28,42}; float distance=1.5f;
            string frame=Environment.GetEnvironmentVariable("VEFX_PREVIEW_FRAME");
            var inv=System.Globalization.CultureInfo.InvariantCulture;
            if(!string.IsNullOrEmpty(frame))
            {
                string[] parts=frame.Split(';'); string[] size=parts[0].Split('x');
                width=int.Parse(size[0],inv); height=int.Parse(size[1],inv);
                ages=Array.ConvertAll(parts[1].Split(','),x=>double.Parse(x,inv)); Array.Sort(ages);
                if(parts.Length>2) distance=float.Parse(parts[2],inv);
                if(parts.Length>3) PreviewVolumes.Elevation=float.Parse(parts[3],inv);
            }
            PreviewVolumes.Width=width; PreviewVolumes.Height=height;
            DumpAtlas(output);
            string only=Environment.GetEnvironmentVariable("VEFX_PREVIEW_ONLY");
            foreach(string name in Names) if(string.IsNullOrEmpty(only)||only.Contains(name))
                RenderScenario(Array.Find(PreviewVolumes.Scenarios,x=>x.Name==name),ages,distance,width,height,output);
            Debug.Log("[VEFX] Wreck preview rendered: "+output);
            EditorApplication.Exit(0);
        }
        catch(Exception ex) { Debug.LogError("[VEFX] "+ex); EditorApplication.Exit(1); }
    }
    static void RenderScenario(PreviewVolumes.Scenario s,double[] ages,float distance,int width,int height,string output)
    {
        using(Wreck w=Build(s,distance))
        {
            int columns=Math.Min(ages.Length,5), rows=(ages.Length+columns-1)/columns;
            var sheet=w.Own(new Texture2D(width*columns,height*rows,TextureFormat.RGB24,false));
            var image=w.Own(new Texture2D(width,height,TextureFormat.RGB24,false));
            double t=0;
            for(int f=0;f<ages.Length;f++)
            {
                while(t<ages[f]-1e-6)
                {
                    float dt=(float)Math.Min(1.0/30,ages[f]-t);
                    Step(w,(float)t,dt);
                    t+=dt;
                }
                Show(w,ages[f]);
                if(Environment.GetEnvironmentVariable("VEFX_WRECK_FOCUS")!=null) Focus(w,ages[f]);
                w.B.Camera.Render(); RenderTexture.active=w.B.Target;
                image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
                sheet.SetPixels((f%columns)*width,(rows-1-f/columns)*height,width,height,image.GetPixels());
            }
            RenderTexture.active=null; sheet.Apply();
            File.WriteAllBytes(Path.Combine(output,"wreck-"+s.Name+".png"),sheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(output,"wreck-"+s.Name+".txt"),string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "pieces {0}, vertices {1}, burning {2}, fire {3:F1} s, smoke {4:F1} s, scorch {5:F1} m, ages {6}\n",
                w.Cut.Count,w.Cut.VertexCount,w.Trails.Count,w.Residue.FireSeconds,w.Residue.SmokeSeconds,w.Residue.ScorchRadius,
                string.Join(" ",Array.ConvertAll(ages,a=>a.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        }
    }
    static void Frame(Wreck w,double t)
    {
        if(!w.Airborne) return;
        Vector3 camera=w.Chase?w.V*(float)t:Vector3.zero;
        w.Ground.position=w.V*(float)(t*Ballistics.E1(w.GasRate*t))-camera;
        w.Effect.position=w.EffectStart-camera;
    }
    static void Step(Wreck w,float age,float dt)
    {
        Frame(w,age+dt);
        w.Emitter.Tick(age,dt,w.Fire,w.Column,w.Tongues);
        if(w.FireVolume!=null)
        {
            float level=(float)Residue.FireLevel(w.Residue,age+dt);
            w.FireVolume.Tick(level,(float)w.Residue.FlameHeight*(0.55f+0.45f*Mathf.Sqrt(level)),w.Residue.PuffHz,40,1);
            w.FireVolume.Show(true);
        }
        w.Trails.Tick(age,dt,w.TrailFire,w.TrailSmoke,w.TrailFlames);
        if(w.Traced.Count>0)
        {
            for(int k=0;k<w.Traced.Count;k++) { Vec3 at=DebrisPlanner.Where(w.Motion[w.Traced[k]],w.Gravity,age); w.TracerAt[k]=new Vector3((float)at.X,(float)at.Y,(float)at.Z); }
            w.Tracers.Update(age+dt,w.TracerAt);
        }
        var state=VolumeEvolution.Evaluate(w.B.Plan,age+dt,true,true,w.B.Ground,w.B.Water,w.B.Layout);
        w.Particles.Tick(age+dt,dt,0,state.Visible&&!w.B.NoVolume,state);
        w.Particles.Simulate(dt);
        foreach(var p in new[]{w.Fire,w.Column,w.Tongues,w.TrailFire,w.TrailSmoke,w.TrailFlames,w.Tracers.System}) p.Simulate(dt,true,false,false);
    }
    static void Show(Wreck w,double age)
    {
        PreviewVolumes.Apply(w.B,age);
        Vector3 gasAt=w.Ground.position;
        w.B.Volume.transform.position=gasAt; if(w.B.Refraction!=null) w.B.Refraction.transform.position=gasAt; w.B.Flash.transform.position+=gasAt;
        var state=VolumeEvolution.Evaluate(w.B.Plan,age,true,true,w.B.Ground,w.B.Water,w.B.Layout);
        Vector3 sun=-w.B.Sun.transform.forward;
        bool dark=w.B.S.Vacuum||w.B.S.Moon;
        Shader.SetGlobalVector("_VefxSun",new Vector4(sun.x,sun.y,sun.z,dark?1.2f:1));
        Color ambient=RenderSettings.ambientLight;
        Shader.SetGlobalVector("_VefxAmbient",new Vector4(Mathf.Max(ambient.r,0.07f),Mathf.Max(ambient.g,0.07f),Mathf.Max(ambient.b,0.08f),1));
        Shader.SetGlobalVector("_VefxSky",dark?Vector4.zero:new Vector4(0.36f,0.43f,0.55f,1));
        Shader.SetGlobalVector("_VefxUp",new Vector4(0,1,0,0));
        float volumeLight=state.Visible?(float)VolumeEvolution.LightIntensity(state):0;
        Shader.SetGlobalVector("_VefxFire0",new Vector4(0,(float)(VolumeEvolution.LightHeight(state)*state.BoxRadius),0,volumeLight));
        float pool=(float)Residue.FireLevel(w.Residue,age);
        Shader.SetGlobalVector("_VefxFire1",new Vector4(w.Ground.position.x,w.Ground.position.y+(float)w.Residue.FlameHeight*0.35f,w.Ground.position.z,pool*1.4f));
        Shader.SetGlobalVector("_VefxFireRange",new Vector4(Mathf.Max(w.B.Flash.range,10),(float)w.Residue.FireRadius*8+5,0,0));
        w.Fragments.SetFloat("_Age",(float)age);
        if(w.HasScorch)
            w.Scorch.SetVector("_Scorch",new Vector4((float)Residue.ScorchLevel(w.Residue,age),dark?0:Mathf.Clamp01(1-(float)age/14)*(float)w.B.Plan.FireFraction,13,dark?1:0));
    }
    static void Focus(Wreck w,double age)
    {
        int best=-1;
        for(int f=0;f<w.Cut.Count;f++) if(best<0||w.Cut.Radius[f]>w.Cut.Radius[best]) best=f;
        if(best<0) return;
        Vec3 at=DebrisPlanner.Where(w.Motion[best],w.Gravity,age);
        Vector3 target=w.Effect.TransformPoint(new Vector3((float)at.X,(float)at.Y,(float)at.Z));
        float reach=(float)w.Cut.Radius[best]*3+1.2f;
        w.B.Camera.transform.position=target+new Vector3(-0.75f,0.55f,-0.6f).normalized*reach;
        w.B.Camera.transform.LookAt(target);
    }
    static Wreck Build(PreviewVolumes.Scenario s,float distance)
    {
        var w=new Wreck(); w.B=PreviewVolumes.Build(s,distance);
        Physics.SyncTransforms();
        FxPlan plan=w.B.Plan;
        w.Gravity=s.Gravity;
        float size=(float)Math.Max(s.Size,2);
        bool surface=w.B.Ground||w.B.Water;
        Quaternion pose=s.Name=="grazing-crash"?Quaternion.Euler(8,20,72):Quaternion.Euler(4,30,6);
        var at=new Vector3(0,surface?size*0.45f:0,0);
        w.Effect=w.Own(new GameObject("Wreck effect frame")).transform; w.Effect.position=at;
        w.Ground=w.Own(new GameObject("Wreck ground frame")).transform; w.Ground.position=Vector3.zero;
        PartMesh tank=TankMesh(28,8,size*0.3f,size);
        w.Cut=Fracture.Cut(tank,DebrisPlanner.Pieces(plan,size),s.Seed,3000,1);
        Vec3 velocity=s.Velocity;
        if(velocity.Length<1e-3&&s.Kind!=EventKind.Destruction) velocity=new Vec3(0,-s.Speed,0);
        w.Motion=DebrisPlanner.Launch(plan,w.Cut,FragmentMeshBuilder.ToQuat(pose),new Vec3(),new Vec3(0,surface?-size*0.3:0,0),velocity,surface,s.Seed);
        double floor=s.Vacuum?double.NegativeInfinity:(s.Airburst?-w.B.Dome*6:0)-at.y;
        for(int f=0;f<w.Motion.Length;f++)
        {
            if(double.IsNegativeInfinity(floor)) continue;
            DebrisPlanner.Land(w.Cut,f,FragmentMeshBuilder.ToQuat(pose),ref w.Motion[f],floor,w.Gravity,120,s.Water);
        }
        w.Mesh=w.Own(new Mesh()); w.Mesh.name="Wreck pieces";
        FragmentMeshBuilder.Build(w.Mesh,w.Cut,w.Motion,pose,Vector3.zero,1,w.Gravity,120,s.Seed);
        var pieces=w.Own(new GameObject("Wreck pieces")); pieces.transform.SetParent(w.Effect,false);
        pieces.AddComponent<MeshFilter>().sharedMesh=w.Mesh;
        w.Fragments=w.Own(new Material(PreviewVolumes.Load("PartFragment",true)));
        w.Fragments.mainTexture=w.Own(TankTexture());
        w.Fragments.SetTexture("_NoiseTex",NoiseTexture.Ensure());
        w.Fragments.SetFloat("_Gravity",(float)w.Gravity); w.Fragments.SetVector("_Life",new Vector4(40,45,0,0));
        w.Fragments.SetFloat("_Cool",s.Vacuum||s.Moon?1.4f:1.8f);
        pieces.AddComponent<MeshRenderer>().sharedMaterial=w.Fragments;
        Texture2D atlasA,atlasB; SmokeFlipbook.Ensure(out atlasA,out atlasB);
        Shader smoke=PreviewVolumes.Load("Smoke",true);
        w.FireMaterial=w.Own(new Material(smoke)); w.FireMaterial.SetTexture("_SmokeA",atlasA); w.FireMaterial.SetTexture("_SmokeB",atlasB); w.FireMaterial.SetFloat("_Emission",1.3f);
        w.SmokeMaterial=w.Own(new Material(smoke)); w.SmokeMaterial.SetTexture("_SmokeA",atlasA); w.SmokeMaterial.SetTexture("_SmokeB",atlasB); w.SmokeMaterial.SetFloat("_Emission",0);
        w.Fire=w.Own(SmokeParticles.Create(w.Ground,"Pool fire",w.FireMaterial,160,0,0.45f)); w.Owned.Add(w.Fire.gameObject);
        w.Column=w.Own(SmokeParticles.Create(w.Ground,"Smoke column",w.SmokeMaterial,240,0.3f,1)); w.Owned.Add(w.Column.gameObject);
        w.TrailFire=w.Own(SmokeParticles.Create(w.Effect,"Debris flames",w.FireMaterial,200,0,0.7f)); w.Owned.Add(w.TrailFire.gameObject);
        w.TrailSmoke=w.Own(SmokeParticles.Create(w.Effect,"Debris smoke",w.SmokeMaterial,400,0.35f,1)); w.Owned.Add(w.TrailSmoke.gameObject);
        w.Residue=Residue.Plan(plan,w.B.Ground,w.B.Water,45);
        Vec3 breeze=Aftermath.Wind(s.Seed*7919u,plan.Atmosphere); var wind=new Vector3((float)breeze.X,0,(float)breeze.Z);
        w.Emitter.Begin(w.Residue,plan,s.Seed,0,wind); SmokeParticles.Bend(w.Column,wind,0.12f);
        w.Trails.FlyingPuffs=false;
        w.Trails.Begin(w.Motion,w.Gravity,6,plan,s.Seed,45,w.B.Water);
        var assets=new ProceduralAssets(PreviewVolumes.Load("Particle",true),PreviewVolumes.Load("Debris",true));
        foreach(UnityEngine.Object o in new UnityEngine.Object[]{assets.Glow,assets.Cloud,assets.Debris,assets.Trail,assets.Shard}) w.Own(o);
        w.TrailMaterial=w.Own(new Material(PreviewVolumes.Load("Trail",true))); w.TrailMaterial.SetTexture("_NoiseTex",NoiseTexture.Ensure());
        var trailGlow=w.Own(new Material(w.TrailMaterial)); trailGlow.SetFloat("_Glow",1);
        var settings=new FxSettings();
        var flame=w.Own(new Material(PreviewVolumes.Load("Flame",true))); flame.SetTexture("_NoiseTex",NoiseTexture.Ensure());
        w.Tongues=SmokeParticles.CreateFlameSheets(w.Ground,"Flames",flame,200); w.Owned.Add(w.Tongues.gameObject); w.Tongues.Play();
        w.Emitter.Sheets=true;
        if(w.Residue.HasFire)
        {
            var fireMaterial=w.Own(new Material(PreviewVolumes.Load("FireVolume",true)));
            fireMaterial.SetTexture("_NoiseTex",NoiseTexture.Ensure()); fireMaterial.SetTexture("_BlueNoise",BlueNoise.Ensure());
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            var box=w.Own(UnityEngine.Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh)); UnityEngine.Object.DestroyImmediate(cube);
            var corners=box.vertices; for(int i=0;i<corners.Length;i++) corners[i]*=2; box.vertices=corners; box.RecalculateBounds();
            w.FireVolume=new FireVolumeRenderer(w.Ground,box,fireMaterial,0);
            w.FireVolume.Begin(w.Residue.FireRadius,w.Residue.FlameHeight,wind,w.Gravity,s.Seed);
            w.Emitter.Volume=true;
        }
        w.TrailFlames=SmokeParticles.CreateFlameSheets(w.Effect,"Debris flame sheets",flame,64); w.Owned.Add(w.TrailFlames.gameObject); w.TrailFlames.Play();
        w.Particles=new BlastParticles(w.Ground,w.Ground,new BlastMaterials{ Glow=assets.Glow, Cloud=assets.Cloud, Debris=assets.Debris, Trail=assets.Trail,
            Shard=assets.Shard, HeatDebris=assets.HeatDebris, SmokeFire=w.FireMaterial, SmokeCloud=w.SmokeMaterial, TrailSmoke=w.TrailMaterial, TrailGlow=trailGlow, Flame=flame },settings);
        for(int i=0;i<w.Ground.childCount;i++) w.Owned.Add(w.Ground.GetChild(i).gameObject);
        Vector3 v=new Vector3((float)s.Velocity.X,(float)s.Velocity.Y,(float)s.Velocity.Z);
        if(v.sqrMagnitude<1e-6f&&s.Kind!=EventKind.Destruction) v=new Vector3(0,-(float)s.Speed,0);
        w.V=v; w.EffectStart=at; w.Airborne=!(surface||s.Kind!=EventKind.Destruction);
        w.GasRate=GasRate(plan,w.B.Layout,!w.Airborne); w.Chase=w.Airborne&&Environment.GetEnvironmentVariable("VEFX_CHASE")!=null;
        w.Particles.Begin(new BlastContext{ Plan=plan, Layout=w.B.Layout, Settings=settings, Kind=s.Kind, Ground=w.B.Ground, Water=w.B.Water,
            Surface=surface||s.Kind!=EventKind.Destruction, Dome=!w.B.NoVolume, Air=(float)Numbers.Clamp(plan.Atmosphere,0,1), DomeRadius=w.B.Dome,
            Gravity=(float)w.Gravity, ImpactSpeed=Mathf.Abs(v.y), Velocity=v, Floor=s.Airburst?-w.B.Dome*6:(float?)null, Sunlit=1,
            DebrisBudget=settings.MaxDebris, Seed=s.Seed*7919u, GasRate=w.GasRate });
        w.Tracers=new TrailShower(w.Effect,"Burning piece trails",assets.Glow,w.TrailMaterial,8); w.Owned.Add(w.Tracers.System.gameObject);
        var lifetimes=new float[6]; var starts=new Vector3[6];
        for(int f=0;f<w.Motion.Length&&w.Traced.Count<6;f++)
        {
            FragmentMotion m=w.Motion[f]; if(!m.Burning) continue;
            float life=(float)Math.Min(m.BurnTime,m.LandTime); if(life<=0.1f) continue;
            lifetimes[w.Traced.Count]=life; Vec3 pivot=m.Pivot; starts[w.Traced.Count]=new Vector3((float)pivot.X,(float)pivot.Y,(float)pivot.Z);
            w.Traced.Add(f); w.TracerAt.Add(starts[w.Traced.Count-1]);
        }
        if(w.Traced.Count>0)
        {
            float dimT=(float)(1-0.5*Numbers.Clamp(plan.Soot,0,1));
            w.Tracers.Trace(w.Traced.Count,new Color((float)plan.SmokeR*dimT,(float)plan.SmokeG*dimT,(float)plan.SmokeB*dimT,1),1.2+2.2*Numbers.Clamp(plan.Atmosphere,0,1),lifetimes,starts,new Color(1,0.66f,0.32f,1),0.9f);
        }
        if(w.Residue.ScorchRadius>0)
        {
            w.ScorchMesh=w.Own(new Mesh());
            float radius=(float)w.Residue.ScorchRadius;
            Transform frame=w.Ground;
            w.HasScorch=ScorchMesh.Build(w.ScorchMesh,radius,16,(Vector3 local,out Vector3 point,out Vector3 normal)=>
            {
                RaycastHit hit; Vector3 world=frame.TransformPoint(local);
                if(Physics.Raycast(world+Vector3.up*(radius+5),Vector3.down,out hit,2*radius+10))
                { point=frame.InverseTransformPoint(hit.point); normal=frame.InverseTransformDirection(hit.normal); return true; }
                point=local; normal=Vector3.up; return false;
            });
            if(w.HasScorch)
            {
                var scorch=w.Own(new GameObject("Scorch")); scorch.transform.SetParent(w.Ground,false);
                scorch.AddComponent<MeshFilter>().sharedMesh=w.ScorchMesh;
                w.Scorch=w.Own(new Material(PreviewVolumes.Load("Scorch",true))); w.Scorch.SetTexture("_NoiseTex",NoiseTexture.Ensure());
                scorch.AddComponent<MeshRenderer>().sharedMaterial=w.Scorch;
            }
        }
        return w;
    }
    static double GasRate(FxPlan plan,BlastLayout layout,bool surface)
    {
        if(surface) return 0;
        double t=Reentry.GasTime(Numbers.Clamp(plan.Atmosphere,0,1),layout.RadiusMeters);
        return double.IsInfinity(t)?0:1/t;
    }
    static PartMesh TankMesh(int segments,int rings,float radius,float height)
    {
        var p=new List<Vec3>(); var n=new List<float>(); var uv=new List<float>(); var tri=new List<int>();
        for(int r=0;r<=rings;r++) for(int k=0;k<=segments;k++)
        {
            double a=2*Math.PI*k/segments, y=height*r/rings-height/2;
            p.Add(new Vec3(radius*Math.Cos(a),y,radius*Math.Sin(a))); n.Add((float)Math.Cos(a)); n.Add(0); n.Add((float)Math.Sin(a));
            uv.Add((float)k/segments); uv.Add((float)r/rings);
        }
        int row=segments+1;
        for(int r=0;r<rings;r++) for(int k=0;k<segments;k++)
        {
            int a=r*row+k, b=a+1, c=a+row, d=c+1;
            tri.AddRange(new[]{a,c,b,b,c,d});
        }
        foreach(float y in new[]{-height/2,height/2})
        {
            int centre=p.Count; p.Add(new Vec3(0,y,0)); n.Add(0); n.Add(Math.Sign(y)); n.Add(0); uv.Add(0.5f); uv.Add(0.02f);
            int ring=p.Count;
            for(int k=0;k<segments;k++)
            {
                double a=2*Math.PI*k/segments;
                p.Add(new Vec3(radius*Math.Cos(a),y,radius*Math.Sin(a))); n.Add(0); n.Add(Math.Sign(y)); n.Add(0);
                uv.Add((float)k/segments); uv.Add(0.02f);
            }
            for(int k=0;k<segments;k++) tri.AddRange(y>0?new[]{centre,ring+(k+1)%segments,ring+k}:new[]{centre,ring+k,ring+(k+1)%segments});
        }
        return new PartMesh { Positions=p.ToArray(), Normals=n.ToArray(), Uvs=uv.ToArray(), Triangles=tri.ToArray(), Groups=new int[tri.Count/3] };
    }
    static Texture2D TankTexture()
    {
        const int size=256; var t=new Texture2D(size,size,TextureFormat.RGB24,true); t.wrapMode=TextureWrapMode.Repeat;
        var c=new Color[size*size];
        for(int y=0;y<size;y++) for(int x=0;x<size;x++)
        {
            float u=(float)x/size, v=(float)y/size;
            float grime=Mathf.PerlinNoise(u*18,v*9)*0.08f;
            Color col=new Color(0.86f,0.86f,0.84f)*(1-grime);
            if(v>0.82f&&v<0.9f || v<0.06f) col=new Color(0.1f,0.1f,0.11f);
            if(x%32<1||y%42<1) col*=0.78f;
            if(u>0.1f&&u<0.22f&&v>0.35f&&v<0.62f) col=new Color(0.14f,0.14f,0.15f);
            c[y*size+x]=col;
        }
        t.SetPixels(c); t.Apply(true); return t;
    }
    static void DumpAtlas(string output)
    {
        Texture2D a,b; SmokeFlipbook.Ensure(out a,out b);
        const int s=512; int big=a.width;
        var sheet=new Texture2D(s*4,s,TextureFormat.RGB24,false);
        Color32[] pa=a.GetPixels32(), pb=b.GetPixels32();
        for(int y=0;y<s;y++) for(int x=0;x<s;x++)
        {
            int i=(y*big/s)*big+x*big/s;
            sheet.SetPixel(x,y,new Color32(pa[i].r,pa[i].g,pa[i].b,255));
            sheet.SetPixel(s+x,y,new Color32(pb[i].r,pb[i].g,pb[i].b,255));
            sheet.SetPixel(2*s+x,y,new Color32(pa[i].a,pa[i].a,pa[i].a,255));
            sheet.SetPixel(3*s+x,y,new Color32(pb[i].a,(byte)(pb[i].a/2),0,255));
        }
        sheet.Apply(); File.WriteAllBytes(Path.Combine(output,"smoke-atlas.png"),sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
    }
}
