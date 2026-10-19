using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VolumetricExplosionFX.Core;

// The baked fireball simulation (Tools/firesim.py): frames stacked along the depth of a 3D texture.
// RGBA = gas density, temperature, soot, 1. Built from work/firesim/fire_<variant>.bin.
public static class FireSimTexture
{
    public static string AssetPath(string variant) { return "Assets/FireSim/FireSim_"+variant+".asset"; }
    public static Texture3D Ensure(string variant)
    {
        int nx,ny,nz,frames;
        switch(variant)
        {
            case "ground": nx=FireSimTable.GroundNx; ny=FireSimTable.GroundNy; nz=FireSimTable.GroundNz; frames=FireSimTable.GroundFrames; break;
            case "ground2": nx=FireSimTable.Ground2Nx; ny=FireSimTable.Ground2Ny; nz=FireSimTable.Ground2Nz; frames=FireSimTable.Ground2Frames; break;
            case "ground3": nx=FireSimTable.Ground3Nx; ny=FireSimTable.Ground3Ny; nz=FireSimTable.Ground3Nz; frames=FireSimTable.Ground3Frames; break;
            case "ground4": nx=FireSimTable.Ground4Nx; ny=FireSimTable.Ground4Ny; nz=FireSimTable.Ground4Nz; frames=FireSimTable.Ground4Frames; break;
            case "air2": nx=FireSimTable.Air2Nx; ny=FireSimTable.Air2Ny; nz=FireSimTable.Air2Nz; frames=FireSimTable.Air2Frames; break;
            case "nuke": nx=FireSimTable.NukeNx; ny=FireSimTable.NukeNy; nz=FireSimTable.NukeNz; frames=FireSimTable.NukeFrames; break;
            case "nuke2": nx=FireSimTable.Nuke2Nx; ny=FireSimTable.Nuke2Ny; nz=FireSimTable.Nuke2Nz; frames=FireSimTable.Nuke2Frames; break;
            case "nukeair": nx=FireSimTable.NukeairNx; ny=FireSimTable.NukeairNy; nz=FireSimTable.NukeairNz; frames=FireSimTable.NukeairFrames; break;
            default: nx=FireSimTable.AirNx; ny=FireSimTable.AirNy; nz=FireSimTable.AirNz; frames=FireSimTable.AirFrames; break;
        }
        string bin=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","firesim","fire_"+variant+".bin"));
        if(!File.Exists(bin)) throw new Exception("Baked fire simulation missing: "+bin+" (run Tools/firesim.py --bake).");
        byte[] bytes=File.ReadAllBytes(bin);
        int expected=nx*ny*nz*frames*4;
        if(bytes.Length!=expected) throw new Exception("Baked fire simulation size mismatch for "+variant+": "+bytes.Length+" vs "+expected+" (rebake).");
        string stamp="FireSim-"+variant+"-"+Convert.ToBase64String(System.Security.Cryptography.SHA1.Create().ComputeHash(bytes));
        string path=AssetPath(variant);
        var existing=AssetDatabase.LoadAssetAtPath<Texture3D>(path);
        if(existing!=null&&NoiseTexture.Current(path,stamp)) return existing;
        if(!AssetDatabase.IsValidFolder("Assets/FireSim")) AssetDatabase.CreateFolder("Assets","FireSim");
        if(existing!=null) AssetDatabase.DeleteAsset(path);
        var tex=new Texture3D(nx,ny,nz*frames,TextureFormat.RGBA32,false);
        tex.name=stamp; tex.wrapMode=TextureWrapMode.Clamp; tex.filterMode=FilterMode.Bilinear;
        var pixels=new Color32[nx*ny*nz*frames];
        for(int i=0;i<pixels.Length;i++) pixels[i]=new Color32(bytes[4*i],bytes[4*i+1],bytes[4*i+2],bytes[4*i+3]);
        tex.SetPixels32(pixels); tex.Apply(false,false);
        AssetDatabase.CreateAsset(tex,path); AssetDatabase.SaveAssets();
        NoiseTexture.Stamp(path,stamp);
        return AssetDatabase.LoadAssetAtPath<Texture3D>(path);
    }
}
