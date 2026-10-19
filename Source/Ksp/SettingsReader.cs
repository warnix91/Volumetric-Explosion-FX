using System;
using System.Globalization;
using System.IO;
using VolumetricExplosionFX.Core;
using UnityEngine;
namespace VolumetricExplosionFX.Ksp
{
    internal static class SettingsReader
    {
        internal static FxSettings Read()
        {
            FxSettings s=Defaults();
            ConfigNode user=SettingsStore.LoadUser();
            if(user!=null) Apply(user,s);
            s.Validate(); return s;
        }
        internal static FxSettings Defaults()
        {
            FxSettings s=new FxSettings();
            // Apple GPUs pay much more for the ray-marched volumes; half resolution keeps the look at about a third of the cost.
            // Apple GPUs pay much more for the ray-marched volumes: 1/2 by default there.
            s.VolumeDivisor=Application.platform==RuntimePlatform.OSXPlayer?2:1;
            ConfigNode[] nodes=GameDatabase.Instance.GetConfigNodes(Node);
            if(nodes.Length>0) Apply(nodes[nodes.Length-1],s);
            s.Validate(); return s;
        }
        internal const string Node="VOLUMETRIC_EXPLOSION_FX", OldNode="PROJECT_DESTRUCTION_FX", LookNode="LOOK";
        internal static void Resources(ResourceClassifier resources)
        {
            foreach(string name in new[]{"VOLUMETRIC_EXPLOSION_RESOURCE","PROJECT_DESTRUCTION_RESOURCE"})
                foreach(ConfigNode n in GameDatabase.Instance.GetConfigNodes(name))
                { ResourceKind kind; if(Enum.TryParse<ResourceKind>(n.GetValue("kind"),true,out kind)) resources.Register(n.GetValue("name"),kind); }
        }
        internal static void Apply(ConfigNode n,FxSettings s)
        {
            Quality q;
            if(Enum.TryParse<Quality>(n.GetValue("quality"),true,out q)) s.ApplyPreset(q);
            s.Enabled=Flag(n,"enabled",s.Enabled); s.Explosions=Flag(n,"explosions",s.Explosions);
            s.Debris=Flag(n,"microDebris",s.Debris); s.Ground=Flag(n,"groundImpact",s.Ground);
            s.Water=Flag(n,"waterImpact",s.Water); s.Burst=Flag(n,"burst",s.Burst);
            s.Lighting=Flag(n,"dynamicLighting",s.Lighting); s.Debug=Flag(n,"debug",s.Debug);
            s.Volumetric=Flag(n,"volumetric",s.Volumetric);
            s.ReplaceStockVisuals=Flag(n,"replaceStockVisuals",s.ReplaceStockVisuals);
            s.Shockwave=Flag(n,"shockwave",s.Shockwave);
            s.CameraShake=Flag(n,"cameraShake",s.CameraShake);
            s.PartDebris=Flag(n,"partDebris",s.PartDebris); s.Residue=Flag(n,"residue",s.Residue);
            s.ResidueSeconds=Number(n,"residueSeconds",s.ResidueSeconds);
            s.Distance=Number(n,"fxDistance",s.Distance); s.Intensity=Number(n,"intensity",s.Intensity);
            s.DebrisDensity=Number(n,"debrisDensity",s.DebrisDensity);
            s.VolumeDivisor=(int)Number(n,"volumeResolution",Flag(n,"halfResolutionVolumes",false)?2:s.VolumeDivisor);
            s.LookEditor=Flag(n,"lookEditor",s.LookEditor); s.Adaptive=Flag(n,"adaptivePerformance",s.Adaptive);
            s.Looks.UseClassic=Flag(n,"classicLook",s.Looks.UseClassic);
            s.UiStyle=(int)Number(n,"uiStyle",s.UiStyle); s.UiLayout=(int)Number(n,"uiLayout",s.UiLayout);
            s.UiOpacity=Number(n,"uiOpacity",s.UiOpacity); s.UiSize=Number(n,"uiSize",s.UiSize);
            foreach(ConfigNode look in n.GetNodes(LookNode))
            {
                LookKind kind;
                if(!LookLibrary.TryParse(look.GetValue("kind"),out kind)) { Debug.LogWarning("[VEFX] Look profile ignored: unknown kind '"+look.GetValue("kind")+"'"); continue; }
                LookProfile p=s.Looks[kind];
                for(int i=0;i<LookProfile.Count;i++) p[i]=Number(look,LookProfile.Keys[i],p[i]);
            }
            if(s.Quality==Quality.Custom)
            {
                s.MaxEvents=(int)Number(n,"maximumEvents",s.MaxEvents);
                s.MaxDebris=(int)Number(n,"maximumDebris",s.MaxDebris);
                s.MaxLights=(int)Number(n,"maximumLights",s.MaxLights);
                s.ParticlesPerEvent=(int)Number(n,"particlesPerEvent",s.ParticlesPerEvent);
                s.MaxVolumes=(int)Number(n,"maximumVolumes",s.MaxVolumes);
                s.VolumeSteps=(int)Number(n,"volumeSteps",s.VolumeSteps);
                s.MaxDebrisParts=(int)Number(n,"maximumDebrisParts",s.MaxDebrisParts);
                s.DebrisTriangles=(int)Number(n,"debrisTrianglesPerPart",s.DebrisTriangles);
            }
            s.Validate();
        }
        internal static ConfigNode Write(FxSettings s)
        {
            var n=new ConfigNode(Node);
            n.AddValue("quality",s.Quality.ToString());
            Add(n,"enabled",s.Enabled); Add(n,"explosions",s.Explosions); Add(n,"microDebris",s.Debris); Add(n,"groundImpact",s.Ground);
            Add(n,"waterImpact",s.Water); Add(n,"burst",s.Burst); Add(n,"dynamicLighting",s.Lighting); Add(n,"volumetric",s.Volumetric);
            Add(n,"replaceStockVisuals",s.ReplaceStockVisuals); Add(n,"shockwave",s.Shockwave); Add(n,"cameraShake",s.CameraShake); Add(n,"partDebris",s.PartDebris);
            Add(n,"residue",s.Residue); Add(n,"residueSeconds",s.ResidueSeconds); Add(n,"fxDistance",s.Distance);
            Add(n,"intensity",s.Intensity); Add(n,"debrisDensity",s.DebrisDensity); Add(n,"debug",s.Debug);
            Add(n,"maximumEvents",s.MaxEvents); Add(n,"maximumDebris",s.MaxDebris); Add(n,"maximumLights",s.MaxLights);
            Add(n,"particlesPerEvent",s.ParticlesPerEvent); Add(n,"maximumVolumes",s.MaxVolumes); Add(n,"volumeSteps",s.VolumeSteps);
            Add(n,"maximumDebrisParts",s.MaxDebrisParts); Add(n,"debrisTrianglesPerPart",s.DebrisTriangles);
            Add(n,"lookEditor",s.LookEditor); Add(n,"adaptivePerformance",s.Adaptive); Add(n,"volumeResolution",s.VolumeDivisor); Add(n,"classicLook",s.Looks.UseClassic); Add(n,"uiStyle",s.UiStyle); Add(n,"uiLayout",s.UiLayout); Add(n,"uiOpacity",s.UiOpacity); Add(n,"uiSize",s.UiSize);
            // Only edited profiles are written, so untouched kinds follow future shipped defaults.
            foreach(LookKind kind in Enum.GetValues(typeof(LookKind)))
            {
                if(s.Looks.IsDefault(kind)) continue;
                var look=n.AddNode(LookNode); look.AddValue("kind",kind.ToString());
                for(int i=0;i<LookProfile.Count;i++) Add(look,LookProfile.Keys[i],s.Looks[kind][i]);
            }
            return n;
        }
        static void Add(ConfigNode n,string key,bool v) { n.AddValue(key,v?"true":"false"); }
        static void Add(ConfigNode n,string key,double v) { n.AddValue(key,v.ToString("R",CultureInfo.InvariantCulture)); }
        static bool Flag(ConfigNode n,string key,bool fallback) { bool b; return bool.TryParse(n.GetValue(key),out b)?b:fallback; }
        static double Number(ConfigNode n,string key,double fallback)
        { double v; return double.TryParse(n.GetValue(key),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback; }
    }
    internal static class SettingsStore
    {
        static FxSettings current;
        internal static int Version { get; private set; }
        internal static FxSettings Settings { get { if(current==null) current=SettingsReader.Read(); return current; } }
        static string Folder { get { return Path.Combine(KSPUtil.ApplicationRootPath,"GameData/VolumetricExplosionFX/PluginData"); } }
        static string UserFile { get { return Path.Combine(Folder,"settings.cfg"); } }
        internal static ConfigNode LoadUser()
        {
            try
            {
                if(!File.Exists(UserFile)) return null;
                ConfigNode root=ConfigNode.Load(UserFile);
                if(root==null) return null;
                ConfigNode n=root.GetNode(SettingsReader.Node)??root.GetNode(SettingsReader.OldNode);
                return n??root;
            }
            catch(Exception ex) { Debug.LogWarning("[VEFX] Settings file ignored: "+ex.GetType().Name); return null; }
        }
        internal static void Touch() { Version++; }
        internal static bool Save()
        {
            string temporary=UserFile+".tmp";
            try
            {
                Directory.CreateDirectory(Folder);
                var root=new ConfigNode(); root.AddNode(SettingsReader.Write(Settings));
                if(!root.Save(temporary)) throw new IOException("ConfigNode could not write the settings file.");
                if(File.Exists(UserFile)) File.Replace(temporary,UserFile,UserFile+".bak");
                else File.Move(temporary,UserFile);
                return true;
            }
            catch(Exception ex) { Debug.LogWarning("[VEFX] Settings not saved: "+ex.GetType().Name+": "+ex.Message); return false; }
            finally { try { if(File.Exists(temporary)) File.Delete(temporary); } catch(IOException) { } catch(UnauthorizedAccessException) { } }
        }
        internal static void Reset()
        {
            // The look profiles have their own reset in the look editor; keep them here.
            LookLibrary looks=Settings.Looks; bool editor=Settings.LookEditor; int style=Settings.UiStyle, layout=Settings.UiLayout;
            double opacity=Settings.UiOpacity, size=Settings.UiSize;
            Settings.CopyFrom(SettingsReader.Defaults());
            Settings.Looks=looks; Settings.LookEditor=editor;
            Settings.UiStyle=style; Settings.UiLayout=layout; Settings.UiOpacity=opacity; Settings.UiSize=size;   // (the window keeps its look)
            Version++;
        }
    }
}
