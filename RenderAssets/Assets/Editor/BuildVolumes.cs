using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildVolumes
{
    public static void Build()
    {
        try
        {
            if(Application.unityVersion!="2019.4.18f1") throw new Exception("Unity 2019.4.18f1 required.");
            string platform=Environment.GetEnvironmentVariable("VEFX_BUNDLE_TARGET")??"windows";
            BuildTarget target=platform=="mac"?BuildTarget.StandaloneOSX:BuildTarget.StandaloneWindows64;
            string output=Environment.GetEnvironmentVariable("VEFX_BUNDLE_OUTPUT");
            if(string.IsNullOrEmpty(output)) throw new Exception("Output directory required.");
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,target))
                throw new Exception("Install Unity standalone support for requested target: "+platform);
            PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);
            PlayerSettings.SetGraphicsAPIs(target,platform=="mac"?
                new[]{GraphicsDeviceType.Metal,GraphicsDeviceType.OpenGLCore}:
                new[]{GraphicsDeviceType.Direct3D11,GraphicsDeviceType.OpenGLCore});
            AssetDatabase.Refresh();
            Shader shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/HeroVolume.shader");
            if(shader==null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Hero shader import failed.");
            Shader flare=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/HeroFlare.shader");
            if(flare==null || ShaderUtil.ShaderHasError(flare)) throw new Exception("Flare shader import failed.");
            Shader particle=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Particle.shader");
            if(particle==null || ShaderUtil.ShaderHasError(particle)) throw new Exception("Particle shader import failed.");
            Shader distortion=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/HeatDistortion.shader");
            if(distortion==null || ShaderUtil.ShaderHasError(distortion)) throw new Exception("Distortion shader import failed.");
            Texture3D noise=NoiseTexture.Ensure();
            if(noise==null) throw new Exception("Noise texture generation failed.");
            Shader debris=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Debris.shader");
            if(debris==null || ShaderUtil.ShaderHasError(debris)) throw new Exception("Debris shader import failed.");
            Shader smoke=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Smoke.shader");
            if(smoke==null || ShaderUtil.ShaderHasError(smoke)) throw new Exception("Smoke shader import failed.");
            Shader fragment=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/PartFragment.shader");
            if(fragment==null || ShaderUtil.ShaderHasError(fragment)) throw new Exception("Part fragment shader import failed.");
            Shader scorch=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Scorch.shader");
            if(scorch==null || ShaderUtil.ShaderHasError(scorch)) throw new Exception("Scorch shader import failed.");
            Shader trail=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Trail.shader");
            if(trail==null || ShaderUtil.ShaderHasError(trail)) throw new Exception("Trail shader import failed.");
            Shader flame=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Flame.shader");
            if(flame==null || ShaderUtil.ShaderHasError(flame)) throw new Exception("Flame shader import failed.");
            Shader fire=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/FireVolume.shader");
            if(fire==null || ShaderUtil.ShaderHasError(fire)) throw new Exception("Fire volume shader import failed.");
            Texture2D smokeA,smokeB; SmokeFlipbook.Ensure(out smokeA,out smokeB);
            BlueNoise.Ensure();
            if(smokeA==null||smokeB==null) throw new Exception("Smoke flipbook generation failed.");
            Directory.CreateDirectory(output);
            var bundle=new AssetBundleBuild { assetBundleName="vefx-"+platform+".unity3d",
                assetNames=new[]{"Assets/Shaders/HeroVolume.shader","Assets/Shaders/HeroFlare.shader","Assets/Shaders/Particle.shader","Assets/Shaders/HeatDistortion.shader","Assets/Shaders/Debris.shader",NoiseTexture.AssetPath,
                    "Assets/Shaders/Smoke.shader","Assets/Shaders/PartFragment.shader","Assets/Shaders/Scorch.shader","Assets/Shaders/Trail.shader","Assets/Shaders/Flame.shader","Assets/Shaders/FireVolume.shader",SmokeFlipbook.PathA,SmokeFlipbook.PathB,BlueNoise.AssetPath} };
            if(BuildPipeline.BuildAssetBundles(output,new[]{bundle},BuildAssetBundleOptions.ForceRebuildAssetBundle|
                BuildAssetBundleOptions.ChunkBasedCompression,target)==null) throw new Exception("Bundle build failed.");
            foreach(Shader compiled in new[]{shader,flare,particle,distortion,debris,smoke,fragment,scorch,trail,flame,fire})
                foreach(var message in ShaderUtil.GetShaderMessages(compiled))
                    if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        throw new Exception(compiled.name+": "+message.message);
            bool host=platform=="windows"&&Application.platform==RuntimePlatform.WindowsEditor ||
                platform=="mac"&&Application.platform==RuntimePlatform.OSXEditor;
            if(host)
            {
                AssetBundle loaded=AssetBundle.LoadFromFile(Path.Combine(output,bundle.assetBundleName));
                if(loaded==null) throw new Exception("Built bundle could not be reloaded.");
                try
                {
                    Shader loadedFlare=loaded.LoadAsset<Shader>("Assets/Shaders/HeroFlare.shader");
                    if(loadedFlare==null||!loadedFlare.isSupported) throw new Exception("Built flare shader unsupported on host graphics API.");
                    Shader loadedParticle=loaded.LoadAsset<Shader>("Assets/Shaders/Particle.shader");
                    if(loadedParticle==null||!loadedParticle.isSupported) throw new Exception("Built particle shader unsupported on host graphics API.");
                    Texture3D loadedNoise=loaded.LoadAsset<Texture3D>(NoiseTexture.AssetPath);
                    if(loadedNoise==null) throw new Exception("Built bundle has no noise texture.");
                    RenderProbe(loaded.LoadAsset<Shader>("Assets/Shaders/HeroVolume.shader"),loadedNoise,output);
                    DistortionProbe(loaded.LoadAsset<Shader>("Assets/Shaders/HeatDistortion.shader"),output);
                    Shader loadedDebris=loaded.LoadAsset<Shader>("Assets/Shaders/Debris.shader");
                    if(loadedDebris==null||!loadedDebris.isSupported) throw new Exception("Built debris shader unsupported on host graphics API.");
                    Texture2D loadedA=loaded.LoadAsset<Texture2D>(SmokeFlipbook.PathA), loadedB=loaded.LoadAsset<Texture2D>(SmokeFlipbook.PathB);
                    if(loadedA==null||loadedB==null) throw new Exception("Built bundle has no smoke flipbook.");
                    SmokeProbe(loaded.LoadAsset<Shader>("Assets/Shaders/Smoke.shader"),loadedA,loadedB,output);
                    FragmentProbe(loaded.LoadAsset<Shader>("Assets/Shaders/PartFragment.shader"),loadedNoise,output);
                    ScorchProbe(loaded.LoadAsset<Shader>("Assets/Shaders/Scorch.shader"),loadedNoise,output);
                    TrailProbe(loaded.LoadAsset<Shader>("Assets/Shaders/Trail.shader"),loadedNoise,output);
                    FlameProbe(loaded.LoadAsset<Shader>("Assets/Shaders/Flame.shader"),loadedNoise,output);
                    FireProbe(loaded.LoadAsset<Shader>("Assets/Shaders/FireVolume.shader"),loadedNoise,output);
                }
                finally { loaded.Unload(true); }
            }
            File.WriteAllText(Path.Combine(output,"unity-validation.json"),
                "{\"unity\":\""+Application.unityVersion+"\",\"target\":\""+platform+
                "\",\"bundle_built\":true,\"host_render_probe\":"+host.ToString().ToLowerInvariant()+
                ",\"graphics_api\":\""+SystemInfo.graphicsDeviceType+"\",\"ksp_test\":false}");
            Debug.Log("[VEFX] Shader bundle and host probe passed: "+platform);
            EditorApplication.Exit(0);
        }
        catch(Exception ex) { Debug.LogError("[VEFX] "+ex); EditorApplication.Exit(1); }
    }
    static void RenderProbe(Shader shader,Texture3D noise,string output)
    {
        if(shader==null || !shader.isSupported) throw new Exception("Built shader unsupported on host graphics API.");
        var cameraObject=new GameObject("Isolated shader test camera");
        var camera=cameraObject.AddComponent<Camera>();
        camera.transform.position=new Vector3(0,0,-5); camera.transform.rotation=Quaternion.identity;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
        camera.nearClipPlane=0.05f; camera.farClipPlane=40; camera.fieldOfView=50;
        camera.depthTextureMode=DepthTextureMode.Depth;
        var rt=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32);
        camera.targetTexture=rt;
        var volume=GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(volume.GetComponent<Collider>());
        Mesh mesh=UnityEngine.Object.Instantiate(volume.GetComponent<MeshFilter>().sharedMesh);
        Vector3[] vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++) vertices[i]*=2;
        mesh.vertices=vertices;mesh.RecalculateBounds();volume.GetComponent<MeshFilter>().sharedMesh=mesh;
        var material=new Material(shader); volume.GetComponent<Renderer>().sharedMaterial=material;
        material.SetTexture("_NoiseTex",noise);
        material.SetFloat("_Steps",40);
        material.SetVector("_LightDirection",new Vector4(-0.5f,0.8f,-0.3f,0.35f));
        material.SetColor("_SunColor",new Color(1,0.96f,0.9f,1));
        var image=new Texture2D(128,128,TextureFormat.RGB24,false);
        try
        {
            SetState(material,new Vector4(0.5f,0.06f,0.8f,0),new Vector4(0.15f,1,1,0),new Vector4(0,0,0,1),new Vector4(0,1,0.2f,37));
            float hot=Capture(camera,rt,image,Path.Combine(output,"probe-hot.png"));
            if(hot<0.003f) throw new Exception("Hot volume did not produce visible pixels.");
            SetState(material,new Vector4(0.5f,0.06f,0,0),new Vector4(0.15f,0,0,0),new Vector4(0,0,0,1),new Vector4(0,1,0.4f,37));
            material.SetVector("_Fire",new Vector4(0.35f,1,0.6f,1)); material.SetVector("_Bounds",new Vector4(0,0,0,0.8f));
            material.SetVector("_L0",new Vector4(0,0,0,0.35f)); material.SetVector("_A0",new Vector4(0,1,0,1)); material.SetVector("_D0",new Vector4(0,0,0,1));
            material.SetVector("_L1",new Vector4(0.25f,0.1f,0,0.2f)); material.SetVector("_A1",new Vector4(1,0,0,1.4f)); material.SetVector("_D1",new Vector4(0,0,0,0.8f));
            if(Capture(camera,rt,image,Path.Combine(output,"probe-fireball.png"))<0.003f) throw new Exception("Fireball lobes did not produce visible pixels.");
            material.SetVector("_Fire",Vector4.zero); material.SetVector("_Bounds",Vector4.zero);
            SetState(material,new Vector4(0.85f,0.1f,0.2f,0.6f),new Vector4(0.27f,0,0,0.05f),new Vector4(0,0,0,1),new Vector4(0,1,2,37));
            material.SetVector("_Fire",new Vector4(0.35f,0,0.6f,0.8f)); material.SetVector("_Bounds",new Vector4(0,0,0,0.8f));
            material.SetVector("_Smoke",new Vector4(0.3f,0.29f,0.28f,0.8f));
            material.SetVector("_L0",new Vector4(0,0,0,0.4f)); material.SetVector("_A0",new Vector4(0,1,0,1)); material.SetVector("_D0",new Vector4(0,0,0,0));
            material.SetVector("_L1",Vector4.zero);
            float cold=Capture(camera,rt,image,Path.Combine(output,"probe-cold.png"));
            if(cold<0.001f || cold>=hot) throw new Exception("Cloud cooling probe failed.");
            material.SetVector("_Fire",Vector4.zero); material.SetVector("_Bounds",Vector4.zero); material.SetVector("_L0",Vector4.zero);
            camera.transform.position=Vector3.zero;
            if(Capture(camera,rt,image,Path.Combine(output,"probe-inside.png"))<0.001f)
                throw new Exception("Camera-inside-volume probe failed.");
            camera.transform.position=new Vector3(0,0,-5);
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.transform.position=new Vector3(0,0,-2); blocker.transform.localScale=new Vector3(4,4,0.1f);
            try
            {
                float covered=Capture(camera,rt,image,null);
                volume.SetActive(false); float baseline=Capture(camera,rt,image,null);
                if(Mathf.Abs(covered-baseline)>0.001f) throw new Exception("Opaque depth clipping probe failed.");
            }
            finally { UnityEngine.Object.DestroyImmediate(blocker); }
            volume.SetActive(true);
            SetState(material,new Vector4(0.85f,0.1f,0,0.85f),new Vector4(0.27f,0,0,0),new Vector4(0,0,0,0),new Vector4(0,1,6,37));
            if(Capture(camera,rt,image,null)>0.0001f) throw new Exception("Expired volume remained visible.");
        }
        finally
        {
            RenderTexture.active=null; camera.targetTexture=null;
            UnityEngine.Object.DestroyImmediate(volume); UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(image);
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }
    }
    static void DistortionProbe(Shader shader,string output)
    {
        if(shader==null || !shader.isSupported) throw new Exception("Built distortion shader unsupported on host graphics API.");
        var cameraObject=new GameObject("Isolated refraction test camera");
        var camera=cameraObject.AddComponent<Camera>();
        camera.transform.position=new Vector3(0,0,-5); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
        camera.nearClipPlane=0.05f; camera.farClipPlane=40; camera.fieldOfView=50; camera.depthTextureMode=DepthTextureMode.Depth;
        var rt=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32); camera.targetTexture=rt;
        var stripes=new Texture2D(64,64,TextureFormat.RGB24,false); stripes.filterMode=FilterMode.Point;
        for(int y=0;y<64;y++) for(int x=0;x<64;x++) stripes.SetPixel(x,y,(x/4)%2==0?Color.white:Color.black);
        stripes.Apply();
        var background=GameObject.CreatePrimitive(PrimitiveType.Quad);
        UnityEngine.Object.DestroyImmediate(background.GetComponent<Collider>());
        background.transform.position=new Vector3(0,0,3); background.transform.localScale=new Vector3(12,12,1);
        var backgroundMaterial=new Material(Shader.Find("Unlit/Texture")); backgroundMaterial.mainTexture=stripes;
        background.GetComponent<Renderer>().sharedMaterial=backgroundMaterial;
        var quad=new Mesh(); quad.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
        quad.triangles=new[]{0,1,2,0,2,3}; quad.bounds=new Bounds(Vector3.zero,Vector3.one*8);
        var effect=new GameObject("Refraction probe"); effect.AddComponent<MeshFilter>().sharedMesh=quad;
        var material=new Material(shader); effect.AddComponent<MeshRenderer>().sharedMaterial=material;
        var image=new Texture2D(128,128,TextureFormat.RGB24,false);
        try
        {
            effect.SetActive(false); Color[] baseline=Pixels(camera,rt,image);
            effect.SetActive(true);
            material.SetVector("_Shock",new Vector4(0.5f,0,0.08f,1)); material.SetVector("_Haze",new Vector4(0,1,0.55f,0.5f));
            if(Difference(baseline,Pixels(camera,rt,image))>0.0005f) throw new Exception("Zero-strength refraction altered the image.");
            material.SetVector("_Shock",new Vector4(0.5f,0.06f,0.08f,1));
            Color[] shocked=Pixels(camera,rt,image);
            File.WriteAllBytes(Path.Combine(output,"probe-refraction.png"),image.EncodeToPNG());
            if(Difference(baseline,shocked)<0.002f) throw new Exception("Shock refraction produced no visible displacement.");
        }
        finally
        {
            RenderTexture.active=null; camera.targetTexture=null; rt.Release();
            UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(effect); UnityEngine.Object.DestroyImmediate(quad); UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(background); UnityEngine.Object.DestroyImmediate(backgroundMaterial);
            UnityEngine.Object.DestroyImmediate(stripes); UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
    static Camera ProbeCamera(Color background,out RenderTexture rt)
    {
        var camera=new GameObject("Isolated residue probe camera").AddComponent<Camera>();
        camera.transform.position=new Vector3(0,0,-5); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=background;
        camera.nearClipPlane=0.05f; camera.farClipPlane=40; camera.fieldOfView=50;
        rt=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32); camera.targetTexture=rt;
        Shader.SetGlobalVector("_VefxSun",new Vector4(0.3f,0.5f,-0.81f,1)); Shader.SetGlobalVector("_VefxAmbient",new Vector4(0.2f,0.2f,0.22f,1));
        Shader.SetGlobalVector("_VefxSky",new Vector4(0.36f,0.43f,0.55f,1)); Shader.SetGlobalVector("_VefxUp",new Vector4(0,1,0,0));
        Shader.SetGlobalVector("_VefxFire0",Vector4.zero); Shader.SetGlobalVector("_VefxFire1",Vector4.zero); Shader.SetGlobalVector("_VefxFireRange",new Vector4(10,10,0,0));
        return camera;
    }
    static Mesh ProbeQuad()
    {
        var m=new Mesh();
        m.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
        m.normals=new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.back};
        m.tangents=new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)};
        m.triangles=new[]{0,2,1,0,3,2};
        return m;
    }
    static float Probe(Camera camera,RenderTexture rt,Mesh mesh,Material material,string path)
    {
        var go=new GameObject("Residue probe"); go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterial=material;
        var image=new Texture2D(128,128,TextureFormat.RGB24,false);
        try { return Capture(camera,rt,image,path); }
        finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(image); }
    }
    static void EndProbe(Camera camera,RenderTexture rt,Mesh mesh,Material material)
    {
        RenderTexture.active=null; camera.targetTexture=null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material);
    }
    static void SmokeProbe(Shader shader,Texture2D a,Texture2D b,string output)
    {
        if(shader==null||!shader.isSupported) throw new Exception("Built smoke shader unsupported on host graphics API.");
        RenderTexture rt; Camera camera=ProbeCamera(Color.black,out rt);
        Mesh quad=ProbeQuad();
        float tile=1f/SmokeFlipbook.Tiles;
        float u0=(12%SmokeFlipbook.Tiles)*tile, v0=1-(12/SmokeFlipbook.Tiles+1)*tile;
        var uv=new List<Vector4>{new Vector4(u0,v0,u0,v0),new Vector4(u0+tile,v0,u0+tile,v0),new Vector4(u0+tile,v0+tile,u0+tile,v0+tile),new Vector4(u0,v0+tile,u0,v0+tile)};
        quad.SetUVs(0,uv); quad.SetUVs(1,new List<Vector2>{Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero});
        quad.colors=new[]{Color.white,Color.white,Color.white,Color.white};
        var material=new Material(shader); material.SetTexture("_SmokeA",a); material.SetTexture("_SmokeB",b); material.SetFloat("_Emission",0);
        try
        {
            if(Probe(camera,rt,quad,material,Path.Combine(output,"probe-smoke.png"))<0.01f) throw new Exception("Lit smoke puff did not produce visible pixels.");
            material.SetFloat("_Density",0);
            if(Probe(camera,rt,quad,material,null)>0.0001f) throw new Exception("Transparent smoke puff remained visible.");
        }
        finally { EndProbe(camera,rt,quad,material); }
    }
    static void FragmentProbe(Shader shader,Texture3D noise,string output)
    {
        if(shader==null||!shader.isSupported) throw new Exception("Built part fragment shader unsupported on host graphics API.");
        RenderTexture rt; Camera camera=ProbeCamera(Color.black,out rt);
        Mesh quad=ProbeQuad();
        var still=new List<Vector4>{new Vector4(0,0,0,1e6f),new Vector4(0,0,0,1e6f),new Vector4(0,0,0,1e6f),new Vector4(0,0,0,1e6f)};
        quad.SetUVs(0,new List<Vector2>{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
        quad.SetUVs(1,still); quad.SetUVs(2,new List<Vector4>{Vector4.zero,Vector4.zero,Vector4.zero,Vector4.zero});
        quad.SetUVs(3,new List<Vector4>{new Vector4(0,0,0,-1e9f),new Vector4(0,0,0,-1e9f),new Vector4(0,0,0,-1e9f),new Vector4(0,0,0,-1e9f)});
        quad.colors32=new[]{new Color32(0,0,0,0),new Color32(0,0,0,0),new Color32(0,0,0,0),new Color32(0,0,0,0)};
        var material=new Material(shader); material.SetTexture("_NoiseTex",noise); material.SetColor("_Color",new Color(0.8f,0.8f,0.78f,1));
        if(material.passCount<2) { EndProbe(camera,rt,quad,material); throw new Exception("Part fragment shader has no depth pass."); }
        material.SetFloat("_Gravity",0); material.SetVector("_Life",new Vector4(40,45,0,0));
        try
        {
            material.SetFloat("_Age",0);
            if(Probe(camera,rt,quad,material,Path.Combine(output,"probe-fragment.png"))<0.01f) throw new Exception("Part fragment did not produce visible pixels.");
            material.SetFloat("_Age",50);
            if(Probe(camera,rt,quad,material,null)>0.0001f) throw new Exception("Crumbled part fragment remained visible.");
        }
        finally { EndProbe(camera,rt,quad,material); }
    }
    static void ScorchProbe(Shader shader,Texture3D noise,string output)
    {
        if(shader==null||!shader.isSupported) throw new Exception("Built scorch shader unsupported on host graphics API.");
        RenderTexture rt; Camera camera=ProbeCamera(Color.white,out rt);
        Mesh quad=ProbeQuad(); quad.SetUVs(0,new List<Vector2>{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
        var material=new Material(shader); material.SetTexture("_NoiseTex",noise);
        try
        {
            material.SetVector("_Scorch",new Vector4(0,0,13,0));
            float clean=Probe(camera,rt,quad,material,null);
            material.SetVector("_Scorch",new Vector4(0.8f,0,13,0));
            float burnt=Probe(camera,rt,quad,material,Path.Combine(output,"probe-scorch.png"));
            if(clean<0.99f||burnt>clean-0.02f) throw new Exception("Scorch probe failed.");
        }
        finally { EndProbe(camera,rt,quad,material); }
    }
    static void TrailProbe(Shader shader,Texture3D noise,string output)
    {
        if(shader==null||!shader.isSupported) throw new Exception("Built trail shader unsupported on host graphics API.");
        RenderTexture rt; Camera camera=ProbeCamera(Color.black,out rt);
        Mesh quad=ProbeQuad(); quad.SetUVs(0,new List<Vector2>{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
        var material=new Material(shader); material.SetTexture("_NoiseTex",noise);
        try
        {
            quad.colors=new[]{Color.white,Color.white,Color.white,Color.white};
            if(Probe(camera,rt,quad,material,Path.Combine(output,"probe-trail.png"))<0.01f) throw new Exception("Smoke trail did not produce visible pixels.");
            quad.colors=new[]{Color.clear,Color.clear,Color.clear,Color.clear};
            if(Probe(camera,rt,quad,material,null)>0.0001f) throw new Exception("Transparent smoke trail remained visible.");
            material.SetFloat("_Glow",1);
            quad.colors=new[]{Color.white,Color.white,Color.white,Color.white};
            if(Probe(camera,rt,quad,material,Path.Combine(output,"probe-trail-glow.png"))<0.01f) throw new Exception("Glowing trail did not produce visible pixels.");
            quad.colors=new[]{Color.clear,Color.clear,Color.clear,Color.clear};
            if(Probe(camera,rt,quad,material,null)>0.0001f) throw new Exception("Transparent glowing trail remained visible.");
        }
        finally { EndProbe(camera,rt,quad,material); }
    }
    static void FlameProbe(Shader shader,Texture3D noise,string output)
    {
        if(shader==null||!shader.isSupported) throw new Exception("Built flame shader unsupported on host graphics API.");
        RenderTexture rt; Camera camera=ProbeCamera(Color.black,out rt);
        Mesh quad=ProbeQuad();
        var centre=new Vector4(0,-1.1f,0,2.4f);
        quad.SetUVs(0,new List<Vector4>{centre,centre,centre,centre});
        var material=new Material(shader); material.SetTexture("_NoiseTex",noise);
        quad.colors=new[]{Color.white,Color.white,Color.white,Color.white};
        try
        {
            quad.SetUVs(1,new List<Vector4>{new Vector4(0,0,0.4f,0.3f),new Vector4(1,0,0.4f,0.3f),new Vector4(1,1,0.4f,0.3f),new Vector4(0,1,0.4f,0.3f)});
            if(Probe(camera,rt,quad,material,Path.Combine(output,"probe-flame.png"))<0.005f) throw new Exception("Volumetric flame did not produce visible pixels.");
            quad.SetUVs(1,new List<Vector4>{new Vector4(0,0,1,0.3f),new Vector4(1,0,1,0.3f),new Vector4(1,1,1,0.3f),new Vector4(0,1,1,0.3f)});
            if(Probe(camera,rt,quad,material,null)>0.0001f) throw new Exception("Burnt-out flame remained visible.");
        }
        finally { EndProbe(camera,rt,quad,material); }
    }
    static void FireProbe(Shader shader,Texture3D noise,string output)
    {
        if(shader==null||!shader.isSupported) throw new Exception("Built fire volume shader unsupported on host graphics API.");
        RenderTexture rt; Camera camera=ProbeCamera(Color.black,out rt);
        camera.depthTextureMode=DepthTextureMode.Depth;
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        Mesh mesh=UnityEngine.Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(cube);
        Vector3[] vertices=mesh.vertices; for(int i=0;i<vertices.Length;i++) vertices[i]*=2; mesh.vertices=vertices; mesh.RecalculateBounds();
        var material=new Material(shader); material.SetTexture("_NoiseTex",noise); material.SetTexture("_BlueNoise",BlueNoise.Ensure());
        var go=new GameObject("Fire probe"); go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterial=material;
        go.transform.position=new Vector3(0,0,0); go.transform.localScale=new Vector3(1.4f,1.4f,1.4f);
        var image=new Texture2D(128,128,TextureFormat.RGB24,false);
        try
        {
            material.SetVector("_Box",new Vector4(1.4f,1.4f,40,3)); material.SetVector("_Fire",new Vector4(0.9f,1.5f,1,0.6f)); material.SetVector("_Lean",new Vector4(0,0,0.8f,1));
            go.transform.position=new Vector3(0,-0.2f,0);
            if(Capture(camera,rt,image,Path.Combine(output,"probe-fire.png"))<0.005f) throw new Exception("Fire volume did not produce visible pixels.");
            material.SetVector("_Fire",new Vector4(0.9f,1.5f,0,0.6f));
            if(Capture(camera,rt,image,null)>0.0001f) throw new Exception("Fire volume with its fire out remained visible.");
        }
        finally
        {
            RenderTexture.active=null; camera.targetTexture=null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(image);
        }
    }
    static Color[] Pixels(Camera camera,RenderTexture rt,Texture2D image)
    {
        camera.Render(); RenderTexture.active=rt;
        image.ReadPixels(new Rect(0,0,128,128),0,0); image.Apply(); return image.GetPixels();
    }
    static float Difference(Color[] a,Color[] b)
    {
        float sum=0; for(int i=0;i<a.Length;i++) sum+=Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b);
        return sum/(a.Length*3);
    }
    static void SetState(Material material,Vector4 shell,Vector4 core,Vector4 cloud,Vector4 context)
    {
        material.SetVector("_Shell",shell); material.SetVector("_Core",core);
        material.SetVector("_Cloud",cloud); material.SetVector("_Context",context);
    }
    static float Capture(Camera camera,RenderTexture rt,Texture2D image,string path)
    {
        camera.Render(); RenderTexture.active=rt;
        image.ReadPixels(new Rect(0,0,128,128),0,0); image.Apply();
        if(path!=null) File.WriteAllBytes(path,image.EncodeToPNG());
        float sum=0; foreach(Color c in image.GetPixels()) sum+=(c.r+c.g+c.b)/3;
        return sum/(128*128);
    }
}
