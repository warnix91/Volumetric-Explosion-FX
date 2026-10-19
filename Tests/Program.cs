using System.Collections.Generic;
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
    static FxPlan LookPlan(FxPlan p,LookProfile look) { p.Look=look; return p; }
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
        Check(VolumeCameraRange.Select(5,500,397)==VolumeCameraSelection.Both,"A large close volume uses both flight camera ranges");
        Check(VolumeCameraRange.Select(100,20,397)==VolumeCameraSelection.Near,"A small close volume stays on the near camera");
        Check(VolumeCameraRange.Select(500,20,397)==VolumeCameraSelection.Far,"A distant volume stays on the far camera");
        Check(VolumeCameraRange.Select(397,20,397)==VolumeCameraSelection.Both,"A volume crossing the camera split uses both ranges");
        Check(VolumeCameraRange.Select(377,20,397)==VolumeCameraSelection.Both,"A volume touching the split is not lost");
        Check(VolumeCameraRange.Select(-500,20,397)==VolumeCameraSelection.Near,"A volume behind the camera is not sent to the far camera");
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
            Check(s.MaxEvents==10&&s.MaxLights==1&&s.MaxDebris==256,"Low preset budgets");
            s.ApplyPreset(Quality.Ultra);Check(s.MaxEvents==32&&s.MaxLights==6,"Ultra preset still capped");
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
            Check(s.MaxVolumes==16&&s.VolumeSteps==56,"Hero volume count and GPU steps remain capped");
            s.ApplyPreset(Quality.Low);Check(s.MaxVolumes==4&&s.VolumeSteps==24,"Low preset bounds hero volume work");
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
            {
                // A booster: full-length side triangles plus a detailed nozzle that eats the triangle budget.
                PartMesh body=TankMesh(16,1,0.6,14), nozzle=TankMesh(96,40,0.5,0.8), pipe=TankMesh(6,28,0.04,14);
                var bp=new List<Vec3>(body.Positions); var bn=new List<float>(body.Normals); var bu=new List<float>(body.Uvs); var bt=new List<int>(body.Triangles);
                foreach(PartMesh extra in new[]{nozzle,pipe})
                {
                    int nb=bp.Count; bool aside=extra==pipe;
                    foreach(Vec3 q in extra.Positions) bp.Add(aside?q+new Vec3(0.7,0,0):q);
                    bn.AddRange(extra.Normals); bu.AddRange(extra.Uvs);
                    foreach(int i in extra.Triangles) bt.Add(i+nb);
                }
                var booster=new PartMesh{ Positions=bp.ToArray(), Normals=bn.ToArray(), Uvs=bu.ToArray(), Triangles=bt.ToArray(), Groups=new int[bt.Count/3] };
                var boosterCut=Fracture.Cut(booster,20,3,2500,1);
                double longestEdge=0;
                for(int t=0;boosterCut!=null&&t<boosterCut.TriangleCount;t++)
                {
                    Vec3 ea=boosterCut.Positions[boosterCut.Triangles[3*t]], eb=boosterCut.Positions[boosterCut.Triangles[3*t+1]], ec=boosterCut.Positions[boosterCut.Triangles[3*t+2]];
                    longestEdge=Math.Max(longestEdge,Math.Max((eb-ea).Length,Math.Max((ec-eb).Length,(ea-ec).Length)));
                }
                double longestPiece=0;
                for(int f=0;boosterCut!=null&&f<boosterCut.Count;f++)
                {
                    Vec3 lo=new Vec3(1e9,1e9,1e9), hi=new Vec3(-1e9,-1e9,-1e9);
                    for(int v=boosterCut.FirstVertex[f];v<boosterCut.FirstVertex[f]+boosterCut.VertexCounts[f];v++)
                    { Vec3 q=boosterCut.Positions[v]; lo=new Vec3(Math.Min(lo.X,q.X),Math.Min(lo.Y,q.Y),Math.Min(lo.Z,q.Z)); hi=new Vec3(Math.Max(hi.X,q.X),Math.Max(hi.Y,q.Y),Math.Max(hi.Z,q.Z)); }
                    Vec3 d=hi-lo; longestPiece=Math.Max(longestPiece,Math.Max(d.X,Math.Max(d.Y,d.Z)));
                }
                Check(boosterCut!=null&&boosterCut.Count>=6&&longestEdge<7&&longestPiece<3,"A long booster tears into pieces, not into full-length needles or strips (edge "+
                    longestEdge.ToString("0.0")+" m, piece "+longestPiece.ToString("0.0")+" m)");
            }
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
            var lookTank=Plan(Part(1.225,new[]{R("LiquidFuel",900),R("Oxidizer",1100)},speed:60,mass:2250),EventKind.GroundImpact);
            var lookLayout=BlastLayout.Create(lookTank,new Vec3(8,-14,3),true,false,7,null);
            var noLook=VolumeEvolution.Evaluate(lookTank,1.2,true,true,true,false,lookLayout);
            double noLookDuration=VolumeEvolution.VolumeDuration(lookTank);
            lookTank.Look=LookProfile.Classic();
            var classic=VolumeEvolution.Evaluate(lookTank,1.2,true,true,true,false,lookLayout);
            Check(classic.FireHeat==noLook.FireHeat&&classic.NoiseRadius==noLook.NoiseRadius&&classic.FireSoot==noLook.FireSoot&&
                classic.ShockStrength==noLook.ShockStrength&&classic.FireBody==noLook.FireBody&&classic.Lobe0.Radius==noLook.Lobe0.Radius&&
                VolumeEvolution.VolumeDuration(lookTank)==noLookDuration&&VolumeEvolution.LightIntensity(classic)==VolumeEvolution.LightIntensity(noLook),
                "The Classic look reproduces the 1.0 evolution exactly");
            {
                var modernTank=LookProfile.Default(LookKind.Tank);
                lookTank.Look=LookProfile.Classic(); double lateClassic=VolumeEvolution.VolumeDuration(lookTank);
                var oldLate=VolumeEvolution.Evaluate(lookTank,0.8*lateClassic,true,true,true,false,lookLayout);
                lookTank.Look=modernTank;
                var newLate=VolumeEvolution.Evaluate(lookTank,0.8*VolumeEvolution.VolumeDuration(lookTank),true,true,true,false,lookLayout);
                Check(!LookProfile.Classic().Modern&&modernTank.Modern&&modernTank.Clone().Modern&&!modernTank.SameAs(LookProfile.Classic())&&
                    LookProfile.Classic().Shader1.Z==0&&modernTank.Shader1.Z==1,"Only the 1.1 profiles switch the new renderer extras on");
                Check(oldLate.Dissolve==0&&newLate.Dissolve>0.3&&newLate.Dissolve<=1&&newLate.Fade>=oldLate.Fade,
                    "1.1 smoke breaks up from its edges while the 1.0 look keeps its smooth fade");
                lookTank.Look=LookProfile.Classic();
            }
            {
                var g=FireSim.Ground;
                bool monotonic=true; double prev=-1;
                for(int k=0;k<=40;k++) { double f=FireSim.Frame(g,g.TMax*k/40.0); monotonic&=f>=prev&&f<=g.Frames-1+1e-9; prev=f; }
                var box=new double[6]; FireSim.Box(g,g.Frames-1,box);
                Check(monotonic&&FireSim.Frame(g,0)==0&&Math.Abs(FireSim.Frame(g,g.TMax*2)-(g.Frames-1))<1e-9&&box[1]>box[0]&&box[3]>box[2]&&box[5]>box[4],
                    "The baked fireball clock runs from the first to the last frame and the gas box is valid");
                VolumeEvolution.UseSim=true;
                try
                {
                    lookTank.Look=LookProfile.Default(LookKind.Tank);
                    var withSim=VolumeEvolution.Evaluate(lookTank,1.0,true,true,true,false,lookLayout);
                    Vec4 lc,la,ld; VolumeEvolution.PackLobe(withSim,0,out lc,out la,out ld);
                    bool finite=!double.IsNaN(lc.X+lc.Y+lc.Z+lc.W+la.X+la.Y+la.Z+la.W+ld.X+ld.Y+ld.Z+ld.W);
                    Check(withSim.Sim&&withSim.Lobe0.Sim&&finite&&lc.W>0&&la.Z!=0&&la.W>=0&&la.W<=g.Frames-1&&VolumeEvolution.Motion(withSim).W==1&&withSim.BoundsRadius>0&&withSim.BoxRadius>0,
                        "1.1 looks pack baked-simulation lobes (box, scale, frame) when the simulation is loaded");
                    // A second copy of the fireball joins the main cloud as it rises: its cap ends inside the main cap.
                    bool joined=true, seen=false;
                    for(uint k=1;k<60;k++)
                    {
                        var two=BlastLayout.Create(lookTank,new Vec3(),true,false,k*7919u,null);
                        var joinedLate=VolumeEvolution.Evaluate(lookTank,14,true,true,true,false,two);
                        if(!joinedLate.Lobe1.Sim) continue; seen=true;
                        joined&=joinedLate.Lobe1.SimCapTop<=joinedLate.Lobe0.SimCapTop+1e-9&&joinedLate.Lobe1.SimCapTop>=joinedLate.Lobe0.SimCapLow-1e-9;
                    }
                    Check(seen&&joined,"A second copy of the fireball rises into the main cloud instead of hanging under it as a separate cloud");
                    lookTank.Look=LookProfile.Classic();
                    var old=VolumeEvolution.Evaluate(lookTank,1.0,true,true,true,false,lookLayout);
                    Check(!old.Sim&&!old.Lobe0.Sim&&VolumeEvolution.Motion(old).W==0,"The Classic look never uses the baked simulation");
                }
                finally { VolumeEvolution.UseSim=false; }
                lookTank.Look=LookProfile.Classic();
            }
            {
                lookTank.Look=LookProfile.Classic();
                var oldFire=Residue.Plan(lookTank,true,false,60);
                lookTank.Look=LookProfile.Default(LookKind.Tank);
                var newFire=Residue.Plan(lookTank,true,false,60);
                Check(oldFire.HasFire&&oldFire.Delay==0&&oldFire.Patchy==0&&Residue.FireLevel(oldFire,0.9)>0&&
                    newFire.Delay>0.5&&newFire.Patchy>0&&Residue.FireLevel(newFire,newFire.Delay*0.9)==0&&Residue.FireLevel(newFire,newFire.Delay+newFire.Ramp)>0.5,
                    "1.1 ground fires take hold after the fireball, 1.0 fires keep their instant start");
                VolumeEvolution.UseSim=true;
                try
                {
                    var gainNew=VolumeEvolution.Evaluate(lookTank,0.3,true,true,true,false,lookLayout);
                    lookTank.Look=LookProfile.Classic();
                    var gainOld=VolumeEvolution.Evaluate(lookTank,0.3,true,true,true,false,lookLayout);
                    Check(gainNew.ProxyGain>=1&&gainOld.ProxyGain==0&&VolumeEvolution.ShockRimGain(gainOld)==1&&VolumeEvolution.ShockRimGain(gainNew)>0,
                        "The shock and dust keep their optical depth in the larger 1.1 proxy box; Classic is untouched");
                    lookTank.Look=LookProfile.Default(LookKind.Tank);
                    var gwEarly=VolumeEvolution.Evaluate(lookTank,0.3,true,true,true,false,lookLayout);
                    var waveLate=VolumeEvolution.Evaluate(lookTank,0.35,true,true,true,false,lookLayout);
                    var gwGone=VolumeEvolution.Evaluate(lookTank,6.0,true,true,true,false,lookLayout);
                    lookTank.Look=LookProfile.Classic();
                    var waveOld=VolumeEvolution.Evaluate(lookTank,0.35,true,true,true,false,lookLayout);
                    Check(waveLate.ShockRadius*waveLate.BoxRadius>waveOld.ShockRadius*waveOld.BoxRadius+20&&waveLate.ShockStrength>0.002&&
                        gwGone.ShockStrength<=0.002&&gwGone.ShockRadius*gwGone.BoxRadius<4000,
                        "The 1.1 blast front (the ground's dust ring, the jolt) runs on at the speed of sound and fades within a few hundred metres");
                    // In the air an ordinary blast's wave is the 1.0 ring again (its timeline, not the front above).
                    Vec4 ringShock,ringHaze; VolumeEvolution.Distortion(waveLate,0,out ringShock,out ringHaze);
                    Check(ringShock.X==waveLate.RingRadius&&waveLate.RingRadius>0&&
                        Math.Abs(waveLate.RingRadius*waveLate.BoxRadius-waveOld.ShockRadius*waveOld.BoxRadius)<waveOld.ShockRadius*waveOld.BoxRadius*0.6,
                        "An ordinary blast's wave in the air is drawn as the 1.0 ring");
                    // A nuclear blast's too, on its front (the speed of sound, with its dust wall and the jolt).
                    var ringNuke=new FxPlan{ Atmosphere=1, Kind=EventKind.GroundImpact, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(ringNuke,new WarheadInfo{ Kind=WarheadKind.Nuke, TntKg=20e6, RangeM=900 });
                    var ringNukeLayout=BlastLayout.Create(ringNuke,new Vec3(),true,false,77,null);
                    var nukeWave=VolumeEvolution.Evaluate(ringNuke,4,true,true,true,false,ringNukeLayout);
                    Vec4 nukeShock,nukeHaze; VolumeEvolution.Distortion(nukeWave,0,out nukeShock,out nukeHaze);
                    var nukeFar=VolumeEvolution.Evaluate(ringNuke,40,true,true,true,false,ringNukeLayout);
                    Check(nukeShock.X==nukeWave.ShockRadius&&nukeShock.Y>0&&VolumeEvolution.ShockRimGain(nukeWave)>0&&nukeHaze.X==0&&!VolumeEvolution.DistortionVisible(nukeFar,0),
                        "A nuclear blast's wave in the air is the 1.0 ring too, on its front, gone past its reach (no shimmer pass)");
                    var nukeWide=VolumeEvolution.Evaluate(ringNuke,8,true,true,true,false,ringNukeLayout);
                    Check(VolumeEvolution.RingOutOfView(nukeWide,600,1.2)&&!VolumeEvolution.RingOutOfView(nukeWide,20000,1.2)&&!VolumeEvolution.RingOutOfView(waveLate,1,1.2),
                        "A nuclear ring that has swept past the edges of the view is no longer drawn (an ordinary ring always is)");
                    lookTank.Look=LookProfile.Default(LookKind.Tank);
                    var shapeA=BlastLayout.Create(lookTank,new Vec3(),true,false,11,null); var shapeB=BlastLayout.Create(lookTank,new Vec3(),true,false,222,null);
                    var oneA=VolumeEvolution.Evaluate(lookTank,1.5,true,true,true,false,shapeA); var oneB=VolumeEvolution.Evaluate(lookTank,1.5,true,true,true,false,shapeB);
                    Check(oneA.Sim&&oneB.Sim&&(Math.Abs(oneA.SimShape.X-oneB.SimShape.X)>1e-3||Math.Abs(oneA.SimShape.W-oneB.SimShape.W)>1e-3)&&
                        oneA.SimShape.X>=0.78/1.32-1e-9&&oneA.SimShape.X<=1.28&&Math.Abs(oneA.Lobe0.Center.Y-oneB.Lobe0.Center.Y)>1e-4,
                        "Two explosions of the same part get their own fireball character (height, lean, raggedness)");
                    FireSim.GroundLoaded=FireSim.GroundRuns.Length; FireSim.AirLoaded=FireSim.AirRuns.Length;
                    var groundSeen=new int[FireSim.GroundRuns.Length]; var airSeen=new int[FireSim.AirRuns.Length];
                    for(uint k=1;k<=80;k++)
                    {
                        groundSeen[VolumeEvolution.Evaluate(lookTank,1.5,true,true,true,false,BlastLayout.Create(lookTank,new Vec3(),true,false,k*7919u,null)).SimRun]++;
                        airSeen[VolumeEvolution.Evaluate(lookTank,1.5,true,true,false,false,BlastLayout.Create(lookTank,new Vec3(),false,false,k*7919u,null)).SimRun]++;
                    }
                    bool everyShape=true; foreach(int seenCount in groundSeen) everyShape&=seenCount>3; foreach(int seenCount in airSeen) everyShape&=seenCount>3;
                    FireSim.GroundLoaded=1; FireSim.AirLoaded=1;
                    var onlyFirst=VolumeEvolution.Evaluate(lookTank,1.5,true,true,true,false,BlastLayout.Create(lookTank,new Vec3(),true,false,3*7919u,null));
                    Check(everyShape&&onlyFirst.SimRun==0,"Explosions of one kind take every baked fireball shape (four on the ground, two in the air), and only the loaded ones");
                    bool kindsVary=true;
                    foreach(WarheadKind runKind in new[]{WarheadKind.Shell,WarheadKind.HighExplosive,WarheadKind.Bomb,WarheadKind.Structure,WarheadKind.Thermobaric,WarheadKind.None})
                        foreach(TankCharacter runChar in new[]{TankCharacter.Mixed,TankCharacter.Fireball,TankCharacter.Jet,TankCharacter.Flash})
                        {
                            var seen=new System.Collections.Generic.HashSet<int>();
                            for(int pickIndex=0;pickIndex<20;pickIndex++) { int runIndex=FireSim.ChooseGround(runKind,runChar,(pickIndex+0.5)/20); seen.Add(runIndex); kindsVary&=runIndex>=0&&runIndex<FireSim.GroundRuns.Length; }
                            kindsVary&=seen.Count>=2;
                        }
                    Check(kindsVary,"Every kind of explosion (each warhead, each tank character) has at least two fireball shapes");
                    FireSim.NukeHave[0]=FireSim.NukeHave[1]=FireSim.NukeHave[2]=true;
                    bool nukeOwn=FireSim.Usable(true,FireSim.NukeRun2)==FireSim.NukeRun2&&FireSim.Run(true,FireSim.NukeRun).Height>FireSim.Ground.Height&&
                        FireSim.Usable(false,FireSim.ChooseAir(WarheadKind.Nuke,0.4))==FireSim.NukeAirRun&&!FireSim.Run(false,FireSim.NukeAirRun).Ground;
                    FireSim.NukeHave[1]=false; bool nukeSwap=FireSim.Usable(true,FireSim.NukeRun2)==FireSim.NukeRun;
                    FireSim.NukeHave[0]=FireSim.NukeHave[1]=FireSim.NukeHave[2]=false; bool nukeFallback=FireSim.Usable(true,FireSim.NukeRun)==0&&FireSim.Usable(false,FireSim.NukeAirRun)==0;
                    Check(FireSim.IsNukeRun(FireSim.ChooseGround(WarheadKind.Nuke,TankCharacter.Mixed,0.3))&&nukeOwn&&nukeSwap&&nukeFallback,
                        "A nuke takes its own baked run (taller box), and the mushroom when that run is not loaded");
                    Func<WarheadKind,double,double> warheadSize=(kind,kg)=>{ var wp=new FxPlan{ Atmosphere=1, Kind=EventKind.Destruction, Look=LookProfile.Default(LookKind.Atmospheric) }; Warhead.Apply(wp,new WarheadInfo{ Kind=kind, TntKg=kg, RangeM=50 }); return VolumeEvolution.FireballRadius(wp); };
                    double nukeSmall=warheadSize(WarheadKind.Nuke,1.5e6), nukeBig=warheadSize(WarheadKind.Nuke,100e6), missile=warheadSize(WarheadKind.HighExplosive,25), bomb=warheadSize(WarheadKind.Bomb,450);
                    Check(nukeBig>2.5*nukeSmall&&nukeSmall>50&&bomb>2.5*missile&&missile>8,"Warheads scale with their power: a 100 kt nuke dwarfs a 1.5 kt one, a 450 kg bomb a 25 kg missile");
                    var nukePlan=new FxPlan{ Atmosphere=1, Kind=EventKind.GroundImpact, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(nukePlan,new WarheadInfo{ Kind=WarheadKind.Nuke, TntKg=20e6, RangeM=900 });
                    var nukeLayout=BlastLayout.Create(nukePlan,new Vec3(),true,false,77,null);
                    var nukeEarly=VolumeEvolution.Evaluate(nukePlan,0.3,true,true,true,false,nukeLayout); var nukeLate=VolumeEvolution.Evaluate(nukePlan,15,true,true,true,false,nukeLayout);
                    Check(nukeEarly.Nuke.X>=1&&nukeEarly.Nuke.X<1.05&&nukeEarly.Nuke.W>0.8&&nukeEarly.Nuke.Y<0.1&&nukeLate.Nuke.Y>0.9&&nukeLate.NukeStem.Z>0&&nukeLate.Nuke.W<0.05,
                        "A nuclear ground burst starts white-hot, then its dust stem climbs to the cloud");
                    var mtP=new FxPlan{ Atmosphere=1, Kind=EventKind.GroundImpact, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(mtP,new WarheadInfo{ Kind=WarheadKind.Nuke, TntKg=1.2e9, RangeM=900 });
                    var mtL=BlastLayout.Create(mtP,new Vec3(),true,false,117,null); mtL.NukeHumid=true; mtL.NukeWilson=true;
                    var mtW=VolumeEvolution.Evaluate(mtP,3,true,true,true,false,mtL);
                    Check(mtL.NukeThermo&&mtW.NukeWilson.X>0.001&&mtW.NukeWilson.Y>0.5&&Math.Abs(mtW.NukeWilson.X-mtW.ShockRadius)<1e-9,
                        "A megaton burst in moist air flashes a Wilson cloud at its blast wave's front");
                    // Real cloud dimensions (Glasstone fig. 2.16): top ~3 km x kt^0.3, cap radius ~0.3 of it; it lasts minutes.
                    FireSim.NukeHave[0]=FireSim.NukeHave[1]=FireSim.NukeHave[2]=true;   // (as in the game: the nuclear runs loaded)
                    var nukeSettled=VolumeEvolution.Evaluate(nukePlan,180,true,true,true,false,nukeLayout);
                    double capTopM=nukeSettled.NukeIce.W*nukeSettled.BoxRadius, capRM=nukeSettled.NukeIce.Y*nukeSettled.BoxRadius;
                    Vec4 settledShock,settledHaze; VolumeEvolution.Distortion(nukeSettled,1,out settledShock,out settledHaze);
                    Check(capTopM>0.75*nukeLayout.NukeTop&&capTopM<1.08*nukeLayout.NukeTop&&nukeLayout.NukeTop>6500&&nukeLayout.NukeTop<8500&&
                        capRM>0.2*capTopM&&capRM<0.42*capTopM&&nukeSettled.Fade>0.5&&VolumeEvolution.VolumeDuration(nukePlan)>=220&&
                        settledHaze.X*nukeSettled.BoxRadius<=0.61&&FireSim.IsNukeRun(nukeSettled.SimRun)&&nukeSettled.NukeCap.X>0.99&&nukeEarly.NukeCap.X<0.01,
                        "A 20 kt cloud climbs to ~7 km with a cap ~0.3 of its height (drawn as its own shape once ripe), hangs for minutes, and its fires' shimmer stays under a metre ("+
                        (capTopM/1000).ToString("0.0")+" km, cap "+(capRM/1000).ToString("0.0")+" km)");
                    FireSim.NukeHave[0]=FireSim.NukeHave[1]=FireSim.NukeHave[2]=false;
                    // The nuclear blast front stays visible for kilometres (it faded out in time, ~2 s, before reaching 1 km).
                    double shockAt3=0, shockFar=0;
                    for(double ta=0.25;ta<40;ta+=0.25)
                    {
                        var sw=VolumeEvolution.Evaluate(nukePlan,ta,true,true,true,false,nukeLayout); double m=sw.ShockRadius*sw.BoxRadius;
                        if(m>2800&&m<3200) shockAt3=Math.Max(shockAt3,sw.ShockStrength);
                        if(m>2*VolumeEvolution.NukeBlastReach(nukePlan)+50) shockFar=Math.Max(shockFar,sw.ShockStrength);
                    }
                    // the nuclear cloud's own renderer (NukeCloud): white-hot sphere, rolled cap, stem, colour.
                    {
                        double nb=VolumeEvolution.BoxRadius(nukePlan,nukeLayout);
                        Func<double,NukeFrame> nkAt=ta=>NukeCloud.Evaluate(nukePlan,nukeLayout,VolumeEvolution.Evaluate(nukePlan,ta,true,true,true,false,nukeLayout),ta,nb,7,600000);
                        NukeFrame n0=nkAt(0.2), n1=nkAt(30), n2=nkAt(150);
                        var hiL=BlastLayout.Create(nukePlan,new Vec3(),false,false,77,null); hiL.Nuke=NukeKind.HighAir; hiL.NukeGround=3000;
                        NukeFrame nh=NukeCloud.Evaluate(nukePlan,hiL,VolumeEvolution.Evaluate(nukePlan,30,true,true,false,false,hiL),30,nb,7,600000);
                        Check(n0.Cap.W>0.8&&n0.Ball.W<0.05&&n1.Ball.W>0.99&&n1.Torus.Z>n0.Torus.Z&&n2.Ball.Y>n1.Ball.Y&&n2.Ball.X>n1.Ball.X&&
                            n1.Stem.W>0.9&&n1.Stem.Z>n1.Stem.X&&nh.Stem.W==0&&nh.Skirt.Z==0&&n0.Torus.W>n1.Torus.W&&
                            n2.Cap.X+n2.Cap.Y+n2.Cap.Z>n0.Cap.X+n0.Cap.Y+n0.Cap.Z&&n2.Ball.Y*nb>0.25*NukeCloud.Top(nukeLayout,VolumeEvolution.FireballRadius(nukePlan),VolumeEvolution.FireballDuration(nukePlan),150),
                            "A nuclear cloud starts as a white-hot sphere, rolls into a toroidal cap on a dust stem (none after a high burst), climbs, widens and pales");
                        // the stem trails the fireball as soon as it lifts off, rather than catching up with the cap later.
                        {
                            double tfN=VolumeEvolution.FireballDuration(nukePlan);
                            NukeFrame e1=nkAt(0.8*tfN), e2=nkAt(2*tfN);
                            double under1=e1.Ball.X-0.55*e1.Ball.Z, under2=e2.Ball.X-0.55*e2.Ball.Z;
                            Check(e1.Stem.W>0.3&&e1.Stem.Z>=under1-1e-9&&e2.Stem.Z>=under2-1e-9&&e2.Stem.W>e1.Stem.W&&e1.Stem.Y<0.27*e1.Ball.Y,
                                "A ground burst's dust stem trails the rising fireball from lift-off and thickens, about a fifth of the cap wide");
                        }
                        // the blast front's veil in the air early; a Wilson cloud in ordinary weather; at the end of its life
                        // the cloud is sheared downwind, erodes away and its stem goes from the ground up.
                        {
                            double life=VolumeEvolution.VolumeDuration(nukePlan);
                            NukeFrame air2=nkAt(2), mid=nkAt(30), end=nkAt(0.97*life);
                            double tfW=VolumeEvolution.FireballDuration(nukePlan);
                            var wl=BlastLayout.Create(nukePlan,new Vec3(),true,false,77,null);
                            bool wilsonOn=wl.Humidity<=0.2||VolumeEvolution.Evaluate(nukePlan,0.3*tfW,true,true,true,false,wl).NukeWilson.Y>0.3;
                            double shearEnd=Math.Sqrt(end.Late.Y*end.Late.Y+end.Late.Z*end.Late.Z), shearMid=Math.Sqrt(mid.Late.Y*mid.Late.Y+mid.Late.Z*mid.Late.Z);
                            Check(air2.Air.Y==0&&air2.Air.X>0&&mid.Late.X==0&&end.Late.X>0.9&&shearEnd>3*shearMid&&end.Late.W>end.Stem.X+0.5*(end.Ball.X-end.Stem.X)&&mid.Late.W<mid.Stem.X&&wilsonOn,
                                "A nuclear blast front races out in the air (as bent air, no veil); a Wilson cloud forms in ordinary moist air; at the end the cloud is sheared downwind, breaks up and its stem goes from the ground up");
                            // the front runs at the speed of sound (with the jolt and BDArmory's boom); a wall of dust rides it
                            // over the ground; a nuclear jolt is felt further and longer.
                            double tA=3000/340.0;
                            var atBoom=VolumeEvolution.Evaluate(nukePlan,tA,true,true,true,false,nukeLayout);
                            NukeFrame w5=nkAt(5);
                            Check(Math.Abs(atBoom.ShockRadius*atBoom.BoxRadius-3000)<30&&Math.Abs(Shake.Arrival(3000,1)-tA)<0.05&&w5.Wave.W>0.2&&w5.Wave.X>0&&
                                Shake.Amplitude(7,4000,1,240,true)>2*Shake.Amplitude(7,4000,1,240)&&Shake.Duration(7,true)>Shake.Duration(7),
                                "A nuclear blast front reaches 3 km with the boom and the jolt; a wall of dust rides it over the ground; a nuclear jolt is felt further and longer");
                            var burn=Residue.Plan(nukePlan,true,false,60);
                            Check(burn.ScorchRadius==0&&!burn.HasFire,"A nuclear ground burst leaves no burn mark or fire on the ground");
                            // white condensation nearly always (a Wilson dome; rings round the stem in moist air); a long ember.
                            int wilsons=0, ringed=0;
                            for(uint k=1;k<=40;k++)
                            {
                                var cl=BlastLayout.Create(nukePlan,new Vec3(),true,false,k*2654435761u,null);
                                if(VolumeEvolution.Evaluate(nukePlan,0.3*tfW,true,true,true,false,cl).NukeWilson.Y>0.3) wilsons++;
                                if(cl.NukeHumid) ringed++;
                            }
                            NukeFrame rung=nkAt(2.2*tfW), ember=nkAt(12*tfW);
                            Check(wilsons>=34&&ringed>=30&&(!nukeLayout.NukeHumid||rung.Pile2.X>=2&&rung.Pile2.Y>0)&&ember.Heat.W>0.02&&ember.Torus.W>0.01,
                                "A nuclear cloud nearly always shows white condensation (a Wilson dome, rings round its stem in moist air) and keeps an ember glowing for a minute");
                        }
                        // In vacuum nothing lifts it: no mushroom, no stem; its gas thins out to nothing within seconds; no afterglow.
                        var vacP=new FxPlan{ Atmosphere=0, Kind=EventKind.GroundImpact, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(vacP,new WarheadInfo{ Kind=WarheadKind.Nuke, TntKg=20e6, RangeM=900 });
                        var vacL=BlastLayout.Create(vacP,new Vec3(),true,false,77,null);
                        double vb=VolumeEvolution.BoxRadius(vacP,vacL), vtf=VolumeEvolution.FireballDuration(vacP);
                        NukeFrame v1=NukeCloud.Evaluate(vacP,vacL,VolumeEvolution.Evaluate(vacP,2*vtf,true,true,true,false,vacL),2*vtf,vb,7,200000);
                        Check(v1.Ball.W==0&&v1.Stem.W==0&&v1.Heat.Y<0.05&&v1.Heat.W==0&&n1.Heat.W>0,"A nuclear blast in vacuum makes no mushroom or stem and fades within seconds");
                        // warheads burn briefly; hydrogen is faint; a fallen building's dust hugs the ground.
                        Check(Warhead.FireLife(WarheadKind.HighExplosive)<1&&Warhead.FireLife(WarheadKind.HighExplosive)>=0.7&&Warhead.FireLife(WarheadKind.Bomb)<1&&Warhead.FireLife(WarheadKind.Thermobaric)==1&&Warhead.FireLife(WarheadKind.None)==1,
                            "A high explosive's fireball burns out a little sooner than a fuel tank's or a thermobaric one's, but is a real fireball");
                        var bldP=new FxPlan{ Atmosphere=1, Kind=EventKind.GroundImpact, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Collapse(bldP,40,30,0);
                        var depP=new FxPlan{ Atmosphere=1, Kind=EventKind.GroundImpact, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Collapse(depP,40,15,50000);
                        Check(BlastLayout.Create(bldP,new Vec3(),true,false,5,null).SimHugsGround&&!BlastLayout.Create(depP,new Vec3(),true,false,5,null).SimHugsGround,
                            "A fallen building's dust rolls along the ground; a fuel store's fire still rises");
                        // the glare is a flash then a bloom while white-hot; in moist air a pileus hood forms over the
                        // rising cap and is gone once the cap has climbed through it.
                        {
                            var hp=new FxPlan{ Atmosphere=1, Kind=EventKind.GroundImpact, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(hp,new WarheadInfo{ Kind=WarheadKind.Nuke, TntKg=20e6, RangeM=900 });
                            var hl=BlastLayout.Create(hp,new Vec3(),true,false,77,null); hl.NukeHumid=true;
                            double hb=VolumeEvolution.BoxRadius(hp,hl), htf=VolumeEvolution.FireballDuration(hp);
                            Func<double,NukeFrame> hAt=ta=>NukeCloud.Evaluate(hp,hl,VolumeEvolution.Evaluate(hp,ta,true,true,true,false,hl),ta,hb,7,600000);
                            NukeFrame h1=hAt(1.2*htf), h2=hAt(30*htf), h0=hAt(0.01);
                            Vec4 g0=NukeCloud.Glare(h0,0.01,htf), g1=NukeCloud.Glare(hAt(1.0),1.0,htf), g9=NukeCloud.Glare(hAt(8*htf),8*htf,htf), g6=NukeCloud.Glare(hAt(6),6,htf);
                            bool hoodOver=h1.Pile0.Z>0.2&&h1.Pile0.X>h1.Ball.X+h1.Ball.Z;
                            var dryL=BlastLayout.Create(hp,new Vec3(),true,false,77,null); dryL.NukeHumid=false;
                            NukeFrame hd=NukeCloud.Evaluate(hp,dryL,VolumeEvolution.Evaluate(hp,1.6*htf,true,true,true,false,dryL),1.6*htf,hb,7,600000);
                            Check(g0.Y>1&&g1.Y>0.5&&g6.Y<0.05&&g1.Z>0.3&&g9.Y+g9.Z<0.05&&hoodOver&&h2.Pile0.Z<0.01&&hd.Pile0.Z==0,
                                "A nuclear burst's glare flashes, stays blinding for a second or more (20 kt), then blooms while white-hot; a pileus hood rises over the cap in moist air (none in dry air) and is gone once punched through");
                        }
                        // a nuclear flash whites out the view (strong up close, fading in seconds and with distance);
                        // yields set clearly different cloud heights (0.25 / 1.5 / 20 kt).
                        double fl0=NukeCloud.ScreenFlash(0.05,3000,71), fl3=NukeCloud.ScreenFlash(3,3000,71), flFar=NukeCloud.ScreenFlash(0.05,60000,71);
                        Func<double,double> topOf=kg=>{ var tp=new FxPlan{ Atmosphere=1, Kind=EventKind.GroundImpact, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(tp,new WarheadInfo{ Kind=WarheadKind.Nuke, TntKg=kg, RangeM=900 }); return BlastLayout.Create(tp,new Vec3(),true,false,5,null).NukeTop; };
                        double t025=topOf(0.25e6), t15=topOf(1.5e6), t20=topOf(20e6);
                        Check(fl0>0.6&&fl3<0.15&&flFar<0.1&&NukeCloud.ScreenFlash(-1,10,71)==0&&t15>2*t025&&t20>3*t15,
                            "A nuclear flash whites out the view and fades; a 20 kt cloud towers over a 1.5 kt one, itself over a 0.25 kt one ("+
                            (t025/1000).ToString("0.0")+" / "+(t15/1000).ToString("0.0")+" / "+(t20/1000).ToString("0.0")+" km)");
                    }
                    Check(shockAt3>0.25&&shockFar<0.002,"A 20 kt blast front is still strong at 3 km and gone past twice its reach ("+shockAt3.ToString("0.00")+")");
                    bool wholeRuns=true;
                    foreach(var runGroup in new[]{FireSim.GroundRuns,FireSim.AirRuns})
                        foreach(var run in runGroup)
                            wholeRuns&=run.LastWhole>=run.Frames/3&&run.LastWhole<run.Frames&&run.Bounds[run.LastWhole*6+3]<0.95*run.Height;
                    Check(wholeRuns,"Every baked run stops at a frame whose cloud is still whole inside its box (no flattening against the ceiling)");
                    double lastTop=-1, lastFrame=-1; bool keepsRising=true, frameHeld=true;
                    var riseLayout=BlastLayout.Create(lookTank,new Vec3(),true,false,11,null);
                    for(double riseAge=1;riseAge<=20;riseAge+=0.5)
                    {
                        var risen=VolumeEvolution.Evaluate(lookTank,riseAge,true,true,true,false,riseLayout);
                        double top=risen.Lobe0.SimOrigin.Y;
                        if(riseAge>6) keepsRising&=top>=lastTop-1e-9;
                        frameHeld&=risen.Lobe0.SimFrame<=FireSim.GroundRuns[risen.SimRun].LastWhole+1e-9&&risen.Lobe0.SimFrame>=lastFrame-1e-9;
                        lastTop=top; lastFrame=risen.Lobe0.SimFrame;
                    }
                    var lateCloud=VolumeEvolution.Evaluate(lookTank,18,true,true,true,false,riseLayout);
                    Check(keepsRising&&frameHeld&&lateCloud.Lobe0.SimAfter>1&&lateCloud.SimStem>1,
                        "Past its last whole frame a fuel cloud holds its shape and keeps rising (no jump back, no frame past the ceiling)");
                    var mixA=new FxPlan{ BurnableKg=1000, SmokeR=0.3, SmokeG=0.3, SmokeB=0.3, Soot=0.5, Look=LookProfile.Default(LookKind.Tank) };
                    var mixB=new FxPlan{ BurnableKg=1000, SmokeR=0.3, SmokeG=0.3, SmokeB=0.3, Soot=0.5, Look=LookProfile.Default(LookKind.Tank) };
                    var mixC=new FxPlan{ BurnableKg=1000, SmokeR=0.3, SmokeG=0.3, SmokeB=0.3, Soot=0.5, Look=LookProfile.Classic() };
                    Chemistry.Vary(mixA,5); Chemistry.Vary(mixB,6); Chemistry.Vary(mixC,5);
                    var sand=new FxPlan{ DustR=0.6, DustG=0.5, DustB=0.4, Look=LookProfile.Default(LookKind.Tank) }; Chemistry.GroundDust(sand,0.9,0.9,0.92);
                    var grass=new FxPlan{ DustR=0.6, DustG=0.5, DustB=0.4, Look=LookProfile.Default(LookKind.Tank) }; Chemistry.GroundDust(grass,0.2,0.5,0.15);
                    var trailSteps=new FireTrailStep[FireTrail.MaxSteps];
                    int trailSlow=FireTrail.Plan(15,8,2000,trailSteps), trailMid=FireTrail.Plan(70,8,2000,trailSteps);
                    double midReach=trailSteps[trailMid-1].Distance;
                    int trailFast=FireTrail.Plan(180,8,2000,trailSteps);
                    Check(trailSlow==0&&trailMid>=1&&trailFast>trailMid&&trailSteps[trailFast-1].Distance>midReach&&trailSteps[0].Delay>0&&trailSteps[trailFast-1].Fuel<trailSteps[0].Fuel&&
                        FireTrail.Plan(180,8,5,trailSteps)==0,"A sideways crash throws burning fuel ahead: more and further fireballs the faster, none without fuel");
                    // not a straight, evenly spaced line: uneven gaps, scattered to the side, different from crash to crash.
                    {
                        var a=new FireTrailStep[FireTrail.MaxSteps]; var b=new FireTrailStep[FireTrail.MaxSteps];
                        int na=FireTrail.Plan(180,8,2000,a,11), nb=FireTrail.Plan(180,8,2000,b,12);
                        bool uneven=Math.Abs((a[1].Distance-a[0].Distance)-(a[2].Distance-a[1].Distance))>0.5, aside=false, ordered=true, differs=false;
                        for(int k=0;k<na;k++) { aside|=Math.Abs(a[k].Side)>1; differs|=Math.Abs(a[k].Side-b[k].Side)>0.5; if(k>0) ordered&=a[k].Distance>a[k-1].Distance; }
                        Check(na==nb&&uneven&&aside&&ordered&&differs,"The fuel thrown along a crash path scatters unevenly to either side, differently each crash");
                    }
                    var stripA=new ResiduePlan{ FireSeconds=25, FireRadius=5, SmokeSeconds=40, FlameHeight=5 };
                    var stripB=new ResiduePlan{ FireSeconds=20, FireRadius=3, SmokeSeconds=30, FlameHeight=4 };
                    var joined=stripA.Joined(stripB,12);
                    Check(joined.FireRadius==5&&joined.FireSeconds==32&&joined.SmokeSeconds>=47&&joined.FlameHeight==5,
                        "Fuel thrown along a crash path joins the fire as a strip that burns as long as the later spill");
                    Check(Warhead.Classify("Standard","Missile","AIM-120",20)==WarheadKind.HighExplosive&&Warhead.Classify("Standard","Missile","Mk84 bomb",400)==WarheadKind.Bomb&&
                        Warhead.Classify("ContinuousRod","Missile","AIM-9",5)==WarheadKind.ContinuousRod&&Warhead.Classify("Nuke","Missile","x",50)==WarheadKind.Nuke&&
                        Warhead.Classify("Standard","Bullet","30mm HE",0.1)==WarheadKind.Shell&&Warhead.Classify("Standard","Missile","Napalm canister",50)==WarheadKind.Incendiary&&
                        Warhead.Classify("EMP","Missile","x",1)==WarheadKind.Emp,"Combat warheads are recognised: missile, bomb, rod, nuke, shell, incendiary, EMP");
                    Check(Warhead.Classify("EMP","Other","B61",5e5)==WarheadKind.Emp&&Warhead.Classify("Nuke","Other","B61",5e5)==WarheadKind.Nuke&&
                        Warhead.Classify("Standard","Missile","Tempest AAM",20)==WarheadKind.HighExplosive&&Warhead.Classify("Standard","Missile","EMP pod",1)==WarheadKind.Emp,
                        "A nuclear EMP stays an EMP, and 'emp' only counts as a whole word (Tempest is a missile)");
                    {
                        // v40 log: the YJ-12 anti-ship missile (360 kg) was drawn as a bomb, and air bursts threw up earth.
                        var airBomb=new FxPlan{ Atmosphere=1, Kind=EventKind.Destruction, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(airBomb,new WarheadInfo{Kind=WarheadKind.Bomb,TntKg=250,RangeM=70});
                        var groundBomb=new FxPlan{ Atmosphere=1, Kind=EventKind.GroundImpact, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(groundBomb,new WarheadInfo{Kind=WarheadKind.Bomb,TntKg=250,RangeM=70});
                        Check(Warhead.Classify("Standard","Missile","YJ-12 ASM",360)==WarheadKind.HighExplosive&&Warhead.Classify("Standard","Missile","AGM-84 Harpoon missile",220)==WarheadKind.HighExplosive&&
                            Warhead.Classify("Standard","Missile","bahaMk84Bomb",430)==WarheadKind.Bomb&&Warhead.Classify("Standard","Missile","x",300)==WarheadKind.Bomb,
                            "A heavy missile is still a missile (bombs by name, or heavy unnamed charges)");
                        Check(airBomb.OreKg==0&&groundBomb.OreKg>0&&airBomb.Soot>groundBomb.Soot,"Only a ground burst throws up earth; an air burst stays a dark smoke ball");
                    }
                    {
                        var empMissile=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(empMissile,new WarheadInfo{Kind=WarheadKind.Emp,TntKg=6,RangeM=25});
                        var empPulse=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(empPulse,new WarheadInfo{Kind=WarheadKind.Emp,TntKg=0.1,RangeM=25});
                        Check(empMissile.BurnableKg>0&&empMissile.ElectricFraction>0.3&&empPulse.BurnableKg==0&&empPulse.ElectricFraction==1,
                            "An EMP missile with a real charge blows up with an electric flash; a pure pulse stays a flash");
                        var lone=new DestructionEvent(Part(1.225),EventKind.GroundImpact,12.5);
                        EventCluster single=EventCluster.Single(77,lone,4.6);
                        Check(single.Id==77&&single.Members==1&&single.Representative==lone&&single.Ground&&single.FirstTime==12.5&&single.RepresentativeScore==4.6,
                            "A combat detonation is spawned on its own at once (never merged, queued or dropped by the clusterer)");
                        var hangar=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Collapse(hangar,45,22,0);
                        var depot=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Collapse(depot,25,14,20000);
                        var hangarL=BlastLayout.Create(hangar,new Vec3(),true,false,5,null);
                        var hangarR=Residue.Plan(hangar,true,false,45);
                        var depotR=Residue.Plan(depot,true,false,45);
                        Check(hangar.Warhead==WarheadKind.Structure&&hangar.OreKg>0&&hangar.Soot<0.2&&hangar.SmokeR>0.6&&depot.Soot>0.4&&depot.BurnableKg>hangar.BurnableKg&&
                            hangarL.SingleCloud&&hangarL.SimTall<0.6&&hangarL.SimHugsGround&&VolumeEvolution.VolumeDuration(hangar)>=29&&!hangarR.HasFire&&hangarR.ScorchRadius==0&&depotR.HasFire&&depotR.ScorchRadius<=25,
                            "A falling building is a broad, low, pale dust cloud that hangs; only a fuel store burns on");
                        var sooty2=new FxPlan{ Look=LookProfile.Default(LookKind.Tank), SmokeR=0.3, SmokeG=0.29, SmokeB=0.28, Kind=EventKind.GroundImpact, DustR=0.6, DustG=0.5, DustB=0.4 };
                        var pale2=new FxPlan{ Look=LookProfile.Default(LookKind.Tank), SmokeR=0.85, SmokeG=0.85, SmokeB=0.84 };
                        Check(Chemistry.SmokeMix(sooty2,0.1).W>0.4&&Chemistry.SmokeMix(sooty2,0.1).X>0.4&&Chemistry.SmokeMix(sooty2,0.5).X>0.5&&Chemistry.SmokeMix(sooty2,0.9).X>0.75&&
                            Chemistry.SmokeMix(pale2,0.5).X<0.6&&Chemistry.SmokeMix(new FxPlan{ SmokeR=0.3 },0.5).W==0,
                            "One explosion carries several smokes: earth, grey combustion smoke or steam in sooty smoke, darker residue in pale smoke");
                        var fullTank=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank), BurnableKg=1500, Fill=0.95, PartKind=PartKind.LiquidTank };
                        var halfTank=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank), BurnableKg=1500, Fill=0.45, PartKind=PartKind.LiquidTank };
                        var dryTank=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank), BurnableKg=1500, Fill=0.05, PartKind=PartKind.LiquidTank };
                        var bigStage=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank), BurnableKg=9000, Fill=0.9, PartKind=PartKind.LiquidTank };
                        Check(BlastLayout.ChooseCharacter(fullTank,0.1)==TankCharacter.Fireball&&BlastLayout.ChooseCharacter(halfTank,0.1)==TankCharacter.Jet&&
                            BlastLayout.ChooseCharacter(dryTank,0.1)==TankCharacter.Flash&&BlastLayout.ChooseCharacter(bigStage,0.1)==TankCharacter.Chain&&
                            BlastLayout.ChooseCharacter(fullTank,0.95)==TankCharacter.Mixed,
                            "Tanks let go by how full they were: fireball, jet, chain, vapour flash, or the mixed look by chance");
                        int kinds=0; var seenKinds=new System.Collections.Generic.HashSet<TankCharacter>();
                        for(uint k=1;k<60;k++) seenKinds.Add(BlastLayout.Create(fullTank,new Vec3(),true,false,k,null).Character);
                        kinds=seenKinds.Count;
                        var classicTank=new FxPlan{ Atmosphere=1, BurnableKg=1500, Fill=0.95, PartKind=PartKind.LiquidTank };
                        Check(kinds>=3&&BlastLayout.Create(classicTank,new Vec3(),true,false,3,null).Character==TankCharacter.Mixed,
                            "The same full tank does not always let go the same way; the 1.0 look keeps its layout");
                        Aftermath.SceneWind=new Vec3(2,0,0);
                        Vec3 w1=Aftermath.Wind(11,1,true), w2=Aftermath.Wind(977,1,true), w0=Aftermath.Wind(11,1,false);
                        var windTank=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank), BurnableKg=1500, Fill=0.95, PartKind=PartKind.LiquidTank };
                        bool downwind=true; for(uint k=1;k<30;k++) { var lw=BlastLayout.Create(windTank,new Vec3(),true,false,k,null); if(lw.Character==TankCharacter.Jet) continue; downwind&=lw.SimLeanX>=0&&Math.Abs(lw.SimLeanZ)<=0.6*lw.SimLeanX+1e-9; }
                        Aftermath.SceneWind=new Vec3();
                        Check(w1.X>1.4&&w2.X>1.4&&Math.Abs(w1.Z)<0.6&&Math.Abs(w2.Z)<0.6&&w0.Length>0&&Aftermath.Wind(11,0,true).Length<1e-9&&downwind,
                            "One breeze for the scene: every plume and cloud leans downwind (only the 1.0 look keeps its per-event breeze)");
                        var sooty=new FxPlan{ Look=LookProfile.Default(LookKind.Tank), Soot=0.8 };
                        var styles=new System.Collections.Generic.HashSet<SmokeStyle>(); for(int k=0;k<20;k++) styles.Add(SmokeStyles.Choose(sooty,k/20.0));
                        double rc,sc,dc,rk,sk,dk; SmokeStyles.Shape(SmokeStyle.Column,out rc,out sc,out dc); SmokeStyles.Shape(SmokeStyle.Creep,out rk,out sk,out dk);
                        Check(styles.Count==4&&SmokeStyles.Choose(new FxPlan{ Soot=0.8 },0.1)==SmokeStyle.None&&rc>1&&sc<1&&rk<0.6&&dk>1.5&&
                            SmokeStyles.Pulse(SmokeStyle.Column,3,0)==1&&SmokeStyles.Pulse(SmokeStyle.Puffing,0.7,0)>1&&SmokeStyles.Pulse(SmokeStyle.Puffing,2.1,0)<0.3,
                            "Burning wrecks smoke in four ways (column, billows, creeping haze, puffs); the 1.0 look keeps its plume");
                        var bigBomb=new FxPlan{ Atmosphere=1, Kind=EventKind.GroundImpact, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(bigBomb,new WarheadInfo{Kind=WarheadKind.Bomb,TntKg=250,RangeM=70});
                        var smallPop=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank), VisualEnergyScore=3 };
                        double waveReach=VolumeEvolution.GroundWaveReach(bigBomb,true,false);
                        var bombLayout=BlastLayout.Create(bigBomb,new Vec3(),true,false,4,null);
                        Vec4 dustEarly=VolumeEvolution.GroundWave(bigBomb,VolumeEvolution.Evaluate(bigBomb,0.3,true,true,true,false,bombLayout),waveReach);
                        Vec4 dustLater=VolumeEvolution.GroundWave(bigBomb,VolumeEvolution.Evaluate(bigBomb,1.5,true,true,true,false,bombLayout),waveReach);
                        Vec4 dustGone=VolumeEvolution.GroundWave(bigBomb,VolumeEvolution.Evaluate(bigBomb,40,true,true,true,false,bombLayout),waveReach);
                        Check(waveReach>=40&&VolumeEvolution.GroundWaveReach(bigBomb,false,false)==0&&VolumeEvolution.GroundWaveReach(bigBomb,true,true)==0&&
                            VolumeEvolution.GroundWaveReach(smallPop,true,false)==0&&dustEarly.Z>0.2&&dustLater.X>dustEarly.X&&dustLater.Z>0.2&&dustGone.Z<0.01,
                            "A big ground blast lifts a sheet of dust that runs out with its wave and settles after a while (not in the air, on water, or for small blasts)");
                        bombLayout.Humidity=1; var dry=BlastLayout.Create(bigBomb,new Vec3(),true,false,4,null); dry.Humidity=0;
                        double tauB=VolumeEvolution.TimeScale(bigBomb);
                        Check(VolumeEvolution.Condensation(bigBomb,bombLayout,0.6*tauB,tauB,1,bigBomb.Look)>0.2&&VolumeEvolution.Condensation(bigBomb,dry,0.6*tauB,tauB,1,bigBomb.Look)==0&&
                            VolumeEvolution.Condensation(bigBomb,bombLayout,3*tauB,tauB,1,bigBomb.Look)==0&&VolumeEvolution.Condensation(bigBomb,bombLayout,0.6*tauB,tauB,1,LookProfile.Classic())==0,
                            "A big blast in humid air flashes a brief condensation cloud (not in dry air, not later, not in the 1.0 look)");
                        var nukeP2=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(nukeP2,new WarheadInfo{Kind=WarheadKind.Nuke,TntKg=20000,RangeM=900});
                        Check(Aftermath.LightningRate(nukeP2,5)>1&&Aftermath.LightningRate(nukeP2,1)==0&&Aftermath.LightningRate(nukeP2,30)==0&&Aftermath.LightningRate(bigBomb,5)==0,
                            "Lightning flickers in the cloud of very large blasts only (a nuke, not a bomb), from a second and a half on");
                        Check(Shake.Amplitude(7,50,1,20)>0.5&&Shake.Amplitude(7,50,1,20)<=2.5&&Shake.Amplitude(7,2000,1,20)<Shake.Amplitude(7,200,1,20)&&
                            Shake.Amplitude(7,50,0,20)==0&&Shake.Amplitude(2,50,1,20)==0&&Math.Abs(Shake.Arrival(680,1)-2)<1e-9&&
                            Shake.Envelope(-0.1,1)==0&&Shake.Envelope(0.05,1)>0.7&&Shake.Envelope(0.9,1)<0.1&&Shake.Envelope(1.1,1)==0,
                            "Camera shake: strong close by, weaker far away, none in vacuum or for small pops, arriving at the speed of sound");
                        var cine=new FxSettings(); cine.ApplyProfile(0); var bal=new FxSettings(); bal.ApplyProfile(1); var light=new FxSettings(); light.ApplyProfile(2);
                        Check(cine.Quality==Quality.Ultra&&cine.VolumeDivisor==1&&!cine.Adaptive&&bal.VolumeDivisor==2&&bal.Adaptive&&light.Quality==Quality.Medium&&
                            light.DebrisTriangles<cine.DebrisTriangles&&light.Profile==2&&FxSettings.PhotoSpeed(0)==1&&FxSettings.PhotoSpeed(1)<1&&FxSettings.PhotoSpeed(2)==0,
                            "Quick setups (cinematic, balanced, performance) and the photo mode speeds");
                        Check(Chemistry.Classify(0.85,0.87,0.9)==GroundKind.Snow&&Chemistry.Classify(0.3,0.45,0.2)==GroundKind.Grass&&Chemistry.Classify(0.75,0.62,0.42)==GroundKind.Sand&&
                            Chemistry.Classify(0.45,0.45,0.44)==GroundKind.Rock&&Chemistry.Classify(0.45,0.36,0.3)==GroundKind.Soil,"The ground under a blast is read from its colour: snow, grass, sand, rock or soil");
                        var snowy=new FxPlan{ Look=LookProfile.Default(LookKind.Tank) }; Chemistry.GroundDust(snowy,0.85,0.87,0.9);
                        Check(snowy.Surface==GroundKind.Snow&&snowy.DustR>0.85&&snowy.DustB>0.9,"A blast on snow throws up white powder");
                        PartSnapshot deep=Part(1.225,new[]{R("LiquidFuel",900,900),R("Oxidizer",1100,1100)}); 
                        var deepEnv=new EnvironmentContext("Kerbin",1.225,101.3,9.81,-20,true,false,true,new Vec3(0,1,0));
                        var deepPart=new PartSnapshot(9,9,Guid.Empty,"tank",new Vec3(),new Vec3(0,-5,0),new Vec3(0,-5,0),new Vec3(),new Vec3(1,2,1),2000,300,new string[0],deep.Resources,deepEnv);
                        var under=VisualEnergy.Calculate(new DestructionEvent(deepPart,EventKind.WaterImpact,0),new ResourceClassifier(),new FxSettings());
                        double fireBefore=under.FireFraction; under.Look=LookProfile.Default(LookKind.Tank); Chemistry.Apply(under);
                        var underR=Residue.Plan(under,false,true,45);
                        Check(Math.Abs(under.Depth-19.8)<1e-9&&under.FireFraction<0.1*fireBefore+1e-9&&under.Soot==0&&Splash.DomeRadius(under)>0&&Splash.DomeDelay(under)>0&&
                            Splash.ColumnHeight(under)>0&&!underR.HasFire,"Under water a blast lifts a white dome and a column, without a fireball or a fire on the surface");
                        var floating=new FxPlan{ Look=LookProfile.Default(LookKind.Tank), Atmosphere=1, FireFraction=0.9, BurnableKg=900, Soot=0.6, Kind=EventKind.WaterImpact, Radius=4 };
                        var slickR=Residue.Plan(floating,false,true,45); floating.Look=LookProfile.Classic(); var plainR=Residue.Plan(floating,false,true,45);
                        Check(Splash.Slick(new FxPlan{ Look=LookProfile.Default(LookKind.Tank), Soot=0.6 })&&slickR.HasFire&&slickR.FireRadius>plainR.FireRadius&&slickR.FlameHeight<plainR.FlameHeight,
                            "Burning fuel floating on the water spreads into a wide, low slick");
                        // The models run once at flight start (compiled before the first blast) without failing.
                        bool warmed=true; try { Warmup.Run(); } catch(Exception) { warmed=false; }
                        Check(warmed,"The explosion models warm up at flight start without failing");
                        // 1.1: a water impact's splash as a volume (crown, jet, spray, surge, foam).
                        var seaVol=Plan(Part(1.225,new[]{R("LiquidFuel",2000),R("Oxidizer",2500)},speed:120,mass:20000),EventKind.WaterImpact); seaVol.Look=LookProfile.Default(LookKind.Tank);
                        var seaOld=Plan(Part(1.225,new[]{R("LiquidFuel",2000),R("Oxidizer",2500)},speed:120,mass:20000),EventKind.WaterImpact); seaOld.Look=LookProfile.Classic();
                        var landVol=Plan(Part(1.225,new[]{R("LiquidFuel",2000),R("Oxidizer",2500)},speed:120,mass:20000),EventKind.GroundImpact); landVol.Look=LookProfile.Default(LookKind.Tank);
                        Check(WaterSplash.Applies(seaVol)&&!WaterSplash.Applies(seaOld)&&!WaterSplash.Applies(landVol)&&!WaterSplash.Applies(null),
                            "A water impact's splash is a volume in the 1.1 look only, and only on the water");
                        double hSea=Splash.ColumnHeight(seaVol), endSea=WaterSplash.Duration(seaVol,9.81);
                        var still=new Vec3(0,0,0);
                        SplashFrame sp0=WaterSplash.Evaluate(seaVol,0.3,9.81,still,still,7), sp1=WaterSplash.Evaluate(seaVol,1.5,9.81,still,still,7);
                        Check(sp0.Visible&&sp0.Crown.W>0&&sp0.Sheet.Y>0&&sp0.Reach>sp0.Crown.X&&sp0.Top>0&&sp1.Reach>sp0.Reach&&sp1.Top>sp0.Top,
                            "The crown rises and spreads from the moment of impact, inside its box");
                        // A quick burst: gone within its life (seconds), leaving a light veil of spray and foam.
                        SplashFrame spGone=WaterSplash.Evaluate(seaVol,sp0.Crown.Z+0.1,9.81,still,still,7);
                        Check(sp0.Crown.Z<=2.6&&spGone.Visible&&spGone.Sheet.Y==0&&spGone.Spray.W>0&&spGone.Foam.Z>0&&spGone.Surge.W==0,
                            "A crash into the sea throws a quick burst of spray that is gone within a couple of seconds, leaving spray and foam");
                        // No water spout up the middle of a surface impact; a warhead's burst is a steep plume.
                        bool jetSeen=false; for(double ta=0.2;ta<8;ta+=0.2) if(WaterSplash.Evaluate(seaVol,ta,9.81,still,still,7).Jet.Z>0) jetSeen=true;
                        var seaBomb=Plan(Part(1.225,null,speed:220,mass:400),EventKind.WaterImpact); seaBomb.Look=LookProfile.Default(LookKind.Tank); seaBomb.Warhead=WarheadKind.Bomb;
                        bool bombJet=false; for(double ta=0.2;ta<8;ta+=0.2) if(WaterSplash.Evaluate(seaBomb,ta,9.81,still,still,7).Jet.Z>0) bombJet=true;
                        SplashFrame bomb1=WaterSplash.Evaluate(seaBomb,0.6,9.81,still,still,7), crash1=WaterSplash.Evaluate(seaVol,0.6,9.81,still,still,7);
                        Check(!jetSeen&&!bombJet&&bomb1.Sheet.W==1&&crash1.Sheet.W==0&&bomb1.Top/bomb1.Crown.X>crash1.Top/crash1.Crown.X*1.4,
                            "A surface impact throws no water spout up the middle; a warhead's burst rises as a steep plume, a crash's spreads");
                        var diveEnv=new EnvironmentContext("Kerbin",1.225,101.3,9.81,-6,true,false,true,new Vec3(0,1,0));
                        var divePart=new PartSnapshot(10,10,Guid.Empty,"missile",new Vec3(),new Vec3(0,-300,0),new Vec3(0,-300,0),new Vec3(),new Vec3(0.3,3,0.3),300,300,new string[0],new ResourceSnapshot[0],diveEnv);
                        var dive=VisualEnergy.Calculate(new DestructionEvent(divePart,EventKind.WaterImpact,0),new ResourceClassifier(),new FxSettings());
                        dive.Look=LookProfile.Default(LookKind.Tank);
                        Check(dive.Depth==0&&Splash.DomeRadius(dive)==0,"A missile found a frame under the sea it hit at 300 m/s is a surface impact, not an underwater blast");
                        // Ground marks: a warhead's crater, a fuel burn, a grazing crash drawn out along its path; the 1.0 mark.
                        var markBomb=new FxPlan{ Look=LookProfile.Default(LookKind.Tank), Warhead=WarheadKind.Bomb, ImpactSpeed=200 };
                        var markBurn=new FxPlan{ Look=LookProfile.Default(LookKind.Tank), FireFraction=0.8, ImpactSpeed=20 };
                        var markSlide=new FxPlan{ Look=LookProfile.Default(LookKind.Tank), FireFraction=0.1, ImpactSpeed=180 };
                        Vec4 lookBomb=Residue.ScorchLook(markBomb,0,-200,0), lookBurn=Residue.ScorchLook(markBurn,0,-20,0), lookSlide=Residue.ScorchLook(markSlide,0,-40,170);
                        Check(lookBomb.X==1&&lookBomb.Y==1&&lookBomb.Z<0.05&&lookBurn.Y==0&&lookSlide.Y>0.5&&lookSlide.Z>0.6&&Math.Abs(lookSlide.W-Math.PI/2)<1e-9&&
                            Residue.ScorchLook(new FxPlan{ Look=LookProfile.Classic(), Warhead=WarheadKind.Bomb },0,-200,0).X==0,
                            "Ground marks: a warhead leaves a crater, fuel a burn, a grazing crash a gouge drawn out along its path; the 1.0 look keeps its mark");
                        Check(endSea>5&&endSea<90&&!WaterSplash.Evaluate(seaVol,endSea+0.01,9.81,still,still,7).Visible&&WaterSplash.Evaluate(seaVol,endSea-6,9.81,still,still,7).Foam.Z>0,
                            "The splash ends, its foam last, within a minute and a half");
                        SplashFrame lean=WaterSplash.Evaluate(seaVol,1,9.81,new Vec3(80,0,0),still,7), again=WaterSplash.Evaluate(seaVol,1,9.81,new Vec3(80,0,0),still,7);
                        Check(lean.Drift.Y>0.1&&Math.Abs(lean.Drift.Z)<1e-9&&sp1.Drift.Y==0&&lean.Reach==again.Reach&&lean.Crown.Y==again.Crown.Y,
                            "A grazing impact throws its crown forward; the same splash is drawn the same way");
                        under.Look=LookProfile.Default(LookKind.Tank);
                        SplashFrame domeUp=WaterSplash.Evaluate(under,0.1,9.81,still,still,7), plumes=WaterSplash.Evaluate(under,1.5,9.81,still,still,7);
                        Check(WaterSplash.Applies(under)&&domeUp.Dome.Z>0&&domeUp.Crown.W==0&&plumes.Dome.Z==0&&plumes.Jet.Y>0&&plumes.Foam.W==1,
                            "Under water a white dome rises first, then bursts into plumes");
                        Check(Aftermath.Afterheat(20,25)>0.2&&Aftermath.Afterheat(20,60)<Aftermath.Afterheat(20,25)&&Aftermath.Afterheat(20,90)==0&&Aftermath.Afterheat(0,25)==0&&Aftermath.Afterheat(20,10)==0,
                            "Heat shimmer lingers over a wreck for about a minute after its fire is out");
                        var bigCrash=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank), BurnableKg=3000 };
                        var popsOut=new Aftermath.Pop[5]; int anyPops=0, maxPops=0;
                        for(uint k=1;k<40;k++) { int n=Aftermath.Pops(bigCrash,k,8,popsOut); if(n>0) anyPops++; maxPops=Math.Max(maxPops,n); for(int i=0;i<n;i++) if(popsOut[i].At<2||popsOut[i].At>15||popsOut[i].Size<=0) maxPops=99; }
                        Check(anyPops>20&&maxPops<=5&&Aftermath.Pops(new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank), BurnableKg=100 },3,8,popsOut)==0&&
                            Aftermath.Pops(new FxPlan{ Atmosphere=1, Look=LookProfile.Classic(), BurnableKg=3000 },3,8,popsOut)==0,
                            "Most big burning wrecks let off a few secondary explosions between 2 and 15 s (not small ones, not the 1.0 look)");
                        var grassFire=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank), FireFraction=0.9, BurnableKg=900, Soot=0.5, Radius=3, Surface=GroundKind.Grass };
                        var grassR=Residue.Plan(grassFire,true,false,90); grassFire.Surface=GroundKind.Sand; var sandR=Residue.Plan(grassFire,true,false,90);
                        Check(Aftermath.CreepSpeed(new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank), Surface=GroundKind.Grass })>0&&Aftermath.CreepSpeed(grassFire)==0&&
                            grassR.SmokeSeconds>sandR.SmokeSeconds+20,"A fire in grassFire creeps outward and keeps smoking for about a minute; not on sand");
                    }
                    {
                        double sx,sz,sr,hx,hz,hr;
                        VolumeEvolution.ShadowDrape(40,0.6,0.62,0,out sx,out sz,out sr);   // sun 46 deg up toward +x
                        VolumeEvolution.ShadowDrape(40,0,1,0,out hx,out hz,out hr);         // sun overhead
                        Check(sx<-10&&Math.Abs(sz)<1e-9&&sr>40*0.85+9&&Math.Abs(hx)<1e-9&&Math.Abs(hr-34)<1e-9,
                            "The ground shadow is laid out away from the sun and wide enough for a tall cloud's shadow (centred when the sun is overhead)");
                    }
                    Check(Warhead.Replaces(new WarheadInfo{Kind=WarheadKind.Shell,TntKg=0.05})&&Warhead.Replaces(new WarheadInfo{Kind=WarheadKind.Kinetic,TntKg=5})&&
                        Warhead.Small(new WarheadInfo{Kind=WarheadKind.Shell,TntKg=0.05})&&!Warhead.Small(new WarheadInfo{Kind=WarheadKind.Shell,TntKg=1})&&
                        Warhead.Small(new WarheadInfo{Kind=WarheadKind.Kinetic,TntKg=5})&&!Warhead.Small(new WarheadInfo{Kind=WarheadKind.Bomb,TntKg=250})&&
                        Warhead.BurstSize(0.1,0)>0.6&&Warhead.BurstSize(0,30)<Warhead.BurstSize(0,120)&&Warhead.BurstSize(0,30)>=0.15,
                        "Every combat detonation is drawn by VEFX: small shells and kinetic hits as a light burst sized by charge or calibre");
                    var heP=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(heP,new WarheadInfo{Kind=WarheadKind.HighExplosive,TntKg=20,RangeM=30});
                    var bombP=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(bombP,new WarheadInfo{Kind=WarheadKind.Bomb,TntKg=250,RangeM=70});
                    var nukeP=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(nukeP,new WarheadInfo{Kind=WarheadKind.Nuke,TntKg=1e6,RangeM=900});
                    var napP=new FxPlan{ Atmosphere=1, Look=LookProfile.Default(LookKind.Tank) }; Warhead.Apply(napP,new WarheadInfo{Kind=WarheadKind.Incendiary,TntKg=60,RangeM=40});
                    Check(VolumeEvolution.FireballRadius(bombP)>VolumeEvolution.FireballRadius(heP)&&VolumeEvolution.FireballRadius(nukeP)>=30&&VolumeEvolution.FireballRadius(nukeP)>VolumeEvolution.FireballRadius(bombP)&&heP.WhiteFlame<0.5&&
                        !Residue.Plan(heP,true,false,60).HasFire&&Residue.Plan(heP,true,false,60).ScorchRadius>0&&Residue.Plan(napP,true,false,60).HasFire,
                        "Warheads scale with their charge; plain explosives scorch the ground, incendiaries leave it burning");
                    var poolA=new ResiduePlan{ FireSeconds=30, FireRadius=6, SmokeSeconds=50, FlameHeight=5, ScorchRadius=8 };
                    var poolB=new ResiduePlan{ FireSeconds=20, FireRadius=6, SmokeSeconds=40, FlameHeight=6, ScorchRadius=8 };
                    var fedPool=poolA.Fed(poolB,10);
                    Check(fedPool.FireRadius>6&&fedPool.FireRadius<12&&fedPool.FireSeconds>30&&fedPool.SmokeSeconds>=fedPool.FireSeconds+20&&fedPool.FlameHeight>6,
                        "A spill landing in a burning pool feeds it: wider, a little taller, burning longer");
                    Check(sand.DustB>0.6&&Math.Abs(grass.DustR-0.6)<1e-9&&Math.Abs(grass.DustG-0.5)<1e-9,"Blast dust takes the ground's colour (snow, sand, rock) but not the grass's green");
                    Check(mixA.BurnableKg>=700&&mixA.BurnableKg<=1250&&Math.Abs(mixA.BurnableKg-mixB.BurnableKg)>1&&mixC.BurnableKg==1000&&mixC.SmokeR==0.3,
                        "Each 1.1 explosion burns a slightly different share of its load and smoke shade; Classic is unchanged");
                    Check(VolumeEvolution.MarchDivisor(1,false)==1&&VolumeEvolution.MarchDivisor(1,true)==2&&VolumeEvolution.MarchDivisor(3,true)==4&&VolumeEvolution.MarchDivisor(9,false)==3,
                        "The simulated fireball is marched one resolution step lower (with four times the steps)");
                }
                finally { VolumeEvolution.UseSim=false; }
                lookTank.Look=LookProfile.Classic();
            }
            {
                var chemMono=LookPlan(Plan(Part(1.225,new[]{R("MonoPropellant",750)},speed:50,mass:1200),EventKind.GroundImpact),LookProfile.Default(LookKind.Tank));
                double chemMonoKg=chemMono.BurnableKg; Chemistry.Apply(chemMono);
                var chemHyper=LookPlan(Plan(Part(1.225,new[]{R("UDMH",2500),R("NTO",4500)},speed:60,mass:9000),EventKind.GroundImpact),LookProfile.Default(LookKind.Tank));
                Chemistry.Apply(chemHyper);
                var chemH2=LookPlan(Plan(Part(1.225,new[]{R("LqdHydrogen",700),R("LqdOxygen",4200)},speed:60,mass:9000),EventKind.GroundImpact),LookProfile.Default(LookKind.Tank));
                Chemistry.Apply(chemH2);
                var chemOld=Plan(Part(1.225,new[]{R("UDMH",2500),R("NTO",4500)},speed:60,mass:9000),EventKind.GroundImpact); chemOld.Look=LookProfile.Classic();
                double chemOldR=chemOld.SmokeR, chemOldSoot=chemOld.Soot; Chemistry.Apply(chemOld);
                Check(chemMono.Mono>0.9&&chemMono.Soot==0&&chemMono.BurnableKg<chemMonoKg&&chemMono.SmokeR>0.7&&chemHyper.SmokeR>chemHyper.SmokeG+0.2&&chemHyper.SmokeG>chemHyper.SmokeB&&
                    chemH2.Hydrogen>0.1&&chemH2.WhiteFlame>0&&chemOld.SmokeR==chemOldR&&chemOld.Soot==chemOldSoot,
                    "1.1 explosions show what burned (monopropellant, hypergolic, hydrogen); Classic plans are left alone");
                var chemOre=Plan(Part(1.225,new[]{R("Ore",6000)},speed:90,mass:8000),EventKind.GroundImpact);
                chemOre.Look=LookProfile.Classic(); var chemClassic=VolumeEvolution.Evaluate(chemOre,0.5,true,true,true,false);
                chemOre.Look=LookProfile.Default(LookKind.Tank); var chemOreState=VolumeEvolution.Evaluate(chemOre,0.5,true,true,true,false);
                Check(chemOre.OreKg==6000&&Chemistry.OreDust(chemOre)>0.9&&chemOreState.Dust>=chemClassic.Dust&&chemOreState.Dust>0.5&&chemOreState.DustR!=chemClassic.DustR&&new ResourceClassifier().Classify(Part(1.225,new[]{R("Ore",10)}))==PartKind.Structure,
                    "An ore tank bursts into rock dust in 1.1, without changing what the part is");
            }
            Check(Conflicts.Find(new[]{"Assembly-CSharp","VolumetricExplosions"},null)=="Volumetric Explosions"&&Conflicts.Find(null,new[]{"volumetricexplosions"})!=null&&
                Conflicts.Find(new[]{"VolumetricExplosionFX","Assembly-CSharp"},new[]{"VolumetricExplosionFX","Squad"})==null,"Another mod that replaces explosions is recognised, our own folder is not");
            {
                lookTank.Look=LookProfile.Classic();
                var shadowOld=VolumeEvolution.Shadow(VolumeEvolution.Evaluate(lookTank,2.5,true,true,true,false,lookLayout),13);
                lookTank.Look=LookProfile.Default(LookKind.Tank);
                var shadowHotState=VolumeEvolution.Evaluate(lookTank,0.2,true,true,true,false,lookLayout);
                var smokeState=VolumeEvolution.Evaluate(lookTank,2.5,true,true,true,false,lookLayout);
                var shadowHot=VolumeEvolution.Shadow(shadowHotState,13); var shadowSmoke=VolumeEvolution.Shadow(smokeState,13);
                Check(shadowOld.Y==0&&shadowSmoke.Y>shadowHot.Y&&shadowSmoke.Y<=0.6&&shadowSmoke.Z>0&&VolumeEvolution.ShadowLobe(smokeState,0).W>0,
                    "1.1 smoke casts a ground shadow, darker once the glowing fire has cooled; Classic casts none");
                lookTank.Look=LookProfile.Classic();
            }
            {
                lookTank.Look=LookProfile.Classic(); var fireOld=Residue.Plan(lookTank,true,false,180);
                lookTank.Look=LookProfile.Default(LookKind.Tank); var fireNew=Residue.Plan(lookTank,true,false,180);
                Check(fireNew.FireSeconds>=fireOld.FireSeconds&&Residue.Plan(lookTank,true,false,10).FireSeconds<=10+fireNew.Delay+1e-9,
                    "1.1 spilled fuel burns at least as long, within the player's residue limit");
                lookTank.Look=LookProfile.Classic();
            }
            var dial=LookProfile.Classic(); dial[LookProfile.IndexOf("coolingRate")]=2; lookTank.Look=dial;
            Check(VolumeEvolution.Evaluate(lookTank,1.2,true,true,true,false,lookLayout).FireHeat<classic.FireHeat,"A faster cooling rate puts the flames out sooner");
            dial=LookProfile.Classic(); dial[LookProfile.IndexOf("expansionSpeed")]=2; lookTank.Look=dial;
            Check(VolumeEvolution.Evaluate(lookTank,0.2,true,true,true,false,lookLayout).Lobe0.Radius>
                VolumeEvolution.Evaluate(LookPlan(lookTank,LookProfile.Classic()),0.2,true,true,true,false,lookLayout).Lobe0.Radius,
                "A faster expansion grows the fireball sooner");
            dial=LookProfile.Classic(); dial[LookProfile.IndexOf("effectLifetime")]=2; dial[LookProfile.IndexOf("lightIntensity")]=0; lookTank.Look=dial;
            Check(VolumeEvolution.VolumeDuration(lookTank)>noLookDuration&&VolumeEvolution.VolumeDuration(lookTank)<=20&&
                VolumeEvolution.LightIntensity(VolumeEvolution.Evaluate(lookTank,0.3,true,true,true,false,lookLayout))==0,
                "Lifetime and light dials act within their bounds");
            dial[0]=99; dial[LookProfile.IndexOf("flameHeat")]=-5;
            Check(dial.Shape==LookProfile.Max[0]&&dial.FlameHeat==LookProfile.Min[11],"Look dials are clamped to their accepted range");
            bool defaultsValid=true;
            foreach(LookKind k in Enum.GetValues(typeof(LookKind)))
            { var d=LookProfile.Default(k); var copy=d.Clone(); copy.Validate(); defaultsValid&=copy.SameAs(d); }
            Check(defaultsValid&&LookProfile.Keys.Length==LookProfile.Count&&LookProfile.Min.Length==LookProfile.Count&&LookProfile.Max.Length==LookProfile.Count,
                "Every shipped look profile lies inside the dial ranges");
            var solidPlan=Plan(Part(1.225,new[]{R("SolidFuel",7000)},speed:12,mass:8500),EventKind.GroundImpact);
            Check(LookProfile.Select(solidPlan,true,false,1)==LookKind.Solid&&LookProfile.Select(lookTank,true,false,1)==LookKind.Tank&&
                LookProfile.Select(lookTank,true,false,6)==LookKind.Vessel&&LookProfile.Select(lookTank,true,true,1)==LookKind.Reentry,
                "Explosions pick the look of what exploded and where");
            var thin=Plan(Part(0.1,new[]{R("LiquidFuel",900),R("Oxidizer",1100)}));
            var thick=Plan(Part(1.225,new[]{R("LiquidFuel",900),R("Oxidizer",1100)}));
            Check(LookProfile.Select(thin,false,false,1)==LookKind.HighAltitude&&LookProfile.Select(thick,false,false,1)==LookKind.Atmospheric,
                "Airbursts pick the atmospheric or high-altitude look by air density");
            var library=new LookLibrary(); library[LookKind.Tank][0]=1.7; library.UseClassic=true;
            Check(library.For(LookKind.Tank).SameAs(LookProfile.Classic())&&library[LookKind.Tank].Shape==1.7,"The Classic switch overrides the edited profiles without losing them");
            library.Reset(LookKind.Tank);
            LookKind parsed;
            Check(library[LookKind.Tank].SameAs(LookProfile.Default(LookKind.Tank))&&LookLibrary.TryParse("highaltitude",out parsed)&&parsed==LookKind.HighAltitude,
                "A profile resets to its shipped default and kinds parse by name");
            var live=new LookLibrary(); LookProfile held=live[LookKind.Vessel]; held[2]=0.1;
            Check(!live.IsDefault(LookKind.Vessel)&&live.IsDefault(LookKind.Tank),"Only edited profiles count as changed");
            live.Reset(LookKind.Vessel);
            Check(ReferenceEquals(held,live[LookKind.Vessel])&&held.SameAs(LookProfile.Default(LookKind.Vessel)),
                "Resetting a profile updates it in place, so explosions on screen follow");
            live.Forced=(int)LookKind.Solid;
            Check(live.Choose(LookKind.Tank)==LookKind.Solid&&new LookLibrary().Choose(LookKind.Tank)==LookKind.Tank,
                "A look-editor test forces the profile being tuned, otherwise the natural one is used");
            live.UseClassic=true; live.Reset();
            Check(!live.UseClassic&&live.IsDefault(LookKind.Solid),"Resetting every look also leaves the 1.0 comparison");
            var gov=new FrameGovernor(22);
            for(int i=0;i<300;i++) gov.Update(14,1/60.0,false);
            for(int i=0;i<240;i++) gov.Update(40,1/25.0,true);
            Check(gov.Scale<0.6&&gov.Scale>=FrameGovernor.MinScale&&gov.Lights(6)<6&&gov.Lights(6)>=1,"Slow frames with effects lower volume detail and lights, within bounds");
            Check(gov.Volumes(8)<8&&gov.Volumes(8)>=6&&new FrameGovernor().Volumes(8)==8,"Under load a few fewer new volumes are admitted, never under three quarters (detail goes first)");
            var lax=new FrameGovernor(22); for(int i=0;i<600;i++) lax.Update(45,1/22.0,false);
            Check(lax.BudgetMs<=33,"A slow moment without effects cannot make the frame budget lax");
            Check(!new FxSettings().Adaptive,"Adaptive performance is off unless the player turns it on");
            var res=new FxSettings { VolumeDivisor=2 };
            res.ApplyPreset(Quality.Low); bool keptLow=res.VolumeDivisor==2; res.ApplyPreset(Quality.Ultra);
            Check(keptLow&&res.VolumeDivisor==2&&res.Quality==Quality.Ultra,"Quality presets leave the volume resolution alone");
            res.VolumeDivisor=9; res.Validate();
            Check(res.VolumeDivisor==3&&new FxSettings().VolumeDivisor==1,"Volume resolution stays between full and a third, full by default");
            double low=gov.Scale;
            for(int i=0;i<600;i++) gov.Update(12,1/60.0,true);
            Check(gov.Scale>low+0.3,"Detail recovers once frames are fast again");
            var heavyGame=new FrameGovernor(22);
            for(int i=0;i<600;i++) heavyGame.Update(30,1/33.0,false);
            for(int i=0;i<300;i++) heavyGame.Update(34,1/30.0,true);
            Check(heavyGame.BudgetMs==33,"A scene already slow without effects gets a larger, bounded budget");
            gov.Update(5000,1,true); gov.Update(double.NaN,1,true);
            Check(gov.Scale>=FrameGovernor.MinScale&&gov.Scale<=1,"Loading hitches and invalid frame times are ignored");
            var hotState=new VolumeState { FireHeat=1, Glow=0, Atmosphere=1 };
            var coolState=new VolumeState { FireHeat=0.15, Glow=0, Atmosphere=1 };
            var flashState=new VolumeState { FireHeat=1, Glow=1, Atmosphere=1 };
            Vec3 hotC=ExplosionLight.Colour(hotState), coolC=ExplosionLight.Colour(coolState), flashC=ExplosionLight.Colour(flashState);
            Check(hotC.Y>coolC.Y&&hotC.Z>coolC.Z&&flashC.Z>hotC.Z&&coolC.X==1&&coolC.Y<0.5,
                "Explosion light runs from a white flash through yellow to deep orange as the fireball cools");
            Check(ExplosionLight.Range(flashState,10,30)>ExplosionLight.Range(hotState,10,30)&&ExplosionLight.Range(hotState,40,30)>ExplosionLight.Range(hotState,5,30)&&
                ExplosionLight.Range(flashState,500,500)<=400,"The flash reaches further, bigger fireballs light further, within bounds");
            Check(ExplosionLight.Priority(2,60,50)>ExplosionLight.Priority(2,60,800)&&ExplosionLight.Priority(2,120,300)>ExplosionLight.Priority(0.5,120,300)&&
                ExplosionLight.Priority(0.01,60,10)==0,"Light priority favours bright, large, close explosions and ignores faint ones");
            var pr=new double[]{0.5,3,0,2,1}; var ch=new bool[5]; var ord=new int[5];
            int taken=ExplosionLight.Select(pr,5,2,ch,ord);
            Check(taken==2&&ord[0]==1&&ord[1]==3&&ch[1]&&ch[3]&&!ch[0]&&!ch[2]&&!ch[4],"The light budget keeps the strongest lights, best first");
            Check(ExplosionLight.Select(pr,5,0,ch,ord)==0&&!ch[1]&&ExplosionLight.Select(new double[]{0,0},2,3,ch,ord)==0,
                "No light is chosen with a zero budget or without candidates");
            {
                var flat=new FragmentMotion{ LandTime=1, LandHeight=0, RestHeight=0, Slide=new Vec3(4,0,0), SlideTime=2, Velocity=new Vec3(10,0,0) };
                var hill=flat; hill.SlopeX=0.3;
                Vec3 endFlat=DebrisPlanner.Where(flat,9.81,10), endHill=DebrisPlanner.Where(hill,9.81,10);
                Check(Math.Abs(endFlat.Y)<1e-9&&Math.Abs(endHill.Y-0.3*(endHill.X-DebrisPlanner.Where(hill,9.81,1).X))<1e-6&&endHill.Y>0.5,
                    "A wreck piece skidding on a hillside follows the slope instead of the flat plane of the blast");
            }
            Check(ExplosionLight.LampScore(3,60,40,20,true,0.1,0.4)>0&&ExplosionLight.LampScore(3,60,40,20,true,1.5,0.4)==0&&
                ExplosionLight.LampScore(3,60,200,20,false,0,0)==0&&ExplosionLight.LampScore(3,60,30,20,false,2,0)>ExplosionLight.LampScore(3,60,70,20,false,2,0)&&
                ExplosionLight.LampScore(0.01,60,10,20,false,0,0)==0,"Lamps light smoke only in reach and inside their cone, more when closer");
            Check(ExplosionLight.Flicker(0.05,1.3)==1&&Math.Abs(ExplosionLight.Flicker(2,1.3)-1)<=0.15,"Light flicker starts smoothly and stays within 15 %");
            Console.WriteLine("Verified assertions: "+count);
        }
        catch(Exception ex) { Console.Error.WriteLine("FAIL "+ex.Message); Environment.Exit(1); }
    }
}
