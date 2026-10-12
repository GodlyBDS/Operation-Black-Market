using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;

namespace R6PortClient
{
    [BepInPlugin("private.r6port.client","Operation Black Market Items","0.1.1")]
    public class Plugin : BaseUnityPlugin
    {
        internal static string Folder;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static readonly Dictionary<string,Sprite> Icons=new Dictionary<string,Sprite>();
        internal static readonly string[] Parts={"d903f4d2664e5e588c007acd",Id("attachment.r4c.scope"),Id("attachment.r4c.grip"),Id("attachment.r4c.muzzle")};
        internal static readonly string[] CharmIds={Id("charm.champion"),Id("charm.emberrise"),Id("charm.neondawn"),Id("charm.shiftingtides")};
        internal static readonly string[] CharmIcons={"charm","charm_emberrise","charm_neondawn","charm_shiftingtides"};
        internal static string Id(string name)
        {
            using(var sha=System.Security.Cryptography.SHA256.Create())return string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("godly.r6port."+name)).Select(b=>b.ToString("x2"))).Substring(0,24);
        }
        void Awake()
        {
            Folder=Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            Log=Logger;
            new Harmony("private.r6port.client").PatchAll();
            Logger.LogInfo("Operation Black Market Items // RAINBOW arsenal support online. Operator Ash ready.");
        }
        void Update()
        {
            Shader.SetGlobalFloat("_R6CharmEditorPreview",0);
            Shader.SetGlobalFloat("_R6CharmRuntimeEnabled",1);
            Shader.SetGlobalFloat("_R6CharmRuntimeTime",Time.unscaledTime);
        }
        internal static void InitializeWeapon(GameObject model)
        {
            var authoredWeapon=model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="weapon"&&t.Find("OBM_ApprovedSocket_mod_scope")!=null);
            if(authoredWeapon!=null&&authoredWeapon.GetComponent<AlignmentFrameDiagnostics>()==null)authoredWeapon.gameObject.AddComponent<AlignmentFrameDiagnostics>();
            foreach(var node in model.GetComponentsInChildren<Transform>(true))
            {
                // BOSG: stacked chambers on the MR43 rig
                if(node.name=="OBM_StackedChambers"&&node.GetComponent<StackedChambers>()==null)node.gameObject.AddComponent<StackedChambers>().Bind();
                if((node.name.Contains("Befestigungsoese")||node.name=="OBM_CharmHolder")&&node.GetComponent<CharmLinkMotion>()==null)
                    node.gameObject.AddComponent<CharmLinkMotion>();
                if(node.name.StartsWith("Charm Y5S1 Champion",StringComparison.Ordinal)||node.name.StartsWith("R6_Champion_",StringComparison.Ordinal))
                {
                    var motion=node.GetComponent<CharmMotion>()??node.gameObject.AddComponent<CharmMotion>();
                    motion.Rebind();
                }
            }
        }
        internal static Sprite Icon(string name,Item item,int scaleFactor)
        {
            var pixels=ItemViewFactory.GetCellPixelSize(item.CalculateCellSize());
            string cacheKey=name+"_"+pixels.X+"x"+pixels.Y+"_"+scaleFactor;
            if(Icons.TryGetValue(cacheKey,out var found))return found;
            var path=Path.Combine(Folder,"icons",name+".png");if(!File.Exists(path))return null;
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path)))return null;
            var data=texture.GetPixels32();int left=texture.width,right=0,bottom=texture.height,top=0;
            for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)if(data[y*texture.width+x].a>8){left=Math.Min(left,x);right=Math.Max(right,x);bottom=Math.Min(bottom,y);top=Math.Max(top,y);}
            if(left>right)return null;
            var rect=new Rect(left,bottom,right-left+1,top-bottom+1);
            float ratio=Math.Max(rect.width/Math.Max(1,(pixels.X-6)*scaleFactor),rect.height/Math.Max(1,(pixels.Y-6)*scaleFactor));
            found=Sprite.Create(texture,rect,new Vector2(.5f,.5f),100*ratio);
            Icons[cacheKey]=found;return found;
        }
    }
    // spring is visual only, so no physics forces on the player/weapon
    public class CharmMotion : MonoBehaviour
    {
        Quaternion rest,lastRotation;Vector3 localRest,motionRest,lastPosition,lastVelocity;Vector2 angle,velocity;bool ready;
        MeshRenderer body;Transform[] nodes;Vector3[] corners;float[] sideSurface;Vector2 surfaceMin,surfaceStep;bool contactReady;int lastLayer=-1;bool initialized;
        const int SurfaceSize=32;
        Transform link;Vector3 linkPoint;
        void Awake()
        {
            rest=transform.localRotation;localRest=motionRest=transform.localPosition;initialized=true;
            nodes=GetComponentsInChildren<Transform>(true);
            var owner=transform.parent;
            while(owner.parent!=null&&owner.name!="weapon")owner=owner.parent;
            body=owner.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r=>r.name.StartsWith("R4-C Glacier",StringComparison.Ordinal));
            link=owner.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="OBM_CharmHolder")
                ??owner.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.Contains("Befestigungsoese"));
            if(link!=null)linkPoint=link.InverseTransformPoint(transform.position);
            var own=GetComponent<MeshFilter>();var gun=body!=null?body.GetComponent<MeshFilter>():null;
            if(own==null||gun==null)return;
            if(!own.sharedMesh.isReadable||!gun.sharedMesh.isReadable)return;
            var charmVertices=own.sharedMesh.vertices;
            int count=Math.Min(128,charmVertices.Length);corners=new Vector3[count];
            for(int i=0;i<count;i++)corners[i]=charmVertices[i*charmVertices.Length/count];
            surfaceMin=new Vector2(localRest.y-.12f,localRest.z-.12f);
            surfaceStep=new Vector2(.24f/(SurfaceSize-1),.24f/(SurfaceSize-1));
            sideSurface=new float[SurfaceSize*SurfaceSize];for(int i=0;i<sideSurface.Length;i++)sideSurface[i]=float.PositiveInfinity;
            var vertices=gun.sharedMesh.vertices;var matrix=transform.parent.worldToLocalMatrix*body.transform.localToWorldMatrix;
            for(int i=0;i<vertices.Length;i++)vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);
            var triangles=gun.sharedMesh.triangles;
            // sample the receiver surface once, per frame only a few lookups
            for(int i=0;i<triangles.Length;i+=3)BakeTriangle(vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]]);
            contactReady=true;
            float penetration=Penetration(Vector2.zero);
            if(penetration>0)localRest.x-=penetration;
            motionRest=localRest;
            if(link!=null)linkPoint=link.InverseTransformPoint(transform.parent.TransformPoint(localRest));
        }
        public void Rebind()
        {
            // pooled charm views can move to another weapon, reset the spring first
            OnDisable();contactReady=false;body=null;link=null;
            Awake();OnEnable();
            foreach(var r in GetComponentsInChildren<MeshRenderer>(true))r.enabled=true;
            var root=transform.parent;
            if(root!=null){var lod=root.GetComponent<LODGroup>();if(lod!=null)lod.ForceLOD(0);}
        }
        void BakeTriangle(Vector3 a,Vector3 b,Vector3 c)
        {
            float det=(b.z-c.z)*(a.y-c.y)+(c.y-b.y)*(a.z-c.z);if(Mathf.Abs(det)<1e-10f)return;
            int y0=Math.Max(0,Mathf.FloorToInt((Mathf.Min(a.y,Mathf.Min(b.y,c.y))-surfaceMin.x)/surfaceStep.x));
            int y1=Math.Min(SurfaceSize-1,Mathf.CeilToInt((Mathf.Max(a.y,Mathf.Max(b.y,c.y))-surfaceMin.x)/surfaceStep.x));
            int z0=Math.Max(0,Mathf.FloorToInt((Mathf.Min(a.z,Mathf.Min(b.z,c.z))-surfaceMin.y)/surfaceStep.y));
            int z1=Math.Min(SurfaceSize-1,Mathf.CeilToInt((Mathf.Max(a.z,Mathf.Max(b.z,c.z))-surfaceMin.y)/surfaceStep.y));
            for(int y=y0;y<=y1;y++)for(int z=z0;z<=z1;z++)
            {
                float py=surfaceMin.x+y*surfaceStep.x,pz=surfaceMin.y+z*surfaceStep.y;
                float u=((b.z-c.z)*(py-c.y)+(c.y-b.y)*(pz-c.z))/det;
                float v=((c.z-a.z)*(py-c.y)+(a.y-c.y)*(pz-c.z))/det;
                if(u<0||v<0||u+v>1)continue;
                int index=y*SurfaceSize+z;sideSurface[index]=Mathf.Min(sideSurface[index],a.x*u+b.x*v+c.x*(1-u-v));
            }
        }
        float Penetration(Vector2 candidate)
        {
            if(!contactReady)return 0;var rotation=rest*Quaternion.Euler(candidate.x,0,candidate.y);float penetration=0;
            foreach(var p in corners)
            {
                var point=motionRest+rotation*Vector3.Scale(p,transform.localScale);
                int y=Mathf.RoundToInt((point.y-surfaceMin.x)/surfaceStep.x),z=Mathf.RoundToInt((point.z-surfaceMin.y)/surfaceStep.y);
                if(y<0||z<0||y>=SurfaceSize||z>=SurfaceSize)continue;
                float surface=sideSurface[y*SurfaceSize+z];if(float.IsPositiveInfinity(surface))continue;
                penetration=Mathf.Max(penetration,point.x-surface+.0015f);
            }
            return penetration;
        }
        bool Intersects(Vector2 candidate)=>Penetration(candidate)>.0001f;
        void OnEnable(){if(!initialized)Awake();angle=velocity=Vector2.zero;transform.localRotation=rest;transform.localPosition=localRest;lastPosition=transform.parent.position;lastRotation=transform.parent.rotation;lastVelocity=Vector3.zero;ready=false;lastLayer=-1;}
        void OnDisable(){if(initialized){transform.localRotation=rest;transform.localPosition=localRest;}}
        void LateUpdate()
        {
            var parent=transform.parent;if(parent==null)return;float dt=Mathf.Min(Time.deltaTime,.033f);if(dt<=0)return;
            motionRest=link!=null?parent.InverseTransformPoint(link.TransformPoint(linkPoint)):localRest;
            if(body!=null&&lastLayer!=body.gameObject.layer){lastLayer=body.gameObject.layer;foreach(var node in nodes)node.gameObject.layer=lastLayer;}
            var worldVelocity=(parent.position-lastPosition)/dt;lastPosition=parent.position;
            var rotationDelta=(Quaternion.Inverse(lastRotation)*parent.rotation).eulerAngles;lastRotation=parent.rotation;
            rotationDelta=new Vector3(Mathf.DeltaAngle(0,rotationDelta.x),Mathf.DeltaAngle(0,rotationDelta.y),Mathf.DeltaAngle(0,rotationDelta.z));
            if(!ready){lastVelocity=worldVelocity;ready=true;return;}
            var acceleration=parent.InverseTransformDirection((worldVelocity-lastVelocity)/dt);lastVelocity=worldVelocity;
            var force=new Vector2(-acceleration.z,acceleration.x)*.18f;
            force+=new Vector2(-rotationDelta.x,rotationDelta.y+rotationDelta.z)*(6f/dt);
            velocity+=(Vector2.ClampMagnitude(force,450)-angle*28-velocity*3.5f)*dt;
            var candidate=Vector2.ClampMagnitude(angle+velocity*dt,45);
            if(Intersects(candidate))
            {
                float low=0,high=1;for(int i=0;i<8;i++){float mid=(low+high)*.5f;if(Intersects(Vector2.Lerp(angle,candidate,mid)))high=mid;else low=mid;}
                candidate=Vector2.Lerp(angle,candidate,low);velocity*=-.25f;
            }
            angle=candidate;
            transform.localPosition=motionRest;
            transform.localRotation=rest*Quaternion.Euler(angle.x,0,angle.y);
        }
    }
    [HarmonyPatch(typeof(GClass768),MethodType.Constructor,new[]{typeof(EFT.AssetsManager.AssetPoolObject),typeof(GameObject)})]
    static class WeaponSpawnPatch
    {
        static void Postfix(GameObject gameObject){Plugin.InitializeWeapon(gameObject);}
    }
    [HarmonyPatch(typeof(GClass928),nameof(GClass928.GetItemHash))]
    static class IconVersionPatch
    {
        static readonly HashSet<string> Collection=new HashSet<string>(new[]{"416-C-CARBINE","556XI","89QCB","9x19VSN","AK-12","BERYL762","BOSG122Name","C8-SFW","Enfield-L85A2","F2","JS9","K1A1","M14Name","M590A1","MAC11","MAC38Name","MP5","MP7","PM9","VEC45"}.SelectMany(k=>new[]{Plugin.Id("weapon."+k),Plugin.Id("magazine."+k)}));
        static void Postfix(Item item,ref int __result){var id=item.TemplateId.ToString();if(id=="d5f8cd3ff7753fb324c2b2b2"||Plugin.CharmIds.Contains(id)||SkinMotion.Ids.Contains(id)||Collection.Contains(id)||id==Plugin.Id("skin.bds")||id==Plugin.Id("skin.proleague_s1"))__result^=0x06421017;}
    }
    [HarmonyPatch(typeof(ItemViewFactory),nameof(ItemViewFactory.LoadItemIcon))]
    static class IconPatch
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Item item,int scaleFactor,ref GClass929 __result)
        {
            string name=null;var template=item.TemplateId.ToString();
            if(template=="d5f8cd3ff7753fb324c2b2b2" && item is Weapon weapon)
            {
                var mods=weapon.AllSlots.Where(s=>s.ID.StartsWith("mod_",StringComparison.Ordinal)&&s.ContainedItem!=null).Select(s=>s.ContainedItem.TemplateId.ToString()).ToArray();
                // native magazines and other attachments use the game's renderer
                if(mods.Any(m=>!Plugin.Parts.Contains(m)))return;
                int mask=0;for(int i=0;i<Plugin.Parts.Length;i++)if(mods.Contains(Plugin.Parts[i]))mask|=1<<i;
                name="weapon_"+mask;
            }
            else if(Plugin.CharmIds.Contains(template))name=Plugin.CharmIcons[Array.IndexOf(Plugin.CharmIds,template)];
            else if(SkinMotion.Ids.Contains(template))name=new[]{"skin_glacier","skin_blackice","skin_golddust","skin_loyalty"}[Array.IndexOf(SkinMotion.Ids,template)];
            else if(template==Plugin.Id("skin.bds"))name="skin_bds";
            else if(template==Plugin.Id("skin.proleague_s1"))name="skin_proleague_s1";
            else {int index=Array.IndexOf(Plugin.Parts,template);if(index>=0)name=new[]{"magazine","scope","grip","muzzle"}[index];}
            if(name==null)return;var icon=Plugin.Icon(name,item,scaleFactor);if(icon==null)return;
            __result=new GClass929(name.GetHashCode()){Sprite=icon};
        }
    }
    [HarmonyPatch(typeof(GClass768.GClass769),nameof(GClass768.GClass769.InsertItem))]
    static class NativeMagazinePatch
    {
        static int diagnostics;
        static void Postfix(GClass768.GClass769 __instance,Item item,GameObject itemView)
        {
            if(item.TemplateId.ToString()==Plugin.Id("support.bosg.barrel"))foreach(var renderer in itemView.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            // our mounts use the weapon frame everywhere (icons, inspect, loot, hands), native bone curves stay
            var approvedMount=AuthoredSlotSettlement.Apply(__instance);
            SkinMotion.SyncVariants(__instance.Bone);
            if(__instance.Bone.name=="mod_tactical_002"&&__instance.Bone.Find("R6_SkinSlot")!=null)
            {
                foreach(var renderer in itemView.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
                SkinMotion.Apply(__instance.Bone,item.TemplateId.ToString());
            }
            if(__instance.Bone.name=="mod_tactical_001")Plugin.InitializeWeapon(itemView);
            if(__instance.Bone.name=="mod_muzzle"&&item.TemplateId.ToString()!=Plugin.Parts[3])
            {
                var muzzleMount=__instance.Bone.Find("R6_NativeMuzzleMount");
                if(approvedMount==null&&muzzleMount!=null)itemView.transform.localPosition+=muzzleMount.localPosition;
            }
            if(__instance.Bone.name=="mod_scope"&&item.TemplateId.ToString()!=Plugin.Parts[1])
            {
                var opticMount=__instance.Bone.Find("R6_NativeOpticMount");
                if(approvedMount==null&&opticMount!=null)itemView.transform.localPosition+=opticMount.localPosition;
            }
            if(__instance.Bone.name=="mod_foregrip"&&item.TemplateId.ToString()!=Plugin.Parts[2])
            {
                var gripMount=__instance.Bone.Find("R6_NativeGripMount");
                if(approvedMount==null&&gripMount!=null)
                {
                    // the old R4-C marker used the RVG grip tip as contact with identity rail axes. Use the same
                    // 180Y frame and rail contact as the collection, without rotating our own Siege grip.
                    var underrail=Quaternion.Euler(0,180,0);
                    itemView.transform.localPosition=gripMount.localPosition+new Vector3(.000129f,.000055f,.075579f)+underrail*itemView.transform.localPosition;
                    itemView.transform.localRotation=underrail*itemView.transform.localRotation;
                }
            }
            if(!(item is MagazineItemClass)||item.TemplateId.ToString()==Plugin.Parts[0])return;
            if(approvedMount!=null)return; // no idle offset on our own mounts
            var approvedRotation=itemView.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="OBM_MagazineMountRotation");
            var approvedOffset=itemView.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="OBM_MagazineMountPosition");
            if(approvedRotation!=null&&approvedOffset!=null)
            {
                var weaponFrame=__instance.Bone;while(weaponFrame.parent!=null&&weaponFrame.name!="weapon")weaponFrame=weaponFrame.parent;
                itemView.transform.rotation=weaponFrame.rotation*approvedRotation.localRotation;
                itemView.transform.position=__instance.Bone.position+weaponFrame.TransformVector(approvedOffset.localPosition);
                return;
            }
            // CurrentAddress can be unset while a pooled view is built, the marker identifies the R4-C
            var mount=__instance.Bone.Find("R6_NativeMagazineMount");if(mount!=null)
            {
                itemView.transform.localPosition+=mount.localPosition;
                // the marker already subtracts the feed lip offset, don't add another shift
                var frame=__instance.Bone;while(frame.parent!=null&&frame.name!="weapon")frame=frame.parent;
                if(diagnostics++<8)Plugin.Log.LogInfo("R4-C native magazine placed: tpl="+item.TemplateId+", bone="+__instance.Bone.name+", weaponFrame="+frame.name+", position="+itemView.transform.localPosition);
            }
        }
    }
    [HarmonyPatch(typeof(EFT.CameraControl.OpticSight),nameof(EFT.CameraControl.OpticSight.OnEnable))]
    static class ScopeDiagnostics
    {
        static int remaining=8;
        static void Postfix(EFT.CameraControl.OpticSight __instance)
        {
            if(remaining<=0||!__instance.name.StartsWith("scope_r4c",StringComparison.Ordinal))return;
            remaining--;var lens=__instance.LensRenderer;var camera=CameraClass.Instance?.Camera;
            Plugin.Log.LogInfo("R4-C optic enabled: shader="+(lens!=null?lens.sharedMaterial.shader.name:"missing")+", lens="+(lens!=null?lens.transform.position.ToString():"missing")+", viewport="+(camera!=null&&lens!=null?camera.WorldToViewportPoint(lens.transform.position).ToString():"no camera")+", cameraData="+(__instance.CameraData!=null)+", scopeData="+(__instance.ScopeData!=null));
        }
    }
    [HarmonyPatch(typeof(GClass926),nameof(GClass926.PoseModelByPivot))]
    static class DynamicIconPatch
    {
        static bool IsCollection(GameObject model)=>model.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="OBM_SkinVariants");
        static void Postfix(GameObject model,Camera camera,float cameraAspect,PreviewPivot.IconSettings settings)
        {
            bool collection=IsCollection(model);
            var visuals=model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="R4C_Visuals");if(visuals==null&&!collection)return;
            var neutral=model.GetComponent<NeutralIconMaterials>()??model.AddComponent<NeutralIconMaterials>();neutral.Apply(model.transform);
            if(collection)
            {
                // hidden finish variants, disabled effect quads and the skin item must not change the icon framing
                var visible=model.GetComponentsInChildren<Renderer>(false).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.bounds.size.sqrMagnitude>0).ToArray();
                if(visible.Length==0)return;
                var bounds=visible[0].bounds;foreach(var r in visible.Skip(1))bounds.Encapsulate(r.bounds);
                var position=model.transform.position-bounds.center;position.y+=camera.transform.position.y;
                if(settings.hasOffset)position+=settings.position;model.transform.position=position;
                camera.orthographic=true;
                camera.orthographicSize=Mathf.Max(bounds.extents.y,bounds.extents.x/Mathf.Max(.01f,cameraAspect))/Mathf.Max(.01f,settings.boundsScale);
            }
        }
    }
    public class NeutralIconMaterials : MonoBehaviour
    {
        readonly Dictionary<Renderer,Material[]> original=new Dictionary<Renderer,Material[]>();
        readonly List<Material> temporary=new List<Material>();
        public void Apply(Transform visuals)
        {
            Restore();var shader=Shader.Find("Unlit/Texture");if(shader==null)return;
            foreach(var renderer in visuals.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool collection=false;for(var parent=renderer.transform.parent;parent!=null;parent=parent.parent)if(parent.name=="OBM_SkinVariants"){collection=true;break;}
                if(!renderer.name.StartsWith("R4-C",StringComparison.Ordinal)&&!collection)continue;
                var mats=renderer.sharedMaterials;original[renderer]=mats;
                renderer.sharedMaterials=mats.Select(m=>
                {
                    if(m==null||m.mainTexture==null)return m;
                    var copy=new Material(shader){mainTexture=m.mainTexture,mainTextureScale=m.mainTextureScale,mainTextureOffset=m.mainTextureOffset};
                    temporary.Add(copy);return copy;
                }).ToArray();
            }
        }
        void Restore(){foreach(var pair in original)if(pair.Key!=null)pair.Key.sharedMaterials=pair.Value;original.Clear();foreach(var m in temporary)Destroy(m);temporary.Clear();}
        void OnDisable(){Restore();}void OnDestroy(){Restore();}
    }
}
