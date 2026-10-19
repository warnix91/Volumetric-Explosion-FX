using System;
namespace VolumetricExplosionFX.Core
{
    // What burned changes how the explosion looks (1.1 profiles only; the Classic look keeps the 1.0 plan).
    // Visual approximations from footage of real accidents, not a combustion model.
    public static class Chemistry
    {
        static double Lerp(double a,double b,double t) { return a+(b-a)*t; }
        public static void Apply(FxPlan plan)
        {
            if(plan==null||plan.Look==null||!plan.Look.Modern) return;
            if(plan.Depth>3)
            {
                // Under water the fireball is quenched at once: a white dome of water and spray, then a column, no flame.
                plan.FireFraction*=0.08; plan.SmokeFraction=0; plan.Soot=0; plan.WhiteFlame=0;
                plan.VaporFraction=Math.Max(plan.VaporFraction,0.6);
                plan.SmokeR=0.92; plan.SmokeG=0.94; plan.SmokeB=0.96;
            }
            double H=Numbers.Clamp(plan.Hydrogen,0,1), M=Numbers.Clamp(plan.Mono,0,1), B=Numbers.Clamp(plan.Brown,0,1);
            // Hydrogen burns almost without soot: a pale, brief flame and a white cloud of condensing steam.
            if(H>0.01)
            {
                plan.Soot*=1-0.9*H; plan.WhiteFlame=Math.Max(plan.WhiteFlame,0.25*H);
                plan.SmokeR=Lerp(plan.SmokeR,0.86,H); plan.SmokeG=Lerp(plan.SmokeG,0.87,H); plan.SmokeB=Lerp(plan.SmokeB,0.89,H);
            }
            // Monopropellant (hydrazine) decomposes without air: a sharp yellow flash, a smaller fireball, a grey-white cloud.
            if(M>0.01)
            {
                // The decomposition gases are mostly clear: a thinner, faintly yellow cloud that clears quickly.
                plan.Soot*=1-M; plan.WhiteFlame=Math.Max(plan.WhiteFlame,0.15*M);
                plan.BurnableKg*=1-0.6*M; plan.FireFraction*=1-0.45*M;
                plan.VaporFraction=Math.Max(plan.VaporFraction,0.5*M);
                plan.SmokeR=Lerp(plan.SmokeR,0.86,M); plan.SmokeG=Lerp(plan.SmokeG,0.82,M); plan.SmokeB=Lerp(plan.SmokeB,0.7,M);
            }
            // Nitrogen tetroxide: the cloud turns a deep reddish brown (NO2), with little soot.
            if(B>0.01)
            {
                plan.Soot*=1-0.5*B;
                plan.SmokeR=Lerp(plan.SmokeR,0.62,B); plan.SmokeG=Lerp(plan.SmokeG,0.31,B); plan.SmokeB=Lerp(plan.SmokeB,0.12,B);
            }
        }
        // No two explosions alike (1.1): how well the propellants mixed before igniting (how much of the load
        // burns in the fireball), the smoke's shade and its soot change a little every time, even for the same part.
        public static void Vary(FxPlan plan,uint seed)
        {
            if(plan==null||plan.Look==null||!plan.Look.Modern) return;
            var rnd=new SplitRandom(seed^0x4348454Du);
            plan.BurnableKg*=rnd.Range(0.7,1.25);
            double shade=rnd.Range(0.82,1.12), warm=rnd.Range(-0.035,0.035);
            plan.SmokeR=Numbers.Clamp(plan.SmokeR*shade+warm,0,1); plan.SmokeG=Numbers.Clamp(plan.SmokeG*shade,0,1); plan.SmokeB=Numbers.Clamp(plan.SmokeB*shade-warm,0,1);
            plan.Soot=Numbers.Clamp(plan.Soot*rnd.Range(0.75,1.25),0,1);
        }
        // 1.1: the dust a blast lifts takes the colour of the ground it hit (sand, snow, rock, Duna's red soil). Grass is
        // not dust: under green ground the soil colour of the body stays.
        // The kind of ground from its colour: pale and grey is snow or ice, green is grass, warm and light is sand.
        public static GroundKind Classify(double r,double g,double b)
        {
            double grey=(r+g+b)/3, spread=Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b));
            if(grey>0.68&&spread<0.12) return GroundKind.Snow;
            if(g-Math.Max(r,b)>0.03) return GroundKind.Grass;
            if(r>g&&g>b&&r-b>0.12&&grey>0.42) return GroundKind.Sand;
            if(spread<0.06) return GroundKind.Rock;
            return GroundKind.Soil;
        }
        public static void GroundDust(FxPlan plan,double r,double g,double b)
        {
            if(plan==null||plan.Look==null||!plan.Look.Modern) return;
            plan.Surface=Classify(r,g,b);
            if(plan.Surface==GroundKind.Snow)
            {
                // Powder snow: the blast throws up a white cloud, not earth.
                plan.DustR=0.9; plan.DustG=0.92; plan.DustB=0.95; return;
            }
            double green=Numbers.Clamp((g-Math.Max(r,b))*4,0,1);
            double grey=(r+g+b)/3, k=(plan.Surface==GroundKind.Sand?0.85:0.6)*(1-green);
            if(k<=0.01) return;
            // Lifted dust is paler and less saturated than the ground.
            double dr=Numbers.Clamp((grey+(r-grey)*0.6)*1.15,0.05,0.95), dg=Numbers.Clamp((grey+(g-grey)*0.6)*1.15,0.05,0.95), db=Numbers.Clamp((grey+(b-grey)*0.6)*1.15,0.05,0.95);
            plan.DustR+=(dr-plan.DustR)*k; plan.DustG+=(dg-plan.DustG)*k; plan.DustB+=(db-plan.DustB)*k;
        }
        // 1.1: one explosion, several smokes. The second tone mixed through the cloud and the plume (rgb) and how much of it
        // (w): sooty fuel smoke carries paler grey combustion smoke and steam, a ground blast brown dust, pale smoke darker
        // residue. pick (0..1) chooses among them per explosion. Zero for the 1.0 look.
        public static Vec4 SmokeMix(FxPlan plan,double pick)
        {
            if(plan==null||plan.Look==null||!plan.Look.Modern) return new Vec4();
            double grey=(plan.SmokeR+plan.SmokeG+plan.SmokeB)/3, share=0.5+0.35*Numbers.Clamp(pick*3%1,0,1);
            // Unburnt nitrogen tetroxide: billows of vivid orange-brown through the cloud (Proton-M, 2013).
            if(plan.Brown>0.3) return new Vec4(0.8,0.42,0.13,Numbers.Clamp(0.55+0.4*plan.Brown,0,0.95));
            if(grey>0.6) return new Vec4(grey*0.62,grey*0.6,grey*0.58,share*0.8);                 // pale smoke: darker residue
            if(plan.Kind==EventKind.GroundImpact&&pick<0.35) return new Vec4(plan.DustR*0.8,plan.DustG*0.75,plan.DustB*0.7,share); // earth
            if(pick<0.7) return new Vec4(0.58,0.57,0.55,share);                                    // grey combustion smoke
            return new Vec4(0.8,0.81,0.83,share*0.8);                                               // steam
        }
        // Rock dust thrown up by ore (0..1), and its colour.
        public static double OreDust(FxPlan plan) { return plan==null||plan.OreKg<=0?0:Numbers.Clamp(plan.OreKg/(plan.OreKg+400),0,1); }
        public static Vec3 OreColour(Vec3 dust) { return new Vec3(dust.X*0.7+0.45*0.3,dust.Y*0.7+0.36*0.3,dust.Z*0.7+0.27*0.3); }
    }
}
