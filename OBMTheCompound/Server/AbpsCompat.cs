using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Cloners;

namespace OBMTheCompound;

// ABPS only supports the vanilla maps. The Compound reuses ABPS' Streets spawns: every time ABPS builds
// the Streets spawns we copy them to Terminal and swap the zone names for ours. Our own bot zones stay.
// The client plugin tells ABPS that Terminal is tarkovstreets during the raid. No-op without ABPS.
public static class AbpsCompat
{
    public static DatabaseService? Db;
    public static ICloner? Cloner;
    static List<BossLocationSpawn> _ownBosses = new();
    static List<Wave> _ownWaves = new();
    static string _zones = "", _snipeZones = "";
    // pause between spawn phases (config botPauseMinSeconds/botPauseMaxSeconds, 0 = use the Streets value)
    public static int PauseMin, PauseMax;

    public static bool Installed => AccessTools.TypeByName("BotPlacementSystemServer.Controllers.MapSpawns") != null;

    // our own spawns and zone names, saved before ABPS data gets merged in
    public static void Capture(LocationBase ours)
    {
        _ownBosses = Cloner!.Clone(ours.BossLocationSpawn ?? new List<BossLocationSpawn>()) ?? new();
        _ownWaves = Cloner!.Clone(ours.Waves ?? new List<Wave>()) ?? new();
        var zones = (ours.SpawnPointParams ?? new List<SpawnPointParam>()).Select(p => p.BotZoneName).Where(z => !string.IsNullOrEmpty(z)).Distinct().ToList();
        _snipeZones = string.Join(",", zones.Where(z => z!.Contains("Snipe")));
        _zones = string.Join(",", zones.Where(z => !z!.Contains("Snipe")));
    }

    public static int CopyStreets()
    {
        if (Db == null || Cloner == null) return 0;
        var locations = Db.GetLocations();
        var all = locations.GetDictionary();
        if (!all.TryGetValue(locations.GetMappedKey("tarkovstreets"), out var streetsLoc) || !all.TryGetValue(locations.GetMappedKey("terminal"), out var oursLoc)) return 0;
        var streets = streetsLoc.Base;
        var ours = oursLoc.Base;
        if (string.IsNullOrEmpty(_zones) && string.IsNullOrEmpty(_snipeZones)) return 0; // no bot zones yet

        string Remap(string? z) => string.IsNullOrEmpty(z) ? z ?? "" : z.Contains("Snipe") && _snipeZones.Length > 0 ? _snipeZones : _zones.Length > 0 ? _zones : _snipeZones;

        var bosses = Cloner.Clone(streets.BossLocationSpawn ?? new List<BossLocationSpawn>()) ?? new();
        foreach (var b in bosses) b.BossZone = Remap(b.BossZone);
        bosses.AddRange(Cloner.Clone(_ownBosses) ?? new());
        ours.BossLocationSpawn = bosses;

        var waves = Cloner.Clone(streets.Waves ?? new List<Wave>()) ?? new();
        foreach (var w in waves) w.SpawnPoints = Remap(w.SpawnPoints);
        waves.AddRange(Cloner.Clone(_ownWaves) ?? new());
        for (var i = 0; i < waves.Count; i++) waves[i].Number = i;
        ours.Waves = waves;

        // spawn settings from ABPS' Streets config
        ours.NewSpawn = streets.NewSpawn; ours.OfflineNewSpawn = streets.OfflineNewSpawn;
        ours.OldSpawn = streets.OldSpawn; ours.OfflineOldSpawn = streets.OfflineOldSpawn;
        ours.BotStart = streets.BotStart;
        if (streets.BotStop.HasValue && streets.EscapeTimeLimit.HasValue && ours.EscapeTimeLimit.HasValue)
            ours.BotStop = (int)(ours.EscapeTimeLimit.Value * 60 - (streets.EscapeTimeLimit.Value * 60 - streets.BotStop.Value));
        ours.BotSpawnTimeOnMin = streets.BotSpawnTimeOnMin; ours.BotSpawnTimeOnMax = streets.BotSpawnTimeOnMax;
        ours.BotSpawnTimeOffMin = PauseMin > 0 ? PauseMin : streets.BotSpawnTimeOffMin;
        ours.BotSpawnTimeOffMax = PauseMax > 0 ? Math.Max(PauseMax, PauseMin) : streets.BotSpawnTimeOffMax;
        ours.BotSpawnPeriodCheck = streets.BotSpawnPeriodCheck; ours.BotSpawnCountStep = streets.BotSpawnCountStep;
        if (streets.BotLocationModifier != null && ours.BotLocationModifier != null)
        {
            ours.BotLocationModifier.AdditionalHostilitySettings = Cloner.Clone(streets.BotLocationModifier.AdditionalHostilitySettings);
            ours.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayer = streets.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayer;
            ours.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayerPvE = streets.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayerPvE;
        }
        if (ours.NonWaveGroupScenario != null && streets.NonWaveGroupScenario != null) ours.NonWaveGroupScenario.Chance = streets.NonWaveGroupScenario.Chance;
        return bosses.Count;
    }
}

// ABPS builds all maps on startup
public class AbpsStreetsInitialPatch : AbstractPatch
{
    public static MethodBase? Target => AccessTools.TypeByName("BotPlacementSystemServer.Controllers.MapSpawns") is { } t ? AccessTools.Method(t, "ReplaceOriginalLocations") : null;
    protected override MethodBase? GetTargetMethod() => Target;
    [PatchPostfix] public static void Postfix() => AbpsCompat.CopyStreets();
}

// ABPS rebuilds a map after each raid, follow Streets
public class AbpsStreetsRebuildPatch : AbstractPatch
{
    public static MethodBase? Target => AccessTools.TypeByName("BotPlacementSystemServer.Controllers.MapSpawns") is { } t ? AccessTools.Method(t, "ReplaceOriginalLocationForMap") : null;
    protected override MethodBase? GetTargetMethod() => Target;
    [PatchPostfix] public static void Postfix(string location) { if (location == "tarkovstreets") AbpsCompat.CopyStreets(); }
}

// ABPS skips the rebuild for unknown maps, so rebuild Streets instead (gets copied over again by the patch above)
public class AbpsRaidFinishedPatch : AbstractPatch
{
    public static MethodBase? Target => AccessTools.TypeByName("BotPlacementSystemServer.Controllers.MapSpawns") is { } t ? AccessTools.Method(t, "RebuildMap") : null;
    protected override MethodBase? GetTargetMethod() => Target;
    [PatchPrefix] public static void Prefix(ref string location) { if (string.Equals(location, "terminal", StringComparison.OrdinalIgnoreCase)) location = "tarkovstreets"; }
}
