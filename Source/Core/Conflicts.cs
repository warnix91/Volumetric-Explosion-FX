using System;
using System.Collections.Generic;
namespace VolumetricExplosionFX.Core
{
    // Other mods that also replace the game's explosions: with both installed every blast is drawn twice.
    public static class Conflicts
    {
        // Assembly or GameData folder name, and the name players know it by.
        static readonly string[,] Known={ {"VolumetricExplosions","Volumetric Explosions"} };
        public static string Find(IEnumerable<string> assemblies,IEnumerable<string> folders)
        {
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(assemblies!=null) foreach(string a in assemblies) if(a!=null) seen.Add(a);
            if(folders!=null) foreach(string f in folders) if(f!=null) seen.Add(f);
            for(int i=0;i<Known.GetLength(0);i++) if(seen.Contains(Known[i,0])) return Known[i,1];
            return null;
        }
    }
}
