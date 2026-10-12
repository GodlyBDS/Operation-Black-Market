using System.Linq;
using System.Threading.Tasks;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace R6PortClient
{
    // Keep the mod_* bone and its item view together, EFT finds sights, reload parts
    // and child slots through that hierarchy.
    internal static class AuthoredSlotSettlement
    {
        static int constructionLogs=24;
        internal static Transform Apply(GClass768.GClass769 binding)
        {
            var bone=binding.Bone;var view=binding.ItemView;
            if(bone==null||view==null)return null;
            var weapon=bone;
            while(weapon.parent!=null&&weapon.name!="weapon")
            {
                // a scope on an adapter goes to the adapter's mod_scope, not the weapon rail
                if(weapon.GetComponent<WeaponModPoolObject>()!=null)return null;
                weapon=weapon.parent;
            }
            if(weapon.name!="weapon")return null;
            var marker=weapon.Find("OBM_ApprovedSocket_"+bone.name);
            if(marker==null)return null;
            if(bone.name=="mod_scope"&&binding.Item.TemplateId.ToString()!=Plugin.Parts[1])
                marker=weapon.Find("OBM_ApprovedNativeScope")??marker;
            if(bone.name=="mod_foregrip"&&binding.Item.TemplateId.ToString()!=Plugin.Parts[2])
                marker=weapon.Find("OBM_ApprovedNativeGrip")??marker;
            if(bone.name=="mod_muzzle"&&binding.Item.TemplateId.ToString()!=Plugin.Parts[3])
                marker=weapon.Find("OBM_ApprovedNativeMuzzle")??marker;
            if(bone.name=="mod_tactical_001")
            {
                int index=System.Array.IndexOf(Plugin.CharmIds,binding.Item.TemplateId.ToString());
                if(index>=0)marker=weapon.Find("OBM_ApprovedCharm_"+new[]{"champion","emberrise","neondawn","shiftingtides"}[index])??marker;
            }
            bool ownMagazine=bone.name=="mod_magazine"&&view.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="OBM_MagazineSourceFrame");
            if(ownMagazine)marker=weapon.Find("OBM_ApprovedOwnMagazine")??marker;
            var nativeRest=bone.name=="mod_magazine"?weapon.Find("OBM_NativeMagazineRest"):null;
            if(nativeRest!=null)
            {
                // keep the donor's animated magazine path for reload and inspect, only offset
                // the idle pose to the saved position instead of pinning the bone every frame
                var pose=bone.localToWorldMatrix*nativeRest.worldToLocalMatrix*marker.localToWorldMatrix;
                bool feedAligned=false;
                if(!ownMagazine)
                {
                    var approvedFeed=weapon.Find("OBM_ApprovedMagazineFeed");
                    var reference=view.GetComponent<NativeMagazineMountReference>()??view.gameObject.AddComponent<NativeMagazineMountReference>();
                    if(approvedFeed!=null&&reference.Resolve(view))
                    {
                        // magazines differ in origin and shape, align them on the feed point, not the pivot.
                        // Saved ammo was rotated for the Siege model. The native cartridge socket uses weapon axes
                        // (magazine +90X and cartridge -90X cancel out), so only take the feed position, not the
                        // cartridge rotation, otherwise the whole magazine flips.
                        var feedFrame=Matrix4x4.TRS(approvedFeed.position,weapon.rotation,Vector3.one);
                        // only the native 416-C magazines sit slightly forward, our own ones are exact
                        var ownerPool=weapon.GetComponentInParent<AssetPoolObject>();
                        if(ownerPool!=null&&ownerPool.ContainerCollectionView!=null&&ownerPool.ContainerCollectionView.ContainerBones
                            .Any(p=>p.Value==binding&&p.Key.ParentItem.TemplateId.ToString()==Plugin.Id("weapon.416-C-CARBINE")))
                            feedFrame=Matrix4x4.Translate(weapon.TransformVector(new Vector3(0,.008f,0)))*feedFrame;
                        pose=bone.localToWorldMatrix*nativeRest.worldToLocalMatrix*feedFrame*reference.FeedInRoot.inverse;
                        feedAligned=true;
                    }
                }
                if(view.parent!=bone)view.SetParent(bone,false);
                view.SetPositionAndRotation(pose.GetColumn(3),pose.rotation);
                var animatedParentScale=bone.lossyScale;var animatedScale=pose.lossyScale;
                view.localScale=new Vector3(animatedScale.x/animatedParentScale.x,animatedScale.y/animatedParentScale.y,animatedScale.z/animatedParentScale.z);
                if(!ownMagazine&&!feedAligned)
                {
                    var nativePlacer=view.GetComponent<ModPlacer>();
                    view.localPosition+=view.localRotation*(nativePlacer!=null?nativePlacer.ModPosition:Vector3.zero);
                    view.localRotation*=Quaternion.Euler(nativePlacer!=null?nativePlacer.ModRotation:new Vector3(90,0,0));
                }
                return marker;
            }
            // runs again after Init/RebindAnimator, the first InsertItem pose doesn't survive init
            bone.SetPositionAndRotation(marker.position,marker.rotation);
            var scale=marker.lossyScale;var parentScale=bone.parent.lossyScale;
            bone.localScale=new Vector3(scale.x/parentScale.x,scale.y/parentScale.y,scale.z/parentScale.z);
            if(view.parent!=bone)view.SetParent(bone,false);
            var placer=view.GetComponent<ModPlacer>();
            bool authoredCharm=bone.name=="mod_tactical_001"&&marker.name.StartsWith("OBM_ApprovedCharm_");
            view.localPosition=ownMagazine||authoredCharm?Vector3.zero:(placer!=null?placer.ModPosition:Vector3.zero);
            view.localRotation=ownMagazine||authoredCharm?Quaternion.identity:Quaternion.Euler(placer!=null?placer.ModRotation:new Vector3(90,0,0));
            view.localScale=ownMagazine||authoredCharm?Vector3.one:(placer!=null?placer.ModScale:Vector3.one);
            if(authoredCharm)
            {
                // the collection editor shows charm meshes with identity child transforms, runtime
                // prefabs still have the R4-C rotation, so undo that before applying the saved pose
                var reference=view.GetComponent<AuthoredCharmReference>()??view.gameObject.AddComponent<AuthoredCharmReference>();
                if(reference.Resolve(view))
                {
                    var inverse=reference.MeshInRoot.inverse;
                    view.localPosition=inverse.GetColumn(3);view.localRotation=inverse.rotation;view.localScale=inverse.lossyScale;
                }
            }
            return marker;
        }
        internal static void Complete(GameObject model)
        {
            if(model==null)return;
            SkinMotion.PrepareVisibleVariants(model.transform);
            foreach(var frame in model.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="OBM_MagazineSourceFrame"))
            {
                var magazine=frame.parent;
                ConfigureMagazinePreview(magazine,frame);
                if(magazine.Find("OBM_AmmoRest_001")!=null)
                    (magazine.GetComponent<AuthoredMagazineAmmo>()??magazine.gameObject.AddComponent<AuthoredMagazineAmmo>()).Bind();
            }
            var pool=model.GetComponent<AssetPoolObject>();
            var collection=pool!=null?pool.ContainerCollectionView:null;
            if(collection==null)return;
            bool authored=false;
            foreach(var binding in collection.ContainerBones.Values)
            {
                // the exported model has the old bone position but no animation to restore the idle pose.
                // Set the reference once on construct/reset, then let the reload animation move the bone.
                if(binding.Bone!=null&&binding.Bone.name=="mod_magazine")
                {
                    var parent=binding.Bone;
                    while(parent.parent!=null&&parent.name!="weapon")parent=parent.parent;
                    var rest=parent.Find("OBM_NativeMagazineRest");
                    if(rest!=null)binding.Bone.SetPositionAndRotation(rest.position,rest.rotation);
                }
                var marker=Apply(binding);authored|=marker!=null;
                if(marker!=null&&constructionLogs>0&&(binding.Bone.name=="mod_magazine"||binding.Bone.name=="mod_scope"))
                {
                    constructionLogs--;
                    Plugin.Log.LogInfo("OBM constructed slot: bone="+binding.Bone.name+", tpl="+binding.Item.TemplateId+", item="+binding.ItemView.name+", parent="+binding.ItemView.parent.name+", marker="+marker.name+", boneInWeapon="+marker.parent.InverseTransformPoint(binding.Bone.position).ToString("F5")+", itemLocalPosition="+binding.ItemView.localPosition.ToString("F5")+", itemLocalRotation="+binding.ItemView.localRotation.ToString("F5"));
                }
            }
            if(!authored)return;
            var settlement=model.GetComponent<AuthoredSlotPose>()??model.AddComponent<AuthoredSlotPose>();
            settlement.Bind(collection);
        }
        static void ConfigureMagazinePreview(Transform magazine,Transform frame)
        {
            var pivot=magazine.GetComponent<PreviewPivot>();if(pivot==null)return;
            var body=frame.GetComponentsInChildren<MeshFilter>(false).Where(f=>f.sharedMesh!=null)
                .OrderByDescending(f=>f.sharedMesh.bounds.size.x*f.sharedMesh.bounds.size.y*f.sharedMesh.bounds.size.z).FirstOrDefault();
            if(body==null)return;
            var size=body.sharedMesh.bounds.size;
            var axes=new[]{Vector3.right,Vector3.up,Vector3.forward};var lengths=new[]{size.x,size.y,size.z};
            int longest=System.Array.IndexOf(lengths,lengths.Max()),thinnest=System.Array.IndexOf(lengths,lengths.Min());
            if(longest==thinnest)return;
            var up=magazine.InverseTransformVector(body.transform.TransformVector(axes[longest])).normalized;
            var normal=magazine.InverseTransformVector(body.transform.TransformVector(axes[thinnest])).normalized;
            var center=magazine.InverseTransformPoint(body.transform.TransformPoint(body.sharedMesh.bounds.center));
            var feed=magazine.Find("OBM_AmmoRest_001");
            if(feed!=null&&Vector3.Dot(feed.localPosition-center,up)<0)up=-up;
            // preview rotation is separate from the equipped placement, the STANAG camera rotation
            // plus -90X left edited mags tilted
            pivot.Icon.rotation=Quaternion.Inverse(Quaternion.LookRotation(normal,up));
            pivot.pivotPosition=center;pivot.scale=Vector3.one;
        }
    }
    public class AuthoredCharmReference : MonoBehaviour
    {
        internal Matrix4x4 MeshInRoot;bool resolved;
        internal bool Resolve(Transform root)
        {
            if(resolved)return true;
            var mesh=root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f=>f.sharedMesh!=null);
            if(mesh==null)return false;
            MeshInRoot=root.worldToLocalMatrix*mesh.transform.localToWorldMatrix;resolved=true;return true;
        }
    }
    public class NativeMagazineMountReference : MonoBehaviour
    {
        internal Matrix4x4 FeedInRoot;bool resolved;
        internal bool Resolve(Transform root)
        {
            if(resolved)return true;
            var feed=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="patron_001");
            if(feed==null)return false;
            FeedInRoot=root.worldToLocalMatrix*feed.localToWorldMatrix;resolved=true;return true;
        }
    }
    public class AuthoredMagazineAmmo : MonoBehaviour
    {
        Transform[] sockets,anchors;
        internal void Bind()
        {
            sockets=GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("patron_")&&transform.Find("OBM_AmmoRest_"+t.name.Substring(7))!=null).ToArray();
            anchors=sockets.Select(t=>transform.Find("OBM_AmmoRest_"+t.name.Substring(7))).ToArray();
            LateUpdate();
        }
        void LateUpdate()
        {
            if(sockets==null)return;
            // keep native ammo creation/visibility, the STANAG fill animation must not move our feed points
            for(int i=0;i<sockets.Length;i++)if(sockets[i]!=null&&anchors[i]!=null)
                sockets[i].SetPositionAndRotation(anchors[i].position,anchors[i].rotation);
        }
    }
    public class AuthoredSlotPose : MonoBehaviour
    {
        GClass768 collection;int frames;static int remaining=36;
        internal void Bind(GClass768 value){collection=value;frames=0;}
        void LateUpdate()
        {
            if(collection==null)return;
            foreach(var binding in collection.ContainerBones.Values)
            {
                var marker=AuthoredSlotSettlement.Apply(binding);
                if(frames==89&&marker!=null&&remaining>0)
                {
                    remaining--;
                    Plugin.Log.LogInfo("OBM actual slot: bone="+binding.Bone.name+", tpl="+binding.Item.TemplateId+", item="+binding.ItemView.name+", parent="+binding.ItemView.parent.name+", marker="+marker.name+", rootPosition="+binding.ItemView.position.ToString("F5")+", markerPosition="+marker.position.ToString("F5")+", localRotation="+binding.ItemView.localRotation.ToString("F5"));
                }
            }
            frames++;
        }
        void OnDisable(){collection=null;}
    }
    [HarmonyPatch(typeof(GClass2091),nameof(GClass2091.CacheDataTransforms))]
    internal static class MigrateMagazineReferenceNames
    {
        static int logged;
        static void Prefix(GClass2091 __instance)
        {
            if(__instance.GameObject_0==null)return;
            int renamed=0;
            // EFT looks for Contains("patron_") and then int.Parse, so rename before the native cache fills
            foreach(var node in __instance.GameObject_0.GetComponentsInChildren<Transform>(true))
                if(node.name.StartsWith("OBM_AmmoRest_patron_",System.StringComparison.Ordinal))
                {node.name="OBM_AmmoRest_"+node.name.Substring("OBM_AmmoRest_patron_".Length);renamed++;}
            if(renamed>0&&logged++<6)Plugin.Log.LogInfo("OBM migrated magazine references before native socket scan: "+renamed);
        }
    }
    [HarmonyPatch(typeof(PoolManagerClass),nameof(PoolManagerClass.CreateItemAsync))]
    internal static class CompleteAuthoredItem
    {
        static void Postfix(ref Task<GameObject> __result)
        {
            __result=Complete(__result);
        }
        static async Task<GameObject> Complete(Task<GameObject> construction)
        {
            var model=await construction;
            AuthoredSlotSettlement.Complete(model);
            return model;
        }
    }
    [HarmonyPatch(typeof(WeaponPrefab),nameof(WeaponPrefab.ResetStatesToDefault))]
    internal static class SettleAfterNativeReset
    {
        static void Postfix(WeaponPrefab __instance)
        {
            AuthoredSlotSettlement.Complete(__instance.gameObject);
        }
    }
}
