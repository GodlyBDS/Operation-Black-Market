# Operation Black Market — Source

Two SPT 4.0 mods, each with a server part (.NET 9) and a BepInEx client plugin (netstandard2.1).

- `OBMTheCompound` – The Compound map (location Terminal): server data/patches, client plugin for lighting, sounds, destruction and bot respawns.
- `OperationBlackMarketItems` – Rainbow Six Siege weapons, skins, charms, Kali trader, the Ash outfit and voice, and custom key definitions supplied through its runtime database. Needs WTT-ServerCommonLib.

See DEPENDENCIES.txt for the required and optional mods.

## Scope

This repository contains the authored C# plugin source and project files. It is not a ready-to-install mod package or the Unity map/weapon authoring project. Game assemblies, extracted Siege/Tarkov assets, audio, compiled asset bundles, profiles and credentials are not included.

Build references resolve against your local SPT/Escape from Tarkov installation. The map server additionally needs its exported `data/location.json`, configuration and optional `MapInfo` slides. The Items server needs its matching custom-item/clothing/voice definitions, Kali data, bundle manifest and privately supplied bundles. Compiling these projects produces DLLs; it does not recreate those runtime data or assets. Keep the corresponding local package when using a newly compiled DLL.

The source targets **SPT 4.0.13**. Compilation or compatibility with SPT 4.1 and later is not claimed.

## Build

Set the path to your SPT install when building:

    dotnet build OBMTheCompound/Server/OBMTheCompound.csproj -c Release -p:SptRoot="C:\SPT\SPT"
    dotnet build OBMTheCompound/Client/OBMTheCompound.Client.csproj -c Release -p:GameRoot="C:\SPT"
    dotnet build OperationBlackMarketItems/Server/OperationBlackMarketItems.csproj -c Release -p:SptRoot="C:\SPT\SPT"
    dotnet build OperationBlackMarketItems/Client/R6PortClient.csproj -c Release -p:EftRoot="C:\SPT"

## Recent corrections

The Compound's static container loot merge includes all native maps while preserving the existing shared-pool precedence. This covers Labyrinth-only crate types. A startup preflight reports missing placed-container or clone donor mappings before raid generation.

Collection weapon preset and Compound key generation belong to the local data packaging pipeline, which is not included in this C# source snapshot. The matching runtime package contains distinct collection presets and the House Walk-in Closet Key definition.

## Credits

Project by Godly, developed with assistance from Claude and OpenAI Codex. Server/client integration uses SPT, BepInEx, Harmony and WTT CommonLib; map navigation integrates with the dependencies listed in DEPENDENCIES.txt. Third-party game content remains the property of its respective owners and is not included here. Existing mod metadata retains its current license designation; no additional code license is granted by this source snapshot.
