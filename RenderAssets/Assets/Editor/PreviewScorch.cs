using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VolumetricExplosionFX.Core;
using VolumetricExplosionFX.Rendering;

// Editor-only sheet of ground marks seen from above: rows of grounds (grass, sand, soil, snow), columns of marks
// (fuel burns, warhead craters, a grazing crash), each with its own seed, as the game lays them.
public static class PreviewScorch
{
    struct Mark { public string Name; public double Crater, Stretch, Angle, Darkness, Radius; }
    public static void Render()
    {
        try
        {
            string output=Environment.GetEnvironmentVariable("VEFX_PREVIEW_OUTPUT");
            if(string.IsNullOrEmpty(output)) throw new Exception("Preview output directory required.");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Color[][] grounds={
                new[]{new Color(0.25f,0.34f,0.15f),new Color(0.33f,0.42f,0.2f)},   // grass
                new[]{new Color(0.62f,0.55f,0.4f),new Color(0.72f,0.64f,0.47f)},   // sand
                new[]{new Color(0.36f,0.3f,0.24f),new Color(0.46f,0.4f,0.32f)},    // soil
                new[]{new Color(0.82f,0.84f,0.88f),new Color(0.9f,0.91f,0.94f)},   // snow
                new[]{new Color(0.5f,0.51f,0.52f),new Color(0.57f,0.58f,0.58f)}};  // concrete (hard ground)
            Mark[] marks={
                new Mark{Name="fuel burn",Crater=0,Darkness=0.6,Radius=11},
                new Mark{Name="fuel burn",Crater=0,Darkness=0.5,Radius=9},
                new Mark{Name="missile",Crater=1,Darkness=0.55,Radius=8},
                new Mark{Name="bomb",Crater=1,Darkness=0.55,Radius=11,Stretch=0.1,Angle=0.7},
                new Mark{Name="hard crash",Crater=0.5,Darkness=0.45,Radius=10,Stretch=0.7,Angle=-0.4}};
            const float spacing=26;
            int cols=marks.Length, rows=grounds.Length;
            var sun=new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(38,-35,0); sun.color=new Color(1,0.96f,0.9f); sun.intensity=1.1f;
            RenderSettings.ambientLight=new Color(0.42f,0.45f,0.5f);
            Vector3 light=-sun.transform.forward;
            Shader.SetGlobalVector("_VefxSun",new Vector4(light.x,light.y,light.z,1));
            Shader scorchShader=PreviewVolumes.Load("Scorch",true);
            Texture3D noise=NoiseTexture.Ensure();
            for(int row=0;row<rows;row++)
            {
                var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);
                plane.transform.position=new Vector3((cols-1)*spacing*0.5f,0,-row*spacing);
                plane.transform.localScale=new Vector3(cols*spacing/10f,1,spacing/10f);
                var pm=new Material(Shader.Find("Standard")); pm.mainTexture=PreviewVolumes.GroundTexture(grounds[row][0],grounds[row][1]);
                pm.mainTextureScale=new Vector2(cols*3,3); pm.SetFloat("_Glossiness",0.05f); plane.GetComponent<Renderer>().sharedMaterial=pm;
                Color soil=Color.Lerp(grounds[row][0],grounds[row][1],0.5f);
                for(int col=0;col<cols;col++)
                {
                    Mark m=marks[col]; float radius=(float)m.Radius;
                    var mesh=new Mesh();
                    ScorchMesh.Build(mesh,radius,16,(Vector3 local,out Vector3 point,out Vector3 normal)=>{ point=local; normal=Vector3.up; return true; });
                    var go=new GameObject(m.Name); go.transform.position=new Vector3(col*spacing,0.02f,-row*spacing);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var mat=new Material(scorchShader); mat.SetTexture("_NoiseTex",noise);
                    uint seed=(uint)(row*131+col*977+17);
                    mat.SetVector("_Scorch",new Vector4((float)m.Darkness,0,seed%997,0));
                    mat.SetVector("_ScorchLook",new Vector4(Environment.GetEnvironmentVariable("VEFX_SCORCH_OLD")!=null?0:1,(float)m.Crater,(float)m.Stretch,(float)m.Angle));
                    mat.SetVector("_ScorchSoil",new Vector4(soil.r,soil.g,soil.b,row==3?1:row==4?2:0));
                    go.AddComponent<MeshRenderer>().sharedMaterial=mat;
                }
            }
            const int W=1600, H=1280;
            var camera=new GameObject("Camera").AddComponent<Camera>();
            camera.orthographic=true; camera.orthographicSize=rows*spacing*0.5f;
            camera.transform.position=new Vector3((cols-1)*spacing*0.5f,200,-(rows-1)*spacing*0.5f); camera.transform.rotation=Quaternion.Euler(90,0,0);
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; camera.nearClipPlane=1; camera.farClipPlane=400;
            camera.aspect=(float)W/H;
            var target=new RenderTexture(W,H,24,RenderTextureFormat.ARGB32); camera.targetTexture=target;
            camera.Render(); RenderTexture.active=target;
            var image=new Texture2D(W,H,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,W,H),0,0); image.Apply();
            RenderTexture.active=null;
            File.WriteAllBytes(Path.Combine(output,"scorch-sheet.png"),image.EncodeToPNG());
            EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
