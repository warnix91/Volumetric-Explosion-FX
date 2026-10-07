using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace VolumetricExplosionFX.Rendering
{
    internal enum UiGlyph { General, Blast, Flame, Wreck, Gauge, Spark }
    internal enum UiRowKind { Section, Toggle, Slider, Choice, Action, Info }
    internal sealed class UiRow
    {
        internal UiRowKind Kind; internal string Label, Hint;
        internal Func<bool> GetBool; internal Action<bool> SetBool;
        internal Func<float> GetValue; internal Action<float> SetValue;
        internal float Min, Max, Step; internal Func<float,string> Format;
        internal string[] Options; internal Func<int> GetChoice; internal Action<int> SetChoice;
        internal string Button; internal Action Click; internal Func<string> Text;
        internal Func<bool> Available;
    }
    internal sealed class UiPage { internal string Title; internal UiGlyph Glyph; internal readonly List<UiRow> Rows=new List<UiRow>(); }
    internal sealed class UiModel
    {
        internal string Title, Subtitle, Reset;
        internal readonly List<UiPage> Pages=new List<UiPage>();
        internal Action Close, ResetAll, Changed;
        internal Func<string> Status;
    }
    internal sealed class UiWindowDrag : MonoBehaviour, IDragHandler
    {
        internal Action<Vector2> Move;
        public void OnDrag(PointerEventData e)
        { if(e.button==PointerEventData.InputButton.Left&&Move!=null) Move(e.delta); }
    }
    internal static class UiArt
    {
        static readonly Dictionary<string,Sprite> sprites=new Dictionary<string,Sprite>();
        static readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        internal static void Release()
        {
            foreach(UnityEngine.Object o in owned) if(o!=null) UnityEngine.Object.Destroy(o);
            owned.Clear(); sprites.Clear();
        }
        static Sprite Make(string key,int size,float border,Func<float,float,Color> pixel)
        {
            Sprite s;
            if(sprites.TryGetValue(key,out s)&&s!=null) return s;
            var t=new Texture2D(size,size,TextureFormat.RGBA32,false);
            t.name="Original settings art "+key; t.wrapMode=TextureWrapMode.Clamp; t.filterMode=FilterMode.Bilinear;
            var c=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++) c[y*size+x]=pixel(x+0.5f,y+0.5f);
            t.SetPixels(c); t.Apply(false,true);
            s=Sprite.Create(t,new Rect(0,0,size,size),new Vector2(0.5f,0.5f),100,0,SpriteMeshType.FullRect,new Vector4(border,border,border,border));
            owned.Add(t); owned.Add(s); sprites[key]=s; return s;
        }
        internal static Sprite Fill(int radius)
        {
            int n=2*radius+4;
            return Make("fill"+radius,n,radius+2,(x,y)=>{
                float qx=Mathf.Abs(x-n*0.5f)-(n*0.5f-radius), qy=Mathf.Abs(y-n*0.5f)-(n*0.5f-radius);
                float a=Mathf.Max(qx,0), b=Mathf.Max(qy,0);
                float d=Mathf.Sqrt(a*a+b*b)+Mathf.Min(Mathf.Max(qx,qy),0)-radius;
                return new Color(1,1,1,Mathf.Clamp01(0.5f-d)); });
        }
        internal static Sprite Check()
        {
            return Make("check",24,0,(x,y)=>{
                var p=new Vector2(x,y);
                float d=Mathf.Min(Segment(p,new Vector2(4,12),new Vector2(10,6)),Segment(p,new Vector2(10,6),new Vector2(20,19)));
                return new Color(1,1,1,Mathf.Clamp01(2-d)); });
        }
        static float Segment(Vector2 p,Vector2 a,Vector2 b)
        { Vector2 v=b-a; return (p-a-v*Mathf.Clamp01(Vector2.Dot(p-a,v)/v.sqrMagnitude)).magnitude; }
        static bool Flame(float x,float y)
        {
            if(x*x+(y+0.32f)*(y+0.32f)<0.36f) return true;
            if(y<-0.32f||y>0.95f) return false;
            float h=(y+0.32f)/1.27f, w=0.6f*Mathf.Pow(1-h,1.15f), bend=0.2f*Mathf.Sin(h*3.4f)*h;
            return Mathf.Abs(x-bend)<w;
        }
        internal static Texture2D Icon(int size)
        {
            var t=new Texture2D(size,size,TextureFormat.RGBA32,false); t.name="Original VolumetricExplosionFX icon"; t.wrapMode=TextureWrapMode.Clamp;
            var c=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                int outer=0, inner=0;
                for(int j=0;j<4;j++) for(int i=0;i<4;i++)
                {
                    float u=((x+(i+0.5f)/4)/size)*2.3f-1.15f, v=((y+(j+0.5f)/4)/size)*2.3f-1.15f;
                    if(Flame(u,v)) outer++;
                    if(Flame(u/0.5f,(v+0.4f)/0.5f)) inner++;
                }
                Color fire=Color.Lerp(new Color(0.95f,0.25f,0.05f),new Color(1,0.78f,0.25f),(float)y/size);
                Color col=Color.Lerp(fire,new Color(1,0.97f,0.85f),inner/16f); col.a=outer/16f; c[y*size+x]=col;
            }
            t.SetPixels(c); t.Apply(false,false); return t;
        }
    }
    internal sealed class SettingsPanel : IDisposable
    {
        static readonly Color Ink=new Color(0.86f,0.87f,0.86f), Soft=new Color(0.60f,0.63f,0.64f);
        static readonly Color Accent=new Color(0.85f,0.70f,0.40f), Face=new Color(0.23f,0.25f,0.26f);
        static readonly Color Rule=new Color(0.30f,0.32f,0.33f), Selected=new Color(0.34f,0.32f,0.26f);
        internal const float Width=620, Height=530;
        const float Pad=16, Controls=282;
        readonly UiModel model; readonly Font font; readonly Camera camera;
        readonly CanvasScaler scaler;
        readonly List<RectTransform> pages=new List<RectTransform>();
        readonly List<Button> tabs=new List<Button>();
        readonly List<GameObject> tabMarks=new List<GameObject>();
        readonly List<List<Action>> updates=new List<List<Action>>();
        readonly ScrollRect scroll; readonly Text status;
        readonly Image scrollTrack; readonly RectTransform scrollThumb;
        internal readonly GameObject Root; internal readonly RectTransform Window;
        internal static Vector2 Position;
        int page, screenW=-1, screenH=-1; float preferredScale, appliedScale;
        bool visible;
        internal bool Visible
        {
            get { return visible; }
            set { if(!value) ReleaseFocus(); visible=value; Root.SetActive(value); if(value) { Fit(); Refresh(); } }
        }
        internal bool Shown { get { return Root.activeSelf; } }
        internal int Page { get { return page; } set { Select(Mathf.Clamp(value,0,pages.Count-1)); } }
        internal SettingsPanel(UiModel model,Font font,Camera camera,float scale)
        {
            this.model=model; this.font=font; this.camera=camera; preferredScale=scale;
            Root=new GameObject("VolumetricExplosionFX settings",typeof(RectTransform));
            var canvas=Root.AddComponent<Canvas>();
            canvas.renderMode=camera!=null?RenderMode.ScreenSpaceCamera:RenderMode.ScreenSpaceOverlay;
            if(camera!=null) { canvas.worldCamera=camera; canvas.planeDistance=1; }
            canvas.sortingOrder=60;
            scaler=Root.AddComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize; scaler.referencePixelsPerUnit=100;
            Root.AddComponent<GraphicRaycaster>();
            Window=Rect("window",Root.transform); Window.anchorMin=Window.anchorMax=Window.pivot=new Vector2(0.5f,0.5f);
            Window.sizeDelta=new Vector2(Width,Height); Window.anchoredPosition=Position;
            Stretch(Img("border",Window,UiArt.Fill(5),Rule).rectTransform,0,0,0,0);
            Stretch(Img("body",Window,UiArt.Fill(4),new Color(0.15f,0.17f,0.18f,0.98f)).rectTransform,1,1,1,1);
            var header=Img("title bar",Window,UiArt.Fill(4),new Color(0.20f,0.22f,0.23f)); Top(header.rectTransform,1,44,1,1); header.raycastTarget=true;
            header.gameObject.AddComponent<UiWindowDrag>().Move=delta=>{ Window.anchoredPosition+=delta/Mathf.Max(appliedScale,0.01f); ClampPosition(); };
            At(Label("title",header.transform,model.Title,17,FontStyle.Normal,Ink).rectTransform,Pad,-12,350,24);
            var subtitle=Label("version",header.transform,model.Subtitle,11,FontStyle.Normal,Soft); subtitle.alignment=TextAnchor.MiddleRight;
            At(subtitle.rectTransform,355,-12,207,24);
            Button close=Button("close",header.transform,"×",()=>{ if(model.Close!=null) model.Close(); });
            At(close.GetComponent<RectTransform>(),Width-42,-9,26,26);
            var nav=Rect("tabs",Window); Top(nav,51,34,Pad,Pad);
            for(int i=0;i<model.Pages.Count;i++)
            {
                int index=i;
                Button tab=Button("tab "+model.Pages[i].Title,nav,model.Pages[i].Title,()=>Select(index));
                RectTransform r=tab.GetComponent<RectTransform>();
                r.anchorMin=new Vector2((float)i/model.Pages.Count,0); r.anchorMax=new Vector2((float)(i+1)/model.Pages.Count,1);
                r.offsetMin=new Vector2(0,0); r.offsetMax=new Vector2(-3,0);
                var mark=Img("selection",r,null,Accent); Bottom(mark.rectTransform,0,2,4,4);
                tabs.Add(tab); tabMarks.Add(mark.gameObject);
            }
            var view=Rect("viewport",Window); Stretch(view,Pad,46,Pad+12,99);
            view.gameObject.AddComponent<RectMask2D>();
            var viewHit=view.gameObject.AddComponent<Image>(); viewHit.color=Color.clear;
            scroll=view.gameObject.AddComponent<ScrollRect>(); scroll.horizontal=false; scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=26; scroll.viewport=view;
            for(int i=0;i<model.Pages.Count;i++) BuildPage(model.Pages[i],view);
            scrollTrack=Img("scroll track",Window,null,Face);
            var track=scrollTrack.rectTransform; track.anchorMin=new Vector2(1,0); track.anchorMax=new Vector2(1,1);
            track.offsetMin=new Vector2(-Pad-5,49); track.offsetMax=new Vector2(-Pad-1,-102);
            scrollThumb=Img("scroll thumb",track,null,Soft).rectTransform;
            scrollThumb.anchorMin=new Vector2(0,1); scrollThumb.anchorMax=new Vector2(1,1); scrollThumb.pivot=new Vector2(0.5f,1);
            Bottom(Img("footer rule",Window,null,Rule).rectTransform,39,1,Pad,Pad);
            if(model.Reset!=null)
            {
                Button reset=Button("reset",Window,model.Reset,()=>{ if(model.ResetAll!=null) model.ResetAll(); Changed(); });
                RectTransform r=reset.GetComponent<RectTransform>(); r.anchorMin=r.anchorMax=r.pivot=Vector2.zero;
                r.anchoredPosition=new Vector2(Pad,8); r.sizeDelta=new Vector2(112,25);
            }
            status=Label("status",Window,"",11,FontStyle.Normal,Soft); status.alignment=TextAnchor.MiddleRight;
            var sr=status.rectTransform; sr.anchorMin=sr.anchorMax=sr.pivot=new Vector2(1,0); sr.sizeDelta=new Vector2(Width-160,30); sr.anchoredPosition=new Vector2(-Pad,6);
            Select(0); SetLayer(Root.transform,5); Fit(); Root.SetActive(false);
        }
        internal void SetScale(float scale)
        { if(Mathf.Abs(preferredScale-scale)<0.001f) return; preferredScale=scale; screenW=-1; Fit(); }
        void Fit()
        {
            int w=camera!=null?camera.pixelWidth:Screen.width, h=camera!=null?camera.pixelHeight:Screen.height;
            if(w==screenW&&h==screenH) return;
            screenW=w; screenH=h;
            appliedScale=Mathf.Max(0.1f,Mathf.Min(Mathf.Clamp(preferredScale,0.5f,2.5f),Mathf.Min((w-24)/Width,(h-24)/Height)));
            scaler.scaleFactor=appliedScale; ClampPosition();
        }
        void ClampPosition()
        {
            float x=Mathf.Max(0,(screenW-24)/appliedScale*0.5f-Width*0.5f), y=Mathf.Max(0,(screenH-24)/appliedScale*0.5f-Height*0.5f);
            Vector2 p=Window.anchoredPosition; p.x=Mathf.Clamp(p.x,-x,x); p.y=Mathf.Clamp(p.y,-y,y);
            Window.anchoredPosition=p; Position=p;
        }
        void Changed() { if(model.Changed!=null) model.Changed(); Refresh(); }
        void Select(int index)
        {
            page=index;
            for(int i=0;i<pages.Count;i++)
            {
                pages[i].gameObject.SetActive(i==page); tabMarks[i].SetActive(i==page);
                Colours(tabs[i],i==page?Selected:Color.clear);
            }
            if(page<pages.Count) { scroll.content=pages[page]; pages[page].anchoredPosition=Vector2.zero; }
            scroll.velocity=Vector2.zero; Refresh();
        }
        void Refresh()
        {
            if(page<updates.Count) foreach(Action update in updates[page]) update();
            if(status!=null&&model.Status!=null) TextIfChanged(status,model.Status());
        }
        internal void Tick(float dt)
        {
            if(!visible) return;
            Fit(); Refresh();
            RectTransform content=scroll.content;
            float viewH=scroll.viewport.rect.height, contentH=content!=null?content.sizeDelta.y:0;
            bool scrolls=contentH>viewH+1;
            scrollTrack.enabled=scrolls; scrollThumb.gameObject.SetActive(scrolls);
            if(scrolls)
            {
                float trackH=scrollTrack.rectTransform.rect.height, h=Mathf.Max(24,trackH*viewH/contentH);
                float pos=Mathf.Clamp01(content.anchoredPosition.y/(contentH-viewH));
                scrollThumb.sizeDelta=new Vector2(0,h); scrollThumb.anchoredPosition=new Vector2(0,-(trackH-h)*pos);
            }
        }
        void BuildPage(UiPage p,RectTransform view)
        {
            var content=Rect("page "+p.Title,view);
            content.anchorMin=new Vector2(0,1); content.anchorMax=new Vector2(1,1); content.pivot=new Vector2(0.5f,1);
            var refresh=new List<Action>(); updates.Add(refresh);
            float y=2;
            foreach(UiRow row in p.Rows) y+=Row(row,content,y,refresh)+3;
            content.sizeDelta=new Vector2(0,y+4); pages.Add(content);
        }
        float Row(UiRow row,RectTransform parent,float y,List<Action> refresh)
        {
            if(row.Kind==UiRowKind.Section)
            {
                Top(Label("section",parent,row.Label,12,FontStyle.Bold,Soft).rectTransform,y+8,18,0,0); return 27;
            }
            float width=Width-2*Pad-12, controlLeft=width-Controls;
            var band=Rect("row "+row.Label,parent);
            bool longRow=row.Kind==UiRowKind.Action||row.Kind==UiRowKind.Info;
            float textW=longRow?width:row.Kind==UiRowKind.Toggle?width-40:controlLeft-12;
            var label=Label("label",band,row.Label,13,FontStyle.Normal,Ink);
            label.horizontalOverflow=HorizontalWrapMode.Wrap;
            At(label.rectTransform,0,-7,textW,20);
            float labelH=Mathf.Max(20,Mathf.Ceil(label.preferredHeight));
            label.rectTransform.sizeDelta=new Vector2(textW,labelH);
            float bodyTop=7+labelH;
            float hintH=0;
            if(!string.IsNullOrEmpty(row.Hint))
            {
                var hint=Label("hint",band,row.Hint,10,FontStyle.Normal,Soft); hint.horizontalOverflow=HorizontalWrapMode.Wrap;
                At(hint.rectTransform,0,-bodyTop,textW,16); hintH=Mathf.Ceil(hint.preferredHeight);
                hint.rectTransform.sizeDelta=new Vector2(textW,hintH);
            }
            float height=Mathf.Max(44,bodyTop+hintH+8);
            var selectables=new List<Selectable>();
            switch(row.Kind)
            {
                case UiRowKind.Toggle:
                {
                    var box=Img("checkbox",band,UiArt.Fill(2),Face); At(box.rectTransform,width-25,-10,22,22); box.raycastTarget=true;
                    Stretch(Img("rim",box.transform,UiArt.Fill(2),Rule).rectTransform,0,0,0,0);
                    Stretch(Img("inner",box.transform,UiArt.Fill(1),Face).rectTransform,1,1,1,1);
                    var mark=Img("check",box.transform,UiArt.Check(),Accent); Stretch(mark.rectTransform,2,2,2,2);
                    var toggle=box.gameObject.AddComponent<Toggle>(); toggle.targetGraphic=box; toggle.graphic=mark; toggle.toggleTransition=Toggle.ToggleTransition.None;
                    toggle.SetIsOnWithoutNotify(row.GetBool());
                    toggle.onValueChanged.AddListener(v=>{ if(toggle.IsActive()&&Available(row)&&v!=row.GetBool()) { row.SetBool(v); Changed(); } });
                    refresh.Add(()=>toggle.SetIsOnWithoutNotify(row.GetBool())); selectables.Add(toggle); break;
                }
                case UiRowKind.Slider:
                {
                    Slider(row,band,controlLeft,width,refresh,selectables); break;
                }
                case UiRowKind.Choice:
                { Choices(row,band,controlLeft,8,Controls,refresh,selectables); break; }
                case UiRowKind.Action:
                {
                    float top=bodyTop+hintH+8;
                    Choices(row,band,0,top,width-92,refresh,selectables);
                    Button b=Button("action",band,row.Button,()=>{ if(Available(row)&&row.Click!=null) row.Click(); });
                    At(b.GetComponent<RectTransform>(),width-82,-top,82,27); selectables.Add(b); height=top+35; break;
                }
                default:
                {
                    var text=Label("info",band,row.Text!=null?row.Text():"",11,FontStyle.Normal,Soft); text.horizontalOverflow=HorizontalWrapMode.Wrap;
                    float top=bodyTop+hintH; At(text.rectTransform,0,-top,width,16);
                    float h=Mathf.Ceil(text.preferredHeight)+4; text.rectTransform.sizeDelta=new Vector2(width,h); height=top+h+8;
                    refresh.Add(()=>{ if(row.Text!=null) TextIfChanged(text,row.Text()); }); break;
                }
            }
            Top(band,y,height,0,0); Bottom(Img("divider",band,null,new Color(Rule.r,Rule.g,Rule.b,0.45f)).rectTransform,0,1,0,0);
            var fade=band.gameObject.AddComponent<CanvasGroup>(); bool? oldAvailable=null;
            refresh.Add(()=>{
                bool on=Available(row); if(oldAvailable==on) return; oldAvailable=on;
                fade.alpha=on?1:0.45f;
                foreach(Selectable s in selectables) s.interactable=on;
            });
            return height;
        }
        static bool Available(UiRow row) { return row.Available==null||row.Available(); }
        void Slider(UiRow row,RectTransform band,float left,float width,List<Action> refresh,List<Selectable> selectables)
        {
            var r=Rect("slider",band); At(r,left,-8,Controls-65,28);
            var hit=r.gameObject.AddComponent<Image>(); hit.color=Color.clear;
            var track=Img("track",r,UiArt.Fill(1),Face); Stretch(track.rectTransform,5,12,5,12);
            var fillArea=Rect("fill area",r); Stretch(fillArea,5,12,5,12);
            var fill=Img("fill",fillArea,UiArt.Fill(1),Accent); Stretch(fill.rectTransform,0,0,0,0);
            var handleArea=Rect("handle area",r);
            handleArea.anchorMin=new Vector2(0,0.5f); handleArea.anchorMax=new Vector2(1,0.5f);
            handleArea.offsetMin=new Vector2(5,-8); handleArea.offsetMax=new Vector2(-5,8);
            var handle=Img("handle",handleArea,UiArt.Fill(2),Ink); handle.raycastTarget=true;
            handle.rectTransform.anchorMin=handle.rectTransform.anchorMax=new Vector2(0,0.5f);
            handle.rectTransform.sizeDelta=new Vector2(10,0);
            var slider=r.gameObject.AddComponent<Slider>(); slider.minValue=row.Min; slider.maxValue=row.Max;
            slider.fillRect=fill.rectTransform; slider.handleRect=handle.rectTransform; slider.targetGraphic=handle;
            slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.SetValueWithoutNotify(row.GetValue());
            slider.onValueChanged.AddListener(v=>{
                if(!slider.IsActive()||!Available(row)) return;
                if(row.Step>0) v=row.Min+Mathf.Round((v-row.Min)/row.Step)*row.Step;
                v=Mathf.Clamp(v,row.Min,row.Max);
                if(Mathf.Abs(v-row.GetValue())>0.0001f) { row.SetValue(v); Changed(); }
            });
            var text=Label("value",band,"",12,FontStyle.Normal,Ink); text.alignment=TextAnchor.MiddleRight;
            At(text.rectTransform,width-61,-8,61,28);
            float last=float.NaN;
            refresh.Add(()=>{
                float v=row.GetValue(); if(v==last) return; last=v;
                slider.SetValueWithoutNotify(v); TextIfChanged(text,row.Format!=null?row.Format(v):v.ToString("0.##"));
            });
            selectables.Add(slider);
        }
        void Choices(UiRow row,RectTransform band,float left,float top,float width,List<Action> refresh,List<Selectable> selectables)
        {
            if(row.Options==null||row.Options.Length==0) return;
            var buttons=new Button[row.Options.Length]; float w=width/buttons.Length;
            for(int i=0;i<buttons.Length;i++)
            {
                int index=i;
                Button b=Button("option "+row.Options[i],band,row.Options[i],()=>{
                    if(Available(row)&&row.GetChoice()!=index) { row.SetChoice(index); Changed(); }
                });
                At(b.GetComponent<RectTransform>(),left+i*w,-top,w-3,27); buttons[i]=b; selectables.Add(b);
            }
            int last=int.MinValue;
            refresh.Add(()=>{
                int chosen=row.GetChoice(); if(chosen==last) return; last=chosen;
                for(int i=0;i<buttons.Length;i++)
                {
                    Colours(buttons[i],i==chosen?Selected:Face);
                    buttons[i].GetComponentInChildren<Text>().color=i==chosen?Accent:Ink;
                }
            });
        }
        Button Button(string name,Transform parent,string text,Action action)
        {
            var face=Img(name,parent,UiArt.Fill(2),Color.white); face.raycastTarget=true;
            var b=face.gameObject.AddComponent<Button>(); b.targetGraphic=face;
            Colours(b,Face);
            var label=Label("label",face.transform,text,11,FontStyle.Normal,Ink); label.alignment=TextAnchor.MiddleCenter; Stretch(label.rectTransform,3,0,3,0);
            b.onClick.AddListener(()=>action()); return b;
        }
        static void Colours(Selectable s,Color normal)
        {
            ColorBlock c=s.colors; c.normalColor=normal;
            c.highlightedColor=new Color(0.34f,0.36f,0.37f); c.selectedColor=c.highlightedColor;
            c.pressedColor=new Color(0.40f,0.42f,0.43f); c.disabledColor=normal;
            c.colorMultiplier=1; c.fadeDuration=0.06f; s.colors=c;
        }
        static void TextIfChanged(Text t,string value) { if(t.text!=value) t.text=value??""; }
        static void SetLayer(Transform t,int layer) { t.gameObject.layer=layer; for(int i=0;i<t.childCount;i++) SetLayer(t.GetChild(i),layer); }
        static RectTransform Rect(string name,Transform parent)
        { var go=new GameObject(name,typeof(RectTransform)); var r=(RectTransform)go.transform; r.SetParent(parent,false); return r; }
        static Image Img(string name,Transform parent,Sprite sprite,Color colour)
        {
            var r=Rect(name,parent); var i=r.gameObject.AddComponent<Image>(); i.sprite=sprite;
            i.type=sprite!=null&&sprite.border!=Vector4.zero?Image.Type.Sliced:Image.Type.Simple; i.color=colour; i.raycastTarget=false; return i;
        }
        Text Label(string name,Transform parent,string text,int size,FontStyle style,Color colour)
        {
            var r=Rect(name,parent); var t=r.gameObject.AddComponent<Text>(); t.font=font; t.fontSize=size; t.fontStyle=style;
            t.color=colour; t.text=text??""; t.raycastTarget=false; t.supportRichText=false; t.alignment=TextAnchor.UpperLeft;
            t.horizontalOverflow=HorizontalWrapMode.Overflow; t.verticalOverflow=VerticalWrapMode.Overflow; return t;
        }
        static void At(RectTransform r,float x,float y,float w,float h)
        { r.anchorMin=r.anchorMax=new Vector2(0,1); r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,y); r.sizeDelta=new Vector2(w,h); }
        static void Stretch(RectTransform r,float left,float bottom,float right,float top)
        { r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.pivot=new Vector2(0.5f,0.5f); r.offsetMin=new Vector2(left,bottom); r.offsetMax=new Vector2(-right,-top); }
        static void Top(RectTransform r,float from,float height,float left,float right)
        { r.anchorMin=new Vector2(0,1); r.anchorMax=new Vector2(1,1); r.pivot=new Vector2(0.5f,1); r.offsetMin=new Vector2(left,-from-height); r.offsetMax=new Vector2(-right,-from); }
        static void Bottom(RectTransform r,float from,float height,float left,float right)
        { r.anchorMin=new Vector2(0,0); r.anchorMax=new Vector2(1,0); r.pivot=new Vector2(0.5f,0); r.offsetMin=new Vector2(left,from); r.offsetMax=new Vector2(-right,from+height); }
        internal bool Contains(Vector2 screen)
        { return visible&&Root.activeSelf&&RectTransformUtility.RectangleContainsScreenPoint(Window,screen,camera); }
        internal bool HasKeyboardFocus
        {
            get
            {
                GameObject selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
                return visible&&selected!=null&&selected.transform.IsChildOf(Root.transform);
            }
        }
        internal void ReleaseFocus()
        { if(HasKeyboardFocus) EventSystem.current.SetSelectedGameObject(null); }
        public void Dispose() { if(Root!=null) UnityEngine.Object.Destroy(Root); }
    }
}
