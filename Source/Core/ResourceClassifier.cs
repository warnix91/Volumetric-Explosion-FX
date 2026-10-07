using System;
using System.Collections.Generic;
namespace VolumetricExplosionFX.Core
{
    public sealed class ResourceClassifier
    {
        readonly Dictionary<string,ResourceKind> profiles = new Dictionary<string,ResourceKind>(StringComparer.OrdinalIgnoreCase);
        public ResourceClassifier()
        {
            Register("LiquidFuel", ResourceKind.Fuel|ResourceKind.Sooty); Register("Oxidizer",ResourceKind.Oxidizer);
            Register("MonoPropellant",ResourceKind.Hypergolic|ResourceKind.Pressurized|ResourceKind.Clean);
            Register("SolidFuel",ResourceKind.Solid); Register("ElectricCharge",ResourceKind.Electric);
            Register("LqdOxygen",ResourceKind.Oxidizer|ResourceKind.Cryogenic); Register("LOX",ResourceKind.Oxidizer|ResourceKind.Cryogenic);
            Register("Kerosene",ResourceKind.Fuel|ResourceKind.Sooty); Register("RP-1",ResourceKind.Fuel|ResourceKind.Sooty);
            Register("LqdHydrogen",ResourceKind.Fuel|ResourceKind.Cryogenic|ResourceKind.Clean); Register("LH2",ResourceKind.Fuel|ResourceKind.Cryogenic|ResourceKind.Clean);
            Register("LqdMethane",ResourceKind.Fuel|ResourceKind.Cryogenic); Register("Methane",ResourceKind.Fuel);
            Register("MMH",ResourceKind.Fuel|ResourceKind.Hypergolic); Register("UDMH",ResourceKind.Fuel|ResourceKind.Hypergolic);
            Register("NTO",ResourceKind.Oxidizer|ResourceKind.Hypergolic); Register("Nitrogen",ResourceKind.Pressurized);
            Register("XenonGas",ResourceKind.Pressurized); Register("ArgonGas",ResourceKind.Pressurized); Register("Helium",ResourceKind.Pressurized);
        }
        public void Register(string name, ResourceKind kind) { if (!string.IsNullOrEmpty(name)) profiles[name]=kind; }
        public ResourceKind Lookup(string name) { ResourceKind k; return name!=null&&profiles.TryGetValue(name,out k)?k:ResourceKind.None; }
        public ResourceKind Contents(PartSnapshot p)
        {
            ResourceKind flags=ResourceKind.None;
            for(int i=0;i<p.Resources.Length;i++) if(p.Resources[i].Amount>0) flags|=Lookup(p.Resources[i].Name);
            return flags;
        }
        public PartKind Classify(PartSnapshot p)
        {
            ResourceKind present=ResourceKind.None;
            for(int i=0;i<p.Resources.Length;i++) if(p.Resources[i].Capacity>0) present|=Lookup(p.Resources[i].Name);
            if((present&ResourceKind.Solid)!=0) return PartKind.SolidBooster;
            for(int i=0;i<p.Modules.Length;i++)
            {
                string m=p.Modules[i];
                if(m=="ModuleEngines"||m=="ModuleEnginesFX"||m=="ModuleEnginesRF") return PartKind.Engine;
                if(m=="ModuleCommand") return PartKind.Capsule;
                if(m=="ModuleLiftingSurface"||m=="ModuleControlSurface") return PartKind.Wing;
                if(m=="ModuleProceduralFairing") return PartKind.Fairing;
            }
            if((present&(ResourceKind.Fuel|ResourceKind.Oxidizer|ResourceKind.Hypergolic|ResourceKind.Cryogenic))!=0) return PartKind.LiquidTank;
            if((present&ResourceKind.Pressurized)!=0) return PartKind.PressurizedTank;
            if((present&ResourceKind.Electric)!=0) return PartKind.Battery;
            return PartKind.Structure;
        }
    }
}
