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
        }
        static int testKind;
        internal static UiModel Build(FxSettings s,Hooks h)
        {
            var text=h.Text??new UiText("en-us");
            string L(string key,string fallback) { return text.Get(key,fallback); }
            var m=new UiModel { Title="Volumetric Explosion FX", Subtitle="VEFX  ·  "+L("Settings","Settings")+"  ·  "+h.Version,
                Reset=L("Defaults","Defaults"), Close=h.Close, ResetAll=h.Reset, Changed=h.Changed, Status=h.Status };
            var general=Page(m,L("General","General"),UiGlyph.General);
            Toggle(general,L("Enabled","Enable effects"),L("EnabledHint","Explosions, flames and visual fragments."),
                ()=>s.Enabled,v=>s.Enabled=v);
            general.Rows.Add(new UiRow { Kind=UiRowKind.Choice, Label=L("Quality","Quality"), Hint=L("QualityHint","Preset rendering limits."),
                Options=new[]{L("Low","Low"),L("Medium","Medium"),L("High","High"),L("Ultra","Ultra"),L("Custom","Custom")}, GetChoice=()=>(int)s.Quality,
                SetChoice=i=>{ if(i<(int)Quality.Custom) s.ApplyPreset((Quality)i); else s.Quality=Quality.Custom; } });
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
            var blasts=Page(m,L("Explosions","Explosions"),UiGlyph.Blast);
            Section(blasts,L("Fireballs","Fireballs"));
            Toggle(blasts,L("Explosions","Explosions"),L("ExplosionsHint","Fire and smoke based on the destroyed part contents."),()=>s.Explosions,v=>s.Explosions=v);
            Toggle(blasts,L("Volumetric","Volumetric rendering"),L("VolumetricHint","3D volumes for explosions and ground fires. Higher GPU cost."),()=>s.Volumetric,v=>s.Volumetric=v);
            Toggle(blasts,L("Shockwave","Shockwave and distortion"),L("ShockwaveHint","Shockwaves and heat distort the image around the explosion."),()=>s.Shockwave,v=>s.Shockwave=v);
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
            perf.Rows.Add(new UiRow { Kind=UiRowKind.Info, Label=L("Limits","Limits"),
                Text=()=>text.Format("LimitsHelp","Changing a limit selects Custom quality. Preset: {0}. Changing capacity clears active effects.",QualityName(s.Quality,text)) });
            Limit(perf,s,text,L("MaxEvents","Simultaneous explosions"),L("MaxEventsHint","Beyond this limit, weaker effects give way to stronger ones."),1,24,1,()=>s.MaxEvents,v=>s.MaxEvents=v);
            Limit(perf,s,text,L("MaxVolumes","Explosion volumes"),L("MaxVolumesHint","Ground fires are counted separately."),0,8,1,()=>s.MaxVolumes,v=>s.MaxVolumes=v);
            Limit(perf,s,text,L("VolumeSteps","Volume detail"),L("VolumeStepsHint","More steps increase detail and GPU cost."),16,56,4,()=>s.VolumeSteps,v=>s.VolumeSteps=v);
            Limit(perf,s,text,L("Particles","Particles per explosion"),L("ParticlesHint","Sparks, flames and smoke in one explosion."),16,256,16,()=>s.ParticlesPerEvent,v=>s.ParticlesPerEvent=v);
            Limit(perf,s,text,L("MaxLights","Dynamic lights"),L("MaxLightsHint","Explosion lights active at the same time."),0,6,1,()=>s.MaxLights,v=>s.MaxLights=v);
            Limit(perf,s,text,L("MaxDebris","Total micro debris"),L("MaxDebrisHint","All flying shards at once."),0,4096,64,()=>s.MaxDebris,v=>s.MaxDebris=v);
            return m;
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
