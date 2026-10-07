using System;
namespace VolumetricExplosionFX.Core
{
    public static class BodyLook
    {
        public static Vec3 Dust(string body)
        {
            switch(body)
            {
                case "Kerbin": return new Vec3(0.62,0.55,0.43);
                case "Mun": return new Vec3(0.6,0.6,0.58);
                case "Minmus": return new Vec3(0.76,0.87,0.83);
                case "Duna": return new Vec3(0.68,0.39,0.25);
                case "Ike": return new Vec3(0.5,0.5,0.5);
                case "Eve": return new Vec3(0.47,0.37,0.53);
                case "Gilly": return new Vec3(0.56,0.46,0.4);
                case "Moho": return new Vec3(0.5,0.43,0.38);
                case "Dres": return new Vec3(0.58,0.56,0.53);
                case "Laythe": return new Vec3(0.66,0.6,0.5);
                case "Vall": return new Vec3(0.78,0.83,0.88);
                case "Tylo": return new Vec3(0.72,0.7,0.64);
                case "Bop": return new Vec3(0.48,0.41,0.35);
                case "Pol": return new Vec3(0.78,0.72,0.48);
                case "Eeloo": return new Vec3(0.85,0.86,0.85);
                default: return new Vec3(0.62,0.56,0.46);
            }
        }
    }
}
