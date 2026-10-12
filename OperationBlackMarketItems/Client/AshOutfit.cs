using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.IO;
using System.Text;
using System.Globalization;
using UnityEngine;
using EFT;
using HarmonyLib;

namespace R6PortClient
{
    internal static class AshOutfit
    {
        internal static readonly string Body=Plugin.Id("operator.ash.body");
        internal static readonly string Feet=Plugin.Id("operator.ash.feet");
        internal static readonly string Head=Plugin.Id("operator.ash.head");
        internal static readonly string Hands=Plugin.Id("operator.ash.hands");

        internal static GClass2197 VisualParts(GClass2197 customization)
        {
            if(customization==null||!customization.TryGetValue(EBodyModelPart.Body,out var body)||body.ToString()!=Body)return customization;
            // work on a copy so the normal head/legs/voice selection comes back when Ash is removed
            var parts=new GClass2197(customization);
            parts[EBodyModelPart.Feet]=Feet;
            parts[EBodyModelPart.Head]=Head;
            parts[EBodyModelPart.Hands]=Hands;
            return parts;
        }
    }

    [HarmonyPatch(typeof(GClass2197),nameof(GClass2197.GetPartsWithVisual))]
    internal static class AshVisualResourcesPatch
    {
        [HarmonyPostfix]
        static void Postfix(ref GClass2197 __result){__result=AshOutfit.VisualParts(__result);}
    }

    [HarmonyPatch]
    internal static class AshPlayerBodyPatch
    {
        static IEnumerable<MethodBase> TargetMethods()=>typeof(PlayerBody).GetMethods().Where(m=>m.Name=="Init"&&m.GetParameters().Length>0&&m.GetParameters()[0].ParameterType==typeof(GClass2197));
        [HarmonyPrefix]
        static void Prefix(ref GClass2197 customization){customization=AshOutfit.VisualParts(customization);}
        [HarmonyPostfix]
        static void Postfix(PlayerBody __instance,GClass2197 customization)
        {
            if(customization!=null&&customization.TryGetValue(EBodyModelPart.Body,out var body)&&body.ToString()==AshOutfit.Body)
                (__instance.GetComponent<AshPoseDiagnostics>()??__instance.gameObject.AddComponent<AshPoseDiagnostics>()).Capture(__instance);
        }
    }
    // store the animated skeleton so arm fixes can use the pose with a weapon in hands
    internal sealed class AshPoseDiagnostics : MonoBehaviour
    {
        Coroutine capture;
        internal void Capture(PlayerBody body){if(capture!=null)StopCoroutine(capture);capture=StartCoroutine(Record(body));}
        static string Quote(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
        static string Matrix(Matrix4x4 value)=>"["+string.Join(",",Enumerable.Range(0,16).Select(i=>value[i/4,i%4].ToString("R",CultureInfo.InvariantCulture)))+"]";
        IEnumerator Record(PlayerBody body)
        {
            yield return new WaitForSecondsRealtime(3);
            if(body==null)yield break;
            var entries=new List<string>();
            foreach(var part in body.BodySkins)
            {
                if(part.Value==null)continue;
                foreach(var render in part.Value.GetRenderers().OfType<SkinnedMeshRenderer>())
                {
                    var mesh=render.sharedMesh;
                    Plugin.Log.LogInfo($"Ash part={part.Key} renderer={render.name} mesh={(mesh==null?"MISSING":mesh.name)} vertices={(mesh==null?0:mesh.vertexCount)} bones={render.bones.Length} enabled={render.enabled} active={render.gameObject.activeInHierarchy} shadow={render.shadowCastingMode}");
                    string bones=string.Join(",",render.bones.Select(b=>b==null?"null":"{\"name\":"+Quote(b.name)+",\"matrix\":"+Matrix(body.transform.worldToLocalMatrix*b.localToWorldMatrix)+"}"));
                    entries.Add("{\"part\":"+Quote(part.Key.ToString())+",\"renderer\":"+Quote(render.name)+",\"mesh\":"+Quote(mesh==null?"":mesh.name)+",\"rendererMatrix\":"+Matrix(body.transform.worldToLocalMatrix*render.transform.localToWorldMatrix)+",\"bones\":["+bones+"]}");
                }
            }
            try
            {
                string directory=Path.Combine(Plugin.Folder,"AshDiagnostics");Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,"pose-"+body.PointOfView.Value+".json"),"{\"renderers\":["+string.Join(",",entries)+"]}");
            }
            catch(Exception e){Plugin.Log.LogWarning("Ash pose diagnostic: "+e.Message);}
            capture=null;
        }
    }

}
