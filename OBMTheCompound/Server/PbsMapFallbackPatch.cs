using System.Reflection;
using HarmonyLib;
using SPTarkov.Reflection.Patching;

namespace OBMTheCompound;

// APBS has a fixed map list for weapon range weights and throws for Terminal, so no bots spawn.
// Unknown maps use the APBS profile set in config.json (apbsMapProfile, default Woods).
// No-op without APBS.
public class PbsMapFallbackPatch : AbstractPatch
{
    static readonly string[] Known =
    {
        "bigmap", "RezervBase", "laboratory", "factory4_night", "factory4_day", "Interchange", "Sandbox", "Sandbox_high",
        "Woods", "Shoreline", "Lighthouse", "TarkovStreets", "Labyrinth",
    };

    public static MethodBase? Target => AccessTools.TypeByName("ProgressiveBotSystem.Models.MapRangeWeights") is { } t
        ? AccessTools.PropertyGetter(t, "Item")
        : null;

    protected override MethodBase? GetTargetMethod() => Target;

    // APBS map profile for The Compound (config apbsMapProfile)
    public static string Profile = "Woods";

    [PatchPrefix]
    public static void Prefix(ref string key)
    {
        if (System.Array.IndexOf(Known, key) < 0) key = System.Array.IndexOf(Known, Profile) >= 0 ? Profile : "Woods";
    }
}
