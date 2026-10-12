using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace R6PortClient
{
    internal static class CollectionSurfaces
    {
        static readonly Dictionary<string,string> AtlasFiles=new Dictionary<string,string>();
        static readonly Dictionary<string,Texture2D> Maps=new Dictionary<string,Texture2D>();
        static readonly Dictionary<string,Material> Materials=new Dictionary<string,Material>();
        static readonly HashSet<Material> Prepared=new HashSet<Material>();
        static bool loaded;static Shader surfaceShader;
        static string Clean(string name)=>name.Replace(" (Instance)","").Replace(" (Clone)","");
        static void Load()
        {
            if(loaded)return;loaded=true;
            string folder=Path.Combine(Plugin.Folder,"textures","collection");
            string manifest=Path.Combine(folder,"surfaces.tsv");
            if(File.Exists(manifest))foreach(var line in File.ReadAllLines(manifest))
            {
                var values=line.Split('\t');if(values.Length!=4)continue;
                AtlasFiles[values[0]+"|"+values[1]+"|"+values[2]]=Path.Combine(folder,values[3]);
            }
            string bundlePath=Path.Combine(Plugin.Folder,"materials","obm_surfaces.bundle");
            if(File.Exists(bundlePath))
            {
                var bundle=AssetBundle.LoadFromFile(bundlePath);
                if(bundle!=null)
                {
                    var reference=bundle.LoadAsset<Material>("obm_surface_normal");
                    if(reference!=null)surfaceShader=reference.shader;
                    bundle.Unload(false);
                }
            }
            Plugin.Log.LogInfo("OBM collection surfaces: "+AtlasFiles.Count+" atlas mappings; specular shader variants="+(surfaceShader!=null));
        }
        internal static Material Prepare(Material original,string skin)
        {
            if(original==null)return null;
            SkinMotion.PrepareTextures(original);
            if(Prepared.Contains(original))return original;
            Load();
            var diffuse=original.mainTexture;if(diffuse==null)return original;
            string atlas=skin+"|"+Clean(original.name)+"|"+Clean(diffuse.name);
            if(!AtlasFiles.TryGetValue(atlas,out var file))return original;
            string cache=original.GetInstanceID()+"|"+skin;
            if(Materials.TryGetValue(cache,out var existing))return existing;
            if(!Maps.TryGetValue(file,out var map))
            {
                if(!File.Exists(file))return original;
                map=new Texture2D(2,2,TextureFormat.RGBA32,true,true);
                if(!ImageConversion.LoadImage(map,File.ReadAllBytes(file),true)){UnityEngine.Object.Destroy(map);return original;}
                map.name="OBM_Surface_"+Path.GetFileNameWithoutExtension(file);map.wrapMode=diffuse.wrapMode;
                map.filterMode=FilterMode.Trilinear;map.anisoLevel=16;Maps[file]=map;
            }
            var result=new Material(original){name=original.name};
            if(surfaceShader!=null)result.shader=surfaceShader;
            result.SetTexture("_SpecGlossMap",map);result.EnableKeyword("_SPECGLOSSMAP");
            result.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            result.SetColor("_SpecColor",Color.white);result.SetFloat("_GlossMapScale",1);
            result.SetFloat("_SpecularHighlights",1);result.SetFloat("_GlossyReflections",1);
            result.SetFloat("_BumpScale",skin=="blackice"?.45f:1f);
            if(result.GetTexture("_BumpMap")!=null)result.EnableKeyword("_NORMALMAP");else result.DisableKeyword("_NORMALMAP");
            SkinMotion.PrepareTextures(result);Prepared.Add(result);Materials[cache]=result;
            Plugin.Log.LogInfo("OBM surface applied: "+atlas+"; "+map.width+"x"+map.height);
            return result;
        }
        internal static void UseSurfaceShader(Material material)
        {
            Load();if(surfaceShader!=null)material.shader=surfaceShader;
        }
        internal static void PrepareVariant(Transform variant,string token)
        {
            foreach(var renderer in variant.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;bool changed=false;
                for(int i=0;i<materials.Length;i++){var next=Prepare(materials[i],token);changed|=next!=materials[i];materials[i]=next;}
                if(changed)renderer.sharedMaterials=materials;
            }
        }
    }
}
