using System;
using System.Globalization;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Rendering
{
    internal static class SettingsModel
    {
        internal sealed class Hooks
        {
            internal string Version;
            internal Action Changed, Close, Reset;
            internal Func<bool> CanTest; internal Func<int,bool> Test;
            internal Func<string> Status;
        }
        static readonly CultureInfo French=Culture();
        static CultureInfo Culture() { try { return CultureInfo.GetCultureInfo("fr-FR"); } catch(Exception) { return CultureInfo.InvariantCulture; } }
        static int testKind;
        internal static UiModel Build(FxSettings s,Hooks h)
        {
            var m=new UiModel { Title="Volumetric Explosion FX", Subtitle="VEFX  ·  Réglages  ·  "+h.Version,
                Reset="Par défaut", Close=h.Close, ResetAll=h.Reset, Changed=h.Changed, Status=h.Status };
            var general=Page(m,"Général",UiGlyph.General);
            Toggle(general,"Activer les effets","Explosions, flammes et fragments visuels.",
                ()=>s.Enabled,v=>s.Enabled=v);
            general.Rows.Add(new UiRow { Kind=UiRowKind.Choice, Label="Qualité", Hint="Préréglage des limites de rendu.",
                Options=new[]{"Basse","Moyenne","Haute","Ultra","Perso"}, GetChoice=()=>(int)s.Quality,
                SetChoice=i=>{ if(i<(int)Quality.Custom) s.ApplyPreset((Quality)i); else s.Quality=Quality.Custom; } });
            Slider(general,"Intensité","1,00 : réglage de référence.",0.25f,2,0.05f,
                ()=>(float)s.Intensity,v=>s.Intensity=v,v=>v.ToString("0.00",French)+" ×");
            Toggle(general,"Remplacer les explosions KSP","Masque leur rendu quand un effet du mod prend le relais. Garde le son.",
                ()=>s.ReplaceStockVisuals,v=>s.ReplaceStockVisuals=v);
            Slider(general,"Distance de création","Les effets déjà lancés restent visibles.",0.1f,30,0.1f,
                ()=>(float)(s.Distance/1000),v=>s.Distance=v*1000,v=>v.ToString("0.#",French)+" km");
            Section(general,"Aperçu en vol");
            general.Rows.Add(new UiRow { Kind=UiRowKind.Action, Label="Explosion de test",
                Hint="Effet devant la caméra, sans dégâts. « Gros crash » : huit explosions en chaîne, leur coût est noté dans KSP.log.",
                Options=new[]{"Réservoir","Étage","Booster","En vol","Gros crash"}, GetChoice=()=>testKind, SetChoice=i=>testKind=i,
                Button="Tester", Click=()=>{ if(h.Test!=null) h.Test(testKind); }, Available=h.CanTest });
            var blasts=Page(m,"Explosions",UiGlyph.Blast);
            Section(blasts,"Boules de feu");
            Toggle(blasts,"Explosions","Feu et fumée selon le contenu de la pièce détruite.",()=>s.Explosions,v=>s.Explosions=v);
            Toggle(blasts,"Rendu volumétrique","Volumes 3D pour les explosions et feux au sol. Coût GPU plus élevé.",()=>s.Volumetric,v=>s.Volumetric=v);
            Toggle(blasts,"Onde de choc et distorsion","Le front de choc et la chaleur déforment l'image autour de l'explosion.",()=>s.Shockwave,v=>s.Shockwave=v);
            Toggle(blasts,"Éclairage dynamique","Les explosions et les feux éclairent le sol, les fusées et la fumée.",()=>s.Lighting,v=>s.Lighting=v);
            Section(blasts,"Impacts");
            Toggle(blasts,"Crashs au sol","Terre et poussière projetées, souffle au ras du sol.",()=>s.Ground,v=>s.Ground=v);
            Toggle(blasts,"Impacts dans l'eau","Colonne d'eau, embruns et anneau d'écume.",()=>s.Water,v=>s.Water=v);
            Toggle(blasts,"Éclatements et vapeurs","Réservoirs sous pression, nuages de vapeur, étincelles électriques.",()=>s.Burst,v=>s.Burst=v);
            var fire=Page(m,"Feu & fumée",UiGlyph.Flame);
            Toggle(fire,"Feux et fumées au sol","Après un crash avec du carburant : feu, fumée et traces au sol.",
                ()=>s.Residue,v=>s.Residue=v);
            Slider(fire,"Durée des résidus","Feux, fumées et fragments au sol.",0,180,5,
                ()=>(float)s.ResidueSeconds,v=>s.ResidueSeconds=v,v=>v<1?"aucune":v.ToString("0",French)+" s");
            fire.Rows.Add(new UiRow { Kind=UiRowKind.Info, Label="Rendu des flammes",
                Text=()=>"Le rendu volumétrique se règle dans Explosions. Sans shader compatible, le mod utilise son rendu de secours." });
            var wreck=Page(m,"Épaves",UiGlyph.Wreck);
            Toggle(wreck,"Fragments des pièces","Morceaux visuels issus du modèle de la pièce détruite.",
                ()=>s.PartDebris,v=>s.PartDebris=v);
            Slider(wreck,"Détail des fragments","Triangles par pièce détruite.",300,16000,100,
                ()=>s.DebrisTriangles,v=>{ s.Quality=Quality.Custom; s.DebrisTriangles=(int)v; },v=>(v/1000).ToString("0.0",French)+" k");
            Slider(wreck,"Pièces conservées","Les plus anciennes cèdent leur place.",0,48,1,
                ()=>s.MaxDebrisParts,v=>{ s.Quality=Quality.Custom; s.MaxDebrisParts=(int)v; },v=>v.ToString("0",French));
            Toggle(wreck,"Micro-débris","Éclats et petits morceaux projetés par l'explosion.",()=>s.Debris,v=>s.Debris=v);
            Slider(wreck,"Densité des éclats","Quantité par explosion.",0,2,0.05f,
                ()=>(float)s.DebrisDensity,v=>s.DebrisDensity=v,v=>v.ToString("0.00",French)+" ×");
            var perf=Page(m,"Performance",UiGlyph.Gauge);
            perf.Rows.Add(new UiRow { Kind=UiRowKind.Info, Label="Limites",
                Text=()=>"Modifier une limite passe la qualité en Perso. Préréglage : "+QualityName(s.Quality)+". Un changement de capacité efface les effets en cours." });
            Limit(perf,s,"Explosions simultanées","Au-delà, les plus faibles laissent la place aux plus fortes.",1,24,1,()=>s.MaxEvents,v=>s.MaxEvents=v);
            Limit(perf,s,"Explosions en volume","Les feux au sol ne comptent pas ici.",0,8,1,()=>s.MaxVolumes,v=>s.MaxVolumes=v);
            Limit(perf,s,"Précision des volumes","Plus de passes : détail et coût GPU accrus.",16,56,4,()=>s.VolumeSteps,v=>s.VolumeSteps=v);
            Limit(perf,s,"Particules par explosion","Étincelles, flammes et fumées d'une explosion.",16,256,16,()=>s.ParticlesPerEvent,v=>s.ParticlesPerEvent=v);
            Limit(perf,s,"Lumières dynamiques","Lumières d'explosion actives en même temps.",0,6,1,()=>s.MaxLights,v=>s.MaxLights=v);
            Limit(perf,s,"Micro-débris au total","Tous les éclats en vol à la fois.",0,4096,64,()=>s.MaxDebris,v=>s.MaxDebris=v);
            return m;
        }
        static string QualityName(Quality q)
        {
            switch(q) { case Quality.Low: return "Basse"; case Quality.Medium: return "Moyenne"; case Quality.High: return "Haute"; case Quality.Ultra: return "Ultra"; default: return "Perso"; }
        }
        static UiPage Page(UiModel m,string title,UiGlyph glyph) { var p=new UiPage { Title=title, Glyph=glyph }; m.Pages.Add(p); return p; }
        static void Section(UiPage p,string title) { p.Rows.Add(new UiRow { Kind=UiRowKind.Section, Label=title }); }
        static void Toggle(UiPage p,string label,string hint,Func<bool> get,Action<bool> set)
        { p.Rows.Add(new UiRow { Kind=UiRowKind.Toggle, Label=label, Hint=hint, GetBool=get, SetBool=set }); }
        static void Slider(UiPage p,string label,string hint,float min,float max,float step,Func<float> get,Action<float> set,Func<float,string> format)
        { p.Rows.Add(new UiRow { Kind=UiRowKind.Slider, Label=label, Hint=hint, Min=min, Max=max, Step=step, GetValue=get, SetValue=set, Format=format }); }
        static void Limit(UiPage p,FxSettings s,string label,string hint,int min,int max,int step,Func<int> get,Action<int> set)
        {
            Slider(p,label,hint,min,max,step,()=>get(),v=>{ s.Quality=Quality.Custom; set((int)Math.Round(v)); s.Validate(); },v=>v.ToString("0",French));
        }
    }
}
