using System.Reflection;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Utils.Json;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Routers;

namespace OBMTheCompound;

public record Metadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "private.obm.thecompound";
    public override string Name { get; init; } = "Operation Black Market - The Compound";
    public override string Author { get; init; } = "Godly";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("0.1.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false; // bundles are loaded from StreamingAssets
    public override string License { get; init; } = "Private local use";
}

// Turns the unused Terminal location into The Compound.
// data/location.json comes from the Unity build and is merged over the Terminal base.
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 10)]
public class Mod(DatabaseService databaseService, JsonUtil jsonUtil, ConfigServer configServer, ICloner cloner, ImageRouter imageRouter, ISptLogger<Mod> logger) : IOnLoad
{
    // stable 24 char hex id from a string
    // Container loot quality (set per container in Unity). Container tables are almost the same on every map,
    // so better loot = weights scaled by (flea price / median)^(0.4 * quality) plus extra items.
    StaticLootDetails Boost(StaticLootDetails pool, int quality, int extra)
    {
        if (quality <= 0 && extra <= 0) return pool;
        var copy = cloner.Clone(pool)!;
        var prices = databaseService.GetTables().Templates.Prices;
        if (quality > 0 && copy.ItemDistribution != null)
        {
            var list = copy.ItemDistribution.ToList();
            var known = list.Select(d => prices.TryGetValue(d.Tpl, out var v) ? v : 0).Where(v => v > 0).OrderBy(v => v).ToList();
            var median = known.Count > 0 ? known[known.Count / 2] : 1;
            foreach (var d in list)
            {
                var price = prices.TryGetValue(d.Tpl, out var v) && v > 0 ? v : median;
                d.RelativeProbability = (float)Math.Max(1.0, Math.Round((d.RelativeProbability ?? 1.0) * Math.Pow(price / median, 0.4 * quality)));
            }
            copy.ItemDistribution = list;
        }
        if (extra > 0 && copy.ItemCountDistribution != null)
        {
            var counts = copy.ItemCountDistribution.ToList();
            foreach (var c in counts) c.Count += extra;
            copy.ItemCountDistribution = counts;
        }
        return copy;
    }

