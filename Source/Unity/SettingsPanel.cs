using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace VolumetricExplosionFX.Rendering
{
    internal enum UiGlyph { General, Blast, Flame, Wreck, Gauge, Spark, Link }
    internal enum UiRowKind { Section, Toggle, Slider, Choice, Action, Info, Status }
    internal sealed class UiRow
    {
        internal UiRowKind Kind; internal string Label, Hint;
        internal Func<bool> GetBool; internal Action<bool> SetBool;
        internal Func<float> GetValue; internal Action<float> SetValue;
        internal float Min, Max, Step; internal Func<float,string> Format;
        internal string[] Options; internal Func<int> GetChoice; internal Action<int> SetChoice;
        internal string Button; internal Action Click; internal Func<string> Text;
        internal Func<bool> Available;
        // A mod's state on the compatibility page (0 not installed, 1 works with VEFX, 2 to check, 3 conflict)
        // and the four words shown for them.
        internal Func<int> GetStatus; internal string[] StatusNames;
    }
    internal sealed class UiPage { internal string Title; internal UiGlyph Glyph; internal readonly List<UiRow> Rows=new List<UiRow>(); }
    internal sealed class UiModel
    {
        internal string Title, Subtitle, Reset;
        internal UiStyle Style; internal UiLayout Layout;   // the window's style and layout (FxSettings.UiStyle, UiLayout)
        internal float Opacity=1;                           // its backgrounds' opacity (FxSettings.UiOpacity; also set live)
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
    // Original procedural art for the settings window (no image files): rounded shapes, a soft shadow, the page icons and
    // the background patterns.
    internal static class UiArt
    {
        static readonly Dictionary<string,Sprite> sprites=new Dictionary<string,Sprite>();
        static readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        internal static void Release()
        {
            foreach(UnityEngine.Object o in owned) if(o!=null) UnityEngine.Object.Destroy(o);
            owned.Clear(); sprites.Clear();
        }
        static Sprite Make(string key,int size,float border,Func<float,float,Color> pixel,bool tiled=false)
        {
            Sprite s;
            if(sprites.TryGetValue(key,out s)&&s!=null) return s;
            var t=new Texture2D(size,size,TextureFormat.RGBA32,false);
            t.name="Original settings art "+key; t.wrapMode=tiled?TextureWrapMode.Repeat:TextureWrapMode.Clamp; t.filterMode=tiled?FilterMode.Point:FilterMode.Bilinear;
            var c=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++) c[y*size+x]=pixel(x+0.5f,y+0.5f);
            t.SetPixels(c); t.Apply(false,true);
            s=Sprite.Create(t,new Rect(0,0,size,size),new Vector2(0.5f,0.5f),100,0,SpriteMeshType.FullRect,new Vector4(border,border,border,border));
            owned.Add(t); owned.Add(s); sprites[key]=s; return s;
        }
        // Signed distance to a rounded box centred in an n x n square (pixels).
        static float Box(float x,float y,int n,float inset,float radius)
        {
            float qx=Mathf.Abs(x-n*0.5f)-(n*0.5f-inset-radius), qy=Mathf.Abs(y-n*0.5f)-(n*0.5f-inset-radius);
            float a=Mathf.Max(qx,0), b=Mathf.Max(qy,0);
            return Mathf.Sqrt(a*a+b*b)+Mathf.Min(Mathf.Max(qx,qy),0)-radius;
        }
        internal static Sprite Fill(int radius)
        {
            radius=Mathf.Max(radius,0);
            int n=2*radius+4;
            return Make("fill"+radius,n,radius+2,(x,y)=>new Color(1,1,1,Mathf.Clamp01(0.5f-Box(x,y,n,0,radius))));
        }
        // A soft drop shadow round a rounded box (sliced: its middle stretches); tinted, a glow.
        internal static Sprite Shadow()
        {
            const int n=96; const float spread=30;
            return Make("shadow",n,46,(x,y)=>{
                float d=Box(x,y,n,spread,10);
                float a=Mathf.Exp(-Mathf.Max(d,0)*Mathf.Max(d,0)/(2*9f*9f));
                return new Color(1,1,1,a); });
        }
        internal static Sprite Check()
        {
            return Make("check",24,0,(x,y)=>{
                var p=new Vector2(x,y);
                float d=Mathf.Min(Segment(p,new Vector2(4,12),new Vector2(10,6)),Segment(p,new Vector2(10,6),new Vector2(20,19)));
                return new Color(1,1,1,Mathf.Clamp01(2-d)); });
        }
        // Background patterns, tiled: scan lines (Terminal), ruled paper (Paper), a grid (Mission).
        internal static Sprite Pattern(int kind)
        {
            switch(kind)
            {
                case 1: return Make("scan",4,0,(x,y)=>new Color(1,1,1,y<1?1:0),true);
                case 2: return Make("ruled",26,0,(x,y)=>new Color(1,1,1,y<1?1:0),true);
                default: return Make("grid",24,0,(x,y)=>new Color(1,1,1,x<1||y<1?1:0),true);
            }
        }
        // A corner bracket (Mission): an L in the lower left of its square.
        internal static Sprite Bracket() { return Make("bracket",18,0,(x,y)=>new Color(1,1,1,x<3||y<3?1:0)); }
        static float Segment(Vector2 p,Vector2 a,Vector2 b)
        { Vector2 v=b-a; return (p-a-v*Mathf.Clamp01(Vector2.Dot(p-a,v)/v.sqrMagnitude)).magnitude; }
        static bool Flame(float x,float y)
        {
            if(x*x+(y+0.32f)*(y+0.32f)<0.36f) return true;
            if(y<-0.32f||y>0.95f) return false;
            float h=(y+0.32f)/1.27f, w=0.6f*Mathf.Pow(1-h,1.15f), bend=0.2f*Mathf.Sin(h*3.4f)*h;
            return Mathf.Abs(x-bend)<w;
        }
        // The white symbol of a page (-1..1 square).
        static bool Symbol(UiGlyph g,float x,float y)
        {
            float r=Mathf.Sqrt(x*x+y*y), a=Mathf.Atan2(y,x);
            switch(g)
            {
                case UiGlyph.General:   // a gear
                {
                    float outer=0.56f+0.14f*Mathf.Clamp01(Mathf.Cos(a*8)*3);
                    return r<outer&&r>0.22f;
                }
                case UiGlyph.Blast:     // a burst
                    return r<0.3f+0.48f*Mathf.Pow(Mathf.Abs(Mathf.Cos(a*4)),6)+0.12f*Mathf.Pow(Mathf.Abs(Mathf.Cos(a*4+0.785f)),6);
                case UiGlyph.Flame: return Flame(x*1.15f,y*1.15f+0.05f);
                case UiGlyph.Wreck:     // shards
                {
                    var p=new Vector2(x,y);
                    return Tri(p,new Vector2(-0.7f,-0.55f),new Vector2(-0.05f,-0.6f),new Vector2(-0.35f,0.2f))||
                        Tri(p,new Vector2(0.05f,-0.45f),new Vector2(0.72f,-0.2f),new Vector2(0.25f,0.35f))||
                        Tri(p,new Vector2(-0.3f,0.35f),new Vector2(0.2f,0.48f),new Vector2(-0.12f,0.78f));
                }
                case UiGlyph.Gauge:     // a dial and its needle
                {
                    bool arc=r>0.48f&&r<0.68f&&(a>-0.5f||a<-2.64f);
                    bool needle=Segment(new Vector2(x,y),new Vector2(0,-0.1f),new Vector2(0.38f,0.32f))<0.09f;
                    return arc||needle||r<0.14f;
                }
                case UiGlyph.Link:      // two linked rings
                {
                    float r1=Mathf.Sqrt((x+0.24f)*(x+0.24f)+y*y), r2=Mathf.Sqrt((x-0.24f)*(x-0.24f)+y*y);
                    return Mathf.Abs(r1-0.36f)<0.1f||Mathf.Abs(r2-0.36f)<0.1f;
                }
                default:                // a sparkle
                    return Mathf.Sqrt(Mathf.Abs(x))+Mathf.Sqrt(Mathf.Abs(y))<0.95f;
            }
        }
        static bool Tri(Vector2 p,Vector2 a,Vector2 b,Vector2 c)
        {
            float d1=(p.x-b.x)*(a.y-b.y)-(a.x-b.x)*(p.y-b.y), d2=(p.x-c.x)*(b.y-c.y)-(b.x-c.x)*(p.y-c.y), d3=(p.x-a.x)*(c.y-a.y)-(c.x-a.x)*(p.y-a.y);
            bool neg=d1<0||d2<0||d3<0, pos=d1>0||d2>0||d3>0; return !(neg&&pos);
        }
        // A page's symbol alone, white (tinted by its image), for tabs.
        internal static Sprite Mark(UiGlyph g)
        {
            const int n=40;
            return Make("mark"+g,n,0,(x,y)=>{
                int hits=0;
                for(int j=0;j<3;j++) for(int i=0;i<3;i++)
                    if(Symbol(g,((x-0.5f+(i+0.5f)/3)/n-0.5f)*2.15f,((y-0.5f+(j+0.5f)/3)/n-0.5f)*2.15f)) hits++;
                return new Color(1,1,1,hits/9f); });
        }
        // The mod's flame, as a sprite for the window's header.
        internal static Sprite Logo()
        {
            Sprite s;
            if(sprites.TryGetValue("logo",out s)&&s!=null) return s;
            Texture2D tex=Icon(64); tex.filterMode=FilterMode.Bilinear;
            s=Sprite.Create(tex,new Rect(0,0,64,64),new Vector2(0.5f,0.5f),100);
            owned.Add(tex); owned.Add(s); sprites["logo"]=s; return s;
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
    internal enum UiLayout { Open, Panel, Compact }
    // 1.1: five complete looks for the window, not shades of one: Glass (smoked, see-through, rounded), Neon (black, magenta and cyan glow, capitals), Terminal (phosphor
    // green, a monospaced system font, scan lines, [ ON ] switches, ##### sliders), Paper (cream, a serif system font, red
    // ink, ruled lines, check boxes, underlined choices), Mission (night blue and amber, condensed capitals, a grid, corner
    // brackets, lamp switches and segmented gauges).
    internal enum UiStyle { Glass, Neon, Terminal, Paper, Mission }
    // 1.1: the settings window, VEFX's own design: a style, a layout (Open: tabs on top and open rows; Panel: a
    // sidebar of pages and grouped sections; Compact: a smaller window with icon tabs), see-through backgrounds and a size
    // the player sets. Animated: the window opens and closes with a fade and a slight zoom, the tab marker glides, pages
    // fade in, switches and choices glide, the scroll bar shows only while scrolling. Original code (uGUI built at run time).
    internal sealed class SettingsPanel : IDisposable
    {
        sealed class Theme
        {
            internal UiStyle Style;
            internal Color Ink, Soft, Faint, Accent, Accent2, OnAccent, Body, Side, Group, Line, Track, Hover, Shadow, Border, Scroll, Pattern, Good, Warn, Bad;
            internal int Radius, Control;   // corner radius of the window and of the controls (px)
            internal bool Upper, Glow;      // tabs, sections and choices in capitals; the shadow is a coloured glow all round
            internal int PatternKind;       // 0 none, 1 scan lines, 2 ruled paper, 3 grid
            internal int LabelSize=13, HintSize=11;
            internal string[] Fonts;        // system fonts (null: the game's)
            static Color C(float r,float g,float b,float a=1) { return new Color(r,g,b,a); }
            internal static Theme Make(UiStyle style)
            {
                switch(style)
                {
                    case UiStyle.Neon: return new Theme { Style=style, Ink=C(0.97f,0.92f,1), Soft=C(0.72f,0.66f,0.84f), Faint=C(0.5f,0.44f,0.62f),
                        Accent=C(1,0.2f,0.74f), Accent2=C(0.25f,0.95f,1), OnAccent=C(0.07f,0,0.05f), Body=C(0.03f,0.012f,0.05f,0.95f), Side=C(0.05f,0.016f,0.08f,0.96f),
                        Group=C(1,0.2f,0.74f,0.05f), Line=C(1,0.2f,0.74f,0.24f), Track=C(0.25f,0.95f,1,0.16f), Hover=C(1,0.2f,0.74f,0.12f), Shadow=C(1,0.15f,0.7f,0.5f),
                        Border=C(1,0.28f,0.8f,0.9f), Scroll=C(0.25f,0.95f,1,0.45f), Good=C(0.25f,0.95f,1), Warn=C(1,0.85f,0.25f), Bad=C(1,0.32f,0.38f),
                        Radius=6, Control=3, Upper=true, Glow=true, Fonts=new[]{"Segoe UI Semibold","Segoe UI","Verdana","Helvetica Neue"} };
                    case UiStyle.Terminal: return new Theme { Style=style, Ink=C(0.48f,1,0.58f), Soft=C(0.32f,0.76f,0.4f), Faint=C(0.22f,0.52f,0.28f),
                        Accent=C(0.52f,1,0.62f), Accent2=C(0.52f,1,0.62f), OnAccent=C(0.01f,0.06f,0.02f), Body=C(0.01f,0.035f,0.018f,0.96f), Side=C(0.008f,0.028f,0.014f,0.97f),
                        Group=C(0.4f,1,0.5f,0.04f), Line=C(0.4f,1,0.5f,0.24f), Track=C(0.4f,1,0.5f,0.14f), Hover=C(0.4f,1,0.5f,0.12f), Shadow=C(0.2f,1,0.35f,0.22f),
                        Border=C(0.42f,1,0.52f,0.75f), Scroll=C(0.4f,1,0.5f,0.4f), Pattern=C(0.4f,1,0.5f,0.05f), Good=C(0.52f,1,0.62f), Warn=C(1,0.85f,0.3f), Bad=C(1,0.38f,0.3f),
                        Radius=1, Control=1, Glow=true, PatternKind=1, LabelSize=12, HintSize=11,
                        Fonts=new[]{"Consolas","Lucida Console","Courier New","Menlo","Monaco","DejaVu Sans Mono"} };
                    case UiStyle.Paper: return new Theme { Style=style, Ink=C(0.14f,0.11f,0.08f), Soft=C(0.38f,0.33f,0.27f), Faint=C(0.56f,0.5f,0.43f),
                        Accent=C(0.74f,0.14f,0.1f), Accent2=C(0.16f,0.3f,0.6f), OnAccent=C(1,0.97f,0.92f), Body=C(0.965f,0.945f,0.885f,0.97f), Side=C(0.93f,0.905f,0.84f,0.97f),
                        Group=C(1,1,1,0.4f), Line=C(0.2f,0.3f,0.55f,0.17f), Track=C(0.14f,0.11f,0.08f,0.2f), Hover=C(0.14f,0.11f,0.08f,0.07f), Shadow=C(0.1f,0.06f,0,0.4f),
                        Border=C(0.35f,0.28f,0.2f,0.42f), Scroll=C(0.14f,0.11f,0.08f,0.3f), Pattern=C(0.2f,0.35f,0.7f,0.14f), Good=C(0.12f,0.45f,0.27f), Warn=C(0.78f,0.46f,0.04f), Bad=C(0.74f,0.14f,0.1f),
                        Radius=3, Control=2, PatternKind=2, LabelSize=14, HintSize=12,
                        Fonts=new[]{"Georgia","Cambria","Palatino Linotype","Book Antiqua","Times New Roman","Baskerville"} };
                    case UiStyle.Mission: return new Theme { Style=style, Ink=C(0.86f,0.91f,0.96f), Soft=C(0.56f,0.66f,0.76f), Faint=C(0.38f,0.47f,0.57f),
                        Accent=C(1,0.7f,0.18f), Accent2=C(0.45f,0.8f,1), OnAccent=C(0.1f,0.06f,0), Body=C(0.035f,0.065f,0.105f,0.95f), Side=C(0.025f,0.05f,0.085f,0.96f),
                        Group=C(0.45f,0.7f,1,0.05f), Line=C(0.5f,0.72f,0.95f,0.17f), Track=C(0.5f,0.72f,0.95f,0.16f), Hover=C(0.5f,0.72f,0.95f,0.09f), Shadow=C(0,0,0,0.55f),
                        Border=C(0.5f,0.72f,0.95f,0.38f), Scroll=C(0.5f,0.72f,0.95f,0.35f), Pattern=C(0.5f,0.72f,0.95f,0.05f), Good=C(0.45f,0.8f,1), Warn=C(1,0.7f,0.18f), Bad=C(1,0.38f,0.32f),
                        Radius=0, Control=0, Upper=true, PatternKind=3,
                        Fonts=new[]{"Bahnschrift SemiCondensed","Bahnschrift","Arial Narrow","Avenir Next Condensed","Roboto Condensed"} };
                    default: return new Theme { Style=UiStyle.Glass, Ink=C(0.94f,0.95f,0.97f), Soft=C(0.64f,0.68f,0.73f), Faint=C(0.45f,0.49f,0.54f),
                        Accent=C(0.32f,0.86f,0.78f), Accent2=C(0.32f,0.86f,0.78f), OnAccent=C(0.03f,0.08f,0.08f), Body=C(0.06f,0.075f,0.09f,0.94f), Side=C(0.045f,0.055f,0.068f,0.96f),
                        Group=C(1,1,1,0.045f), Line=C(1,1,1,0.08f), Track=C(1,1,1,0.14f), Hover=C(1,1,1,0.06f), Shadow=C(0,0,0,0.55f),
                        Border=C(1,1,1,0.15f), Scroll=C(1,1,1,0.3f), Good=C(0.32f,0.86f,0.78f), Warn=C(1,0.72f,0.3f), Bad=C(1,0.42f,0.42f), Radius=18, Control=11 };
                }
            }
        }
        // The style's system font, with wide fallbacks for Chinese and Japanese (null: the game's font).
        static readonly Dictionary<UiStyle,Font> styleFonts=new Dictionary<UiStyle,Font>();
        static readonly string[] Wide={"Microsoft YaHei","Meiryo","Yu Gothic","PingFang SC","Hiragino Sans","Noto Sans CJK SC","Noto Sans CJK JP","Arial Unicode MS","DejaVu Sans","Arial"};
        static Font StyleFont(Theme theme,Font fallback)
        {
            if(theme.Fonts==null) return fallback;
            Font f;
            if(styleFonts.TryGetValue(theme.Style,out f)&&f!=null) return f;
            try { var names=new List<string>(theme.Fonts); names.AddRange(Wide); f=Font.CreateDynamicFontFromOSFont(names.ToArray(),14); }
            catch(Exception) { f=null; }
            if(f==null) return fallback;
            styleFonts[theme.Style]=f; return f;
        }
        readonly Theme t; readonly UiLayout layout; readonly UiStyle style;
        internal readonly float Width, Height;
        readonly float contentLeft, contentRight, contentTop, contentWidth, rightColumn;
        const float Inner=14;
        readonly UiModel model; readonly Font font; readonly Camera camera;
        readonly CanvasScaler scaler; readonly CanvasGroup windowFade;
        readonly List<RectTransform> pages=new List<RectTransform>();
        readonly List<CanvasGroup> pageFades=new List<CanvasGroup>();
        readonly List<Graphic> navInk=new List<Graphic>();
        readonly List<Vector2> navSpans=new List<Vector2>();   // each tab: x (or y) and width (or height) of its marker
        readonly List<List<Action>> updates=new List<List<Action>>();
        readonly List<Action<float>> animations=new List<Action<float>>();
        readonly List<Image> backs=new List<Image>(); readonly List<float> backAlpha=new List<float>();   // see-through backgrounds
        readonly ScrollRect scroll; readonly Text status; readonly CanvasGroup statusFade;
        readonly Image scrollThumbImage; readonly RectTransform scrollThumb; RectTransform marker;
        internal readonly GameObject Root; internal readonly RectTransform Window;
        internal static Vector2 Position;
        int page=-1, screenW=-1, screenH=-1; float preferredScale, appliedScale, opacity=-1;
        bool visible; float openTime=-1, closeTime=-1, pageTime=1, thumbShown, scrollIdle=9;
        Vector2 markerPos, markerVel; float markerSize, markerSizeVel; Vector2 lastScroll; string lastStatus;
        internal bool Visible
        {
            get { return visible; }
            set
            {
                if(value==visible&&(value?Root.activeSelf:true)) return;
                if(!value) ReleaseFocus();
                visible=value;
                windowFade.blocksRaycasts=value; windowFade.interactable=value;
                if(value) { Root.SetActive(true); Fit(); Refresh(); openTime=0; closeTime=-1; Animate(0); }
                else if(Root.activeSelf) { closeTime=0; openTime=-1; }
            }
        }
        internal bool Shown { get { return Root.activeSelf; } }
        internal int Page { get { return page; } set { Select(Mathf.Clamp(value,0,pages.Count-1)); } }
        bool Term { get { return style==UiStyle.Terminal; } }
        string Cap(string s) { return t.Upper&&s!=null?s.ToUpperInvariant():s; }
        internal SettingsPanel(UiModel model,Font font,Camera camera,float scale)
        {
            this.model=model; this.camera=camera; preferredScale=scale;
            layout=model.Layout; style=model.Style; t=Theme.Make(style);
            this.font=StyleFont(t,font);
            switch(layout)
            {
                case UiLayout.Panel: Width=740; Height=560; contentLeft=196+22; contentRight=32; contentTop=18; rightColumn=212; break;
                case UiLayout.Compact: Width=540; Height=540; contentLeft=20; contentRight=28; contentTop=104; rightColumn=176; break;
                default: Width=720; Height=560; contentLeft=28; contentRight=36; contentTop=110; rightColumn=220; break;
            }
            contentWidth=Width-contentLeft-contentRight;
            Root=new GameObject("VolumetricExplosionFX settings",typeof(RectTransform));
            var canvas=Root.AddComponent<Canvas>();
            canvas.renderMode=camera!=null?RenderMode.ScreenSpaceCamera:RenderMode.ScreenSpaceOverlay;
            if(camera!=null) { canvas.worldCamera=camera; canvas.planeDistance=1; }
            canvas.sortingOrder=60;
            scaler=Root.AddComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize; scaler.referencePixelsPerUnit=100;
            Root.AddComponent<GraphicRaycaster>();
            Window=Rect("window",Root.transform); Window.anchorMin=Window.anchorMax=Window.pivot=new Vector2(0.5f,0.5f);
            Window.sizeDelta=new Vector2(Width,Height); Window.anchoredPosition=Position;
            windowFade=Window.gameObject.AddComponent<CanvasGroup>();
            // The window: its shadow (or a coloured glow), a rim, the body, the style's pattern and marks.
            var shade=Back(Img("shadow",Window,UiArt.Shadow(),t.Shadow));
            if(t.Glow) Stretch(shade.rectTransform,-30,-30,-30,-30); else Stretch(shade.rectTransform,-30,-38,-30,-24);
            Stretch(Img("border",Window,UiArt.Fill(t.Radius),t.Border).rectTransform,-1,-1,-1,-1);
            Stretch(Back(Img("body",Window,UiArt.Fill(t.Radius),t.Body)).rectTransform,0,0,0,0);
            if(t.PatternKind>0)
            {
                var pattern=Img("pattern",Window,UiArt.Pattern(t.PatternKind),t.Pattern); pattern.type=Image.Type.Tiled;
                Stretch(pattern.rectTransform,Mathf.Max(t.Radius,1),Mathf.Max(t.Radius,1),Mathf.Max(t.Radius,1),layout==UiLayout.Panel?Mathf.Max(t.Radius,1):100);
            }
            if(style==UiStyle.Paper&&layout!=UiLayout.Panel)
            {
                // The red margin of a ruled page.
                var margin=Img("margin",Window,null,new Color(0.78f,0.22f,0.18f,0.45f)).rectTransform;
                margin.anchorMin=new Vector2(0,0); margin.anchorMax=new Vector2(0,1); margin.offsetMin=new Vector2(contentLeft-13,0); margin.offsetMax=new Vector2(contentLeft-11.5f,-100);
            }
            if(style==UiStyle.Mission) Brackets();
            var grip=Img("drag",Window,null,Color.clear); grip.raycastTarget=true; Top(grip.rectTransform,0,layout==UiLayout.Panel?64:56,0,0);
            grip.gameObject.AddComponent<UiWindowDrag>().Move=delta=>{ Window.anchoredPosition+=delta/Mathf.Max(appliedScale,0.01f); ClampPosition(); };
            if(layout==UiLayout.Panel) BuildSidebar(); else BuildTabs();
            // Close: a plain cross ([x] in a terminal), a soft disc under it on hover.
            Button close=Button("close",Window,"",()=>{ if(model.Close!=null) model.Close(); },UiArt.Fill(Mathf.Min(t.Radius,14)),Color.clear);
            At(close.GetComponent<RectTransform>(),Width-(Term?50:44),-14,Term?36:30,30);
            var cross=Label("×",close.transform,Term?"[x]":"×",Term?13:19,FontStyle.Normal,t.Soft); cross.alignment=TextAnchor.MiddleCenter; Stretch(cross.rectTransform,0,1,0,0);
            // Content.
            var view=Rect("viewport",Window); Stretch(view,contentLeft,48,contentRight-8,contentTop);   // (left, bottom, right, top)
            view.gameObject.AddComponent<RectMask2D>();
            var viewHit=view.gameObject.AddComponent<Image>(); viewHit.color=Color.clear;
            scroll=view.gameObject.AddComponent<ScrollRect>(); scroll.horizontal=false; scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Elastic; scroll.elasticity=0.12f; scroll.inertia=true; scroll.decelerationRate=0.12f;
            scroll.scrollSensitivity=30; scroll.viewport=view;
            for(int i=0;i<model.Pages.Count;i++) BuildPage(model.Pages[i],view);
            scrollThumbImage=Img("scroll thumb",Window,UiArt.Fill(t.Control>1?2:0),t.Scroll); scrollThumb=scrollThumbImage.rectTransform;
            scrollThumb.anchorMin=scrollThumb.anchorMax=new Vector2(1,1); scrollThumb.pivot=new Vector2(1,1);
            // Footer: defaults (a quiet text button) and the status.
            if(model.Reset!=null)
            {
                Button reset=Button("reset",Window,Term?"[ "+model.Reset.ToLowerInvariant()+" ]":Cap(model.Reset),()=>{ if(model.ResetAll!=null) model.ResetAll(); Changed(); },
                    UiArt.Fill(Mathf.Min(t.Radius,12)),Color.clear);
                reset.GetComponentInChildren<Text>().color=t.Accent;
                RectTransform r=reset.GetComponent<RectTransform>(); r.anchorMin=r.anchorMax=r.pivot=Vector2.zero;
                r.anchoredPosition=new Vector2(layout==UiLayout.Panel?14:contentLeft-10,12); r.sizeDelta=new Vector2(layout==UiLayout.Panel?168:Mathf.Max(120,TextWidth(Cap(model.Reset),12,FontStyle.Normal)+40),28);
            }
            status=Label("status",Window,"",11,FontStyle.Normal,t.Faint); status.alignment=TextAnchor.MiddleRight;
            statusFade=status.gameObject.AddComponent<CanvasGroup>();
            var sr=status.rectTransform; sr.anchorMin=sr.anchorMax=sr.pivot=new Vector2(1,0); sr.sizeDelta=new Vector2(contentWidth-140,24); sr.anchoredPosition=new Vector2(-contentRight+8,14);
            SetOpacity(model.Opacity);
            Select(0); pageTime=1; SetLayer(Root.transform,5); Fit(); Root.SetActive(false);
        }
        Image Back(Image i) { backs.Add(i); backAlpha.Add(i.color.a); return i; }
        // How see-through the window's backgrounds are (the player's setting, applied live).
        internal void SetOpacity(float value)
        {
            value=Mathf.Clamp(value,0.3f,1);
            if(Mathf.Abs(value-opacity)<0.002f) return;
            opacity=value;
            for(int i=0;i<backs.Count;i++) { if(backs[i]==null) continue; Color c=backs[i].color; c.a=backAlpha[i]*value; backs[i].color=c; }
        }
        // Mission: amber brackets at the four corners.
        void Brackets()
        {
            for(int k=0;k<4;k++)
            {
                var b=Img("corner",Window,UiArt.Bracket(),t.Accent).rectTransform;
                bool right=k==1||k==2, top=k>=2;
                b.anchorMin=b.anchorMax=new Vector2(right?1:0,top?1:0); b.pivot=Vector2.zero;
                b.sizeDelta=new Vector2(18,18); b.anchoredPosition=new Vector2(right?4:-4,top?4:-4);
                b.localScale=new Vector3(right?-1:1,top?-1:1,1);
            }
        }
        // Open and Compact: the name at the top, tabs under it with a gliding marker.
        void BuildTabs()
        {
            bool compact=layout==UiLayout.Compact, icons=!Term;
            float tx=compact?48:56;
            if(icons) { var logo=Img("logo",Window,UiArt.Logo(),Color.white); At(logo.rectTransform,compact?18:24,-16,24,24); }
            else tx=compact?18:24;
            string name=Term?"> "+model.Title.ToLowerInvariant().Replace(' ','_'):Cap(model.Title);
            int titleSize=compact?13:15;
            var title=Label("title",Window,name,titleSize,FontStyle.Bold,Term?t.Accent:t.Ink); At(title.rectTransform,tx,-18,420,22);
            float titleW=TextWidth(name,titleSize,FontStyle.Bold);
            var sub=Label("version",Window,model.Subtitle,10,FontStyle.Normal,t.Faint); At(sub.rectTransform,tx+titleW+10,compact?-21:-22,240,16);
            if(compact||titleW>Width-320) sub.gameObject.SetActive(false);
            Top(Img("rule",Window,null,t.Line).rectTransform,compact?97:99,1,0,0);
            float x=compact?14:22, y=compact?-56:-60;
            // The tabs fit the window: smaller and tighter with many pages or long names, then the symbols alone
            // ("[n]" in a terminal) (the compatibility page pushed the last tab out of the window).
            int size=13; float pad=icons?38:20, gap=6; bool labels=!compact||Term, numbers=false;
            float room=Width-x-(compact?214:44);
            tabPlain=false;
            for(int attempt=0;attempt<5;attempt++)
            {
                float total=0;
                for(int i=0;i<model.Pages.Count;i++) total+=(labels?TextWidth(TabText(i,compact||numbers),size,FontStyle.Normal)+pad:40)+gap;
                if(total<=room) break;
                if(attempt==0) { size=12; pad=icons?32:16; gap=4; }
                else if(attempt==1) { size=11; pad=icons?28:12; gap=2; }
                else if(attempt==2&&Term) tabPlain=true;   // (the names without their numbers)
                else if(Term) { numbers=true; tabPlain=false; } else labels=false;
            }
            for(int i=0;i<model.Pages.Count;i++)
            {
                int index=i; UiPage p=model.Pages[i];
                string text=TabText(i,compact||numbers);
                bool label=labels;
                float labelW=label?TextWidth(text,size,FontStyle.Normal):0;
                float w=!label?40:labelW+pad;
                Button tab=Button("tab "+p.Title,Window,"",()=>Select(index),UiArt.Fill(Mathf.Min(t.Radius,9)),Color.clear);
                At(tab.GetComponent<RectTransform>(),x,y,w,34);
                if(icons) { var icon=Img("icon",tab.transform,UiArt.Mark(p.Glyph),t.Soft); At(icon.rectTransform,label?Mathf.Max(6,pad-26):12,-9,16,16); navInk.Add(icon); }
                else navInk.Add(null);
                if(label)
                {
                    var lb=Label("label",tab.transform,text,size,FontStyle.Normal,t.Soft); lb.alignment=TextAnchor.MiddleLeft;
                    At(lb.rectTransform,icons?Mathf.Max(6,pad-26)+20:pad*0.5f,0,labelW+4,34); navInk.Add(lb);
                }
                else navInk.Add(null);
                navSpans.Add(new Vector2(x+(label?10:12),label?w-20:16));
                x+=w+gap;
            }
            if(compact)
            {
                // The page's name beside the tabs.
                pageName=Label("page name",Window,"",12,FontStyle.Bold,t.Ink); pageName.alignment=TextAnchor.MiddleRight;
                At(pageName.rectTransform,Width-200-44,-60,190,34);
                if(Term) pageName.gameObject.SetActive(false);
            }
            marker=Img("marker",Window,UiArt.Fill(style==UiStyle.Glass?1:0),t.Accent).rectTransform;
            marker.anchorMin=marker.anchorMax=new Vector2(0,1); marker.pivot=new Vector2(0,1);
        }
        Text pageName;
        bool tabPlain;
        string TabText(int i,bool shortForm)
        {
            UiPage p=model.Pages[i];
            if(Term&&tabPlain&&!shortForm) return p.Title.ToLowerInvariant();
            return Term?"["+(i+1)+"]"+(shortForm?"":" "+p.Title.ToLowerInvariant()):Cap(p.Title);
        }
        // Panel: a sidebar with the name, the pages and an accent bar beside the chosen one.
        void BuildSidebar()
        {
            const float side=196;
            var bar=Back(Img("sidebar",Window,UiArt.Fill(t.Radius),t.Side)).rectTransform;
            bar.anchorMin=Vector2.zero; bar.anchorMax=new Vector2(0,1); bar.pivot=new Vector2(0,0.5f); bar.offsetMin=Vector2.zero; bar.offsetMax=new Vector2(side+16,0);
            var seam=Back(Img("seam",Window,null,t.Body)).rectTransform;
            seam.anchorMin=new Vector2(0,0); seam.anchorMax=new Vector2(0,1); seam.offsetMin=new Vector2(side,0); seam.offsetMax=new Vector2(side+16,0);
            var rule=Img("rule",Window,null,t.Line).rectTransform;
            rule.anchorMin=new Vector2(0,0); rule.anchorMax=new Vector2(0,1); rule.offsetMin=new Vector2(side,0); rule.offsetMax=new Vector2(side+1,0);
            float tx=52;
            if(!Term) { var logo=Img("logo",Window,UiArt.Logo(),Color.white); At(logo.rectTransform,18,-20,26,26); } else tx=18;
            At(Label("title",Window,Term?"> vefx":"VEFX",16,FontStyle.Bold,Term?t.Accent:t.Ink).rectTransform,tx,-18,side-tx-8,20);
            At(Label("name",Window,Term?model.Title.ToLowerInvariant():model.Title,10,FontStyle.Normal,t.Faint).rectTransform,tx,-37,side-tx-8,14);
            var highlight=Img("selection",Window,UiArt.Fill(Mathf.Min(t.Radius,8)),new Color(t.Accent.r,t.Accent.g,t.Accent.b,style==UiStyle.Paper?0.1f:0.14f)).rectTransform;
            highlight.anchorMin=highlight.anchorMax=new Vector2(0,1); highlight.pivot=new Vector2(0,1); highlight.sizeDelta=new Vector2(side-20,32);
            sideHighlight=highlight;
            for(int i=0;i<model.Pages.Count;i++)
            {
                int index=i; UiPage p=model.Pages[i];
                float y=-78-i*36;
                Button item=Button("tab "+p.Title,Window,"",()=>Select(index),UiArt.Fill(Mathf.Min(t.Radius,8)),Color.clear);
                At(item.GetComponent<RectTransform>(),10,y,side-20,32);
                if(!Term) { var icon=Img("icon",item.transform,UiArt.Mark(p.Glyph),t.Soft); At(icon.rectTransform,12,-8,16,16); navInk.Add(icon); }
                else navInk.Add(null);
                var label=Label("label",item.transform,Term?"["+(i+1)+"] "+p.Title.ToLowerInvariant():Cap(p.Title),13,FontStyle.Normal,t.Soft); label.alignment=TextAnchor.MiddleLeft;
                At(label.rectTransform,Term?12:38,0,side-64,32); navInk.Add(label);
                navSpans.Add(new Vector2(y,32));
            }
            marker=Img("marker",Window,UiArt.Fill(style==UiStyle.Glass?1:0),t.Accent).rectTransform;
            marker.anchorMin=marker.anchorMax=new Vector2(0,1); marker.pivot=new Vector2(0,1);
        }
        RectTransform sideHighlight;
        internal void SetScale(float scale)
        { if(Mathf.Abs(preferredScale-scale)<0.001f) return; preferredScale=scale; screenW=-1; Fit(); }
        void Fit()
        {
            int w=camera!=null?camera.pixelWidth:Screen.width, h=camera!=null?camera.pixelHeight:Screen.height;
            if(w==screenW&&h==screenH) return;
            screenW=w; screenH=h;
            appliedScale=Mathf.Max(0.1f,Mathf.Min(Mathf.Clamp(preferredScale,0.35f,2.5f),Mathf.Min((w-24)/Width,(h-24)/Height)));
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
            if(index<0||index>=pages.Count) return;
            bool changed=index!=page; page=index;
            for(int i=0;i<pages.Count;i++)
            {
                pages[i].gameObject.SetActive(i==page);
                Graphic icon=navInk[2*i], label=navInk[2*i+1];
                if(icon!=null) icon.color=i==page?t.Accent:t.Soft;
                if(label!=null) label.color=i==page?(Term?t.Accent:t.Ink):t.Soft;
            }
            if(pageName!=null) pageName.text=Cap(model.Pages[page].Title);
            scroll.content=pages[page]; pages[page].anchoredPosition=Vector2.zero;
            scroll.velocity=Vector2.zero;
            if(changed) pageTime=0;
            Refresh();
        }
        void Refresh()
        {
            if(page>=0&&page<updates.Count) foreach(Action update in updates[page]) update();
            if(status!=null&&model.Status!=null)
            {
                string s=model.Status();
                if(s!=lastStatus) { lastStatus=s; status.text=s??""; statusFade.alpha=0; }
            }
        }
        static float OutCubic(float x) { x=Mathf.Clamp01(x); return 1-(1-x)*(1-x)*(1-x); }
        static float OutBack(float x) { x=Mathf.Clamp01(x); const float c=1.2f; float u=x-1; return 1+(c+1)*u*u*u+c*u*u; }
        static float Ease(float current,float target,float dt,float speed) { return target+(current-target)*Mathf.Exp(-dt*speed); }
        // A lightly damped spring, sub-stepped (frame-rate independent).
        static void Spring(ref float x,ref float v,float target,float dt)
        {
            for(float left=dt;left>0;left-=1/120f) { float h=Mathf.Min(left,1/120f); v+=(480*(target-x)-38*v)*h; x+=v*h; }
        }
        void Animate(float dt)
        {
            if(openTime>=0)
            {
                openTime+=dt; float x=openTime/0.34f;
                windowFade.alpha=OutCubic(x*1.6f); Window.localScale=Vector3.one*(0.96f+0.04f*OutBack(x));
                if(x>=1) { openTime=-1; windowFade.alpha=1; Window.localScale=Vector3.one; }
            }
            else if(closeTime>=0)
            {
                closeTime+=dt; float x=Mathf.Clamp01(closeTime/0.17f);
                windowFade.alpha=1-x*x; Window.localScale=Vector3.one*(1-0.03f*x);
                if(x>=1) { closeTime=-1; Root.SetActive(false); Window.localScale=Vector3.one; windowFade.alpha=1; }
            }
            if(page>=0&&page<navSpans.Count)
            {
                // The marker glides to the chosen tab: an underline (Open), a dot (Compact), a bar beside the item (Panel).
                Vector2 span=navSpans[page];
                Vector2 target=layout==UiLayout.Panel?new Vector2(10,span.x):new Vector2(span.x,layout==UiLayout.Compact?-92:-94);
                float size=span.y;
                if(dt<=0||markerSize<=0) { markerPos=target; markerVel=Vector2.zero; markerSize=size; markerSizeVel=0; }
                else { Spring(ref markerPos.x,ref markerVel.x,target.x,dt); Spring(ref markerPos.y,ref markerVel.y,target.y,dt); Spring(ref markerSize,ref markerSizeVel,size,dt); }
                if(layout==UiLayout.Panel)
                {
                    marker.anchoredPosition=new Vector2(10,markerPos.y-7); marker.sizeDelta=new Vector2(3,18);
                    sideHighlight.anchoredPosition=new Vector2(10,markerPos.y);
                }
                else { marker.anchoredPosition=markerPos; marker.sizeDelta=new Vector2(markerSize,layout==UiLayout.Compact||Term||style==UiStyle.Mission?3:2); }
            }
            if(page>=0&&pageTime<1)
            {
                pageTime=Mathf.Min(1,pageTime+dt/0.24f); float e=OutCubic(pageTime);
                pageFades[page].alpha=e; pages[page].GetChild(0).localPosition=new Vector3(0,-(1-e)*10,0);
            }
            else if(page>=0) { pageFades[page].alpha=1; pages[page].GetChild(0).localPosition=Vector3.zero; }
            statusFade.alpha=Ease(statusFade.alpha,1,dt,10);
            foreach(Action<float> a in animations) a(dt);
        }
        internal void Tick(float dt)
        {
            if(!Root.activeSelf) return;
            if(visible) { Fit(); Refresh(); }
            Animate(dt);
            if(!Root.activeSelf) return;
            RectTransform content=scroll.content;
            float viewH=scroll.viewport.rect.height, contentH=content!=null?content.sizeDelta.y:0;
            bool scrolls=contentH>viewH+1;
            Vector2 at=content!=null?content.anchoredPosition:Vector2.zero;
            if((at-lastScroll).sqrMagnitude>0.01f) scrollIdle=0; else scrollIdle+=dt;
            lastScroll=at;
            thumbShown=Ease(thumbShown,scrolls&&scrollIdle<0.9f?1:0,dt,scrollIdle<0.9f?18:6);
            scrollThumbImage.color=new Color(t.Scroll.r,t.Scroll.g,t.Scroll.b,t.Scroll.a*thumbShown);
            if(scrolls)
            {
                float trackH=viewH-8, h=Mathf.Max(28,trackH*viewH/contentH);
                float pos=Mathf.Clamp01(at.y/(contentH-viewH));
                scrollThumb.sizeDelta=new Vector2(4,h); scrollThumb.anchoredPosition=new Vector2(-8,-(contentTop+4)-(trackH-h)*pos);
            }
        }
        void BuildPage(UiPage p,RectTransform view)
        {
            var content=Rect("page "+p.Title,view);
            content.anchorMin=new Vector2(0,1); content.anchorMax=new Vector2(1,1); content.pivot=new Vector2(0.5f,1);
            pageFades.Add(content.gameObject.AddComponent<CanvasGroup>());
            var inner=Rect("inner",content); inner.anchorMin=new Vector2(0,1); inner.anchorMax=new Vector2(1,1); inner.pivot=new Vector2(0.5f,1);
            inner.offsetMin=inner.offsetMax=Vector2.zero;
            var refresh=new List<Action>(); updates.Add(refresh);
            float y=6;
            if(layout==UiLayout.Panel)
            {
                string heading=Term?"> "+p.Title.ToLowerInvariant():Cap(p.Title);
                At(Label("heading",inner,heading,style==UiStyle.Paper?21:19,style==UiStyle.Paper?FontStyle.BoldAndItalic:FontStyle.Bold,Term?t.Accent:t.Ink).rectTransform,2,-6,contentWidth,28); y=48;
            }
            bool cards=layout==UiLayout.Panel;
            RectTransform group=null; float groupTop=0, groupY=0; int groups=0;
            Action close=()=>{ if(group==null) return; Top(group,groupTop,groupY,0,0); y=groupTop+groupY+(cards?16:10); group=null; };
            foreach(UiRow row in p.Rows)
            {
                if(row.Kind==UiRowKind.Section)
                {
                    close();
                    y=Section(inner,row.Label??"",y,cards);
                    continue;
                }
                if(group==null)
                {
                    group=cards?Back(Img("card "+(groups++),inner,UiArt.Fill(Mathf.Min(t.Radius,10)),t.Group)).rectTransform:Rect("group "+(groups++),inner);
                    groupTop=y; groupY=0;
                }
                if(groupY>0) Top(Img("hairline",group,null,t.Line).rectTransform,groupY,1,cards?Inner:0,cards?Inner:0);
                groupY+=Row(row,group,groupY,refresh,cards?Inner:0);
            }
            close();
            content.sizeDelta=new Vector2(0,y+6); inner.sizeDelta=new Vector2(0,y+6); pages.Add(content);
        }
        // A section heading, in the style's manner; returns where the rows start.
        float Section(RectTransform inner,string title,float y,bool cards)
        {
            switch(style)
            {
                case UiStyle.Terminal:
                {
                    string text="## "+title.ToLowerInvariant();
                    Top(Label("section",inner,text,12,FontStyle.Bold,t.Accent).rectTransform,y+8,16,0,0);
                    Top(Img("section line",inner,null,t.Line).rectTransform,y+16,1,TextWidth(text,12,FontStyle.Bold)+10,0);
                    return y+28;
                }
                case UiStyle.Paper:
                {
                    Top(Label("section",inner,title,15,FontStyle.Italic,t.Ink).rectTransform,y+6,20,0,0);
                    var under=Img("section line",inner,null,t.Accent).rectTransform; At(under,0,-(y+27),Mathf.Min(TextWidth(title,15,FontStyle.Italic)+6,contentWidth),1.5f);
                    return y+34;
                }
                case UiStyle.Mission:
                {
                    var bar=Img("section mark",inner,null,t.Accent).rectTransform; At(bar,0,-(y+9),3,12);
                    string text=title.ToUpperInvariant();
                    Top(Label("section",inner,text,11,FontStyle.Bold,t.Accent).rectTransform,y+7,16,9,0);
                    Top(Img("section line",inner,null,t.Line).rectTransform,y+15,1,TextWidth(text,11,FontStyle.Bold)+20,0);
                    return y+28;
                }
                default:
                {
                    if(cards&&style==UiStyle.Glass) { Top(Label("section",inner,title,12,FontStyle.Bold,t.Ink).rectTransform,y+2,18,4,0); return y+24; }
                    // An accent label in capitals and a fine line running from it.
                    string text=title.ToUpperInvariant();
                    Top(Label("section",inner,text,10,FontStyle.Bold,t.Accent).rectTransform,y+8,14,0,0);
                    Top(Img("section line",inner,null,t.Line).rectTransform,y+15,1,TextWidth(text,10,FontStyle.Bold)+10,0);
                    return y+26;
                }
            }
        }
        // A row: the label and its hint on the left, the control on the right; wide controls go under it.
        float Row(UiRow row,RectTransform group,float y,List<Action> refresh,float pad)
        {
            float width=contentWidth-2*pad, Right=rightColumn;
            var band=Rect("row "+row.Label,group);
            float statusW=row.Kind==UiRowKind.Status?StatusWidth(row):0;
            bool sideControl=row.Kind==UiRowKind.Toggle||row.Kind==UiRowKind.Slider||row.Kind==UiRowKind.Status||row.Kind==UiRowKind.Choice&&FitsBeside(row,Right);
            float textW=!sideControl?width:row.Kind==UiRowKind.Toggle?width-SwitchWidth()-16:row.Kind==UiRowKind.Status?width-statusW-16:width-(Right+16);
            float top=layout==UiLayout.Compact?9:12, labelH=0, hintH=0;
            if(!string.IsNullOrEmpty(row.Label))
            {
                var label=Label("label",band,row.Label,t.LabelSize,FontStyle.Normal,t.Ink); label.horizontalOverflow=HorizontalWrapMode.Wrap;
                At(label.rectTransform,pad,-top,textW,18); labelH=Mathf.Max(18,Mathf.Ceil(label.preferredHeight));
                label.rectTransform.sizeDelta=new Vector2(textW,labelH);
            }
            if(!string.IsNullOrEmpty(row.Hint))
            {
                var hint=Label("hint",band,row.Hint,t.HintSize,FontStyle.Normal,t.Soft); hint.horizontalOverflow=HorizontalWrapMode.Wrap;
                At(hint.rectTransform,pad,-(top+labelH+1),textW,14); hintH=Mathf.Ceil(hint.preferredHeight)+1;
                hint.rectTransform.sizeDelta=new Vector2(textW,hintH);
            }
            float textBottom=top+labelH+hintH;
            float height=Mathf.Max(layout==UiLayout.Compact?36:42,textBottom+top);
            var selectables=new List<Selectable>();
            float right=pad+width;
            switch(row.Kind)
            {
                case UiRowKind.Toggle: Switch(row,band,right,height*0.5f,refresh,selectables); break;
                case UiRowKind.Slider:
                {
                    float mid=(height-22)*0.5f;
                    var value=Label("value",band,"",12,FontStyle.Normal,style==UiStyle.Neon?t.Accent2:t.Soft); value.alignment=TextAnchor.MiddleRight;
                    At(value.rectTransform,right-50,-mid,50,22);
                    Slider(row,band,right-Right,mid,Right-58,value,refresh,selectables); break;
                }
                case UiRowKind.Choice:
                    if(sideControl) Chips(row,band,right-Right,(height-26)*0.5f,Right,true,refresh,selectables);
                    else { float at=textBottom+(labelH>0||hintH>0?8:-2); Chips(row,band,pad,at,width,false,refresh,selectables); height=at+26+top; }
                    break;
                case UiRowKind.Action:
                {
                    string text=Term?"[ "+(row.Button??"").ToLowerInvariant()+" ]":Cap(row.Button);
                    float buttonW=Mathf.Max(92,TextWidth(text,12,FontStyle.Bold)+24), below=textBottom+8, choicesW=row.Options!=null&&row.Options.Length>0?width-buttonW-10:0;
                    if(choicesW>0) Chips(row,band,pad,below,choicesW,false,refresh,selectables);
                    Color face=Term?t.Track:style==UiStyle.Paper?t.Ink:t.Accent;
                    Button b=Button("action",band,text,()=>{ if(Available(row)&&row.Click!=null) row.Click(); },UiArt.Fill(style==UiStyle.Glass?13:t.Control),face);
                    var bl=b.GetComponentInChildren<Text>(); bl.color=Term?t.Accent:style==UiStyle.Paper?t.OnAccent:t.OnAccent; bl.fontStyle=FontStyle.Bold;
                    At(b.GetComponent<RectTransform>(),right-buttonW,-below,buttonW,26); selectables.Add(b); height=below+26+top; break;
                }
                case UiRowKind.Status: StatusPill(row,band,right,statusW,height); break;
                default:
                {
                    var text=Label("info",band,row.Text!=null?row.Text():"",t.HintSize,FontStyle.Normal,t.Soft); text.horizontalOverflow=HorizontalWrapMode.Wrap;
                    float at=top+labelH+1; At(text.rectTransform,pad,-at,width,14);
                    float h=Mathf.Ceil(text.preferredHeight)+1; text.rectTransform.sizeDelta=new Vector2(width,h); height=at+h+top;
                    refresh.Add(()=>{ if(row.Text!=null) TextIfChanged(text,row.Text()); }); break;
                }
            }
            Top(band,y,height,0,0);
            var fade=band.gameObject.AddComponent<CanvasGroup>(); bool on=true; float shown=1;
            refresh.Add(()=>{ bool now=Available(row); if(now==on) return; on=now; foreach(Selectable s in selectables) s.interactable=now; });
            animations.Add(dt=>{ shown=Ease(shown,on?1:0.4f,dt,12); fade.alpha=shown; });
            if(!Available(row)) { on=false; shown=0.4f; fade.alpha=0.4f; foreach(Selectable s in selectables) s.interactable=false; }
            return height;
        }
        bool FitsBeside(UiRow row,float width)
        {
            if(row.Options==null||row.Options.Length==0||row.Options.Length>3) return false;
            float total=0;
            foreach(string o in row.Options) total+=TextWidth(Cap(o),12,FontStyle.Normal)+22;
            return total+6*(row.Options.Length-1)<=width;
        }
        float TextWidth(string text,int size,FontStyle fontStyle)
        {
            if(font==null||string.IsNullOrEmpty(text)) return 0;
            font.RequestCharactersInTexture(text,size,fontStyle);
            float w=0; CharacterInfo info;
            foreach(char ch in text) if(font.GetCharacterInfo(ch,out info,size,fontStyle)) w+=info.advance;
            return w;
        }
        static bool Available(UiRow row) { return row.Available==null||row.Available(); }
        // A mod's state: a small tag on the right, coloured by how it works with VEFX.
        string StatusWord(UiRow row,int state)
        {
            string word=row.StatusNames!=null&&state>=0&&state<row.StatusNames.Length?row.StatusNames[state]:"";
            return Term?"["+word.ToLowerInvariant()+"]":Cap(word);
        }
        float StatusWidth(UiRow row)
        {
            float w=0;
            for(int i=0;i<4;i++) w=Mathf.Max(w,TextWidth(StatusWord(row,i),11,FontStyle.Bold));
            return w+22;
        }
        void StatusPill(UiRow row,RectTransform band,float right,float w,float height)
        {
            var pill=Img("status",band,UiArt.Fill(style==UiStyle.Glass?11:t.Control),Color.clear);
            At(pill.rectTransform,right-w,-(height-22)*0.5f,w,22);
            var word=Label("status text",pill.transform,"",11,FontStyle.Bold,t.Faint); word.alignment=TextAnchor.MiddleCenter; Stretch(word.rectTransform,2,0,2,0);
            int last=-1;
            Action update=()=>{
                int state=row.GetStatus!=null?row.GetStatus():0; if(state==last) return; last=state;
                Color c=state==1?t.Good:state==2?t.Warn:state==3?t.Bad:t.Faint;
                pill.color=state==0?(Term?Color.clear:t.Line):new Color(c.r,c.g,c.b,Term?0:style==UiStyle.Paper?0.12f:0.18f);
                word.color=c; word.text=StatusWord(row,state);
            };
            update(); animations.Add(dt=>update());
        }
        float SwitchWidth()
        {
            switch(style) { case UiStyle.Neon: return 42; case UiStyle.Terminal: return Mathf.Max(TextWidth("[ off ]",12,FontStyle.Bold)+4,40); case UiStyle.Paper: return 20; case UiStyle.Mission: return 48; default: return 38; }
        }
        // A switch, in the window's style: a slim pill with a gliding knob (Glass), a glowing bar (Neon), [ ON ] (Terminal),
        // an inked check box (Paper), a lamp (Mission).
        void Switch(UiRow row,RectTransform band,float right,float mid,List<Action> refresh,List<Selectable> selectables)
        {
            float w=SwitchWidth(), h=style==UiStyle.Paper?20:style==UiStyle.Glass||Term?22:20;
            Color trackColour=Term?Color.clear:style==UiStyle.Paper?new Color(t.Ink.r,t.Ink.g,t.Ink.b,0.75f):t.Track;
            var track=Img("checkbox",band,UiArt.Fill(style==UiStyle.Glass?11:t.Control),trackColour); At(track.rectTransform,right-w,-(mid-h*0.5f),w,h); track.raycastTarget=true;
            Image glow=null, knob=null, inside=null, check=null; Text word=null;
            switch(style)
            {
                case UiStyle.Terminal:
                    word=Label("state",track.transform,"",12,FontStyle.Bold,t.Soft); word.alignment=TextAnchor.MiddleCenter; Stretch(word.rectTransform,0,0,0,0); break;
                case UiStyle.Paper:
                    inside=Img("box",track.transform,UiArt.Fill(1),new Color(t.Body.r,t.Body.g,t.Body.b,1)); Stretch(inside.rectTransform,1.5f,1.5f,1.5f,1.5f);
                    check=Img("check",track.transform,UiArt.Check(),t.Accent); Stretch(check.rectTransform,1,1,1,1); break;
                case UiStyle.Mission:
                    glow=Img("on",track.transform,UiArt.Fill(0),t.Accent); Stretch(glow.rectTransform,0,0,0,0);
                    word=Label("state",track.transform,"",10,FontStyle.Bold,t.Soft); word.alignment=TextAnchor.MiddleCenter; Stretch(word.rectTransform,0,0,0,0); break;
                default:
                    glow=Img("on",track.transform,UiArt.Fill(style==UiStyle.Glass?11:t.Control),t.Accent); Stretch(glow.rectTransform,0,0,0,0);
                    knob=Img("knob",track.transform,UiArt.Fill(style==UiStyle.Glass?9:2),Color.white); break;
            }
            var toggle=track.gameObject.AddComponent<Toggle>(); toggle.targetGraphic=track; toggle.toggleTransition=Toggle.ToggleTransition.None;
            ColorBlock c=toggle.colors; c.normalColor=Color.white; c.highlightedColor=Color.white; c.pressedColor=new Color(0.9f,0.9f,0.9f);
            c.selectedColor=Color.white; c.disabledColor=Color.white; c.fadeDuration=0.1f; toggle.colors=c;
            toggle.SetIsOnWithoutNotify(row.GetBool());
            float k=toggle.isOn?1:0;
            Action<float> place=v=>{
                if(glow!=null) glow.color=new Color(t.Accent.r,t.Accent.g,t.Accent.b,style==UiStyle.Neon?v*0.9f:v);
                if(knob!=null)
                {
                    float ks=h-6;
                    var r=knob.rectTransform; r.anchorMin=r.anchorMax=new Vector2(0,0.5f); r.pivot=new Vector2(0,0.5f); r.sizeDelta=new Vector2(ks,ks);
                    r.anchoredPosition=new Vector2(3+v*(w-ks-6),0);
                    knob.color=style==UiStyle.Neon?Color.Lerp(t.Accent2,Color.white,v):Color.Lerp(new Color(0.85f,0.87f,0.89f),Color.white,v);
                }
                if(check!=null) { check.color=new Color(t.Accent.r,t.Accent.g,t.Accent.b,v); check.rectTransform.localScale=Vector3.one*(0.6f+0.4f*v); }
                if(word!=null)
                {
                    bool on=v>0.5f;
                    word.text=Term?(on?"[ on ]":"[ off ]"):(on?"ON":"OFF");
                    word.color=Term?(on?t.Accent:t.Faint):(on?t.OnAccent:t.Soft);
                }
            };
            place(k);
            toggle.onValueChanged.AddListener(v=>{ if(toggle.IsActive()&&Available(row)&&v!=row.GetBool()) { row.SetBool(v); Changed(); } });
            refresh.Add(()=>toggle.SetIsOnWithoutNotify(row.GetBool()));
            animations.Add(dt=>{ float target=toggle.isOn?1:0; if(Mathf.Abs(k-target)<0.001f&&dt>0) return; k=dt>0?Ease(k,target,dt,16):target; place(k); });
            selectables.Add(toggle);
        }
        // A slider, in the window's style: a fine line and a ringed knob (Glass), a glowing line and a square knob (Neon), a
        // [#####.....] bar (Terminal), an ink line and a diamond (Paper), a segmented gauge (Mission).
        void Slider(UiRow row,RectTransform band,float left,float top,float width,Text value,List<Action> refresh,List<Selectable> selectables)
        {
            bool drawn=Term||style==UiStyle.Mission;   // drawn by text or segments: the slider's own graphics are hidden
            var r=Rect("slider",band); At(r,left-6,-top,width+12,22);
            var hit=r.gameObject.AddComponent<Image>(); hit.color=Color.clear;
            var track=Img("track",r,UiArt.Fill(1),drawn?Color.clear:t.Track); Stretch(track.rectTransform,8,10,8,10);
            var fillArea=Rect("fill area",r); Stretch(fillArea,8,10,8,10);
            var fill=Img("fill",fillArea,UiArt.Fill(1),drawn?Color.clear:t.Accent); Stretch(fill.rectTransform,0,0,0,0);
            var handleArea=Rect("handle area",r);
            handleArea.anchorMin=new Vector2(0,0.5f); handleArea.anchorMax=new Vector2(1,0.5f);
            handleArea.offsetMin=new Vector2(8,-8); handleArea.offsetMax=new Vector2(-8,8);
            Color handleColour=drawn?Color.clear:style==UiStyle.Neon?t.Accent2:style==UiStyle.Paper?t.Ink:t.Accent;
            var handle=Img("handle",handleArea,UiArt.Fill(style==UiStyle.Glass?8:1),handleColour); handle.raycastTarget=true;
            handle.rectTransform.anchorMin=handle.rectTransform.anchorMax=new Vector2(0,0.5f);
            float knob=style==UiStyle.Neon?12:style==UiStyle.Paper?11:16;
            handle.rectTransform.sizeDelta=new Vector2(knob,knob-16);   // (stretched over the area's 16 px height: square)
            if(style==UiStyle.Glass) { var core=Img("core",handle.transform,UiArt.Fill(4),new Color(t.Body.r,t.Body.g,t.Body.b,1)); Stretch(core.rectTransform,4,4,4,4); }
            if(style==UiStyle.Paper) { handle.rectTransform.localRotation=Quaternion.Euler(0,0,45); var core=Img("core",handle.transform,UiArt.Fill(1),t.Accent); Stretch(core.rectTransform,3,3,3,3); }
            Text bar=null; Image[] cells=null;
            if(Term)
            {
                bar=Label("bar",r,"",12,FontStyle.Bold,t.Accent); bar.alignment=TextAnchor.MiddleLeft; Stretch(bar.rectTransform,4,0,0,0);
            }
            else if(style==UiStyle.Mission)
            {
                int n=Mathf.Clamp((int)(width/9),8,30); cells=new Image[n];
                float cw=(width-(n-1)*2f)/n;
                for(int i=0;i<n;i++) { cells[i]=Img("cell",r,null,t.Track); At(cells[i].rectTransform,6+i*(cw+2),-5,cw,12); }
            }
            int columns=Term?Mathf.Max(6,(int)(width/Mathf.Max(TextWidth("#",12,FontStyle.Bold),4))-2):0;
            Action<float> draw=v=>{
                float f=row.Max>row.Min?Mathf.Clamp01((v-row.Min)/(row.Max-row.Min)):0;
                if(bar!=null) { int on=Mathf.RoundToInt(f*columns); TextIfChanged(bar,"["+new string('#',on)+new string('.',columns-on)+"]"); }
                if(cells!=null) { int on=Mathf.RoundToInt(f*cells.Length); for(int i=0;i<cells.Length;i++) cells[i].color=i<on?t.Accent:t.Track; }
            };
            var slider=r.gameObject.AddComponent<Slider>(); slider.minValue=row.Min; slider.maxValue=row.Max;
            slider.fillRect=fill.rectTransform; slider.handleRect=handle.rectTransform; slider.targetGraphic=handle;
            slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;
            ColorBlock c=slider.colors; c.normalColor=Color.white; c.highlightedColor=new Color(1.1f,1.1f,1.1f); c.pressedColor=new Color(0.85f,0.85f,0.85f);
            c.selectedColor=Color.white; c.disabledColor=Color.white; c.fadeDuration=0.1f; slider.colors=c;
            slider.SetValueWithoutNotify(row.GetValue());
            slider.onValueChanged.AddListener(v=>{
                if(!slider.IsActive()||!Available(row)) return;
                if(row.Step>0) v=row.Min+Mathf.Round((v-row.Min)/row.Step)*row.Step;
                v=Mathf.Clamp(v,row.Min,row.Max);
                if(Mathf.Abs(v-row.GetValue())>0.0001f) { row.SetValue(v); Changed(); }
            });
            float last=float.NaN;
            refresh.Add(()=>{
                float v=row.GetValue(); if(v==last) return; last=v;
                slider.SetValueWithoutNotify(v); TextIfChanged(value,row.Format!=null?row.Format(v):v.ToString("0.##")); draw(v);
            });
            draw(row.GetValue());
            selectables.Add(slider);
        }
        // Choices side by side; the chosen one marked in the style's manner (the mark glides between them): an accent pill
        // (Glass), a glowing box (Neon), inverse video (Terminal), a red underline (Paper), a lit segment of a bar (Mission).
        void Chips(UiRow row,RectTransform band,float left,float top,float width,bool beside,List<Action> refresh,List<Selectable> selectables)
        {
            if(row.Options==null||row.Options.Length==0) return;
            int n=row.Options.Length; float gap=style==UiStyle.Mission?0:Term?4:6;
            var shown=new string[n]; for(int i=0;i<n;i++) shown[i]=Cap(row.Options[i]);
            var widths=new float[n]; float natural=0;
            for(int i=0;i<n;i++) { widths[i]=TextWidth(shown[i],12,FontStyle.Normal)+22; natural+=widths[i]; }
            natural+=gap*(n-1);
            // Beside the label: packed to the right; under it: spread over the width (each a little wider, left-aligned).
            float grow=beside?1:Mathf.Clamp((width-gap*(n-1))/Mathf.Max(natural-gap*(n-1),1),1,1.5f);
            var xs=new float[n]; float used=gap*(n-1);
            for(int i=0;i<n;i++) { widths[i]*=grow; used+=widths[i]; }
            float x=beside?left+width-used:left;
            for(int i=0;i<n;i++) { xs[i]=x; x+=widths[i]+gap; }
            Sprite chip=UiArt.Fill(style==UiStyle.Glass?12:t.Control);
            bool underline=style==UiStyle.Paper;
            var fill=Img("chosen",band,chip,t.Accent);
            fill.rectTransform.anchorMin=fill.rectTransform.anchorMax=new Vector2(0,1); fill.rectTransform.pivot=new Vector2(0,1);
            var labels=new Text[n];
            for(int i=0;i<n;i++)
            {
                int index=i;
                if(style!=UiStyle.Terminal&&!underline) { var outline=Img("chip outline",band,chip,t.Line); At(outline.rectTransform,xs[i],-top,widths[i],26); }
                Button b=Button("option "+row.Options[i],band,shown[i],()=>{
                    if(Available(row)&&row.GetChoice()!=index) { row.SetChoice(index); Changed(); }
                },chip,Color.clear);
                At(b.GetComponent<RectTransform>(),xs[i],-top,widths[i],26); selectables.Add(b);
                labels[i]=b.GetComponentInChildren<Text>();
            }
            float fx=-1, fw=0, seen=0;
            int lastChoice=int.MinValue;
            refresh.Add(()=>{
                int chosen=row.GetChoice(); if(chosen==lastChoice) return; lastChoice=chosen;
                for(int i=0;i<n;i++) labels[i].color=i==chosen?(underline?t.Accent:t.OnAccent):t.Soft;
            });
            animations.Add(dt=>{
                int chosen=row.GetChoice(); bool any=chosen>=0&&chosen<n;
                float tx=any?xs[chosen]:fx, tw=any?widths[chosen]:fw;
                if(fx<0||dt<=0) { fx=tx; fw=tw; seen=any?1:0; }
                else { fx=Ease(fx,tx,dt,18); fw=Ease(fw,tw,dt,18); seen=Ease(seen,any?1:0,dt,14); }
                if(underline) { fill.rectTransform.anchoredPosition=new Vector2(fx+8,-(top+22)); fill.rectTransform.sizeDelta=new Vector2(Mathf.Max(fw-16,4),2); }
                else { fill.rectTransform.anchoredPosition=new Vector2(fx,-top); fill.rectTransform.sizeDelta=new Vector2(fw,26); }
                fill.color=new Color(t.Accent.r,t.Accent.g,t.Accent.b,seen);
            });
        }
        Button Button(string name,Transform parent,string text,Action action,Sprite sprite,Color colour)
        {
            var face=Img(name,parent,sprite,Color.white); face.raycastTarget=true;
            var b=face.gameObject.AddComponent<Button>(); b.targetGraphic=face;
            ColorBlock c=b.colors; c.normalColor=colour;
            Color hover=colour.a<0.01f?t.Hover:new Color(Mathf.Min(1,colour.r*1.08f+0.02f),Mathf.Min(1,colour.g*1.08f+0.02f),Mathf.Min(1,colour.b*1.08f+0.02f),Mathf.Max(colour.a,0.12f));
            c.highlightedColor=hover; c.selectedColor=colour; c.pressedColor=new Color(hover.r*0.85f,hover.g*0.85f,hover.b*0.85f,Mathf.Max(hover.a,0.08f));
            c.disabledColor=colour; c.colorMultiplier=1; c.fadeDuration=0.12f; b.colors=c;
            if(!string.IsNullOrEmpty(text))
            {
                var label=Label("label",face.transform,text,12,FontStyle.Normal,t.Ink); label.alignment=TextAnchor.MiddleCenter; Stretch(label.rectTransform,4,0,4,0);
            }
            b.onClick.AddListener(()=>action()); return b;
        }
        static void TextIfChanged(Text x,string value) { if(x.text!=value) x.text=value??""; }
        static void SetLayer(Transform x,int layer) { x.gameObject.layer=layer; for(int i=0;i<x.childCount;i++) SetLayer(x.GetChild(i),layer); }
        static RectTransform Rect(string name,Transform parent)
        { var go=new GameObject(name,typeof(RectTransform)); var r=(RectTransform)go.transform; r.SetParent(parent,false); return r; }
        static Image Img(string name,Transform parent,Sprite sprite,Color colour)
        {
            var r=Rect(name,parent); var i=r.gameObject.AddComponent<Image>(); i.sprite=sprite;
            i.type=sprite!=null&&sprite.border!=Vector4.zero?Image.Type.Sliced:Image.Type.Simple; i.color=colour; i.raycastTarget=false; return i;
        }
        Text Label(string name,Transform parent,string text,int size,FontStyle fontStyle,Color colour)
        {
            var r=Rect(name,parent); var x=r.gameObject.AddComponent<Text>(); x.font=font; x.fontSize=size; x.fontStyle=fontStyle;
            x.color=colour; x.text=text??""; x.raycastTarget=false; x.supportRichText=false; x.alignment=TextAnchor.UpperLeft;
            x.horizontalOverflow=HorizontalWrapMode.Overflow; x.verticalOverflow=VerticalWrapMode.Overflow; return x;
        }
        static void At(RectTransform r,float x,float y,float w,float h)
        { r.anchorMin=r.anchorMax=new Vector2(0,1); r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,y); r.sizeDelta=new Vector2(w,h); }
        static void Stretch(RectTransform r,float left,float bottom,float right,float top)
        { r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.pivot=new Vector2(0.5f,0.5f); r.offsetMin=new Vector2(left,bottom); r.offsetMax=new Vector2(-right,-top); }
        static void Top(RectTransform r,float from,float height,float left,float right)
        { r.anchorMin=new Vector2(0,1); r.anchorMax=new Vector2(1,1); r.pivot=new Vector2(0.5f,1); r.offsetMin=new Vector2(left,-from-height); r.offsetMax=new Vector2(-right,-from); }
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
        { if(EventSystem.current!=null) { GameObject s=EventSystem.current.currentSelectedGameObject; if(s!=null&&s.transform.IsChildOf(Root.transform)) EventSystem.current.SetSelectedGameObject(null); } }
        public void Dispose() { if(Root!=null) UnityEngine.Object.Destroy(Root); }
    }
}
