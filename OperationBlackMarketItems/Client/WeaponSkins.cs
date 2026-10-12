using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace R6PortClient
{
    // one skin item, each weapon has its own base/Glacier textures
    internal static class SkinMotion
    {
        internal const string GlacierId="b12966cb3341df94acd539b6";
        internal static readonly string[] Ids={GlacierId,Plugin.Id("skin.blackice"),Plugin.Id("skin.golddust"),Plugin.Id("skin.loyalty")};
        static readonly HashSet<Texture> PreparedTextures=new HashSet<Texture>();
        static readonly PropertyInfo IgnoreMipLimit=typeof(Texture2D).GetProperty("ignoreMipmapLimit");
        static Texture2D BlackIceFullResolution;
        static bool BlackIceLoadAttempted;
        static void PrepareBlackIce(Material material)
        {
            if(material==null||!material.name.StartsWith("BlackIce",StringComparison.Ordinal))return;
            if(!BlackIceLoadAttempted)
            {
                BlackIceLoadAttempted=true;
                string path=Path.Combine(Plugin.Folder,"textures","blackice_diffuse.png");
                if(File.Exists(path))
                {
                    // original 4K with mipmaps, no upscaling
                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,false);
                    if(ImageConversion.LoadImage(texture,File.ReadAllBytes(path),true))
                    {
                        texture.name="R6_BlackIce_Original4K_FullResolution";
                        texture.wrapMode=material.mainTexture!=null?material.mainTexture.wrapMode:TextureWrapMode.Repeat;
                        BlackIceFullResolution=texture;
                        Plugin.Log.LogInfo("Black Ice full-resolution diffuse: "+texture.width+"x"+texture.height+", mips="+texture.mipmapCount+", format="+texture.format);
                    }
                    else {UnityEngine.Object.Destroy(texture);Plugin.Log.LogWarning("Black Ice full-resolution PNG could not be loaded; retaining bundle map.");}
                }
                else Plugin.Log.LogWarning("Black Ice full-resolution PNG missing; retaining bundle map.");
            }
            if(BlackIceFullResolution!=null)material.mainTexture=BlackIceFullResolution;
        }
        static readonly Dictionary<string,Texture2D> SurfaceMaps=new Dictionary<string,Texture2D>();
        static void PrepareSurface(Material material,string skinId)
        {
            // these maps fit the R4-C atlas, other weapons need their own
            bool black=skinId==Plugin.Id("skin.blackice")&&material.name.StartsWith("BlackIce",StringComparison.Ordinal);
            bool gold=skinId==Plugin.Id("skin.golddust")&&material.name.StartsWith("GoldDust",StringComparison.Ordinal);
            if(!black&&!gold)return;
            string name=black?"blackice_surface.png":"golddust_surface.png";
            if(!SurfaceMaps.TryGetValue(name,out var map))
            {
                string path=Path.Combine(Plugin.Folder,"textures",name);
                if(!File.Exists(path))return;
                map=new Texture2D(2,2,TextureFormat.RGBA32,true,true);
                if(!ImageConversion.LoadImage(map,File.ReadAllBytes(path),true))
                {UnityEngine.Object.Destroy(map);return;}
                map.name="R6_R4C_"+name;SurfaceMaps.Add(name,map);
                Plugin.Log.LogInfo("R4-C surface revision loaded: "+name+" "+map.width+"x"+map.height);
            }
            CollectionSurfaces.UseSurfaceShader(material);
            material.SetTexture("_SpecGlossMap",map);material.EnableKeyword("_SPECGLOSSMAP");
            material.SetColor("_SpecColor",Color.white);material.SetFloat("_GlossMapScale",1);
            material.SetFloat("_BumpScale",black?.45f:1f);
            material.SetFloat("_SpecularHighlights",1);material.SetFloat("_GlossyReflections",1);
        }
        internal static void PrepareTextures(Material material)
        {
            if(material==null)return;
            foreach(string property in material.GetTexturePropertyNames())
            {
                var texture=material.GetTexture(property);
                if(texture==null||!PreparedTextures.Add(texture))continue;
                texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=16;texture.mipMapBias=0;
                if(texture is Texture2D&&IgnoreMipLimit!=null&&IgnoreMipLimit.CanWrite)
                {
                    try{IgnoreMipLimit.SetValue(texture,true);}
                    catch(Exception error){Plugin.Log.LogWarning("R6 map mip-limit override unavailable: "+error.Message);}
                }
                Plugin.Log.LogInfo("R6 skin map "+texture.name+": "+texture.width+"x"+texture.height+", trilinear/aniso16, local mip limit override="+(IgnoreMipLimit!=null));
            }
        }
        internal static void Apply(Transform bone,string skinId)
        {
            var root=bone;while(root.parent!=null&&root.name!="weapon")root=root.parent;
            ApplyVariants(root,skinId);
            foreach(var profile in root.GetComponentsInChildren<Transform>(true))
            {
                if(profile.name!="R6_SkinProfile"&&profile.name!="R6_SkinProfile_Glacier")continue;
                var data=profile.GetComponent<MeshRenderer>();var target=profile.parent.GetComponent<Renderer>();
                if(data==null||target==null)continue;
                var pair=data.sharedMaterials;int index=Array.IndexOf(Ids,skinId)+1;
                if(index>=pair.Length)index=0;
                if(skinId==Plugin.Id("skin.blackice"))PrepareBlackIce(pair[index]);
                PrepareSurface(pair[index],skinId);
                PrepareTextures(pair[index]);target.sharedMaterial=pair[index];
            }
        }
        internal static void ApplyVariants(Transform root,string skinId)
        {
            string token=skinId==GlacierId?"glacier":skinId==Plugin.Id("skin.blackice")?"blackice":skinId==Plugin.Id("skin.golddust")?"golddust":skinId==Plugin.Id("skin.loyalty")?"loyalty":skinId==Plugin.Id("skin.bds")?"bds":skinId==Plugin.Id("skin.proleague_s1")?"proleague_s1":"basic";
            var groups=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="OBM_SkinVariants").ToArray();
            if(groups.Length==0)return;
            var marker=root.Cast<Transform>().FirstOrDefault(t=>t.name.StartsWith("OBM_ActiveSkin_",StringComparison.Ordinal));
            if(marker==null){marker=new GameObject().transform;marker.SetParent(root,false);}marker.name="OBM_ActiveSkin_"+token;
            SetVariants(groups,token);
        }
        static void SetVariants(IEnumerable<Transform> groups,string token)
        {
            foreach(var group in groups)
            {
                string target=group.Find("OBM_Skin_"+token)!=null?token:"basic";
                foreach(Transform child in group)
                {
                    bool selected=child.name=="OBM_Skin_"+target;child.gameObject.SetActive(selected);
                    if(selected)CollectionSurfaces.PrepareVariant(child,target);
                }
            }
        }
        internal static void PrepareVisibleVariants(Transform root)
        {
            foreach(var group in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="OBM_SkinVariants"))
                foreach(Transform child in group)if(child.gameObject.activeSelf)
                    CollectionSurfaces.PrepareVariant(child,child.name.Substring("OBM_Skin_".Length));
        }
        internal static void SyncVariants(Transform bone)
        {
            var root=bone;while(root.parent!=null&&root.name!="weapon")root=root.parent;
            var marker=root.Cast<Transform>().FirstOrDefault(t=>t.name.StartsWith("OBM_ActiveSkin_",StringComparison.Ordinal));
            if(marker==null)return;
            SetVariants(root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="OBM_SkinVariants"),marker.name.Substring("OBM_ActiveSkin_".Length));
        }
    }
    [HarmonyPatch(typeof(GClass768.GClass769),nameof(GClass768.GClass769.RemoveItem))]
    internal static class RemoveWeaponSkin
    {
        static void Postfix(GClass768.GClass769 __instance)
        {
            if(__instance.Bone!=null&&__instance.Bone.name=="mod_tactical_002"&&__instance.Bone.Find("R6_SkinSlot")!=null)
                SkinMotion.Apply(__instance.Bone,null);
        }
    }
}
