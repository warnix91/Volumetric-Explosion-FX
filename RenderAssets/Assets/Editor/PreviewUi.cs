using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using VolumetricExplosionFX.Core;
using VolumetricExplosionFX.Rendering;

// Editor-only settings-window probes.
public static class PreviewUi
{
    public static void Render()
    {
        try
        {
            string output=Environment.GetEnvironmentVariable("VEFX_PREVIEW_OUTPUT");
            if(string.IsNullOrEmpty(output)) throw new Exception("Preview output directory required.");
            Directory.CreateDirectory(output);
            const int W=1280, H=800;
            var camera=new GameObject("UI preview camera").AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(0.13f,0.16f,0.21f);
            camera.cullingMask=1<<5; camera.orthographic=true; camera.nearClipPlane=0.1f; camera.farClipPlane=10;
            var target=new RenderTexture(W,H,24,RenderTextureFormat.ARGB32); camera.targetTexture=target;
            string backdrop=Path.Combine(output,"wreck-tank-crash-ksc.png");
            if(File.Exists(backdrop))
            {
                var tex=new Texture2D(2,2); tex.LoadImage(File.ReadAllBytes(backdrop));
                var back=new GameObject("Backdrop",typeof(RectTransform)); back.layer=5;
                var canvas=back.AddComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=5; canvas.sortingOrder=0;
                var raw=new GameObject("Image",typeof(RectTransform)); raw.layer=5; raw.transform.SetParent(back.transform,false);
                var img=raw.AddComponent<RawImage>(); img.texture=tex;
                int columns=Math.Max(1,tex.width/Math.Max(1,tex.height*16/9));
                img.uvRect=new Rect(0,0,1f/columns,1);
                var r=(RectTransform)raw.transform; r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.offsetMin=r.offsetMax=Vector2.zero;
            }
            var settings=new FxSettings { LookEditor=true };
            int changes=0, tests=0; bool canTest=true;
            var hooks=new SettingsModel.Hooks { Version="1.1.0", Changed=()=>changes++, Close=()=>{ }, Reset=()=>settings.CopyFrom(new FxSettings()), CanTest=()=>canTest, Test=k=>{ tests++; return true; },
                Status=()=>"Settings saved", HasMod=names=>Array.Exists(names,n=>n=="Waterfall"||n=="Scatterer"||n=="EVEManager"||n=="BDArmory"||n=="GroundBlastFx"||n=="BlastFX") };
            var font=Resources.GetBuiltinResource<Font>("Arial.ttf");
            var panel=new SettingsPanel(SettingsModel.Build(settings,hooks),font,camera,1);
            panel.Visible=true;
            var events=new GameObject("UI probe events").AddComponent<PreviewEventSystem>();
            events.Activate(); // Editor mode does not run the play-mode event loop.
            var submit=new BaseEventData(events);
            Transform general=panel.Root.transform.Find("window/viewport/page General");
            var enabled=Deep(general,"row Enable effects","checkbox").gameObject;
            ExecuteEvents.Execute(enabled,submit,ExecuteEvents.submitHandler);
            Require(!settings.Enabled&&changes==1,"checkbox keyboard submit must update the shared settings once");
            ExecuteEvents.Execute(enabled,submit,ExecuteEvents.submitHandler);
            var quality=Deep(general,"row Quality","option High").gameObject;
            ExecuteEvents.Execute(quality,submit,ExecuteEvents.submitHandler);
            var high=new FxSettings(); high.ApplyPreset(Quality.High);
            Require(settings.Quality==Quality.High&&settings.MaxEvents==high.MaxEvents,"quality choice must apply its preset");
            var intensity=Deep(general,"row Intensity","slider").GetComponent<Slider>();
            intensity.value=1.63f;
            Require(Math.Abs(settings.Intensity-1.65)<0.001,"slider must snap and update the shared value");
            var test=Deep(general,"row Test explosion","action").gameObject;
            canTest=false; panel.Tick(0);
            ExecuteEvents.Execute(test,submit,ExecuteEvents.submitHandler);
            Require(tests==0&&!test.GetComponent<Button>().interactable,"unavailable preview must stay disabled");
            canTest=true; panel.Tick(0);
            ExecuteEvents.Execute(test,submit,ExecuteEvents.submitHandler);
            Require(tests==1,"available preview must call its hook once");
            panel.Page=4;
            var limit=Deep(panel.Root.transform.Find("window/viewport/page Performance"),"row Simultaneous explosions","slider").GetComponent<Slider>();
            limit.value=9;
            Require(settings.Quality==Quality.Custom&&settings.MaxEvents==9,"manual limit must switch to Custom");
            settings.CopyFrom(new FxSettings());
            var fresh=new FxSettings();
            Require(settings.Quality==fresh.Quality&&settings.Explosions&&settings.MaxEvents==fresh.MaxEvents,"reset must keep the shared object and restore defaults");
            Transform lookPage=panel.Root.transform.Find("window/viewport/page Look");
            panel.Page=6; panel.Tick(0);
            var dial=Deep(lookPage,"row Turbulence","slider").GetComponent<Slider>();
            dial.value=1.5f;
            Require(Math.Abs(settings.Looks[LookKind.Tank].Turbulence-1.5)<0.001,"look dial must update the edited profile");
            var resetLook=Deep(lookPage,"row Reset this profile","action").gameObject;
            ExecuteEvents.Execute(resetLook,submit,ExecuteEvents.submitHandler); panel.Tick(0);
            Require(settings.Looks.IsDefault(LookKind.Tank)&&Math.Abs(dial.value-(float)LookProfile.Default(LookKind.Tank).Turbulence)<0.001,
                "resetting a look profile must restore its dials and the sliders");
            var image=new Texture2D(W,H,TextureFormat.RGB24,false);
            // The opening animation, a few frames in.
            panel.Visible=false; for(int i=0;i<30;i++) panel.Tick(1/60f); panel.Visible=true;
            for(int f=0;f<3;f++)
            {
                for(int i=0;i<4;i++) { panel.Tick(1/60f); Canvas.ForceUpdateCanvases(); }
                camera.Render(); RenderTexture.active=target; image.ReadPixels(new Rect(0,0,W,H),0,0); image.Apply();
                File.WriteAllBytes(Path.Combine(output,"ui-open-"+f+".png"),image.EncodeToPNG());
            }
            for(int p=0;p<7;p++)
            {
                panel.Page=p;
                for(int i=0;i<90;i++) { panel.Tick(1/60f); Canvas.ForceUpdateCanvases(); }
                camera.Render(); RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,W,H),0,0); image.Apply();
                File.WriteAllBytes(Path.Combine(output,"ui-page-"+p+".png"),image.EncodeToPNG());
            }
            Require(settings.Quality==fresh.Quality&&settings.Explosions&&settings.MaxEvents==fresh.MaxEvents,"drawing and switching pages must not edit settings");
            panel.Page=0; panel.Window.anchoredPosition=new Vector2(100000,-100000); panel.SetScale(2.5f);
            Canvas.ForceUpdateCanvases(); RequireFits(panel,camera,W,H);
            camera.Render(); RenderTexture.active=target; image.ReadPixels(new Rect(0,0,W,H),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(output,"ui-scale-2.5.png"),image.EncodeToPNG());
            var small=new RenderTexture(800,600,24,RenderTextureFormat.ARGB32); camera.targetTexture=small;
            panel.Tick(0); Canvas.ForceUpdateCanvases(); RequireFits(panel,camera,800,600);
            camera.Render(); RenderTexture.active=small;
            var smallImage=new Texture2D(800,600,TextureFormat.RGB24,false); smallImage.ReadPixels(new Rect(0,0,800,600),0,0); smallImage.Apply();
            File.WriteAllBytes(Path.Combine(output,"ui-small-screen.png"),smallImage.EncodeToPNG());
            events.SetSelectedGameObject(enabled); Require(panel.HasKeyboardFocus,"selected widget must report keyboard focus");
            panel.Visible=false;
            for(int i=0;i<30;i++) panel.Tick(1/60f);
            Require(!panel.Root.activeSelf&&!panel.Contains(Vector2.zero)&&events.currentSelectedGameObject==null,"closing must release the pointer and keyboard focus immediately");
            File.WriteAllText(Path.Combine(output,"ui-validation.txt"),"Native widgets: toggle submit, quality preset, slider snapping, disabled and enabled preview, custom limits passed.\nWindow bounds: oversized scale and 800x600 resize passed.\nImmediate close passed.\nEditor checks only; KSP input locks and profile persistence need a game test.\n");
            // The light appearance.
            camera.targetTexture=target;
            // Every style, in each layout (the first panel hidden meanwhile); the general page and compatibility.
            panel.Root.SetActive(false);
            foreach(var style in new[]{ new[]{0,0}, new[]{0,1}, new[]{0,2}, new[]{0,3}, new[]{0,4}, new[]{1,1}, new[]{1,3}, new[]{2,2}, new[]{2,4} })
            {
                var styled=new FxSettings { LookEditor=true, UiLayout=style[0], UiStyle=style[1] };
                SettingsPanel.Position=Vector2.zero;
                var other=new SettingsPanel(SettingsModel.Build(styled,hooks),font,camera,1);
                other.Visible=true;
                foreach(int lp in new[]{0,5})
                {
                    other.Page=lp;
                    for(int i=0;i<60;i++) { other.Tick(1/60f); Canvas.ForceUpdateCanvases(); }
                    camera.Render(); RenderTexture.active=target; image.ReadPixels(new Rect(0,0,W,H),0,0); image.Apply();
                    File.WriteAllBytes(Path.Combine(output,"ui-style-"+style[0]+"-"+style[1]+"-p"+lp+".png"),image.EncodeToPNG());
                }
                UnityEngine.Object.DestroyImmediate(other.Root);   // (Destroy waits for a frame the batch editor never runs)
            }
            panel.Root.SetActive(true);
            RenderTexture.active=null;
            var icon=UiArt.Icon(64); File.WriteAllBytes(Path.Combine(output,"ui-icon.png"),icon.EncodeToPNG());
            Debug.Log("[VEFX] UI preview rendered: "+output);
            EditorApplication.Exit(0);
        }
        catch(Exception ex) { Debug.LogError("[VEFX] "+ex); EditorApplication.Exit(1); }
    }
    static void Require(bool condition,string message) { if(!condition) throw new Exception("UI probe: "+message); }
    static Transform Deep(Transform root,string row,string child)
    {
        if(root==null) throw new Exception("UI probe: page not found for "+row);
        foreach(Transform t in root.GetComponentsInChildren<Transform>(true))
            if(t.name==row) { Transform c=t.Find(child); if(c!=null) return c; }
        throw new Exception("UI probe: "+row+"/"+child+" not found");
    }
    static void RequireFits(SettingsPanel panel,Camera camera,int width,int height)
    {
        var corners=new Vector3[4]; panel.Window.GetWorldCorners(corners);
        foreach(Vector3 corner in corners)
        {
            Vector3 p=camera.WorldToScreenPoint(corner);
            Require(p.x>=-0.5f&&p.y>=-0.5f&&p.x<=width+0.5f&&p.y<=height+0.5f,"window must fit the viewport");
        }
    }
}
public sealed class PreviewEventSystem : EventSystem
{
    internal void Activate() { if(EventSystem.current!=this) base.OnEnable(); }
}
