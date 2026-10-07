using System;
using VolumetricExplosionFX.Core;
class Program
{
    static int count;
    static void Check(bool b,string label) { if(!b) throw new Exception(label); count++; Console.WriteLine("PASS "+label); }
    static ResourceSnapshot R(string name,double mass,double amount=1) { return new ResourceSnapshot(name,amount,10,mass); }
    static PartSnapshot Part(double density,ResourceSnapshot[] resources=null,double speed=100,double mass=1000,int instance=1,Guid? vessel=null,Vec3? pos=null)
    {
        return new PartSnapshot((uint)instance,instance,vessel??new Guid("11111111-1111-1111-1111-111111111111"),"test",
            pos??new Vec3(),new Vec3(0,-speed,0),new Vec3(0,3000,0),new Vec3(),new Vec3(2,2,4),mass,300,
            new string[0],resources,new EnvironmentContext("test body",density,101,9.81,0,true,false,false,new Vec3(0,1,0)));
    }
    static FxPlan Plan(PartSnapshot p,EventKind kind=EventKind.Destruction)
    { return VisualEnergy.Calculate(new DestructionEvent(p,kind,0),new ResourceClassifier(),new FxSettings()); }
    static PartMesh TankMesh(int segments,int rings,double radius,double height)
    {
        var p=new System.Collections.Generic.List<Vec3>(); var n=new System.Collections.Generic.List<float>(); var uv=new System.Collections.Generic.List<float>();
        var tri=new System.Collections.Generic.List<int>();
        for(int r=0;r<=rings;r++) for(int k=0;k<segments;k++)
        {
            double a=2*Math.PI*k/segments, y=height*r/rings-height/2;
            p.Add(new Vec3(radius*Math.Cos(a),y,radius*Math.Sin(a))); n.Add((float)Math.Cos(a)); n.Add(0); n.Add((float)Math.Sin(a));
            uv.Add((float)k/segments); uv.Add((float)r/rings);
        }
        for(int r=0;r<rings;r++) for(int k=0;k<segments;k++)
        {
            int a=r*segments+k, b=r*segments+(k+1)%segments, c=a+segments, d=b+segments;
            tri.AddRange(new[]{a,c,b,b,c,d});
        }
        foreach(double y in new[]{-height/2,height/2})
        {
            int centre=p.Count; p.Add(new Vec3(0,y,0)); n.Add(0); n.Add(Math.Sign(y)); n.Add(0); uv.Add(0.5f); uv.Add(0.5f);
            int ring=p.Count;
            for(int k=0;k<segments;k++)
            {
                double a=2*Math.PI*k/segments;
                p.Add(new Vec3(radius*Math.Cos(a),y,radius*Math.Sin(a))); n.Add(0); n.Add(Math.Sign(y)); n.Add(0);
                uv.Add((float)(0.5+0.5*Math.Cos(a))); uv.Add((float)(0.5+0.5*Math.Sin(a)));
            }
            for(int k=0;k<segments;k++) tri.AddRange(y>0?new[]{centre,ring+(k+1)%segments,ring+k}:new[]{centre,ring+k,ring+(k+1)%segments});
        }
        var groups=new int[tri.Count/3];
        return new PartMesh { Positions=p.ToArray(), Normals=n.ToArray(), Uvs=uv.ToArray(), Triangles=tri.ToArray(), Groups=groups };
    }
    static double MeshArea(PartMesh m)
    {
        double a=0;
        for(int t=0;t<m.TriangleCount;t++)
        {
            Vec3 p0=m.Positions[m.Triangles[3*t]], p1=m.Positions[m.Triangles[3*t+1]], p2=m.Positions[m.Triangles[3*t+2]];
            a+=0.5*Vec3.Cross(p1-p0,p2-p0).Length;
        }
        return a;
    }
    static double MedianRadius(FractureResult cut) { var r=(double[])cut.Radius.Clone(); Array.Sort(r); return r[r.Length/2]; }
    static bool AllPiecesConnected(FractureResult cut)
    {
        for(int f=0;f<cut.Count;f++)
        {
            var tris=new System.Collections.Generic.List<int>();
            for(int t=0;t<cut.TriangleCount;t++) if(cut.Fragment[cut.Triangles[3*t]]==f) tris.Add(t);
            var seen=new bool[tris.Count]; var stack=new System.Collections.Generic.Stack<int>(); seen[0]=true; stack.Push(0); int reached=1;
            while(stack.Count>0)
            {
                int i=stack.Pop();
                for(int j=0;j<tris.Count;j++) if(!seen[j]&&ShareVertex(cut,tris[i],tris[j])) { seen[j]=true; reached++; stack.Push(j); }
            }
            if(reached!=tris.Count) return false;
        }
        return true;
    }
    static bool ShareVertex(FractureResult cut,int a,int b)
    {
        for(int i=0;i<3;i++) for(int j=0;j<3;j++)
            if((cut.Positions[cut.Triangles[3*a+i]]-cut.Positions[cut.Triangles[3*b+j]]).Length<1e-9) return true;
        return false;
    }
    static void LocalizationChecks()
    {
        string[] languages={"en-us","fr-fr","de-de","es-es","it-it","pt-br","ru","ja","zh-cn"};
        foreach(string language in languages)
            Check(new UiText(language).Language==language,"KSP language recognized: "+language);
        Check(new UiText(" FR_fr ").Language=="fr-fr","KSP language codes tolerate case and separator variants");
        Check(new UiText("ja-JP").Language=="ja"&&new UiText("ru-RU").Language=="ru","Japanese and Russian regional aliases resolve");
        Check(new UiText(null).Language=="en-us"&&new UiText("unknown").Language=="en-us","Unknown or unset language uses English");
        var originalCulture=System.Globalization.CultureInfo.CurrentCulture;
        var french=new UiText("fr-fr",tag=>tag=="#VEFX_Settings"?"Réglages":null);
        Check(french.Get("Settings","Settings")=="Réglages","Native KSP tags select the localized UI text");
        Check(french.Get("Missing","English fallback")=="English fallback","Missing translations use readable English");
        Check(new UiText("fr-fr",tag=>tag).Get("Settings","Settings")=="Settings","Unresolved tags are never shown as labels");
        Check(new UiText("fr-fr",tag=>"").Get("Settings","Settings")=="Settings","Empty translations use English");
        Check((1.25).ToString("0.00",french.Culture)=="1,25"&&(1.25).ToString("0.00",new UiText("en-us").Culture)=="1.25",
            "Numbers follow the game language rather than the operating system");
        Check(System.Globalization.CultureInfo.CurrentCulture.Equals(originalCulture),"UI localization does not change another mod's culture");
        int lookups=0;
        var cached=new UiText("ja",tag=>{ lookups++; return "設定"; });
        Check(cached.Get("Settings","Settings")=="設定"&&cached.Get("Settings","Settings")=="設定"&&lookups==1,
            "Localized Unicode labels are cached without repeated dictionary lookups");
        var format=new UiText("fr-fr",tag=>"Profil : <<1>> / <<2>>");
        Check(format.Format("Preset","Preset: {0} / {1}","Haute",1.5)=="Profil : Haute / 1,5",
            "KSP numbered localization arguments preserve localized numbers");
        Check(new UiText("en-us",tag=>"Broken {9}").Format("Value","Value: {0}",7)=="Value: 7",
            "Malformed translated formatting does not break the settings window");
        Check(new UiText("fr-fr").Format("Value","Value: {0}",1.5)=="Value: 1,5","Fallback messages retain the selected numeric culture");
    }
    static void Main()
    {
        try
        {
            LocalizationChecks();
            var c=new ResourceClassifier();
            var full=new[]{R("LiquidFuel",100),R("Oxidizer",100)};
            var air=Plan(Part(1.225,full)); var vacuum=Plan(Part(0,full));
            Check(vacuum.FireFraction>0.5,"Fuel plus oxidizer can produce flash in vacuum");
            Check(vacuum.SmokeFraction==0,"No persistent combustion smoke in vacuum");
            Check(air.SmokeFraction>0.8,"Dense atmosphere supports longer smoke");
            Check(vacuum.ExpansionSpeed>air.ExpansionSpeed,"Vacuum expands faster");
            double previous=0;
            foreach(double d in new[]{0.0,0.00001,0.001,0.01,0.1,0.5,1.225})
            { double blend=Part(d).Environment.AtmosphereBlend; Check(blend>=previous&&blend<=1,"Continuous density blend "+d); previous=blend; }
            Check(Plan(Part(0,new[]{R("LiquidFuel",100)})).FireFraction==0,"Unpaired fuel has no sustained vacuum fire");
            Check(Plan(Part(0,new[]{R("LqdOxygen",100)})).FireFraction==0,"LOX alone is not a fuel");
            Check(Plan(Part(0,new[]{R("LqdOxygen",100)})).VaporFraction>0,"Cryogenic tank produces vapor");
            Check(Plan(Part(0,new[]{R("SolidFuel",100)})).FireFraction>0.5,"Solid propellant contains oxidizer");
            Check(Plan(Part(1, new[]{R("LiquidFuel",0,0),R("Oxidizer",0,0)})).FireFraction==0,"Empty tanks do not become fireballs");
            Check(c.Classify(Part(0,new[]{R("SolidFuel",0,0)}))==PartKind.SolidBooster,"Capacity classifies empty booster");
            Check(c.Classify(Part(0,new[]{R("ElectricCharge",0)}))==PartKind.Battery,"Battery classification");
            Check(c.Lookup("UnknownModFuel")==ResourceKind.None,"Unknown resource conservative fallback");
            c.Register("CustomFuel",ResourceKind.Fuel|ResourceKind.Cryogenic);
            Check((c.Contents(Part(0,new[]{R("CustomFuel",10)}))&ResourceKind.Cryogenic)!=0,"Resource profiles extend without gameplay dependencies");
            Check(Plan(Part(0,speed:7000)).KineticJoules==0,"Orbital speed does not enlarge generic destruction");
            var slow=Plan(Part(0,speed:10,mass:1000),EventKind.GroundImpact);
            var fast=Plan(Part(0,speed:20,mass:1000),EventKind.GroundImpact);
            Check(slow.KineticJoules==50000&&fast.KineticJoules==200000,"Impact uses kg and m/s, quadratic speed");
            Check(fast.VisualEnergyScore>slow.VisualEnergyScore,"Higher impact energy increases visual score");
            var bad=Plan(Part(double.NaN,speed:double.PositiveInfinity,mass:double.PositiveInfinity),EventKind.GroundImpact);
            Check(!double.IsNaN(bad.Radius)&&!double.IsInfinity(bad.Radius),"Malformed input cannot create infinite radius");
            var huge=Plan(Part(2,full,speed:1e12,mass:1e12),EventKind.GroundImpact);
            Check(huge.Radius<=30&&huge.DebrisCount<=128,"Huge event remains bounded");
            Check(Plan(Part(1,full),EventKind.WaterImpact).FireFraction<air.FireFraction,"Water reduces initial flame fraction");
            var s=new FxSettings();s.ApplyPreset(Quality.Low);
            Check(s.MaxEvents==6&&s.MaxLights==1&&s.MaxDebris==256,"Low preset budgets");
            s.ApplyPreset(Quality.Ultra);Check(s.MaxEvents==24&&s.MaxLights==6,"Ultra preset still capped");
            s.Intensity=double.NaN;s.MaxDebris=int.MaxValue;s.MaxLights=-10;s.Validate();
            Check(s.Intensity==0.25&&s.MaxDebris==4096&&s.MaxLights==0,"Custom settings sanitize limits");
            var clusters=new EventClusterer();
            for(int i=0;i<100;i++) clusters.Add(new DestructionEvent(Part(1,instance:i+1,pos:new Vec3(i%5,0,0)),EventKind.Destruction,i*0.0004),2);
            Check(clusters.TakeReady(0.05)==null,"Cluster waits fixed collection window");
            var group=clusters.TakeReady(0.09);
            Check(group!=null&&group.Members==100&&clusters.TakeReady(0.09)==null,"100-part chain becomes one bounded event");
            int first=clusters.Add(new DestructionEvent(Part(1),EventKind.Destruction,1),1);
            int debris=clusters.Add(new DestructionEvent(Part(1,vessel:Guid.NewGuid()),EventKind.Destruction,1.01),1);
            Check(first>0&&debris==first&&clusters.TakeReady(1.2).Members==2,"Debris vessels of one crash merge into one effect");
            clusters.Add(new DestructionEvent(Part(1),EventKind.Destruction,1.5),1);
            clusters.Add(new DestructionEvent(Part(1,pos:new Vec3(40,0,0)),EventKind.Destruction,1.51),1);
            Check(clusters.TakeReady(1.7).Members==1&&clusters.TakeReady(1.7).Members==1,"Distant simultaneous deaths stay separate");
            clusters.Add(new DestructionEvent(Part(1),EventKind.GroundImpact,2),1);
            clusters.Add(new DestructionEvent(Part(1),EventKind.WaterImpact,2.01),1);
            Check(clusters.TakeReady(2.2).Members==1&&clusters.TakeReady(2.2).Members==1,"Ground and water contexts remain distinct");
            clusters.Add(new DestructionEvent(Part(1,pos:new Vec3(0,3,0)),EventKind.Destruction,3),5);
            clusters.Add(new DestructionEvent(Part(1,pos:new Vec3(1,0,0)),EventKind.GroundImpact,3.01),1);
            var mixed=clusters.TakeReady(3.2);
            Check(mixed.Members==2&&mixed.Ground&&mixed.GroundPoint.X==1&&mixed.Representative.Kind==EventKind.Destruction,
                "Ground contact anchors a mixed crash cluster");
            var limited=new EventClusterer(1);
            int kept=limited.Add(new DestructionEvent(Part(1),EventKind.Destruction,1),1);
            Check(kept>0,"Accept free event slot");
            Check(limited.Add(new DestructionEvent(Part(1,pos:new Vec3(100,0,0)),EventKind.Destruction,1),1)==0&&limited.Dropped==1,"Queue overflow drops safely");
            int stronger=limited.Add(new DestructionEvent(Part(1,pos:new Vec3(100,0,0)),EventKind.Destruction,1),3);
            Check(stronger>0&&limited.LastEvicted==kept&&limited.Replaced==1,"Stronger event evicts weakest pending cluster and reports it");
            limited.Shift(new Vec3(12000,0,0)); Check(limited.TakeReady(1.2).Position.X==-11900,"Pending event follows origin shift");
            var dedup=new RecentEvents(8);
            Check(dedup.Accept(5,0)&&!dedup.Accept(5,0.1)&&dedup.Accept(6,0.1)&&dedup.Accept(5,3),"Duplicate destruction callbacks deduplicated with expiry");
            Check(full[0].MassKg==100&&full[0].Amount==1,"Visual planning leaves snapshot resources untouched");
            var snapshots=new SnapshotCache(2);
            var before=Part(1,full,speed:100);
            snapshots.Capture(before,EventKind.GroundImpact,new Vec3(4,0,0),1);
            snapshots.Capture(Part(1,speed:0),EventKind.Destruction,new Vec3(9,0,0),1.01);
            var confirmed=snapshots.Confirm(1,1.02,null);
            Check(confirmed.Kind==EventKind.GroundImpact&&confirmed.Part.SurfaceVelocity.Length==100,
                "Confirmed death preserves pre-impact speed and surface evidence");
            Check(confirmed.Part.Resources.Length==2&&confirmed.Part.Position.X==4,
                "Will-die does not erase tank contents or contact position");
            Check(snapshots.Confirm(1,1.03,null)==null,"Cached evidence can only be consumed once");
            snapshots.Capture(before,EventKind.WaterImpact,new Vec3(12,0,0),2);
            snapshots.Shift(new Vec3(1000,0,0));
            Check(snapshots.Confirm(1,2.1,null).Part.Position.X==-988,"Pre-death cache survives floating origin shift");
            snapshots.Capture(before,EventKind.GroundImpact,new Vec3(),3);
            Check(snapshots.Confirm(1,6,null)==null,"Stale collision evidence expires without creating an effect");
            Check(snapshots.Confirm(1,6,before).Kind==EventKind.Destruction,"Missing cache falls back conservatively to generic destruction");
            Check(Plan(Part(0,new[]{R("ElectricCharge",0,100)})).ElectricFraction>0.5,"Charged battery yields brief electric sparks without fuel fire");
            clusters.Add(new DestructionEvent(Part(1,instance:1),EventKind.Destruction,4),1);
            clusters.Add(new DestructionEvent(Part(1,instance:2),EventKind.Destruction,4.01),7);
            Check(clusters.TakeReady(4.2).Representative.Part.InstanceId==2,"Cluster selects strongest individual event");
            var initial=VolumeEvolution.Evaluate(air,0,true,true);
            var cooling=VolumeEvolution.Evaluate(air,2,true,true);
            Check(initial.Heat>cooling.Heat && cooling.FireSoot>initial.FireSoot && cooling.FireBody>0,"Fireball cools into a sooty cloud");
            var bigTank=Plan(Part(1.225,new[]{R("LiquidFuel",800),R("Oxidizer",800)}));
            Check(Math.Abs(VolumeEvolution.FireballRadius(bigTank)/VolumeEvolution.FireballRadius(air)-2)<0.05,"Fireball size follows the cube root of burning propellant");
            Check(VolumeEvolution.FireballRadius(Plan(Part(1)))==0,"Empty structure has no fireball");
            double tf=VolumeEvolution.FireballDuration(air);
            Check(tf>1&&tf<9,"Fireball duration in the real-world range");
            Check(VolumeEvolution.Evaluate(vacuum,tf,true,true).FireBody<VolumeEvolution.Evaluate(air,tf,true,true).FireBody,"Vacuum fireball disperses sooner than in air");
            Check(VolumeEvolution.Evaluate(air,tf,true,true).FireRise>0&&VolumeEvolution.Evaluate(vacuum,tf,true,true).FireRise==0,"Fireball lifts off on buoyancy only in air");
            bool contained=true;
            foreach(double t in new[]{0.0,0.2,0.5,1,2,4,8,12})
            { var st=VolumeEvolution.Evaluate(bigTank,t,true,true,true); if(st.FireRise+st.FireRadius*1.3>1.01) contained=false; }
            Check(contained,"Fireball and lift-off stay inside the render proxy");
            Check(Plan(Part(1,new[]{R("LiquidFuel",100),R("Oxidizer",100)})).Soot>0.9,"Kerosene-like fuel gives black soot");
            Check(Plan(Part(1,new[]{R("LqdHydrogen",100),R("LqdOxygen",100)})).Soot==0,"Hydrogen burns without soot");
            Check(Plan(Part(1,new[]{R("SolidFuel",100)})).SmokeR>0.8,"Solid propellant gives white smoke");
            var hyper=Plan(Part(1,new[]{R("MMH",100),R("NTO",100)}));
            Check(hyper.SmokeR>hyper.SmokeB+0.2,"Nitrogen tetroxide tints the cloud reddish-brown");
            Check(cooling.ShellRadius>initial.ShellRadius&&cooling.ShellRadius<=VolumeEvolution.ShellLimit,"Shock dome expands inside its bounded proxy");
            var early=VolumeEvolution.Evaluate(air,0.2,true,true); var late=VolumeEvolution.Evaluate(air,3,true,true);
            Check(early.ShellOpacity>late.ShellOpacity&&late.ShellErosion>early.ShellErosion,"Dome thins and breaks up after the opaque phase");
            var airless=VolumeEvolution.Evaluate(vacuum,1,true,true);
            Check(airless.ShellOpacity<VolumeEvolution.Evaluate(air,1,true,true).ShellOpacity&&airless.Dust==0&&airless.Cloud==0,
                "Vacuum gives a faint transient gas shell without dust or smoke");
            var crash=Plan(Part(1,speed:150,mass:20000),EventKind.GroundImpact);
            var crashDome=VolumeEvolution.Evaluate(crash,0.3,true,true,true);
            Check(crashDome.Visible&&crashDome.Dust>0&&crashDome.Heat==0,"Fuel-free ground crash raises a dust dome without fake fire");
            var meteor=Plan(Part(0,speed:3000,mass:5000),EventKind.GroundImpact);
            Check(VolumeEvolution.Evaluate(meteor,0.05,true,true,true).Heat>0.5,"Hypervelocity impact produces a kinetic flash");
            var splash=VolumeEvolution.Evaluate(Plan(Part(1,speed:150,mass:20000),EventKind.WaterImpact),0.3,true,true,false,true);
            Check(splash.Water&&!splash.Ground&&splash.ShellOpacity>0&&splash.Heat==0,"Water impact gives a spray dome, not a fireball");
            var blast=VolumeEvolution.Evaluate(air,0.05,true,true,true); var later=VolumeEvolution.Evaluate(air,0.4,true,true,true);
            Check(later.ShockRadius>later.ShellRadius&&later.ShockRadius>blast.ShockRadius,"Shock front races ahead of the visible dome");
            Check(blast.ShockStrength>0&&VolumeEvolution.Evaluate(air,4,true,true,true).ShockStrength<blast.ShockStrength*0.01,"Shock ripple fades within about a second");
            Check(VolumeEvolution.Evaluate(vacuum,0.05,true,true).ShockStrength==0,"No refractive shock front in vacuum");
            Check(VolumeEvolution.Evaluate(air,1.5,true,true,true).Haze>0&&VolumeEvolution.Evaluate(vacuum,1.5,true,true).Haze==0,"Heat shimmer needs hot gas and air");
            Vec4 shockVector,hazeVector;
            VolumeEvolution.Distortion(VolumeEvolution.Evaluate(air,6,true,true,true),1,out shockVector,out hazeVector);
            Check(hazeVector.X>0&&hazeVector.W==6&&shockVector.W>0,"Burning ground keeps shimmering after the dome");
            Check(!VolumeEvolution.DistortionVisible(VolumeEvolution.Evaluate(Plan(Part(1)),0.1,true,true),0),"Empty structure creates no distortion");
            var airlessDome=VolumeEvolution.Evaluate(vacuum,0.3,true,true);
            Check(airlessDome.ShellOpacity<0.1,"Vacuum gas shell stays faint");
            Vec4 shellVector,coreVector,cloudVector,contextVector;
            VolumeEvolution.Pack(crashDome,1234,out shellVector,out coreVector,out cloudVector,out contextVector);
            Check(shellVector.X==crashDome.ShellRadius&&contextVector.X==1&&contextVector.W<997,"Shader constants pack the pure evolution state");
            Check(!VolumeEvolution.Evaluate(air,20,true,true).Visible,"Hero volume expires within bounded lifetime");
            Check(!VolumeEvolution.Evaluate(Plan(Part(1.225,full,speed:60,mass:2250),EventKind.GroundImpact),4.5,true,true,true).Visible,
                "A tank-sized dome is gone within a few seconds");
            Check(VolumeEvolution.VolumeDuration(huge)<=7,"Even the largest dome dissipates within seven seconds");
            var stage=Plan(Part(1.225,new[]{R("LiquidFuel",1e6),R("Oxidizer",1e6)},mass:2e6));
            Check(VolumeEvolution.VolumeDuration(stage)<=14&&!VolumeEvolution.Evaluate(stage,14.5,true,true,true).Visible,"Even a huge fireball and its smoke end within fourteen seconds");
            var moved=Part(1).At(new Vec3(5,0,0));
            Check(moved.Position.X==5&&moved.InstanceId==1&&moved.Resources.Length==0,"Anchor move keeps the observed snapshot otherwise intact");
            var near=new VisualReplacementWindows(2); near.Claim(new Vec3(),0,5,1);
            Check(near.NearestDistance(new Vec3(3,4,0),0.1)==5&&near.NearestDistance(new Vec3(),1)==-1,"Claim diagnostics report nearest open claim");
            Check(!VolumeEvolution.Evaluate(air,0.2,false,false).Visible,"Disabled explosion and burst have no volume");
            var vapor=Plan(Part(0,new[]{R("LqdOxygen",100)}));
            Check(VolumeEvolution.Evaluate(vapor,2,true,true).Cloud<VolumeEvolution.Evaluate(vapor,0,true,true).Cloud*0.01,
                "Vacuum cryogenic cloud disperses without persistent smoke");
            Check(!VolumeEvolution.Evaluate(Plan(Part(1)),0,true,true).Visible,"Empty structure has no fuel hero volume");
            Check(!double.IsNaN(VolumeEvolution.Evaluate(air,double.NaN,true,true).ShellRadius),"Volume age sanitizes NaN");
            var claims=new VisualReplacementWindows(4);
            claims.Claim(new Vec3(),10,5,7); claims.Claim(new Vec3(100,0,0),10,5,VisualReplacementWindows.Covered);
            Check(claims.OwnerAt(new Vec3(3,0,0),10.1)==7&&claims.OwnerAt(new Vec3(20,0,0),10.1)==0,"Stock claim covers only nearby confirmed deaths");
            claims.Release(7);
            Check(claims.OwnerAt(new Vec3(3,0,0),10.1)==0&&claims.OwnerAt(new Vec3(100,0,0),10.1)==VisualReplacementWindows.Covered,
                "Unshown cluster releases its stock claim");
            Check(claims.OwnerAt(new Vec3(100,0,0),11)==0&&!claims.Any(11),"Stock claims expire quickly");
            claims.Claim(new Vec3(50,0,0),20,5,3); claims.Shift(new Vec3(50,0,0));
            Check(claims.OwnerAt(new Vec3(),20.1)==3,"Stock claims follow floating origin");
            s.MaxVolumes=999;s.VolumeSteps=999;s.Validate();
            Check(s.MaxVolumes==8&&s.VolumeSteps==56,"Hero volume count and GPU steps remain capped");
            s.ApplyPreset(Quality.Low);Check(s.MaxVolumes==2&&s.VolumeSteps==24,"Low preset bounds hero volume work");
            var tank=Plan(Part(1.225,new[]{R("LiquidFuel",900),R("Oxidizer",1100)},speed:60,mass:2250),EventKind.GroundImpact);
            Check(BlastLayout.Create(tank,new Vec3(120,-35,0),true,false,7,null).Form==BlastForm.Grazing,"Shallow fast crash is a grazing blast");
            Check(BlastLayout.Create(tank,new Vec3(5,-160,3),true,false,7,null).Form==BlastForm.Vertical,"Steep fast crash is a vertical splash");
            Check(BlastLayout.Create(tank,new Vec3(3,-10,0),true,false,7,null).Form==BlastForm.Rupture,"Slow impact is a rupture");
            Check(BlastLayout.Create(tank,new Vec3(300,80,0),false,false,7,null).Form==BlastForm.Streak,"Fast breakup in air is a streak");
            Check(BlastLayout.Create(Plan(Part(0,full),EventKind.Destruction),new Vec3(300,0,0),false,false,7,null).Form==BlastForm.Vacuum,"Airless blast has its own form");
            Check(BlastLayout.Create(tank,new Vec3(0,-50,0),false,true,7,null).Form==BlastForm.Water,"Water impact has its own form");
            var a1=BlastLayout.Create(tank,new Vec3(3,-10,0),true,false,7,null); var a2=BlastLayout.Create(tank,new Vec3(3,-10,0),true,false,7,null);
            var a3=BlastLayout.Create(tank,new Vec3(3,-10,0),true,false,8,null);
            Check(a1.Lobes[1].Offset.X==a2.Lobes[1].Offset.X&&a1.NoiseScale==a2.NoiseScale,"Same blast and seed give the same shape");
            Check(a1.Lobes[1].Offset.X!=a3.Lobes[1].Offset.X||a1.ActiveCount!=a3.ActiveCount,"Another seed gives another shape");
            var graze=BlastLayout.Create(tank,new Vec3(120,-35,0),true,false,7,null);
            var gs=VolumeEvolution.Evaluate(tank,0.8,true,true,true,false,graze);
            double ahead=-1e9, behind=1e9, side=0;
            for(int i=0;i<4;i++) { var l=gs.Lobe(i); if(l.Radius<=0) continue; ahead=Math.Max(ahead,l.Center.X+l.Radius); behind=Math.Min(behind,l.Center.X-l.Radius); side=Math.Max(side,Math.Abs(l.Center.Z)+l.Radius); }
            Check(ahead-behind>2.2*side,"Grazing fireball rolls out along the direction of travel");
            var vert=BlastLayout.Create(tank,new Vec3(5,-160,3),true,false,7,null);
            Check(vert.Lobes[0].Stretch<1&&Math.Abs(vert.Lobes[0].Axis.Y)>0.99,"Vertical splash starts as a flattened pancake");
            var streak=BlastLayout.Create(tank,new Vec3(300,80,0),false,false,7,null);
            var ss=VolumeEvolution.Evaluate(tank,0.6,true,true,false,false,streak);
            Check(ss.Lobe(1).Center.X<ss.Lobe(0).Center.X&&ss.Lobe(1).Center.X<0,"In-flight breakup leaves burning gas behind its moving core");
            Check(double.IsInfinity(Reentry.GasTime(0,10))&&Reentry.GasTime(1,10)<Reentry.GasTime(0.1,10)&&Reentry.GasTime(1,30)>Reentry.GasTime(1,5),
                "Gas keeps the vehicle's momentum in vacuum, gives it up faster in dense air and slower for a big fireball");
            Check(Reentry.GasTime(1,10)>0.5&&Reentry.GasTime(1,10)<2,"At sea level a fireball follows the vehicle for about a second");
            var hot=Plan(Part(0.01,new[]{R("LiquidFuel",200),R("Oxidizer",250)},speed:2300,mass:3000),EventKind.Destruction);
            var slowHot=Plan(Part(0.01,new[]{R("LiquidFuel",200),R("Oxidizer",250)},speed:1200,mass:3000),EventKind.Destruction);
            var denseFast=Plan(Part(1.225,new[]{R("LiquidFuel",200),R("Oxidizer",250)},speed:2300,mass:3000),EventKind.Destruction);
            Check(Reentry.Applies(hot,false,2300)&&!Reentry.Applies(hot,false,500)&&!Reentry.Applies(hot,true,2300)&&!Reentry.Applies(denseFast,false,2300),
                "Only a fast breakup in thin air is a re-entry breakup");
            var rp=Reentry.Plan(hot,false,2300,200); var rs=Reentry.Plan(slowHot,false,1200,200);
            Check(rp.Active&&rp.Count>rs.Count&&rp.GlowSeconds>rs.GlowSeconds,"Faster re-entry breakups shed more fragments that glow longer");
            Check(rp.DragMax>rp.DragMin*5&&rp.DragMin>0,"Re-entry fragments brake at very different rates");
            Check(Reentry.Plan(hot,false,2300,20).Count<=20,"Re-entry fragments respect the particle budget");
            var rThick=Reentry.Plan(Plan(Part(0.2,new[]{R("LiquidFuel",200),R("Oxidizer",250)},speed:2300,mass:3000),EventKind.Destruction),false,2300,200);
            Check(rp.SmokeTrails==0&&rThick.SmokeTrails>0,"Smoke trails only in thicker air"); 
            var chain=new[]{new BlastSource(new Vec3(0,9,1),0.05,0.8),new BlastSource(new Vec3(1,18,0),0.12,0.6)};
            var rocket=BlastLayout.Create(tank,new Vec3(0,-15,0),true,false,5,chain);
            Check(rocket.Lobes[1].Offset.Y==9&&rocket.Lobes[2].Delay==0.12,"Other parts of the blast burn at their real place and time");
            var beforeIgnition=VolumeEvolution.Evaluate(tank,0.08,true,true,true,false,rocket);
            var afterIgnition=VolumeEvolution.Evaluate(tank,0.3,true,true,true,false,rocket);
            Check(beforeIgnition.Lobe(2).Radius==0&&afterIgnition.Lobe(2).Radius>0,"A delayed pocket ignites when its part died");
            Check(afterIgnition.Pulse>0&&afterIgnition.Pulse<1.01,"A late pocket adds a light pulse");
            bool inside=true;
            foreach(var lay in new[]{graze,vert,streak,rocket,BlastLayout.Create(tank,new Vec3(0,-50,0),false,true,3,null)})
                foreach(double t in new[]{0.05,0.3,0.8,1.5,3,6,10})
                {
                    bool g=lay==graze||lay==vert||lay==rocket, w=lay.Form==BlastForm.Water;
                    var st=VolumeEvolution.Evaluate(tank,t,true,true,g,w,lay);
                    for(int i=0;i<5;i++) { var l=st.Lobe(i); if(l.Radius>0&&l.Center.Length+l.Radius*Math.Max(l.Stretch,1)>1.06) inside=false; }
                    if(st.BoundsRadius>0) for(int i=0;i<4;i++) { var l=st.Lobe(i); if(l.Radius>0&&(l.Center-st.BoundsCenter).Length>st.BoundsRadius) inside=false; }
                }
            Check(inside,"Every lobe stays inside the render proxy and the marched region");
            var free=BlastLayout.Create(tank,new Vec3(3,-10,0),true,false,1,null);
            int lobesBefore=free.ActiveCount;
            bool added=free.AddSecondary(new Vec3(5,2,0),0.4,0.7);
            Check(added==(lobesBefore<4)&&free.ActiveCount==Math.Min(4,lobesBefore+1),"A part dying inside the blast adds a pocket while there is room");
            while(free.AddSecondary(new Vec3(1,1,1),0.5,0.5)) {}
            Check(free.ActiveCount==4&&!free.AddSecondary(new Vec3(),0.6,1),"Burning pockets stay bounded");
            var big=Plan(Part(1.225,new[]{R("LiquidFuel",14000),R("Oxidizer",18000)},speed:20,mass:36000),EventKind.GroundImpact);
            var bigLayout=BlastLayout.Create(big,new Vec3(0,-20,0),true,false,9,null);
            Check(VolumeEvolution.Evaluate(big,8,true,true,true,false,bigLayout).StemDensity>0,"Large ground fireball grows a mushroom stem as it lifts off");
            var moon=Plan(Part(0,new[]{R("LiquidFuel",14000),R("Oxidizer",18000)},speed:20,mass:36000),EventKind.GroundImpact);
            Check(VolumeEvolution.Evaluate(moon,8,true,true,true,false,BlastLayout.Create(moon,new Vec3(0,-20,0),true,false,9,null)).StemDensity==0,"No mushroom stem without air");
            double v0,tc,reach; VolumeEvolution.ShockFront(tank,graze,out v0,out tc,out reach);
            Check(v0>250&&reach>VolumeEvolution.DomeRadius(tank),"Shock front leaves fast and outruns the dome");
            var compact=VolumeEvolution.Evaluate(tank,0.05,true,true,true,false,BlastLayout.Create(tank,new Vec3(3,-10,0),true,false,7,null));
            var wide=VolumeEvolution.Evaluate(tank,0.05,true,true,true,false,vert);
            Check(Math.Abs(compact.CoreRadius*compact.BoxRadius-wide.CoreRadius*wide.BoxRadius)<1e-6,"Core flash keeps its real size whatever the proxy size");
            var merge=new EventClusterer(4);
            merge.Add(new DestructionEvent(Part(1,instance:1,pos:new Vec3(0,0,0)),EventKind.Destruction,5),2);
            merge.Add(new DestructionEvent(Part(1,instance:2,pos:new Vec3(0,6,0)),EventKind.Destruction,5.02),5);
            merge.Add(new DestructionEvent(Part(1,instance:3,pos:new Vec3(0,9,0)),EventKind.Destruction,5.03),1);
            merge.Shift(new Vec3(0,1,0));
            var merged=merge.TakeReady(5.2);
            Check(merged.Representative.Part.InstanceId==2&&merged.SampleCount==2&&merged.SamplePositions[0].Y==-1&&merged.SamplePositions[1].Y==8,
                "Blast keeps where and when its other parts died, following origin shifts");
            var anchoredMerge=new EventClusterer(4);
            var high=Part(1,instance:4,pos:new Vec3(0,20,0));
            anchoredMerge.Add(new DestructionEvent(Part(1,instance:5),EventKind.GroundImpact,7),5);
            anchoredMerge.Add(new DestructionEvent(high.At(new Vec3(0,0,0)),EventKind.GroundImpact,7.02,high.Position),2);
            Check(anchoredMerge.TakeReady(7.2).SamplePositions[0].Y==20,"A part anchored on the ground still burns at its real height");
            Check(Math.Abs(Ballistics.E1(0.02-1e-12)-Ballistics.E1(0.02))<1e-8&&Math.Abs(Ballistics.E2(0.02-1e-12)-Ballistics.E2(0.02))<1e-8,
                "Drag integrals are continuous where the series takes over");
            Vec3 fxThrown=Ballistics.Position(new Vec3(0,10,0),new Vec3(3,5,0),0,9.81,1.3);
            Check(Math.Abs(fxThrown.X-3.9)<1e-9&&Math.Abs(fxThrown.Y-(10+5*1.3-0.5*9.81*1.69))<1e-9,"Without air a fragment follows a parabola");
            double fxSink=Ballistics.Height(0,0,0.8,9.81,31)-Ballistics.Height(0,0,0.8,9.81,30);
            Check(Math.Abs(fxSink+9.81/0.8)<1e-3,"In air a fragment settles to its terminal speed");
            double fxLanded=Ballistics.LandingTime(2,14,0.6,9.81,-1.5,60);
            Check(fxLanded>Ballistics.ApexTime(14,0.6,9.81)&&Math.Abs(Ballistics.Height(2,14,0.6,9.81,fxLanded)+1.5)<1e-6,"Landing time finds the ground after the apex");
            Check(double.IsPositiveInfinity(Ballistics.LandingTime(500,0,0,1.6,0,5))&&Ballistics.LandingTime(-1,-3,0.2,9.81,0,60)==0,
                "A fragment lands later than the horizon or is already down");
            var fxTank=TankMesh(24,10,1.25,4);
            var fxCut=Fracture.Cut(fxTank,12,7,3000,1);
            var fxAgain=Fracture.Cut(fxTank,12,7,3000,1);
            var fxOther=Fracture.Cut(fxTank,12,8,3000,1);
            Check(fxCut!=null&&fxCut.Count>=6&&fxCut.Count<=24,"A tank tears into a dozen pieces");
            Check(fxCut.Pivot[0].X==fxAgain.Pivot[0].X&&fxCut.Count==fxAgain.Count&&(fxOther.Count!=fxCut.Count||fxOther.Pivot[0].X!=fxCut.Pivot[0].X),
                "Same part and seed cut the same way, another seed differently");
            bool fxOwnVertices=true;
            for(int t=0;t<fxCut.TriangleCount;t++)
            {
                int f=fxCut.Fragment[fxCut.Triangles[3*t]];
                for(int k=0;k<3;k++) { int v=fxCut.Triangles[3*t+k]; if(fxCut.Fragment[v]!=f||v<fxCut.FirstVertex[f]||v>=fxCut.FirstVertex[f]+fxCut.VertexCounts[f]) fxOwnVertices=false; }
            }
            Check(fxOwnVertices,"Every piece owns its vertices: pieces fly apart without stretching");
            double fxPieceArea=0; foreach(double a in fxCut.Area) fxPieceArea+=a;
            Check(Math.Abs(fxPieceArea-MeshArea(fxTank))<0.01*MeshArea(fxTank),"Pieces cover the whole skin when nothing is dropped");
            Check(AllPiecesConnected(fxCut),"A piece never spans both sides of the tank");
            int fxRim=0; foreach(float e in fxCut.Edge) if(e>0.5f) fxRim++;
            Check(fxRim>0&&fxRim<fxCut.VertexCount*0.9,"Torn rims are marked for glowing edges");
            var fxDense=Fracture.Cut(TankMesh(48,40,1.25,4),16,3,1200,1);
            Check(fxDense!=null&&fxDense.TriangleCount<=1200,"Dense meshes stay within the triangle budget");
            var fxPlate=new PartMesh { Positions=new[]{new Vec3(-1,0,-1),new Vec3(1,0,-1),new Vec3(1,0,1),new Vec3(-1,0,1)},
                Normals=new float[]{0,1,0,0,1,0,0,1,0,0,1,0}, Uvs=new float[]{0,0,1,0,1,1,0,1}, Triangles=new[]{0,2,1,0,3,2} };
            var fxPlateCut=Fracture.Cut(fxPlate,6,5,3000,1);
            Check(fxPlateCut!=null&&fxPlateCut.Count>=3,"A two-triangle panel is subdivided so it can still tear");
            Check(Fracture.Cut(TankMesh(200,100,1,4),8,1,1000,1)==null,"Meshes far beyond the budget are not cut (no hitch)");
            var fxBlast=Plan(Part(1.225,new[]{R("LiquidFuel",900),R("Oxidizer",1100)},speed:40,mass:2250),EventKind.GroundImpact);
            var fxStill=new Quat(0,0,0,1);
            var fxMotion=DebrisPlanner.Launch(fxBlast,fxCut,fxStill,new Vec3(0,2,0),new Vec3(0,0,0),new Vec3(20,-40,0),true,11);
            bool fxUpward=true; foreach(var mm in fxMotion) if(mm.Velocity.Y<=0) fxUpward=false;
            Check(fxUpward,"A crash throws its pieces up off the ground, never into it");
            double fxSmallSpeed=0, fxBigSpeed=0; int fxSmallN=0, fxBigN=0; double fxMedian=MedianRadius(fxCut);
            for(int f=0;f<fxCut.Count;f++) { if(fxCut.Radius[f]<fxMedian) { fxSmallSpeed+=fxMotion[f].Velocity.Length; fxSmallN++; } else { fxBigSpeed+=fxMotion[f].Velocity.Length; fxBigN++; } }
            Check(fxSmallN>0&&fxBigN>0&&fxSmallSpeed/fxSmallN>fxBigSpeed/fxBigN*0.9,"Small pieces fly at least as fast as large plates");
            var fxVacuumBlast=Plan(Part(0,new[]{R("LiquidFuel",900),R("Oxidizer",1100)},speed:40,mass:2250),EventKind.GroundImpact);
            bool fxNoDrag=true; foreach(var mm in DebrisPlanner.Launch(fxVacuumBlast,fxCut,fxStill,new Vec3(),new Vec3(),new Vec3(),true,11)) if(mm.Drag!=0) fxNoDrag=false;
            Check(fxNoDrag,"Without air pieces keep their speed (no drag)");
            var fxPiece=fxMotion[0];
            DebrisPlanner.Land(fxCut,0,fxStill,ref fxPiece,-0.5,9.81,120);
            double fxSpun=Ballistics.SpinAngle(fxPiece.SpinRate,fxPiece.Drag,fxPiece.LandTime);
            double fxLowest=DebrisPlanner.LowestPoint(fxCut,0,fxStill,fxPiece.SpinAxis,fxSpun,fxPiece.SettleAxis,fxPiece.SettleAngle);
            Check(fxPiece.LandTime>0&&!double.IsInfinity(fxPiece.LandTime)&&Math.Abs(fxPiece.RestHeight+fxLowest+0.5)<0.02,"A piece comes to rest with its lowest point on the ground");
            Check(Math.Abs(fxPiece.LandHeight+DebrisPlanner.LowestPoint(fxCut,0,fxStill,fxPiece.SpinAxis,fxSpun)+0.5)<0.02,"A piece touches down with its lowest point on the ground");
            Vec3 fxNormal=Vec3.Rotate(Vec3.Rotate(fxCut.Normal[0],fxPiece.SpinAxis,fxSpun),fxPiece.SettleAxis,fxPiece.SettleAngle);
            Check(Math.Abs(fxNormal.Y)>0.999,"A landed piece settles flat on its broadest side");
            Vec3 fxRest=DebrisPlanner.Where(fxPiece,9.81,fxPiece.LandTime+20);
            Check(fxRest.Y==fxPiece.RestHeight&&DebrisPlanner.Where(fxPiece,9.81,fxPiece.LandTime*0.5).Y>fxPiece.RestHeight-1e-6,"Landed pieces come to rest on the ground");
            double fxSkid=0; bool fxStops=true;
            for(int f=0;f<fxMotion.Length;f++)
            {
                var mm=fxMotion[f]; DebrisPlanner.Land(fxCut,f,fxStill,ref mm,-0.5,9.81,120);
                if(double.IsInfinity(mm.LandTime)) continue;
                Vec3 sk0=DebrisPlanner.Where(mm,9.81,mm.LandTime), sk1=DebrisPlanner.Where(mm,9.81,mm.LandTime+mm.SlideTime);
                Vec3 sk2=DebrisPlanner.Where(mm,9.81,mm.LandTime+mm.SlideTime+5);
                fxSkid=Math.Max(fxSkid,new Vec3(sk1.X-sk0.X,0,sk1.Z-sk0.Z).Length);
                if(new Vec3(sk2.X-sk1.X,0,sk2.Z-sk1.Z).Length>1e-9) fxStops=false;
            }
            Check(fxSkid>0.3&&fxStops,"Crash wreckage skids along the ground, then stops");
            var fxWet=fxMotion[0]; DebrisPlanner.Land(fxCut,0,fxStill,ref fxWet,-0.5,9.81,120,true);
            Check(fxWet.Hop==0&&fxWet.SlideTime==0,"A piece falling in the sea neither hops nor skids");
            Check(DebrisPlanner.Pieces(fxBlast,4)>DebrisPlanner.Pieces(Plan(Part(1.225,null,speed:5,mass:200)),0.5)&&DebrisPlanner.Pieces(fxBlast,1000)<=48,
                "Bigger, fiercer blasts break parts into more pieces, within bounds");
            var fxPool=Residue.Plan(fxBlast,true,false,45);
            Check(fxPool.HasFire&&fxPool.FireSeconds<=45&&fxPool.SmokeSeconds>fxPool.FireSeconds&&fxPool.ScorchRadius>1,"A fuel crash leaves a pool fire, a smoke column and a scorch");
            Check(!Residue.Plan(fxVacuumBlast,true,false,45).HasFire,"No pool fire without air");
            Check(!Residue.Plan(Plan(Part(1.225,null,speed:40,mass:2250),EventKind.GroundImpact),true,false,45).HasFire,"No pool fire without propellant");
            Check(Residue.Plan(fxBlast,false,true,45).FireSeconds<fxPool.FireSeconds&&Residue.Plan(fxBlast,false,true,45).ScorchRadius==0,"Burning fuel on water is shorter and leaves no scorch");
            Check(Residue.FireLevel(fxPool,1)>Residue.FireLevel(fxPool,fxPool.FireSeconds*0.8)&&Residue.FireLevel(fxPool,fxPool.FireSeconds)==0,"The pool fire dies down as the fuel is used up");
            Check(!Residue.Plan(fxBlast,true,false,0).HasFire&&Residue.Plan(fxBlast,true,false,0).ScorchRadius==0,"Residue can be switched off");
            var fxLow=new FxSettings(); fxLow.ApplyPreset(Quality.Low);
            Check(fxLow.MaxDebrisParts<new FxSettings().MaxDebrisParts&&fxLow.DebrisTriangles<new FxSettings().DebrisTriangles,"Low preset cuts fewer parts into fewer triangles");
            var srb=Plan(Part(1.225,new[]{R("SolidFuel",7000)},speed:12,mass:8500),EventKind.GroundImpact);
            var srbShower=Shower.Plan(srb,true,9.81,128);
            Check(srbShower.Kind==ShowerKind.Solid&&srbShower.Count>=30&&srbShower.Count<=128,"A bursting solid booster throws a shower of burning chunks, within the particle budget");
            Check(srbShower.Trails&&srbShower.TrailLife>1&&srbShower.TrailStep>0,"Their smoke trails hang in the air after the chunks burnt out");
            var srbHeavy=Shower.Heavy(srb,true,9.81,128);
            Check(srbHeavy.Kind==ShowerKind.Solid&&srbHeavy.Count>=4&&srbHeavy.Count<=16&&srbHeavy.LifeMin>srbShower.LifeMin&&srbHeavy.Drag<srbShower.Drag&&srbHeavy.SpeedMax<srbShower.SpeedMax,
                "A big booster also throws a few heavy chunks: slower, less braked, burning longer");
            Check(Shower.Plan(fxBlast,true,9.81,128).Kind==ShowerKind.Liquid&&Shower.Heavy(fxBlast,true,9.81,128).Kind==ShowerKind.None,"A fuel fireball throws glowing pieces, no heavy propellant chunks");
            Check(Shower.Plan(Plan(Part(1.225,null,speed:40,mass:2250),EventKind.GroundImpact),true,9.81,128).Kind==ShowerKind.None,"An empty part throws no burning fragments");
            Check(Shower.Plan(srb,true,9.81,2).Kind==ShowerKind.None,"No shower without a particle budget");
            var vacuumSrb=Plan(Part(0,new[]{R("SolidFuel",7000)},speed:12,mass:8500));
            Check(Shower.Plan(vacuumSrb,false,0,128).Kind==ShowerKind.Solid&&!Shower.Plan(vacuumSrb,false,0,128).Trails,"Solid propellant burns in vacuum too, without smoke trails");
            var srbFlight=Plan(Part(0.95,new[]{R("SolidFuel",7000)},speed:260,mass:8500));
            Check(Shower.Runaways(srbFlight,false)==2&&Shower.Runaways(srb,true)==0&&Shower.Runaways(vacuumSrb,false)==0&&
                Shower.Runaways(Plan(Part(0.95,new[]{R("SolidFuel",300)},speed:260,mass:800)),false)==0,
                "A big booster bursting in flight leaves runaway segments; none on the ground, in vacuum or for a nearly empty case");
            var runA=RunawayPath.Create(7,new Vec3(140,220,0),9.81,0.9,double.NegativeInfinity,12);
            var runB=RunawayPath.Create(7,new Vec3(140,220,0),9.81,0.9,double.NegativeInfinity,12);
            Check(runA.At(3).X==runB.At(3).X&&runA.At(3).Y==runB.At(3).Y&&runA.At(3).Z==runB.At(3).Z,"Runaway paths are deterministic");
            Check(runA.At(0).Length<1e-9&&runA.At(2).Length>300&&runA.ThrustTime>=3&&runA.ThrustTime<=7,"A runaway segment leaves the blast fast and thrusts for a few seconds");
            Vec3 runEnd=runA.At(runA.ThrustTime), runAxis=new Vec3(140,220,0).Normalized(new Vec3(0,1,0));
            Check((runEnd-runAxis*Vec3.Dot(runEnd,runAxis)).Length>20,"Its tumbling thrust throws it off the flight line");
            var runDown=RunawayPath.Create(9,new Vec3(0,-200,0),9.81,0.9,-50,10);
            Check(Math.Abs(runDown.At(10).Y+50)<1e-9&&Math.Abs(runDown.At(5).Y+50)<1e-9,"A runaway segment stops where it hits the ground");
            Check(!double.IsInfinity(Aftermath.Start(fxBlast))&&double.IsInfinity(Aftermath.Start(fxVacuumBlast))&&
                double.IsInfinity(Aftermath.Start(Plan(Part(1.225,null,speed:40,mass:2250),EventKind.GroundImpact))),
                "A fuel fireball in air leaves a lingering cloud; none in vacuum or without fuel");
            var afLayout=BlastLayout.Create(fxBlast,new Vec3(0,-20,0),true,false,5,null);
            var afState=VolumeEvolution.Evaluate(fxBlast,Aftermath.Start(fxBlast),true,true,true,false,afLayout);
            var afPuffs=new AftermathPuff[48];
            int afCount=Aftermath.Build(afState,fxBlast,5,Aftermath.Wind(5,fxBlast.Atmosphere),true,afPuffs);
            bool afSane=afCount>=4; double afLowest=1e9, afRise=0;
            for(int i=0;i<afCount;i++)
            {
                afLowest=Math.Min(afLowest,afPuffs[i].Position.Y-afPuffs[i].Size*0.3); afRise+=afPuffs[i].Velocity.Y;
                if(afPuffs[i].Life<10||afPuffs[i].Alpha<=0||afPuffs[i].Alpha>1||afPuffs[i].Size<=0) afSane=false;
            }
            Check(afSane&&afCount<=48,"The cloud takes over with long-lived puffs where the fireball was");
            Check(afLowest>=-1e-6&&afRise>0,"Cloud puffs start above the ground and keep rising");
            Check(Aftermath.Wind(5,0).Length<1e-9&&Aftermath.Wind(5,1).Length>0.5,"No wind without air");
            Check(srb.WhiteFlame>0.9&&fxBlast.WhiteFlame==0,"Aluminized solid propellant burns white-hot; hydrocarbon fire does not");
            Check(VolumeEvolution.Motion(VolumeEvolution.Evaluate(srb,0.5,true,true,true,false)).Y>0.9,"The volume receives the white-hot flame share");
            Vec3 dunaDust=BodyLook.Dust("Duna"), munDust=BodyLook.Dust("Mun"), otherDust=BodyLook.Dust("Some modded planet");
            Check(dunaDust.X>dunaDust.Z+0.3&&Math.Abs(munDust.X-munDust.Z)<0.05&&otherDust.X>0.3&&otherDust.X<0.9,"Duna's dust is rust red, the Mun's grey, unknown bodies a neutral tan");
            var dunaCrash=VisualEnergy.Calculate(new DestructionEvent(new PartSnapshot(3,3,Guid.Empty,"test",new Vec3(),new Vec3(0,-90,0),new Vec3(0,-90,0),new Vec3(),
                new Vec3(2,2,4),4000,300,new string[0],new[]{R("LiquidFuel",600),R("Oxidizer",750)},
                new EnvironmentContext("Duna",0.068,6.7,2.94,0,false,false,false,new Vec3(0,1,0))),EventKind.GroundImpact,0),new ResourceClassifier(),new FxSettings());
            Check(Math.Abs(dunaCrash.DustR-dunaDust.X)<1e-9&&Math.Abs(dunaCrash.DustB-dunaDust.Z)<1e-9,"A crash takes the dust colour of the body it hits");
            var hardHit=Plan(Part(1.225,null,speed:450,mass:6000),EventKind.GroundImpact);
            var dirtPlan=Shower.Dirt(hardHit,9.81,128,new Vec3(0,-450,0));
            Check(dirtPlan.Kind==ShowerKind.Dirt&&dirtPlan.Count>=8&&dirtPlan.Count<=40&&dirtPlan.MinUp>0&&dirtPlan.Axis.Y>0.99,"A hard crash throws dirt jets up out of the crater");
            var grazeDirt=Shower.Dirt(hardHit,9.81,128,new Vec3(400,-60,0));
            Check(grazeDirt.Axis.X>0.3&&grazeDirt.Axis.Y>0,"A grazing crash throws its dirt downrange");
            Check(Shower.Dirt(Plan(Part(1.225,null,speed:5,mass:200),EventKind.GroundImpact),9.81,128,new Vec3(0,-5,0)).Kind==ShowerKind.None,"A gentle touchdown throws no dirt jets");
            Check(Shower.Dirt(Plan(Part(1.225,null,speed:450,mass:6000),EventKind.WaterImpact),9.81,128,new Vec3(0,-450,0)).Kind==ShowerKind.None,"No dirt jets from the sea");
            var seaHit=Plan(Part(1.225,new[]{R("LiquidFuel",2000),R("Oxidizer",2500)},speed:120,mass:20000),EventKind.WaterImpact);
            var seaFast=Plan(Part(1.225,new[]{R("LiquidFuel",2000),R("Oxidizer",2500)},speed:300,mass:20000),EventKind.WaterImpact);
            Check(Splash.ColumnHeight(seaHit)>=6&&Splash.ColumnHeight(seaFast)>Splash.ColumnHeight(seaHit)&&Splash.ColumnHeight(seaFast)<=90&&Splash.ColumnHeight(fxBlast)==0,
                "A water impact throws a splash column that grows with the impact speed, within bounds; none on land");
            var srbBundles=Shower.Plan(srb,true,9.81,128);
            Check(srbBundles.Clumps>0&&srbBundles.ClumpShare>0&&srbBundles.ClumpShare<1,"Burning chunks fly in sheaves, not as an even hedgehog");
            Check(Math.Abs(BlastLayout.ExpansionFor(0.97)-1)<0.05&&BlastLayout.ExpansionFor(0.23)>2&&BlastLayout.ExpansionFor(0.23)<=2.5&&BlastLayout.ExpansionFor(0)==1,
                "In thin air the same gas grows into a larger ball (bounded); at sea level and in vacuum it does not");
            var looks=new System.Collections.Generic.HashSet<string>();
            for(uint k=1;k<=6;k++)
            {
                var lay=BlastLayout.Create(fxBlast,new Vec3(8,-14,3),true,false,k*7919u,null);
                looks.Add(Math.Round(lay.Tempo,2)+"/"+Math.Round(lay.HeatBias,2)+"/"+Math.Round(lay.SootBias,2));
                if(lay.Tempo<0.85||lay.Tempo>1.15||lay.HeatBias<0.82||lay.HeatBias>1.05) looks.Clear();
            }
            Check(looks.Count==6,"Every blast gets its own pace, colour and soot, within bounds");
            var thinAir=Plan(Part(0.03,new[]{R("SolidFuel",7000)},speed:300,mass:8500));
            Check(!Shower.Plan(thinAir,false,9.81,128).Trails&&Shower.Plan(srb,true,9.81,128).Trails,"No smoke trails in the thin upper atmosphere");
            Check(Shower.Runaways(thinAir,false)==0,"No runaway plume where the air is too thin to hold it");
            int sideOuter=0, sideInner=0, sideWall=0;
            foreach(float sd in fxCut.Side) { if(sd<0.25f) sideOuter++; else if(sd>0.75f) sideInner++; else sideWall++; }
            Check(sideOuter>0&&sideInner==sideOuter&&sideWall>0,"Pieces are sheets with an inner skin and torn edge walls");
            Check(fxCut.TriangleCount<=3000,"Thick pieces stay within the triangle budget");
            var fxStrip=Fracture.Cut(TankMesh(16,1,1.25,6),10,4,3000,1);
            double fxLongestEdge=0;
            for(int t=0;t<fxStrip.TriangleCount;t++)
            {
                int sv=fxStrip.Triangles[3*t];
                if(fxStrip.Side[sv]>0.25f) continue;
                for(int e=0;e<3;e++) fxLongestEdge=Math.Max(fxLongestEdge,(fxStrip.Positions[fxStrip.Triangles[3*t+e]]-fxStrip.Positions[fxStrip.Triangles[3*t+(e+1)%3]]).Length);
            }
            Check(fxStrip!=null&&fxLongestEdge<1.2,"The 6 m strips of a low-poly tank are split into small triangles before tearing (jagged tears)");
            var fxMany=Fracture.Cut(TankMesh(32,16,1.25,4),24,9,4500,1);
            var fxAreas=(double[])fxMany.Area.Clone(); Array.Sort(fxAreas); Array.Reverse(fxAreas);
            double fxAll=0; foreach(double a in fxAreas) fxAll+=a;
            Check(fxAreas[0]+fxAreas[1]+fxAreas[2]>0.25*fxAll&&fxAreas[fxAreas.Length/2]<fxAll/fxAreas.Length,
                "A torn tank leaves a few large plates and many small shards");
            var bigPool=Residue.Plan(Plan(Part(1.225,new[]{R("LiquidFuel",9000),R("Oxidizer",11000)},speed:40,mass:22000),EventKind.GroundImpact),true,false,45);
            Check(fxPool.FlameHeight>=1.6*fxPool.FireRadius&&bigPool.FlameHeight>fxPool.FlameHeight&&bigPool.FlameHeight<=45,
                "Pool fire flames reach the height of real pool fires of that size (Heskestad), within bounds");
            Check(fxPool.PuffHz>0.2&&fxPool.PuffHz<=2&&bigPool.PuffHz<fxPool.PuffHz,"Larger fires puff more slowly");
            Check(Residue.Plan(Plan(Part(1.225,null,speed:15,mass:300),EventKind.GroundImpact),true,false,45).ScorchRadius==0,
                "A small part landing without fuel leaves no scorch mark");
            var sup=new Suppression<string>();
            Check(!sup.Hide("already off",false)&&sup.Count==0,"A stock renderer or light already off is never taken over");
            Check(sup.Hide("flash",true)&&sup.Hide("flash",true)&&sup.Count==1,"A visual switched back on by its owner is hidden again without being counted twice");
            var given=new System.Collections.Generic.List<string>();
            Check(sup.Restore(x=>given.Add(x))==1&&given.Count==1&&given[0]=="flash"&&sup.Count==0,
                "Only what the filter switched off is given back, once");
            Check(StockNames.IsInstanceOf("Explosion 2(Clone)","Explosion 2")&&StockNames.IsInstanceOf("Thud 0","Thud 0"),
                "Stock explosion instances are recognised by their exact prefab name");
            Check(!StockNames.IsInstanceOf("Explosion 20(Clone)","Explosion 2")&&!StockNames.IsInstanceOf("Explosion 2 smoke","Explosion 2")&&
                !StockNames.IsInstanceOf("Explosion 2(Clone)(Clone)","Explosion 2"),"Look-alike names are not taken for a stock explosion");
            var frames=new FrameStats(100);
            for(int i=1;i<=100;i++) frames.Add(i);
            Check(frames.Median==50&&frames.P95==95&&frames.Worst==100,"Frame statistics give the median, 95th percentile and worst frame");
            for(int i=0;i<50;i++) frames.Add(1000);
            Check(frames.Count==100&&frames.Worst==1000&&frames.Median==100,"Frame statistics keep only the latest window");
            var load=new EffectLoad(); load.Peak(new EffectLoad { Blasts=3, FireVolumes=1 }); load.Peak(new EffectLoad { Blasts=1, Particles=900 });
            Check(load.Blasts==3&&load.FireVolumes==1&&load.Particles==900&&load.Busy,"The probe keeps the peak of each kind of load");
            Console.WriteLine("Verified assertions: "+count);
        }
        catch(Exception ex) { Console.Error.WriteLine("FAIL "+ex.Message); Environment.Exit(1); }
    }
}
