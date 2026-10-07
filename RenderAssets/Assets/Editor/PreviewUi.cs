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
            var settings=new FxSettings();
            int changes=0, tests=0; bool canTest=true;
            var hooks=new SettingsModel.Hooks { Version="0.9.0", Changed=()=>changes++, Close=()=>{ }, Reset=()=>settings.CopyFrom(new FxSettings()), CanTest=()=>canTest, Test=k=>{ tests++; return true; },
                Status=()=>"Réglages enregistrés" };
            var font=Resources.GetBuiltinResource<Font>("Arial.ttf");
            var panel=new SettingsPanel(SettingsModel.Build(settings,hooks),font,camera,1);
            panel.Visible=true;
            var events=new GameObject("UI probe events").AddComponent<PreviewEventSystem>();
            events.Activate(); // Editor mode does not run the play-mode event loop.
            var submit=new BaseEventData(events);
            string general="window/viewport/page Général/";
            var enabled=panel.Root.transform.Find(general+"row Activer les effets/checkbox").gameObject;
            ExecuteEvents.Execute(enabled,submit,ExecuteEvents.submitHandler);
            Require(!settings.Enabled&&changes==1,"checkbox keyboard submit must update the shared settings once");
            ExecuteEvents.Execute(enabled,submit,ExecuteEvents.submitHandler);
            var quality=panel.Root.transform.Find(general+"row Qualité/option Haute").gameObject;
            ExecuteEvents.Execute(quality,submit,ExecuteEvents.submitHandler);
            Require(settings.Quality==Quality.High&&settings.MaxEvents==20,"quality choice must apply its preset");
            var intensity=panel.Root.transform.Find(general+"row Intensité/slider").GetComponent<Slider>();
            intensity.value=1.63f;
            Require(Math.Abs(settings.Intensity-1.65)<0.001,"slider must snap and update the shared value");
            var test=panel.Root.transform.Find(general+"row Explosion de test/action").gameObject;
            canTest=false; panel.Tick(0);
            ExecuteEvents.Execute(test,submit,ExecuteEvents.submitHandler);
            Require(tests==0&&!test.GetComponent<Button>().interactable,"unavailable preview must stay disabled");
            canTest=true; panel.Tick(0);
            ExecuteEvents.Execute(test,submit,ExecuteEvents.submitHandler);
            Require(tests==1,"available preview must call its hook once");
            panel.Page=4;
            var limit=panel.Root.transform.Find("window/viewport/page Performance/row Explosions simultanées/slider").GetComponent<Slider>();
            limit.value=9;
            Require(settings.Quality==Quality.Custom&&settings.MaxEvents==9,"manual limit must switch to Custom");
            settings.CopyFrom(new FxSettings());
            Require(settings.Quality==Quality.Medium&&settings.Explosions&&settings.MaxEvents==12,"reset must keep the shared object and restore defaults");
            var image=new Texture2D(W,H,TextureFormat.RGB24,false);
            for(int p=0;p<5;p++)
            {
                panel.Page=p;
                for(int i=0;i<90;i++) { panel.Tick(1/60f); Canvas.ForceUpdateCanvases(); }
                camera.Render(); RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,W,H),0,0); image.Apply();
                File.WriteAllBytes(Path.Combine(output,"ui-page-"+p+".png"),image.EncodeToPNG());
            }
            Require(settings.Quality==Quality.Medium&&settings.Explosions&&settings.MaxEvents==12,"drawing and switching pages must not edit settings");
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
            Require(!panel.Root.activeSelf&&!panel.Contains(Vector2.zero)&&events.currentSelectedGameObject==null,"closing must release the pointer and keyboard focus immediately");
            File.WriteAllText(Path.Combine(output,"ui-validation.txt"),"Native widgets: toggle submit, quality preset, slider snapping, disabled and enabled preview, custom limits passed.\nWindow bounds: oversized scale and 800x600 resize passed.\nImmediate close passed.\nEditor checks only; KSP input locks and profile persistence need a game test.\n");
            RenderTexture.active=null;
            var icon=UiArt.Icon(64); File.WriteAllBytes(Path.Combine(output,"ui-icon.png"),icon.EncodeToPNG());
            Debug.Log("[VEFX] UI preview rendered: "+output);
            EditorApplication.Exit(0);
        }
        catch(Exception ex) { Debug.LogError("[VEFX] "+ex); EditorApplication.Exit(1); }
    }
    static void Require(bool condition,string message) { if(!condition) throw new Exception("UI probe: "+message); }
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