    static string Hex24(string text) =>
        Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..24].ToLowerInvariant();

    public Task OnLoad()
    {
        var modFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var configPath = Path.Combine(modFolder, "config.json");
        if (!File.Exists(configPath))
            File.WriteAllText(configPath, """
                {
                  "_info": "apbsMapProfile: welches APBS-Kartenprofil The Compound nutzt (bigmap, RezervBase, laboratory, Interchange, Woods, Shoreline, Lighthouse, TarkovStreets, Sandbox, Labyrinth, factory4_day)",
                  "apbsMapProfile": "Woods",
                  "_botPause": "Pause zwischen zwei Bot-Spawn-Phasen auf The Compound in Sekunden (0 = wie Streets in ABPS)",
                  "botPauseMinSeconds": 180,
                  "botPauseMaxSeconds": 420,
                  "_abpsStreetsSpawns": "true = zusaetzlich alle ABPS-Spawns von Streets (PMCs, Scavs, Bosse) auf The Compound; false = nur die Bot-Zonen aus Unity",
                  "abpsStreetsSpawns": false,
                  "_abpsPmcSpawns": "true = ABPS spawnt die PMCs an den PMC-Spawnpunkten der Map (weit weg von Spielern, sie laufen ueber die ganze Map) - braucht mehrere verteilte PMC-Spawnpunkte; false = PMCs nur aus den Unity-Bot-Zonen",
                  "abpsPmcSpawns": false
                }
                """);
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        if (PbsMapFallbackPatch.Target != null)
        {
            PbsMapFallbackPatch.Profile = config["apbsMapProfile"]?.GetValue<string>() ?? "Woods";
            var pbs = new PbsMapFallbackPatch();
            if (!pbs.IsActive) pbs.Enable();
            logger.Info($"The Compound: APBS nutzt fuer The Compound das Kartenprofil '{PbsMapFallbackPatch.Profile}'.");
        }
        var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var dataPath = Path.Combine(folder, "data", "location.json");
        if (!File.Exists(dataPath)) { logger.Warning("The Compound: data/location.json fehlt – Terminal unveraendert."); return Task.CompletedTask; }
        var overrides = JsonNode.Parse(File.ReadAllText(dataPath))!.AsObject();

        var tables = databaseService.GetTables();
        var terminal = tables.Locations.Terminal!;
        // Terminal was never finished and misses a lot of fields the client and Fika need,
        // so start from the Woods base and apply our data on top.
        var baseNode = JsonNode.Parse(jsonUtil.Serialize(tables.Locations.Woods!.Base)!)!.AsObject();
        var terminalNode = JsonNode.Parse(jsonUtil.Serialize(terminal.Base)!)!.AsObject();
        foreach (var key in new[] { "_Id", "Id", "Name", "IconX", "IconY", "Area" })
            if (terminalNode.TryGetPropertyValue(key, out var v)) baseNode[key] = v?.DeepClone();
        baseNode["transits"] = new JsonArray(); // no transits
        baseNode["secretExits"] = new JsonArray();
        // the client reads AirdropParameters[0], keep one entry but disable planes
        if (baseNode["AirdropParameters"] is JsonArray drops && drops.Count > 0)
        {
            var drop = drops[0]!.DeepClone().AsObject();
            drop["PlaneAirdropChance"] = 0;
            drop["PlaneAirdropMax"] = 0;
            baseNode["AirdropParameters"] = new JsonArray(drop);
        }
        foreach (var (key, value) in overrides["base"]!.AsObject())
            baseNode[key] = value?.DeepClone();
        terminal.Base = jsonUtil.Deserialize<LocationBase>(baseNode.ToJsonString())!;

        // loose loot from the Unity item spawns
        var looseLootJson = overrides["looseLoot"]?.ToJsonString();
        terminal.LooseLoot = new LazyLoad<LooseLoot>(() =>
            (looseLootJson != null ? jsonUtil.Deserialize<LooseLoot>(looseLootJson) : null)
            ?? new LooseLoot { Spawnpoints = [], SpawnpointsForced = [], SpawnpointCount = new SpawnpointCount { Mean = 0, Std = 0 } });
        // placed containers use Tarkov's static loot for their container type (first map that has it)
        // and the Woods ammo distribution
        var containersJson = overrides["staticContainers"]?.ToJsonString();
        terminal.StaticContainers = new LazyLoad<StaticContainerDetails>(() =>
            (containersJson != null ? jsonUtil.Deserialize<StaticContainerDetails>(containersJson) : null)
            ?? new StaticContainerDetails { StaticWeapons = [], StaticContainers = [], StaticForced = [] });
        var all = tables.Locations;
        var locationsByKey = all.GetDictionary();
        // Preserve the shared-pool precedence, then include every other native map.
        // Exclude Terminal: it now points to this mod's own lazy loot dictionary.
        var donors = new[] { all.Woods, all.Bigmap, all.Shoreline, all.Interchange, all.RezervBase, all.Lighthouse, all.TarkovStreets, all.Factory4Day, all.Laboratory, all.Sandbox }
            .Concat(locationsByKey.Values.Where(d => d != null && !ReferenceEquals(d, terminal)))
            .Distinct().ToArray();
        // containers with a map specific loot table get a cloned template, the clone uses that map's table
        var pools = (overrides["staticContainers"]?["obmPools"] as JsonArray ?? new JsonArray())
            .Select(n => (clone: n!["clone"]!.GetValue<string>(), tpl: n["tpl"]!.GetValue<string>(), map: n["map"]!.GetValue<string>(),
                          quality: n["quality"]?.GetValue<int>() ?? 0, extra: n["extra"]?.GetValue<int>() ?? 0)).ToList();
        // needs a deep copy so the clone has its own grids, otherwise the container spawns empty
        foreach (var p in pools)
            if (tables.Templates.Items.TryGetValue(new MongoId(p.tpl), out var original))
            {
                var copy = cloner.Clone(original)!;
                copy.Id = new MongoId(p.clone);
                if (copy.Properties?.Grids != null)
                    foreach (var g in copy.Properties.Grids)
                    {
                        g.Parent = p.clone;
                        g.Id = new MongoId(Hex24($"{p.clone}|{g.Name}"));
                    }
                tables.Templates.Items[copy.Id] = copy;
            }
        terminal.StaticLoot = new LazyLoad<Dictionary<MongoId, StaticLootDetails>>(() =>
        {
            var merged = new Dictionary<MongoId, StaticLootDetails>();
            foreach (var d in donors)
                if (d?.StaticLoot?.Value != null)
                    foreach (var (tpl, pool) in d.StaticLoot.Value) merged.TryAdd(tpl, pool);
            foreach (var p in pools)
            {
                var loc = locationsByKey.TryGetValue(p.map, out var l) ? l : locationsByKey.TryGetValue(all.GetMappedKey(p.map), out var l2) ? l2 : null;
                if (ReferenceEquals(loc, terminal))
                    throw new InvalidDataException($"The Compound: container pool {p.clone} references Terminal itself; choose a native donor map.");
                StaticLootDetails? source = null;
                if (loc?.StaticLoot?.Value != null && loc.StaticLoot.Value.TryGetValue(new MongoId(p.tpl), out var pool)) source = pool;
                else if (merged.TryGetValue(new MongoId(p.tpl), out var fallback)) source = fallback;
                if (source == null)
                    throw new InvalidDataException($"The Compound: no loot pool for cloned container {p.clone} (donor {p.tpl}, map '{p.map}'). Correct the exported container donor.");
                merged[new MongoId(p.clone)] = Boost(source, p.quality, p.extra);
            }
            foreach (var container in overrides["staticContainers"]?["staticContainers"] as JsonArray ?? new JsonArray())
            {
                var template = container!["template"]!;
                var rootId = template["Root"]?.GetValue<string>();
                var root = (template["Items"] as JsonArray)?.FirstOrDefault(i => i?["_id"]?.GetValue<string>() == rootId);
                var tpl = root?["_tpl"]?.GetValue<string>();
                if (tpl == null || !merged.ContainsKey(new MongoId(tpl)))
                    throw new InvalidDataException($"The Compound: placed container '{template["Id"]}' has no static loot pool for template '{tpl ?? "missing root"}'. Correct the container type or donor map in the export.");
            }
            return merged;
        });
        terminal.StaticAmmo = all.Woods!.StaticAmmo;
        // container spawn groups: only min..max containers of a group spawn per raid
        var groupsJson = overrides["staticContainers"]?["obmGroups"] as JsonArray ?? new JsonArray();
        var memberJson = overrides["staticContainers"]?["obmContainerGroups"] as JsonArray ?? new JsonArray();
        terminal.Statics = new StaticContainer
        {
            ContainersGroups = groupsJson.ToDictionary(g => g!["id"]!.GetValue<string>(),
                g => new ContainerMinMax { MinContainers = g!["min"]!.GetValue<int>(), MaxContainers = g["max"]!.GetValue<int>() }),
            Containers = memberJson.ToDictionary(m => m!["id"]!.GetValue<string>(), m => new ContainerData { GroupId = m!["group"]!.GetValue<string>() }),
        };
        if (groupsJson.Count > 0)
        {
            // SPT only randomises containers on maps in its location config
            var maps = configServer.GetConfig<LocationConfig>().ContainerRandomisationSettings.Maps;
            maps["terminal"] = true; maps["Terminal"] = true;
        }
        terminal.AllExtracts = (overrides["allExtracts"] as JsonArray)?.Select(e => jsonUtil.Deserialize<AllExtractsExit>(e!.ToJsonString())!).ToList() ?? [];

        var id = "5704e5a4d2720bb45b8b4567"; // Terminal _id (also the locale key prefix)
        // map info from the MapInfo folder: name.txt, beschreibung.txt, banner.jpg/.png
        var infoDir = Path.Combine(modFolder, "MapInfo");
        string InfoText(string file) => File.Exists(Path.Combine(infoDir, file)) ? File.ReadAllText(Path.Combine(infoDir, file)).Trim() : "";
        var name = InfoText("name.txt") is { Length: > 0 } infoName ? infoName : overrides["name"]?.GetValue<string>() ?? "The Compound";
        var description = InfoText("beschreibung.txt") is { Length: > 0 } infoDesc ? infoDesc : overrides["description"]?.GetValue<string>() ?? "";
        // raid menu slides: banner.jpg/.txt, banner2.jpg/.txt, ...
        // txt: first line is the title, the rest is the text. Slides without a picture use banner.jpg
        string? Pic(string n) => new[] { n + ".jpg", n + ".png" }.Select(f => Path.Combine(infoDir, f)).FirstOrDefault(File.Exists);
        var firstPic = Pic("banner");
        var slides = new List<(string id, string title, string text)>();
        var bannerList = new JsonArray();
        for (var i = 1; i <= 20; i++)
        {
            var n = i == 1 ? "banner" : "banner" + i;
            var pic = Pic(n);
            var txt = Path.Combine(infoDir, n + ".txt");
            if (pic == null && !File.Exists(txt)) continue;
            pic ??= firstPic;
            if (pic == null) continue;
            var routeName = $"obm_thecompound_{n}";
            imageRouter.AddRoute("/files/banners/" + routeName, pic);
            var file = routeName + Path.GetExtension(pic).ToLowerInvariant();
            var slideId = i == 1 ? Hex24("obm banner") : Hex24("obm banner " + i);
            var lines = File.Exists(txt) ? File.ReadAllLines(txt) : new string[0];
            var title = lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? name;
            var text = lines.Length > 0 ? string.Join("\n", lines.SkipWhile(l => l.Trim().Length == 0).Skip(1)).Trim() : description;
            slides.Add((slideId, title, text));
            bannerList.Add(new JsonObject
            {
                ["id"] = slideId,
                ["pic"] = new JsonObject { ["file"] = file, ["path"] = "banners/" + file, ["rcid"] = "", ["type"] = "banners" },
            });
        }
        if (bannerList.Count > 0)
        {
            terminal.Base.Banners = jsonUtil.Deserialize<List<Banner>>(bannerList.ToJsonString());
            logger.Info($"The Compound: {bannerList.Count} eigene Banner-Folie(n)");
        }
        // exit names from Unity (data/exit-names.json: exit id -> text shown in game)
        var exitNames = new Dictionary<string, string>();
        var exitNamesPath = Path.Combine(folder, "data", "exit-names.json");
        if (File.Exists(exitNamesPath))
            foreach (var (key, value) in JsonNode.Parse(File.ReadAllText(exitNamesPath))!.AsObject())
                if (value?.GetValue<string>() is { Length: > 0 } exitText) exitNames[key] = exitText;
        foreach (var (_, locale) in tables.Locales.Global)
        {
            locale.AddTransformer(data =>
            {
                if (data == null) return data;
                data[$"{id} Name"] = name;
                data[$"{id} ShortName"] = name;
                data[$"{id} Description"] = description;
                foreach (var (slideId, title, text) in slides) { data[$"{slideId} Name"] = title; data[$"{slideId} Description"] = text; }
                foreach (var (exitId, shown) in exitNames) data[exitId] = shown;
                foreach (var p in pools)
                    foreach (var suffix in new[] { "Name", "ShortName", "Description" })
                        if (data.TryGetValue($"{p.tpl} {suffix}", out var text)) data[$"{p.clone} {suffix}"] = text;
                return data;
            });
        }
        // AI brain weights for terminal (a missing key leaves bots without a brain), copied from Streets
        var pmcConfig = configServer.GetConfig<PmcConfig>();
        var botConfig = configServer.GetConfig<BotConfig>();
        foreach (var perType in pmcConfig.PmcType.Values)
            if (perType.TryGetValue("tarkovstreets", out var w)) perType["terminal"] = w;
        if (botConfig.AssaultBrainType.TryGetValue("tarkovstreets", out var assault)) botConfig.AssaultBrainType["terminal"] = assault;
        if (botConfig.PlayerScavBrainType.TryGetValue("tarkovstreets", out var pscav)) botConfig.PlayerScavBrainType["terminal"] = pscav;

        // ABPS support, see AbpsCompat
        if (AbpsCompat.Installed && (config["abpsStreetsSpawns"]?.GetValue<bool>() ?? false))
        {
            AbpsCompat.Db = databaseService;
            AbpsCompat.Cloner = cloner;
            AbpsCompat.PauseMin = config["botPauseMinSeconds"]?.GetValue<int>() ?? 180;
            AbpsCompat.PauseMax = config["botPauseMaxSeconds"]?.GetValue<int>() ?? 420;
            AbpsCompat.Capture(terminal.Base);
            foreach (var patch in new AbstractPatch[] { new AbpsStreetsInitialPatch(), new AbpsStreetsRebuildPatch(), new AbpsRaidFinishedPatch() })
                if (!patch.IsActive) patch.Enable();
            var n = AbpsCompat.CopyStreets(); // ABPS may have built its maps before we loaded
            logger.Info($"The Compound: ABPS erkannt – Streets-Einstellungen werden uebernommen ({n} Boss/PMC-Eintraege).");
        }
        else
        {
            if (config["abpsPmcSpawns"] == null && config is JsonObject cfgObj)
            {
                // add the new option to older config files
                cfgObj["_abpsPmcSpawns"] = "true = ABPS spawnt die PMCs an den PMC-Spawnpunkten der Map (weit weg von Spielern, sie laufen ueber die ganze Map) - braucht mehrere verteilte PMC-Spawnpunkte; false = PMCs nur aus den Unity-Bot-Zonen";
                cfgObj["abpsPmcSpawns"] = false;
                File.WriteAllText(configPath, cfgObj.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            // only our bot zones: disable the non-wave scav top-up, it would put scavs into every zone
            // including raider/PMC only zones
            terminal.Base.NewSpawn = false; terminal.Base.OfflineNewSpawn = false;
            terminal.Base.OldSpawn = true; terminal.Base.OfflineOldSpawn = true;
            logger.Info("The Compound: nur eigene Bot-Zonen (kein Scav-Nachschub ausserhalb der Unity-Zonen).");
        }
        logger.Success($"The Compound geladen: Szene {terminal.Base.Scene?.Path}, {terminal.Base.SpawnPointParams?.Count() ?? 0} Spawns, {terminal.Base.Exits?.Count() ?? 0} Exits, {(overrides["looseLoot"]?["spawnpoints"] as JsonArray)?.Count ?? 0}+{(overrides["looseLoot"]?["spawnpointsForced"] as JsonArray)?.Count ?? 0} Item-Spawns, {(overrides["staticContainers"]?["staticContainers"] as JsonArray)?.Count ?? 0} Container.");
        return Task.CompletedTask;
    }
}

// Resolve after other mods finish their database changes, before a raid starts.
[Injectable(TypePriority = int.MaxValue)]
public class StaticLootPreflight(DatabaseService databaseService, ISptLogger<StaticLootPreflight> logger) : IOnLoad
{
    public Task OnLoad()
    {
        var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        if (!File.Exists(Path.Combine(folder, "data", "location.json"))) return Task.CompletedTask;
        var loot = databaseService.GetTables().Locations.Terminal?.StaticLoot?.Value;
        logger.Info($"The Compound: static container loot mappings checked ({loot?.Count ?? 0} pools).");
        return Task.CompletedTask;
    }
}
