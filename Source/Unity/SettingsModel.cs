using System;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    internal static class SettingsModel
    {
        internal sealed class Hooks
        {
            internal string Version;
            internal UiText Text;
            internal Action Changed, Close, Reset;
            internal Func<bool> CanTest; internal Func<int,bool> Test;
            internal Func<string> Status;
            internal Func<int,bool> TestLook;
            internal string Conflict;   // another installed explosion mod (null: none)
            internal Func<string[],bool> HasMod;   // whether a mod is loaded (its assemblies' name prefixes)
        }
        static int testKind, lookKind;
        // Look editor dial names and hints stay in English: an advanced tuning tool, named as in the docs.
        static readonly string[] DialNames={"Fireball shape","Turbulence","Temperature gradient","Smoke density","Smoke opacity",
            "Light intensity","Expansion speed","Cooling rate","Noise scale","Shockwave strength","Effect lifetime","Flame heat","Soot darkness"};
        static readonly string[] DialHints={"Billow size of the silhouette.","Fine ragged detail on the fire's surface.",
            "Hot pockets against cooler lanes; spreads the cooling.","How much soot the fire makes.","How opaque the cooled smoke is.",
            "Light cast on the ground, parts and debris.","How fast the fireball reaches full size.","How fast the flames go out.",
            "Size of the billows (higher = finer).","Shock dome and ring visibility.","How long the explosion and its smoke last.",
            "Whole flame colour: + whiter, − redder.","How dark cooling, sooty gas becomes."};
        internal static UiModel Build(FxSettings s,Hooks h)
        {
            var text=h.Text??new UiText("en-us");
            string L(string key,string fallback) { return text.Get(key,fallback); }
            var m=new UiModel { Title="Volumetric Explosion FX", Subtitle="VEFX  ·  "+L("Settings","Settings")+"  ·  "+h.Version,
                Reset=L("Defaults","Defaults"), Close=h.Close, ResetAll=h.Reset, Changed=h.Changed, Status=h.Status };
            m.Style=(UiStyle)s.UiStyle; m.Layout=(UiLayout)s.UiLayout; m.Opacity=(float)s.UiOpacity;
            var general=Page(m,L("General","General"),UiGlyph.General);
            if(!string.IsNullOrEmpty(h.Conflict))
                general.Rows.Add(new UiRow { Kind=UiRowKind.Info, Label=L("OtherModTitle","Another explosion mod"),
                    Text=()=>text.Format("OtherMod","{0} is also installed: both replace the game's explosions, so every blast is drawn twice. Keep only one of them.",h.Conflict) });
            Toggle(general,L("Enabled","Enable effects"),L("EnabledHint","Explosions, flames and visual fragments."),
                ()=>s.Enabled,v=>s.Enabled=v);
            general.Rows.Add(new UiRow { Kind=UiRowKind.Choice, Label=L("Quality","Quality"), Hint=L("QualityHint","Preset rendering limits."),
                Options=new[]{L("Low","Low"),L("Medium","Medium"),L("High","High"),L("Ultra","Ultra"),L("Custom","Custom")}, GetChoice=()=>(int)s.Quality,
                SetChoice=i=>{ if(i<(int)Quality.Custom) s.ApplyPreset((Quality)i); else s.Quality=Quality.Custom; s.Profile=-1; } });
            general.Rows.Add(new UiRow { Kind=UiRowKind.Choice, Label=L("QuickSetup","Quick setup"),
                Hint=L("QuickSetupHint","Cinematic: everything at full detail. Balanced: half-resolution volumes that adapt to the frame rate. Performance: lighter effects for modest PCs."),
                Options=new[]{L("Cinematic","Cinematic"),L("Balanced","Balanced"),L("PerfProfile","Performance")}, GetChoice=()=>s.Profile,
                SetChoice=i=>s.ApplyProfile(i) });
            Slider(general,L("Intensity","Intensity"),L("IntensityHint","1.00: reference setting."),0.25f,2,0.05f,
                ()=>(float)s.Intensity,v=>s.Intensity=v,v=>v.ToString("0.00",text.Culture)+" ×");
            Toggle(general,L("ReplaceStock","Replace KSP explosions"),L("ReplaceStockHint","Hides stock visuals when VEFX replaces them. Keeps their sound."),
                ()=>s.ReplaceStockVisuals,v=>s.ReplaceStockVisuals=v);
            Slider(general,L("SpawnDistance","Spawn distance"),L("SpawnDistanceHint","Existing effects remain visible."),0.1f,30,0.1f,
                ()=>(float)(s.Distance/1000),v=>s.Distance=v*1000,v=>v.ToString("0.#",text.Culture)+" km");
            Section(general,L("Preview","In-flight preview"));
            general.Rows.Add(new UiRow { Kind=UiRowKind.Action, Label=L("TestExplosion","Test explosion"),
                Hint=L("TestHint","Preview in front of the camera, without damage. Big crash: eight explosions, with performance recorded in KSP.log."),
                Options=new[]{L("Tank","Tank"),L("Stage","Stage"),L("Booster","Booster"),L("InFlight","In flight"),L("BigCrash","Big crash")}, GetChoice=()=>testKind, SetChoice=i=>testKind=i,
                Button=L("Test","Test"), Click=()=>{ if(h.Test!=null) h.Test(testKind); }, Available=h.CanTest });
            general.Rows.Add(new UiRow { Kind=UiRowKind.Choice, Label=L("PhotoMode","Photo mode"),
                Hint=L("PhotoModeHint","Slows down or freezes the explosions only (the game keeps running), for screenshots. Shortcut: Alt+F7."),
                Options=new[]{L("PhotoOff","Off"),L("PhotoSlow","Slow motion"),L("PhotoFrozen","Frozen")}, GetChoice=()=>s.PhotoMode,
                SetChoice=i=>s.PhotoMode=i });
            Toggle(general,L("LookEditor","Look editor (advanced)"),L("LookEditorHint","Adds a page to tune each kind of explosion by hand."),
                ()=>s.LookEditor,v=>s.LookEditor=v);
            // The window itself: its style, layout, opacity and size.
            Section(general,L("WindowSection","Window"));
            general.Rows.Add(new UiRow { Kind=UiRowKind.Choice, Label=L("Style","Style"), Hint=L("StyleHint","Five complete looks for this window."),
                Options=new[]{L("SkinGlass","Glass"),L("SkinNeon","Neon"),L("SkinTerminal","Terminal"),L("SkinPaper","Paper"),L("SkinMission","Mission")},
                GetChoice=()=>s.UiStyle, SetChoice=i=>s.UiStyle=i });
            general.Rows.Add(new UiRow { Kind=UiRowKind.Choice, Label=L("Layout","Layout"),
                Hint=L("LayoutHint","Open: tabs on top. Panel: a sidebar of pages. Compact: a smaller window."),
                Options=new[]{L("StyleOpen","Open"),L("StylePanel","Panel"),L("StyleCompact","Compact")}, GetChoice=()=>s.UiLayout, SetChoice=i=>s.UiLayout=i });
            Slider(general,L("Opacity","Opacity"),L("OpacityHint","How much of the game shows through the window."),0.35f,1,0.05f,
                ()=>(float)s.UiOpacity,v=>s.UiOpacity=v,v=>(v*100).ToString("0",text.Culture)+" %");
            Slider(general,L("WindowSize","Window size"),L("WindowSizeHint","Size of this window on the screen."),0.7f,1.3f,0.05f,
                ()=>(float)s.UiSize,v=>s.UiSize=v,v=>(v*100).ToString("0",text.Culture)+" %");
            var blasts=Page(m,L("Explosions","Explosions"),UiGlyph.Blast);
            Section(blasts,L("Fireballs","Fireballs"));
            Toggle(blasts,L("Explosions","Explosions"),L("ExplosionsHint","Fire and smoke based on the destroyed part contents."),()=>s.Explosions,v=>s.Explosions=v);
            Toggle(blasts,L("Volumetric","Volumetric rendering"),L("VolumetricHint","3D volumes for explosions and ground fires. Higher GPU cost."),()=>s.Volumetric,v=>s.Volumetric=v);
            Toggle(blasts,L("Shockwave","Shockwave and distortion"),L("ShockwaveHint","Shockwaves and heat distort the image around the explosion."),()=>s.Shockwave,v=>s.Shockwave=v);
            Toggle(blasts,L("CameraShake","Camera shake"),L("CameraShakeHint","Nearby explosions shake the camera; the blast arrives after the flash, like the sound."),
                ()=>s.CameraShake,v=>s.CameraShake=v);
            Toggle(blasts,L("Lighting","Dynamic lighting"),L("LightingHint","Explosions and fires light the ground, rockets and smoke."),()=>s.Lighting,v=>s.Lighting=v);
            Section(blasts,L("Impacts","Impacts"));
            Toggle(blasts,L("Ground","Ground crashes"),L("GroundHint","Thrown soil and dust, with a blast along the ground."),()=>s.Ground,v=>s.Ground=v);
            Toggle(blasts,L("Water","Water impacts"),L("WaterHint","Water column, spray and a foam ring."),()=>s.Water,v=>s.Water=v);
            Toggle(blasts,L("Burst","Bursts and vapour"),L("BurstHint","Pressurized tanks, vapour clouds and electrical sparks."),()=>s.Burst,v=>s.Burst=v);
            var fire=Page(m,L("FireSmoke","Fire & smoke"),UiGlyph.Flame);
            Toggle(fire,L("Residue","Ground fire and smoke"),L("ResidueHint","After a fuelled crash: fire, smoke and ground marks."),
                ()=>s.Residue,v=>s.Residue=v);
            Slider(fire,L("ResidueDuration","Residue duration"),L("ResidueDurationHint","Ground fires, smoke and fragments."),0,180,5,
                ()=>(float)s.ResidueSeconds,v=>s.ResidueSeconds=v,v=>v<1?L("None","None"):v.ToString("0",text.Culture)+" s");
            fire.Rows.Add(new UiRow { Kind=UiRowKind.Info, Label=L("FlameRendering","Flame rendering"),
                Text=()=>L("FlameRenderingHint","Volumetric rendering is set under Explosions. Without a compatible shader, VEFX uses its fallback renderer.") });
            var wreck=Page(m,L("Wreckage","Wreckage"),UiGlyph.Wreck);
            Toggle(wreck,L("PartFragments","Part fragments"),L("PartFragmentsHint","Visual pieces from the destroyed part model."),
                ()=>s.PartDebris,v=>s.PartDebris=v);
            Slider(wreck,L("FragmentDetail","Fragment detail"),L("FragmentDetailHint","Triangles per destroyed part."),300,16000,100,
                ()=>s.DebrisTriangles,v=>{ s.Quality=Quality.Custom; s.DebrisTriangles=(int)v; },v=>(v/1000).ToString("0.0",text.Culture)+" k");
            Slider(wreck,L("RetainedParts","Retained parts"),L("RetainedPartsHint","The oldest parts are replaced."),0,48,1,
                ()=>s.MaxDebrisParts,v=>{ s.Quality=Quality.Custom; s.MaxDebrisParts=(int)v; },v=>v.ToString("0",text.Culture));
            Toggle(wreck,L("MicroDebris","Micro debris"),L("MicroDebrisHint","Shards and small pieces thrown by the explosion."),()=>s.Debris,v=>s.Debris=v);
            Slider(wreck,L("DebrisDensity","Debris density"),L("DebrisDensityHint","Amount per explosion."),0,2,0.05f,
                ()=>(float)s.DebrisDensity,v=>s.DebrisDensity=v,v=>v.ToString("0.00",text.Culture)+" ×");
            var perf=Page(m,L("Performance","Performance"),UiGlyph.Gauge);
            perf.Rows.Add(new UiRow { Kind=UiRowKind.Choice, Label=L("VolumeResolution","Volume resolution"),
                Hint=L("VolumeResolutionHint","Fireballs and fires drawn at reduced resolution: much faster, slightly softer."),
                Options=new[]{L("Full","Full"),"1/2","1/3"}, GetChoice=()=>s.VolumeDivisor-1,
                SetChoice=i=>{ s.VolumeDivisor=i+1; s.Validate(); } });
            Toggle(perf,L("Adaptive","Adaptive performance"),L("AdaptiveHint","Lowers volume detail and lights while the frame rate drops, then restores them."),
                ()=>s.Adaptive,v=>s.Adaptive=v);
            perf.Rows.Add(new UiRow { Kind=UiRowKind.Info, Label=L("Limits","Limits"),
                Text=()=>text.Format("LimitsHelp","Changing a limit selects Custom quality. Preset: {0}. Changing capacity clears active effects.",QualityName(s.Quality,text)) });
            Limit(perf,s,text,L("MaxEvents","Simultaneous explosions"),L("MaxEventsHint","Beyond this limit, weaker effects give way to stronger ones."),1,32,1,()=>s.MaxEvents,v=>s.MaxEvents=v);
            Limit(perf,s,text,L("MaxVolumes","Explosion volumes"),L("MaxVolumesHint","Ground fires are counted separately."),0,16,1,()=>s.MaxVolumes,v=>s.MaxVolumes=v);
            Limit(perf,s,text,L("VolumeSteps","Volume detail"),L("VolumeStepsHint","More steps increase detail and GPU cost."),16,56,4,()=>s.VolumeSteps,v=>s.VolumeSteps=v);
            Limit(perf,s,text,L("Particles","Particles per explosion"),L("ParticlesHint","Sparks, flames and smoke in one explosion."),16,256,16,()=>s.ParticlesPerEvent,v=>s.ParticlesPerEvent=v);
            Limit(perf,s,text,L("MaxLights","Dynamic lights"),L("MaxLightsHint","Explosion lights active at the same time."),0,6,1,()=>s.MaxLights,v=>s.MaxLights=v);
            Limit(perf,s,text,L("MaxDebris","Total micro debris"),L("MaxDebrisHint","All flying shards at once."),0,4096,64,()=>s.MaxDebris,v=>s.MaxDebris=v);
            CompatibilityPage(m,h,text);
            if(s.LookEditor) LookPage(m,s,h,text);
            return m;
        }
        // The mods VEFX knows of, whether they are in this game and how they work with it.
        sealed class Mod
        {
            internal string Name, Note; internal int Fit; internal string[] Assemblies;   // fit: 0 works, 1 to check, 2 conflict
            internal Mod(string name,int fit,string note,params string[] assemblies) { Name=name; Fit=fit; Note=note; Assemblies=assemblies; }
        }
        static void CompatibilityPage(UiModel m,Hooks h,UiText text)
        {
            string L(string key,string fallback) { return text.Get(key,fallback); }
            var page=Page(m,L("Compat","Compatibility"),UiGlyph.Link);
            page.Rows.Add(new UiRow { Kind=UiRowKind.Info, Label=L("CompatTitle","Other mods"),
                Text=()=>L("CompatHelp","How the mods found in this game work with the explosions of VEFX.") });
            var words=new[]{L("StatusMissing","Not installed"),L("StatusWorks","Works"),L("StatusCheck","Check"),L("StatusConflict","Conflict")};
            var mods=new[]{
                new Mod("BDArmory",0,L("ModBDArmory","Weapons and warheads: VEFX draws their explosions."),"BDArmory"),
                new Mod("Waterfall",0,L("ModWaterfall","Engine flames stay in front of the explosion clouds."),"Waterfall"),
                new Mod("Environmental Visual Enhancements",0,L("ModEVE","Explosions hide behind its cloud layers."),"EVEManager"),
                new Mod("Scatterer",0,L("ModScatterer","Atmosphere and sea: the explosions are drawn over its haze."),"Scatterer"),
                new Mod("Parallax Continued",0,L("ModParallax","Wreckage lands on its rocks and trees."),"ParallaxContinued","Parallax"),
                new Mod("TUFX",0,L("ModTUFX","Post-processing: keep its bloom moderate or the flashes blow out."),"TUFX"),
                new Mod("Deferred",0,L("ModDeferred","Deferred rendering: supported."),"Deferred"),
                new Mod("Kopernicus",0,L("ModKopernicus","Other worlds: dust, sea and air taken from each body."),"Kopernicus"),
                new Mod("Firefly",0,L("ModFirefly","Re-entry heating: both show side by side."),"Firefly"),
                new Mod("Volumetric Ground FX",0,L("ModVGFX","Engine dust and water: made to work with VEFX."),"GroundBlastFx","VolumetricGroundFX"),
                new Mod("PlumeDynamics",0,L("ModPlume","Engine plumes: drawn in front of the clouds."),"PlumeDynamics"),
                new Mod("SmokeScreen",0,L("ModSmokeScreen","Engine smoke: works alongside."),"SmokeScreen"),
                new Mod("KerbalFX",1,L("ModKerbalFX","Contrails and dust work together; its own blast effect adds to the explosions of VEFX."),"KerbalFX"),
                new Mod("BlastFX",1,L("ModBlastFX","Also draws explosion effects: both may show at once."),"BlastFX"),
                new Mod("Volumetric Explosions",2,L("ModVolumetricExplosions","Also replaces the explosions: every blast is drawn twice. Keep only one."),"VolumetricExplosions"),
            };
            var found=new bool[mods.Length]; int count=0;
            for(int i=0;i<mods.Length;i++) { found[i]=h.HasMod!=null&&h.HasMod(mods[i].Assemblies); if(found[i]) count++; }
            for(int pass=0;pass<2;pass++)
            {
                if(pass==0&&count==0||pass==1&&count==mods.Length) continue;
                Section(page,pass==0?L("CompatInstalled","Installed"):L("CompatOther","Not installed"));
                for(int i=0;i<mods.Length;i++)
                {
                    if(found[i]!=(pass==0)) continue;
                    int state=found[i]?1+mods[i].Fit:0;
                    page.Rows.Add(new UiRow { Kind=UiRowKind.Status, Label=mods[i].Name, Hint=mods[i].Note, StatusNames=words, GetStatus=()=>state });
                }
            }
        }
        static void LookPage(UiModel m,FxSettings s,Hooks h,UiText text)
        {
            string L(string key,string fallback) { return text.Get(key,fallback); }
            var look=Page(m,L("Look","Look"),UiGlyph.Spark);
            look.Rows.Add(new UiRow { Kind=UiRowKind.Info, Label=L("LookEditor","Look editor (advanced)"),
                Text=()=>L("LookEditorHelp","Changes apply at once, also to explosions on screen. Saved with your settings.") });
            // Six kinds on two rows of three, so long translations still fit.
            look.Rows.Add(new UiRow { Kind=UiRowKind.Choice, Label=L("LookProfile","Profile"), Hint=L("LookProfileHint","Kind of explosion being tuned."),
                Options=new[]{L("LookTank","Tank"),L("LookSolid","Booster"),L("LookVessel","Vessel")},
                GetChoice=()=>lookKind<3?lookKind:-1, SetChoice=i=>lookKind=i });
            look.Rows.Add(new UiRow { Kind=UiRowKind.Choice, Label="",
                Options=new[]{L("LookAtmospheric","Airburst"),L("LookHighAltitude","High altitude"),L("LookReentry","Re-entry")},
                GetChoice=()=>lookKind>=3?lookKind-3:-1, SetChoice=i=>lookKind=i+3 });
            look.Rows.Add(new UiRow { Kind=UiRowKind.Action, Label=L("TestLook","Test this profile"), Hint=L("TestHint2","Preview in front of the camera, without damage."),
                Button=L("Test","Test"), Click=()=>{ if(h.TestLook!=null) h.TestLook(lookKind); }, Available=h.CanTest });
            Toggle(look,L("ClassicLook","Compare with 1.0"),L("ClassicLookHint","Renders every explosion with the 1.0 look while on."),
                ()=>s.Looks.UseClassic,v=>s.Looks.UseClassic=v);
            Section(look,L("LookDials","Dials"));
            for(int i=0;i<LookProfile.Count;i++)
            {
                int dial=i; float step=dial==11?0.01f:0.05f;
                Slider(look,DialNames[dial],DialHints[dial],(float)LookProfile.Min[dial],(float)LookProfile.Max[dial],step,
                    ()=>(float)s.Looks[(LookKind)lookKind][dial],v=>s.Looks[(LookKind)lookKind][dial]=v,
                    v=>dial==11?v.ToString("+0.00;−0.00;0.00",text.Culture):v.ToString("0.00",text.Culture));
            }
            look.Rows.Add(new UiRow { Kind=UiRowKind.Action, Label=L("ResetLook","Reset this profile"), Hint=L("ResetLookHint","Back to the shipped values of this kind."),
                Button=L("Defaults","Defaults"), Click=()=>{ s.Looks.Reset((LookKind)lookKind); if(h.Changed!=null) h.Changed(); } });
        }
        static string QualityName(Quality q,UiText text)
        {
            switch(q) { case Quality.Low: return text.Get("Low","Low"); case Quality.Medium: return text.Get("Medium","Medium"); case Quality.High: return text.Get("High","High"); case Quality.Ultra: return text.Get("Ultra","Ultra"); default: return text.Get("Custom","Custom"); }
        }
        static UiPage Page(UiModel m,string title,UiGlyph glyph) { var p=new UiPage { Title=title, Glyph=glyph }; m.Pages.Add(p); return p; }
        static void Section(UiPage p,string title) { p.Rows.Add(new UiRow { Kind=UiRowKind.Section, Label=title }); }
        static void Toggle(UiPage p,string label,string hint,Func<bool> get,Action<bool> set)
        { p.Rows.Add(new UiRow { Kind=UiRowKind.Toggle, Label=label, Hint=hint, GetBool=get, SetBool=set }); }
        static void Slider(UiPage p,string label,string hint,float min,float max,float step,Func<float> get,Action<float> set,Func<float,string> format)
        { p.Rows.Add(new UiRow { Kind=UiRowKind.Slider, Label=label, Hint=hint, Min=min, Max=max, Step=step, GetValue=get, SetValue=set, Format=format }); }
        static void Limit(UiPage p,FxSettings s,UiText text,string label,string hint,int min,int max,int step,Func<int> get,Action<int> set)
        {
            Slider(p,label,hint,min,max,step,()=>get(),v=>{ s.Quality=Quality.Custom; set((int)Math.Round(v)); s.Validate(); },v=>v.ToString("0",text.Culture));
        }
    }
}
