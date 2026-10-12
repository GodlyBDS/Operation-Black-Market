using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace R6Port;

[Injectable(TypePriority = 400010)]
public class KaliTrader(ModHelper modHelper, DatabaseService databaseService,
    ImageRouter imageRouter, ConfigServer configServer, ISptLogger<KaliTrader> logger) : IOnLoad
{
    public Task OnLoad()
    {
        string folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var details = modHelper.GetJsonDataFromFile<TraderBase>(folder, "Kali/base.json");
        var assort = modHelper.GetJsonDataFromFile<TraderAssort>(folder, "Kali/assort.json");
        var database = databaseService.GetTables();
        var trader = new Trader
        {
            Base = details, Assort = assort, Dialogue = [],
            QuestAssort = new() { ["Started"] = [], ["Success"] = [], ["Fail"] = [] },
            Suits = [], Services = []
        };
        if (!database.Traders.TryAdd(details.Id, trader))
        {
            logger.Warning("Kali trader ID already registered; skipping duplicate.");
            return Task.CompletedTask;
        }
        string avatarRoute = Path.ChangeExtension(details.Avatar!, null);
        imageRouter.AddRoute(avatarRoute, Path.Combine(folder, "Kali/kali.png"));
        // SPT 4.0 registers ConfigServer, direct config injection only works from 4.1
        var config = configServer.GetConfig<TraderConfig>();
        config.UpdateTime.RemoveAll(entry => entry.TraderId == details.Id);
        config.UpdateTime.Add(new UpdateTime { Name = "Kali", TraderId = details.Id,
            Seconds = new MinMax<int>(3600, 3600) });
        configServer.GetConfig<RagfairConfig>().Traders[details.Id] = false;
        details.NextResupply = checked((int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600));
        var texts = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            File.ReadAllText(Path.Combine(folder, "Kali/locales.json")))!;
        foreach (var (language, locale) in database.Locales.Global)
        {
            string description = texts.TryGetValue(language, out var value)
                ? value["description"] : texts["en"]["description"];
            locale.AddTransformer(data =>
            {
                if(data == null)throw new InvalidDataException("Missing locale data for Kali");
                data[$"{details.Id} FullName"] = "Kali";
                data[$"{details.Id} FirstName"] = "Kali";
                data[$"{details.Id} Nickname"] = "Kali";
                data[$"{details.Id} Location"] = "Rainbow Operations";
                data[$"{details.Id} Description"] = description;
                return data;
            });
        }
        logger.Info("Operation Black Market Items: Kali trader and Rainbow arsenal registered.");
        return Task.CompletedTask;
    }
}
