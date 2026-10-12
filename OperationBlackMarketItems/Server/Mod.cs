using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Models.Logging;
using SemanticVersioning;

namespace R6Port;

public record Metadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "private.r6port.r4c";
    public override string Name { get; init; } = "Operation Black Market Items";
    public override string Author { get; init; } = "Godly";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("0.1.1");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    { ["com.wtt.commonlib"] = new("^2.0.22") };
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = true;
    public override string License { get; init; } = "MIT";
}

[Injectable(InjectionType.Scoped, null, int.MaxValue, TypePriority = 400002)]
public class Mod(WTTServerCommonLib.WTTServerCommonLib common, ISptLogger<Mod> logger) : IOnLoad
{
    public async Task OnLoad()
    {
        var assembly=Assembly.GetExecutingAssembly();
        var folder=Path.GetDirectoryName(assembly.Location)!;
        // only register items whose bundles are packaged
        var required=new[]{"weapon_r4c_container.bundle", "magazine_r4c_30.bundle",
            "scope_r4c_y5s4.bundle", "grip_r4c_y5s4.bundle", "muzzle_r4c_y5s4.bundle", "charm_r4c_champion.bundle", "skin_siege_glacier.bundle",
            "charm_r4c_emberrise.bundle","charm_r4c_neondawn.bundle","charm_r4c_shiftingtides.bundle",
            "skin_siege_blackice.bundle","skin_siege_golddust.bundle","skin_siege_loyalty.bundle"};
        if(required.Any(name=>!File.Exists(Path.Combine(folder,"bundles","assets","r6port","r4c",name))))
        {
            logger.Warning("Operation Black Market Items: bundles incomplete; weapon registration skipped.");
            return;
        }
        await common.CustomItemServiceExtended.CreateCustomItems(assembly);
        await common.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly);
        if(File.Exists(Path.Combine(folder,"bundles","assets","r6port","ash","ash_equipped.bundle")))
        {
            await common.CustomClothingService.CreateCustomClothing(assembly);
            // prefab rcids pick body/feet/head/hands from the shared Ash bundle
            await common.CustomCustomizationService.CreateCustomCustomizations(assembly);
            logger.Info("Operation Black Market Items: Ash outfit registered for USEC and BEAR.");
        }
        if(File.Exists(Path.Combine(folder,"db","CustomVoices","ash.json")))
        {
            if(!File.Exists(Path.Combine(folder,"bundles","assets","r6port","voices","ash_voice.bundle")))
                throw new InvalidDataException("Operation Black Market Items: Ash voice bundle is missing.");
            await common.CustomVoiceService.CreateCustomVoices(assembly);
            logger.LogWithColor("[Operation Black Market Items] RAINBOW // Arsenal secured. Operator Ash ready.",LogTextColor.Cyan);
            logger.LogWithColor("[Operation Black Market Items] Weapons, operator gear and voice online. Welcome to the Black Market.",LogTextColor.Cyan);
        }
        else logger.Info("Operation Black Market Items: weapons registered; Ash voice data is not packaged.");
    }
}
